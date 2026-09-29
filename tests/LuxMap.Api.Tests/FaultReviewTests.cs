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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

/// <summary><c>PATCH /api/v1/faults/{id}</c> — a Manager's review of a fault (BE-19).</summary>
[Collection(nameof(AssetDatabaseCollection))]
public class FaultReviewTests(AssetImportFixture factory, ITestOutputHelper output) : IAsyncLifetime
{
    private const string Route = "/api/v1/faults";

    private readonly Dictionary<string, AppUser> users = [];
    private readonly Dictionary<string, HttpClient> clients = [];
    private string home = null!;
    private string foreign = null!;
    private string segment = null!;
    private string foreignSegment = null!;

    private Task<T> Db<T>(Func<LuxMapDbContext, Task<T>> work) => factory.QueryAsync(work);

    public async Task InitializeAsync()
    {
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var communes = new[]
            {
                new AdministrativeUnit { Name = "Fault review home " + Guid.NewGuid() },
                new AdministrativeUnit { Name = "Fault review foreign " + Guid.NewGuid() },
            };
            db.AddRange(communes);
            await db.SaveChangesAsync();
            home = communes[0].CommuneId;
            foreign = communes[1].CommuneId;

            foreach (var (key, role) in new[] { ("manager", UserRole.Manager), ("a", UserRole.FieldEngineer) })
            {
                var user = new AppUser
                {
                    Username = "fr" + Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid", FullName = key,
                    PasswordHash = "", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", Role = role,
                };
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, factory.AccountPassword);
                users[key] = user;
                db.Add(user);
            }

            await db.SaveChangesAsync();
            foreach (var user in users.Values)
            {
                db.Add(new AppUserCommune { UserId = user.UserId, CommuneId = home });
            }

            var roads = new[] { home, foreign }.Select(commune => new RoadSegment
            {
                CommuneId = commune, SegmentName = "Fault review road", RoadClass = RoadClass.InterVillage, DataSource = DataSource.Simulated,
                Geom = new LineString([new Coordinate(108, 16), new Coordinate(108.01, 16.01)]) { SRID = 4326 }, LengthM = 100,
            }).ToArray();
            db.AddRange(roads);
            await db.SaveChangesAsync();
            segment = roads[0].SegmentId;
            foreignSegment = roads[1].SegmentId;
            return 0;
        });

        foreach (var (key, user) in users)
        {
            var client = factory.CreateClient();
            var token = await (await client.PostLoginAsync(user.Username, factory.AccountPassword)).ReadTokensAsync();
            client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
            clients[key] = client;
        }
    }

    public async Task DisposeAsync()
    {
        await Db(async db =>
        {
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await db.Database.ExecuteSqlRawAsync("SET LOCAL luxmap.audit_purge = 'on'");
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit_event WHERE commune_id = {home} OR commune_id = {foreign}");
                await transaction.CommitAsync();
            }

            foreach (var table in new[] { "work_order_fault", "work_order", "fault", "road_segment" })
            {
                var sql = $"DELETE FROM {table} WHERE commune_id = {{0}} OR commune_id = {{1}}";
                await db.Database.ExecuteSqlRawAsync(sql, home, foreign);
            }

            var ids = users.Values.Select(user => user.UserId).ToArray();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user WHERE user_id = ANY({ids})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM administrative_unit WHERE commune_id = {home} OR commune_id = {foreign}");
            return 0;
        });

        foreach (var client in clients.Values)
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Confirming_records_who_and_when_in_one_audit_and_closes_the_review_actions()
    {
        var fault = await PlantAsync();
        var (item, events) = await PatchAsync(fault, new { fault_status = "confirmed", note = "  Seen on camera  " }, 200);

        Assert.Equal("confirmed", item.GetProperty("fault_status").GetString());
        Assert.Equal("Seen on camera", item.GetProperty("review_note").GetString());
        Assert.Equal(["reclassify", "set_severity", "edit_note"], Actions(item));

        var saved = await Db(db => db.Set<Fault>().IgnoreQueryFilters().SingleAsync(x => x.FaultId == fault));
        Assert.Equal(users["manager"].UserId, saved.ConfirmedBy);
        Assert.NotNull(saved.ConfirmedAt);

        var audit = Assert.Single(events);
        Assert.Equal(AuditEntityType.Fault, audit.EntityType);
        Assert.Equal(AuditAction.Confirmed, audit.Action);
        Assert.Equal("detected", JsonDocument.Parse(audit.BeforeState!).RootElement.GetProperty("fault_status").GetString());
        Assert.Equal("confirmed", JsonDocument.Parse(audit.AfterState!).RootElement.GetProperty("fault_status").GetString());
        Assert.Equal("Seen on camera", audit.Note);
    }

    [Fact]
    public async Task Rejecting_with_a_severity_is_one_decision_and_then_the_fault_is_history()
    {
        var fault = await PlantAsync();
        var (item, events) = await PatchAsync(fault, new { fault_status = "rejected", severity = "low" }, 200);

        Assert.Equal("rejected", item.GetProperty("fault_status").GetString());
        Assert.Equal("low", item.GetProperty("severity").GetString());
        Assert.Empty(Actions(item));
        Assert.Equal(AuditAction.Rejected, Assert.Single(events).Action);

        await PatchAsync(fault, new { fault_status = "rejected", severity = "high" }, 409, "INVALID_STATE_TRANSITION");
        await PatchAsync(fault, new { fault_status = "confirmed" }, 409, "INVALID_STATE_TRANSITION");
    }

    [Fact]
    public async Task Only_the_review_transitions_are_made_here()
    {
        var detected = await PlantAsync();
        await PatchAsync(detected, new { fault_status = "in_progress" }, 409, "INVALID_STATE_TRANSITION");
        await PatchAsync(detected, new { fault_status = "verified" }, 409, "INVALID_STATE_TRANSITION");

        var confirmed = await PlantAsync(status: FaultStatus.Confirmed);
        await PatchAsync(confirmed, new { fault_status = "rejected" }, 409, "INVALID_STATE_TRANSITION");

        // Legal in section 3.2 but NOT here (D-2): these moves belong to repair work orders. Without
        // this pair the test would pass on the state machine alone and never exercise PATCH's own rule.
        await PatchAsync(confirmed, new { fault_status = "in_progress" }, 409, "INVALID_STATE_TRANSITION");
        var working = await PlantAsync(status: FaultStatus.InProgress);
        await PatchAsync(working, new { fault_status = "resolved" }, 409, "INVALID_STATE_TRANSITION");
        Assert.Equal(FaultStatus.Confirmed, await Db(db => db.Set<Fault>().IgnoreQueryFilters()
            .Where(x => x.FaultId == confirmed).Select(x => x.FaultStatus).SingleAsync()));
    }

    [Fact]
    public async Task The_current_status_edits_in_place_and_a_real_no_op_writes_nothing()
    {
        var fault = await PlantAsync(status: FaultStatus.Confirmed);
        var before = await Db(db => db.Set<Fault>().IgnoreQueryFilters().SingleAsync(x => x.FaultId == fault));

        var (noop, none) = await PatchAsync(fault, new { fault_status = "confirmed", severity = "medium" }, 200);
        Assert.Empty(none);
        Assert.Equal(before.UpdatedAt, noop.GetProperty("updated_at").GetDateTime());

        var (edited, events) = await PatchAsync(fault, new { fault_status = "confirmed", severity = "critical", note = "School gate" }, 200);
        Assert.Equal("critical", edited.GetProperty("severity").GetString());
        Assert.Equal(AuditAction.DetailsChanged, Assert.Single(events).Action);

        var (kept, _) = await PatchAsync(fault, new { fault_status = "confirmed", severity = "high" }, 200);
        Assert.Equal("School gate", kept.GetProperty("review_note").GetString());
        var (cleared, _) = await PatchAsync(fault, new { fault_status = "confirmed", note = "   " }, 200);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("review_note").ValueKind);
    }

    [Fact]
    public async Task Reclassifying_keeps_the_reported_type_and_filters_on_the_decided_one()
    {
        var fault = await PlantAsync(type: FaultType.LampOut);
        var (item, _) = await PatchAsync(fault, new { fault_status = "detected", override_fault_type = "lamp_dim" }, 200);
        Assert.Equal("lamp_dim", item.GetProperty("fault_type").GetString());

        var saved = await Db(db => db.Set<Fault>().IgnoreQueryFilters().SingleAsync(x => x.FaultId == fault));
        Assert.Equal(FaultType.LampOut, saved.FaultType);
        Assert.Equal(FaultType.LampDim, saved.OverrideFaultType);
        Assert.Contains(fault, await IdsAsync($"?commune_id={home}&fault_type=lamp_dim"));
        Assert.DoesNotContain(fault, await IdsAsync($"?commune_id={home}&fault_type=lamp_out"));

        await PatchAsync(fault, new { fault_status = "detected", override_fault_type = "lamp_out" }, 200);
        Assert.Null(await Db(db => db.Set<Fault>().IgnoreQueryFilters().Where(x => x.FaultId == fault).Select(x => x.OverrideFaultType).SingleAsync()));

        await PatchAsync(fault, new { fault_status = "detected", override_fault_type = "node_offline" }, 400, "VALIDATION_FAILED");
        var feeder = await PlantAsync(type: FaultType.NodeOffline);
        await PatchAsync(feeder, new { fault_status = "detected", override_fault_type = "lamp_out" }, 400, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task A_fault_held_by_a_repair_is_not_reviewable_but_one_under_inspection_is()
    {
        var repaired = await PlantAsync(status: FaultStatus.Confirmed);
        var repair = await PlantWorkOrderAsync(TaskKind.Repair, repaired);
        var (_, _) = await PatchAsync(repaired, new { fault_status = "confirmed", severity = "high" }, 409, "FAULT_IN_ACTIVE_REPAIR");
        var listed = await ItemAsync(repaired);
        Assert.Equal(repair, listed.GetProperty("work_order_id").GetString());
        Assert.Empty(Actions(listed));

        var inspected = await PlantAsync();
        await PlantWorkOrderAsync(TaskKind.Inspection, inspected);
        await PatchAsync(inspected, new { fault_status = "rejected" }, 200);
    }

    [Fact]
    public async Task Outside_scope_is_404_and_a_bad_body_is_400_before_any_lookup()
    {
        var elsewhere = await PlantAsync(commune: foreign, road: foreignSegment);
        await PatchAsync(elsewhere, new { fault_status = "confirmed" }, 404, "FAULT_NOT_FOUND");
        await PatchAsync("FAULT-DOES-NOT-EXIST", new { fault_status = "confirmed" }, 404, "FAULT_NOT_FOUND");

        await PatchAsync("FAULT-DOES-NOT-EXIST", new { severity = "high" }, 400, "VALIDATION_FAILED");
        var fault = await PlantAsync();
        await PatchAsync(fault, new { fault_status = "confirmed", severity = "urgent" }, 400, "VALIDATION_FAILED");
        await PatchAsync(fault, new { fault_status = "confirmed", severity = (string?)null }, 400, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Only_the_manager_is_offered_actions()
    {
        var fault = await PlantAsync();
        Assert.Equal(["confirm", "reject", "reclassify", "set_severity", "edit_note"], Actions(await ItemAsync(fault)));

        var asEngineer = await clients["a"].GetFromJsonAsync<JsonElement>($"{Route}?commune_id={home}");
        Assert.All(asEngineer.GetProperty("items").EnumerateArray(), item => Assert.Empty(Actions(item)));
    }

    /// <summary>Fault is IAudited: an ordinary write with no audit event is refused before it reaches the database.</summary>
    [Fact]
    public async Task A_fault_written_without_an_audit_event_is_refused()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Db(async db =>
        {
            db.Add(NewFault(home, segment));
            return await db.SaveChangesAsync();
        }));
        Assert.Contains("audit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(JsonElement Body, List<AuditEvent> Events)> PatchAsync(
        string fault, object body, int expected, string? code = null)
    {
        var correlation = Guid.NewGuid().ToString();
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{Route}/{fault}") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Correlation-Id", correlation);
        var response = await clients["manager"].SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        output.WriteLine($"PATCH {fault} → {(int)response.StatusCode} {text}");
        Assert.True((int)response.StatusCode == expected, $"expected {expected}, got {(int)response.StatusCode}: {text}");

        var json = JsonDocument.Parse(text).RootElement.Clone();
        if (code is not null)
        {
            Assert.Equal(code, json.GetProperty("error").GetProperty("code").GetString());
        }

        var events = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().Where(x => x.CorrelationId == correlation).ToListAsync());
        if (expected >= 400)
        {
            Assert.Empty(events);
        }

        return (json, events);
    }

    private async Task<JsonElement> ItemAsync(string fault)
    {
        var page = await clients["manager"].GetFromJsonAsync<JsonElement>($"{Route}?commune_id={home}&page_size=200");
        return page.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("fault_id").GetString() == fault);
    }

    private async Task<string[]> IdsAsync(string query)
    {
        var page = await clients["manager"].GetFromJsonAsync<JsonElement>(Route + query);
        return page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("fault_id").GetString()!).ToArray();
    }

    private static string[] Actions(JsonElement item)
        => item.GetProperty("allowed_actions").EnumerateArray().Select(x => x.GetString()!).ToArray();

    private Fault NewFault(string commune, string road, FaultStatus status = FaultStatus.Detected, FaultType type = FaultType.LampOut)
        => new()
        {
            CommuneId = commune, SegmentId = road, Lat = 16.001, Lng = 108.001, FaultStatus = status, FaultType = type,
            Severity = Severity.Medium, SourceChannel = SourceChannel.Cv, DataSource = DataSource.Simulated, DetectedAt = DateTime.UtcNow,
        };

    private Task<string> PlantAsync(FaultStatus status = FaultStatus.Detected, FaultType type = FaultType.LampOut,
        string? commune = null, string? road = null)
        => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var fault = NewFault(commune ?? home, road ?? segment, status, type);
            db.Add(fault);
            await db.SaveChangesAsync();
            return fault.FaultId;
        });

    private Task<string> PlantWorkOrderAsync(TaskKind kind, string fault)
        => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var now = DateTime.UtcNow;
            var workOrder = new WorkOrder
            {
                CommuneId = home, Title = "Fault review work order", CreatedBy = users["manager"].UserId, TaskKind = kind,
                WoStatus = WorkOrderStatus.Assigned, SegmentId = segment, AssignedTo = users["a"].UserId, AssignedAt = now,
                CreatedAt = now, UpdatedAt = now,
            };
            db.Add(workOrder);
            await db.SaveChangesAsync();
            db.Add(new WorkOrderFault { WorkOrderId = workOrder.WorkOrderId, FaultId = fault, CommuneId = home, LinkedAt = now });
            await db.SaveChangesAsync();
            return workOrder.WorkOrderId;
        });
}
