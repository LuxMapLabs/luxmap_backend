using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-41 — a field engineer reports a fault on site (<c>POST /faults</c>) and attaches photos afterwards
/// (<c>POST /faults/{id}/photos</c>, drift EV-2).
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class FaultReportTests(AssetImportFixture factory) : IAsyncLifetime
{
    private readonly Dictionary<string, AppUser> users = [];
    private readonly Dictionary<string, HttpClient> clients = [];
    private readonly PhotoStore photos = new();
    private WebApplicationFactory<Program> host = null!;
    private string home = null!, second = null!, outside = null!, road = null!, pole = null!, fixture = null!, foreignPole = null!;

    private Task<T> Db<T>(Func<LuxMapDbContext, Task<T>> work) => factory.QueryAsync(work);

    public async Task InitializeAsync()
    {
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var communes = new[] { "home", "second", "outside" }.Select(n => new AdministrativeUnit { Name = $"Report {n} {Guid.NewGuid():N}" }).ToArray();
            db.AddRange(communes); await db.SaveChangesAsync();
            (home, second, outside) = (communes[0].CommuneId, communes[1].CommuneId, communes[2].CommuneId);
            foreach (var (key, role, scope) in new[] { ("crew", UserRole.FieldEngineer, new[] { home }), ("multi", UserRole.FieldEngineer, new[] { home, second }),
                         ("other", UserRole.FieldEngineer, new[] { home }), ("manager", UserRole.Manager, new[] { home, second }) })
            {
                var user = new AppUser { Username = "report" + Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid", FullName = key,
                    PasswordHash = "", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", Role = role };
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, factory.AccountPassword);
                db.Add(user); await db.SaveChangesAsync(); users[key] = user;
                foreach (var commune in scope) db.Add(new AppUserCommune { UserId = user.UserId, CommuneId = commune });
            }
            var segment = new RoadSegment { CommuneId = home, SegmentName = "Report road", RoadClass = RoadClass.InterVillage, DataSource = DataSource.Simulated,
                LengthM = 100, Geom = new LineString([new Coordinate(108.3, 16.3), new Coordinate(108.31, 16.31)]) { SRID = 4326 } };
            var far = new RoadSegment { CommuneId = outside, SegmentName = "Far road", RoadClass = RoadClass.InterVillage, DataSource = DataSource.Field,
                LengthM = 100, Geom = new LineString([new Coordinate(108.4, 16.4), new Coordinate(108.41, 16.41)]) { SRID = 4326 } };
            db.AddRange(segment, far); await db.SaveChangesAsync(); road = segment.SegmentId;
            // A TESTBED-like pole (simulated): a report on it must not be counted as field data.
            var mine = new Pole { CommuneId = home, SegmentId = road, DataSource = DataSource.Simulated, Geom = new Point(108.305, 16.305) { SRID = 4326 } };
            var theirs = new Pole { CommuneId = outside, SegmentId = far.SegmentId, DataSource = DataSource.Field, Geom = new Point(108.405, 16.405) { SRID = 4326 } };
            db.AddRange(mine, theirs); await db.SaveChangesAsync(); (pole, foreignPole) = (mine.PoleId, theirs.PoleId);
            var lamp = new Fixture { PoleId = pole, CommuneId = home, FixtureType = FixtureType.LedRoadLamp, PowerSource = PowerSource.Grid, LampWatt = 90,
                InstallDate = new DateOnly(2025, 1, 1), DataSource = DataSource.Simulated };
            db.Add(lamp); await db.SaveChangesAsync(); fixture = lamp.FixtureId;
            return 0;
        });
        host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IObjectStore>(photos))));
        foreach (var (key, user) in users)
        {
            var client = host.CreateClient();
            var token = await (await client.PostLoginAsync(user.Username, factory.AccountPassword)).ReadTokensAsync();
            client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
            clients[key] = client;
        }
    }

    public async Task DisposeAsync()
    {
        await Db(async db =>
        {
            string[] communes = [home, second, outside];
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await db.Database.ExecuteSqlRawAsync("SET LOCAL luxmap.audit_purge = 'on'");
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit_event WHERE commune_id = ANY({communes})");
                await transaction.CommitAsync();
            }
            foreach (var table in new[] { "repair_evidence", "fault", "fixture", "pole", "road_segment", "app_user_commune" })
            {
                var sql = "DELETE FROM " + table + " WHERE commune_id = ANY({0})"; // table names are this list's literals
                await db.Database.ExecuteSqlRawAsync(sql, new object[] { communes });
            }
            var ids = users.Values.Select(x => x.UserId).ToArray();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user WHERE user_id = ANY({ids})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM administrative_unit WHERE commune_id = ANY({communes})");
            return 0;
        });
        foreach (var client in clients.Values) client.Dispose();
        await host.DisposeAsync();
    }

    private async Task<(int Status, JsonElement Body)> Report(string who, object body)
    {
        var response = await clients[who].PostAsJsonAsync("/api/v1/faults", body);
        var text = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private async Task<(int Status, JsonElement Body)> Photo(string who, string faultId, byte[]? bytes = null, string? clientOpId = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes ?? PhotoStore.Jpeg());
        file.Headers.ContentType = new("image/jpeg");
        form.Add(file, "file", "photo.jpg");
        form.Add(new StringContent("2026-10-04T14:00:00Z"), "captured_at");
        form.Add(new StringContent("16.305"), "lat");
        form.Add(new StringContent("108.305"), "lng");
        if (clientOpId is not null) form.Add(new StringContent(clientOpId), "client_op_id");
        var response = await clients[who].PostAsync($"/api/v1/faults/{faultId}/photos", form);
        var text = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static string? Code(JsonElement body) => body.ValueKind == JsonValueKind.Object && body.TryGetProperty("error", out var e) ? e.GetProperty("code").GetString() : null;

    private static object OnPole(string poleId, string? key = null, string type = "lamp_out", string note = "Lamp is dark all night")
        => new { client_op_id = key ?? Guid.NewGuid().ToString(), pole_id = poleId, fault_type = type, note };

    [Fact]
    public async Task A_report_on_a_pole_starts_detected_as_a_field_report_and_takes_everything_else_from_the_pole()
    {
        var key = Guid.NewGuid().ToString();
        var (status, body) = await Report("crew", OnPole(pole, key));

        Assert.Equal(201, status);
        Assert.Equal(key, body.GetProperty("client_op_id").GetString());
        Assert.Equal(("detected", "field_report", "medium"), (body.GetProperty("fault_status").GetString(), body.GetProperty("source_channel").GetString(),
            body.GetProperty("severity").GetString()));
        Assert.Equal(users["crew"].UserId, body.GetProperty("reported_by").GetString());
        Assert.Equal(road, body.GetProperty("segment_id").GetString());
        // The pole is a testbed-like (simulated) pole: the report keeps ITS data source, never `field` (drift BE-41).
        Assert.Equal("simulated", body.GetProperty("data_source").GetString());
        Assert.Equal(16.305, body.GetProperty("location").GetProperty("lat").GetDouble(), 6); // falls back to the pole's point

        var faultId = body.GetProperty("fault_id").GetString()!;
        var saved = await Db(db => db.Set<Fault>().IgnoreQueryFilters().SingleAsync(f => f.FaultId == faultId));
        Assert.Equal(home, saved.CommuneId);
        Assert.Equal(1, await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(e => e.EntityId == faultId && e.Action == AuditAction.Created)));
    }

    [Fact]
    public async Task Resending_the_same_client_op_id_returns_the_same_fault_and_another_user_cannot_reuse_it()
    {
        var key = Guid.NewGuid().ToString();
        var first = await Report("crew", OnPole(pole, key));
        var again = await Report("crew", OnPole(pole, key));
        var stranger = await Report("other", OnPole(pole, key));

        Assert.Equal((201, 200), (first.Status, again.Status));
        Assert.Equal(first.Body.GetProperty("fault_id").GetString(), again.Body.GetProperty("fault_id").GetString());
        Assert.Equal((409, "IDEMPOTENCY_CONFLICT"), (stranger.Status, Code(stranger.Body)));
        Assert.Equal(1, await Db(db => db.Set<Fault>().IgnoreQueryFilters().CountAsync(f => f.ClientOpId == key)));
    }

    [Fact]
    public async Task Without_a_pole_the_location_is_required_and_the_commune_comes_from_the_scope()
    {
        var missing = await Report("crew", new { client_op_id = Guid.NewGuid(), fault_type = "lamp_out", note = "A lamp not on the map" });
        Assert.Equal((400, "LOCATION_REQUIRED"), (missing.Status, Code(missing.Body)));

        var (status, body) = await Report("crew", new { client_op_id = Guid.NewGuid(), fault_type = "lamp_dim", note = "A lamp not on the map",
            location = new { lat = 16.31, lng = 108.31 } });
        Assert.Equal(201, status);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("pole_id").ValueKind);
        Assert.Equal("field", body.GetProperty("data_source").GetString());
        var faultId = body.GetProperty("fault_id").GetString()!;
        Assert.Equal(home, await Db(db => db.Set<Fault>().IgnoreQueryFilters().Where(f => f.FaultId == faultId).Select(f => f.CommuneId).SingleAsync()));
    }

    [Fact]
    public async Task An_engineer_of_several_communes_names_one_and_only_one_inside_their_scope()
    {
        object Body(string? commune) => new { client_op_id = Guid.NewGuid(), fault_type = "lamp_out", note = "A lamp not on the map",
            location = new { lat = 16.31, lng = 108.31 }, commune_id = commune };

        var unnamed = await Report("multi", Body(null));
        var named = await Report("multi", Body(second));
        var foreign = await Report("multi", Body(outside));

        Assert.Equal((400, "VALIDATION_FAILED"), (unnamed.Status, Code(unnamed.Body)));
        Assert.Equal(201, named.Status);
        Assert.Equal((403, "COMMUNE_FORBIDDEN"), (foreign.Status, Code(foreign.Body)));
    }

    [Fact]
    public async Task With_a_pole_the_commune_is_never_taken_from_the_client_and_a_pole_out_of_scope_is_404()
    {
        var withCommune = await Report("crew", new { client_op_id = Guid.NewGuid(), pole_id = pole, fault_type = "lamp_out", note = "Lamp is dark all night", commune_id = home });
        var foreign = await Report("crew", OnPole(foreignPole));

        Assert.Equal((400, "VALIDATION_FAILED"), (withCommune.Status, Code(withCommune.Body)));
        Assert.Equal((404, "POLE_NOT_FOUND"), (foreign.Status, Code(foreign.Body)));
    }

    [Theory]
    [InlineData("segment_outage", "Lamp is dark all night", null, "FAULT_TYPE_NOT_REPORTABLE")]
    [InlineData("runtime_decline", "Lamp is dark all night", null, "FAULT_TYPE_NOT_REPORTABLE")]
    [InlineData("lamp_out", "too short", null, "VALIDATION_FAILED")]
    [InlineData("lamp_out", "Lamp is dark all night", "FRM-0001", "VALIDATION_FAILED")]
    public async Task What_cannot_be_reported_is_refused(string type, string note, string? photoFrameId, string code)
    {
        var (status, body) = await Report("crew", new { client_op_id = Guid.NewGuid(), pole_id = pole, fault_type = type, note, photo_frame_id = photoFrameId });
        Assert.Equal((400, code), (status, Code(body)));
    }

    [Fact]
    public async Task A_fixture_must_be_the_lamp_in_use_on_that_pole_and_a_future_time_is_refused()
    {
        var wrong = await Report("crew", new { client_op_id = Guid.NewGuid(), pole_id = pole, fixture_id = "FIX-999999", fault_type = "lamp_out", note = "Lamp is dark all night" });
        var future = await Report("crew", new { client_op_id = Guid.NewGuid(), pole_id = pole, fault_type = "lamp_out", note = "Lamp is dark all night",
            detected_at = DateTime.UtcNow.AddHours(2) });
        var (status, body) = await Report("crew", new { client_op_id = Guid.NewGuid(), pole_id = pole, fixture_id = fixture, fault_type = "lamp_out",
            note = "Lamp is dark all night", detected_at = "2026-10-04T12:30:00Z" });

        Assert.Equal((400, "VALIDATION_FAILED"), (wrong.Status, Code(wrong.Body)));
        Assert.Equal((400, "VALIDATION_FAILED"), (future.Status, Code(future.Body)));
        Assert.Equal(201, status);
        Assert.Equal(fixture, body.GetProperty("fixture_id").GetString());
        Assert.Equal("2026-10-04T12:30:00Z", body.GetProperty("detected_at").GetString()); // queued offline: when it was SEEN
    }

    [Fact]
    public async Task The_reporter_attaches_photos_that_everyone_reading_the_fault_can_see()
    {
        var faultId = (await Report("crew", OnPole(pole))).Body.GetProperty("fault_id").GetString()!;
        var original = PhotoStore.Jpeg(33);

        var (status, photo) = await Photo("crew", faultId, original);
        Assert.Equal(201, status);
        Assert.Equal(("observation", faultId), (photo.GetProperty("kind").GetString(), photo.GetProperty("fault_id").GetString()));
        Assert.Equal(JsonValueKind.Null, photo.GetProperty("work_order_id").ValueKind);

        var list = await clients["manager"].GetFromJsonAsync<JsonElement>($"/api/v1/faults/{faultId}/photos");
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal(original, await clients["manager"].GetByteArrayAsync(photo.GetProperty("original_url").GetString()));
    }

    [Fact]
    public async Task Only_the_reporter_adds_photos_and_only_while_the_fault_is_open()
    {
        var faultId = (await Report("crew", OnPole(pole))).Body.GetProperty("fault_id").GetString()!;

        var (status, body) = await Photo("other", faultId);
        Assert.Equal((404, "FAULT_NOT_FOUND"), (status, Code(body)));

        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var fault = await db.Set<Fault>().IgnoreQueryFilters().SingleAsync(f => f.FaultId == faultId);
            fault.FaultStatus = FaultStatus.Rejected; fault.ConfirmedBy = users["manager"].UserId; fault.ConfirmedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(); return 0;
        });
        (status, body) = await Photo("crew", faultId);
        Assert.Equal((409, "FAULT_NOT_OPEN"), (status, Code(body)));
        Assert.Equal(0, await Db(db => db.Set<RepairEvidence>().IgnoreQueryFilters().CountAsync(x => x.FaultId == faultId)));
    }

    [Fact]
    public async Task The_database_keeps_one_parent_per_photo_and_fault_photos_are_observations()
    {
        var faultId = (await Report("crew", OnPole(pole))).Body.GetProperty("fault_id").GetString()!;
        async Task<string?> Insert(EvidenceKind kind, bool alsoWorkOrder) => await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            db.Add(new RepairEvidence { FaultId = faultId, WorkOrderId = alsoWorkOrder ? "WO-0001" : null, CommuneId = home, Kind = kind,
                CapturedAt = DateTime.UtcNow, Lat = 16, Lng = 108, ObjectKey = "o", ThumbnailKey = "t", ByteCount = 1, ThumbnailBytes = 1,
                UploadedBy = users["crew"].UserId });
            try { await db.SaveChangesAsync(); return null; }
            catch (DbUpdateException error) { return (error.InnerException as Npgsql.PostgresException)?.ConstraintName; }
        });

        Assert.Equal("ck_repair_evidence_fault_observation", await Insert(EvidenceKind.After, false));
        Assert.Equal("ck_repair_evidence_one_parent", await Insert(EvidenceKind.Observation, true));
    }
}
