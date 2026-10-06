using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.Sync.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-43 — the offline bundle and the offline queue. Own communes, accounts, roads and poles, all removed again.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class SyncTests(AssetImportFixture factory) : IAsyncLifetime
{
    private const string Push = "/api/v1/sync/push";
    private const string Bundle = "/api/v1/sync/bundle";
    private readonly Dictionary<string, AppUser> users = [];
    private readonly Dictionary<string, HttpClient> clients = [];
    private string home = null!, foreign = null!, road1 = null!, road2 = null!, foreignRoad = null!;
    private string pole1 = null!, pole2 = null!, poleOnRoad2 = null!;

    private Task<T> Db<T>(Func<LuxMapDbContext, Task<T>> work) => factory.QueryAsync(work);

    public async Task InitializeAsync()
    {
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var communes = new[] { new AdministrativeUnit { Name = "Sync home " + Guid.NewGuid() }, new AdministrativeUnit { Name = "Sync foreign " + Guid.NewGuid() } };
            db.AddRange(communes);
            await db.SaveChangesAsync();
            (home, foreign) = (communes[0].CommuneId, communes[1].CommuneId);

            foreach (var (key, role) in new[] { ("manager", UserRole.Manager), ("a", UserRole.FieldEngineer), ("b", UserRole.FieldEngineer), ("superior", UserRole.Superior) })
            {
                var user = new AppUser
                {
                    Username = "sync" + Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid", FullName = "Sync " + key,
                    PasswordHash = "", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", PasswordSetAt = DateTime.UtcNow, Role = role,
                };
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, factory.AccountPassword);
                users[key] = user;
                db.Add(user);
            }

            await db.SaveChangesAsync();
            foreach (var user in users.Values) db.Add(new AppUserCommune { UserId = user.UserId, CommuneId = home });

            var roads = new[] { (home, 108.0), (home, 108.1), (foreign, 108.2) }.Select(r => new RoadSegment
            {
                CommuneId = r.Item1, SegmentName = "Sync road", RoadClass = RoadClass.InterVillage, DataSource = DataSource.Simulated, LengthM = 100,
                Geom = new LineString([new Coordinate(r.Item2, 16), new Coordinate(r.Item2 + 0.01, 16.01)]) { SRID = 4326 },
            }).ToArray();
            db.AddRange(roads);
            await db.SaveChangesAsync();
            (road1, road2, foreignRoad) = (roads[0].SegmentId, roads[1].SegmentId, roads[2].SegmentId);

            var poles = new[] { (road1, home, 108.0), (road1, home, 108.005), (road2, home, 108.1), (foreignRoad, foreign, 108.2) }.Select(p => new Pole
            {
                SegmentId = p.Item1, CommuneId = p.Item2, DataSource = DataSource.Simulated, Geom = new Point(p.Item3, 16) { SRID = 4326 },
            }).ToArray();
            db.AddRange(poles);
            await db.SaveChangesAsync();
            (pole1, pole2, poleOnRoad2) = (poles[0].PoleId, poles[1].PoleId, poles[2].PoleId);
            return 0;
        });

        foreach (var (key, user) in users)
        {
            clients[key] = await LoginAsync(factory.CreateClient(), user);
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

            // Restrict keys point at pole and app_user: dependents first, poles before the accounts that edited them.
            foreach (var table in new[] { "notification", "work_order_fault", "work_order_segment", "repair_evidence", "work_order", "lux_reading", "fault", "fixture", "pole", "road_segment" })
            {
                var sql = $"DELETE FROM {table} WHERE commune_id = {{0}} OR commune_id = {{1}}";
                await db.Database.ExecuteSqlRawAsync(sql, home, foreign);
            }

            var ids = users.Values.Select(x => x.UserId).ToArray();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user WHERE user_id = ANY({ids})"); // sync_operation cascades
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM administrative_unit WHERE commune_id = {home} OR commune_id = {foreign}");
            return 0;
        });
        foreach (var client in clients.Values) client.Dispose();
    }

    // ── Bundle ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Without_segments_the_bundle_packs_the_roads_of_my_open_orders_and_only_my_orders()
    {
        var fault = await FaultAsync(pole1, FaultStatus.Confirmed);
        var mine = await CreateOrderAsync("repair", users["a"], faults: [fault]);
        await CreateOrderAsync("inspection", users["b"], road: road2);
        await NoteAsync(pole1, "Cạnh cổng trường");

        var bundle = await GetAsync("a", Bundle);

        Assert.Equal([road1], Strings(bundle.GetProperty("segment_ids")));
        Assert.Equal([road1], bundle.GetProperty("segments").GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("properties").GetProperty("segment_id").GetString()!));
        var poles = bundle.GetProperty("poles").GetProperty("features").EnumerateArray().Select(f => f.GetProperty("properties")).ToArray();
        Assert.Equal(IdOrder(pole1, pole2), poles.Select(p => p.GetProperty("pole_id").GetString()!));
        var noted = poles.Single(p => p.GetProperty("pole_id").GetString() == pole1);
        Assert.Equal("Cạnh cổng trường", noted.GetProperty("note").GetString());
        Assert.Equal(JsonValueKind.Null, poles.Single(p => p.GetProperty("pole_id").GetString() == pole2).GetProperty("note").ValueKind);
        Assert.True(noted.TryGetProperty("fixture_status", out _), "a bundle pole carries the map layer's properties");
        Assert.Equal([fault], bundle.GetProperty("open_faults").EnumerateArray().Select(f => f.GetProperty("fault_id").GetString()!));
        var orders = bundle.GetProperty("work_orders").EnumerateArray().ToArray();
        Assert.Equal([mine], orders.Select(o => o.GetProperty("work_order_id").GetString()!));
        Assert.Contains("start", Strings(orders[0].GetProperty("allowed_actions")));
    }

    [Fact]
    public async Task Named_segments_come_back_in_the_order_asked_and_a_foreign_one_is_404()
    {
        var bundle = await GetAsync("a", $"{Bundle}?segment_id={road2}&segment_id={road1}&segment_id={road2}");
        Assert.Equal([road2, road1], Strings(bundle.GetProperty("segment_ids")));
        Assert.Equal(IdOrder(pole1, pole2, poleOnRoad2), bundle.GetProperty("poles").GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("properties").GetProperty("pole_id").GetString()!));

        await ExpectErrorAsync("a", $"{Bundle}?segment_id={foreignRoad}", HttpStatusCode.NotFound, "ASSET_NOT_FOUND");
        await ExpectErrorAsync("a", $"{Bundle}?segment_id={road1}&since=2026-10-01T00:00:00Z", HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        var tooMany = string.Join('&', Enumerable.Range(0, 21).Select(i => $"segment_id=SEG-{i}"));
        await ExpectErrorAsync("a", $"{Bundle}?{tooMany}", HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    // ── Push ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Each kind goes through its own endpoint's service, and resending the same batch writes nothing new —
    /// including start / complete, which on their own endpoint would answer a resend with 409.
    /// </summary>
    [Fact]
    public async Task Every_operation_kind_applies_once_and_a_resent_batch_is_a_replay()
    {
        var order = await CreateOrderAsync("inspection", users["a"], road: road1);
        var startedAt = DateTime.UtcNow; // after the assignment, before the push — what a queued step looks like
        var batch = new[]
        {
            Op("fault_report", new { pole_id = pole1, fault_type = "lamp_out", note = "Đèn tắt hẳn lúc kiểm tra tối nay" }),
            Op("lux_reading", new { pole_id = pole2, measured_at = DateTime.UtcNow.AddMinutes(-20), lux_value = 12.5, data_source = "field" }),
            Op("pole_note", new { pole_id = pole2, note = "Cột nghiêng nhẹ", base_note = (string?)null }),
            Op("work_order_start", new { work_order_id = order, performed_at = startedAt }),
            Op("work_order_complete", new { work_order_id = order, report_note = "Đã kiểm tra hết cột trên tuyến" }),
        };

        var first = await PushAsync("a", batch);
        Assert.Empty(first.GetProperty("conflicts").EnumerateArray());
        Assert.Empty(first.GetProperty("rejected").EnumerateArray());
        var applied = first.GetProperty("applied").EnumerateArray().ToArray();
        Assert.Equal(5, applied.Length);
        Assert.All(applied, a => Assert.False(a.GetProperty("replayed").GetBoolean()));
        Assert.StartsWith("FAULT-", applied[0].GetProperty("id").GetString());
        Assert.StartsWith("LUX-", applied[1].GetProperty("id").GetString());
        Assert.Equal([pole2, order, order], applied[2..].Select(a => a.GetProperty("id").GetString()!));

        var counts = await CountsAsync(order);
        var second = await PushAsync("a", batch);
        Assert.All(second.GetProperty("applied").EnumerateArray(), a => Assert.True(a.GetProperty("replayed").GetBoolean()));
        Assert.Equal(5, second.GetProperty("applied").GetArrayLength());
        Assert.Equal(counts, await CountsAsync(order));

        var stored = await Db(db => db.Set<WorkOrder>().IgnoreQueryFilters().AsNoTracking().SingleAsync(w => w.WorkOrderId == order));
        Assert.Equal(WorkOrderStatus.Done, stored.WoStatus);
        Assert.Equal(new DateTime(startedAt.Ticks / 10 * 10, DateTimeKind.Utc), stored.StartedAt);
        Assert.Equal("Cột nghiêng nhẹ", await Db(db => db.Set<Pole>().IgnoreQueryFilters().Where(p => p.PoleId == pole2).Select(p => p.Note).SingleAsync()));
    }

    [Fact]
    public async Task A_conflict_carries_the_server_state_and_blocks_later_steps_on_the_same_order_only()
    {
        var order = await CreateOrderAsync("inspection", users["a"], road: road1);
        await SendAsync("manager", HttpMethod.Post, $"/api/v1/work-orders/{order}/cancel", new { note = "Huỷ vì trùng việc" }, HttpStatusCode.OK);
        var start = Op("work_order_start", new { work_order_id = order });
        var complete = Op("work_order_complete", new { work_order_id = order, report_note = "Đã kiểm tra hết cột trên tuyến" });

        var result = await PushAsync("a", start, complete,
            Op("lux_reading", new { pole_id = pole1, measured_at = DateTime.UtcNow, lux_value = 3.0, data_source = "field" }));

        var conflict = Assert.Single(result.GetProperty("conflicts").EnumerateArray());
        Assert.Equal("INVALID_STATE_TRANSITION", conflict.GetProperty("reason").GetString());
        Assert.Equal("cancelled", conflict.GetProperty("server_state").GetProperty("wo_status").GetString());
        var blocked = Assert.Single(result.GetProperty("rejected").EnumerateArray());
        Assert.Equal("BLOCKED_BY_EARLIER_OP", blocked.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(Key(start), blocked.GetProperty("error").GetProperty("details").GetProperty("blocked_by").GetGuid());
        Assert.Equal("lux_reading", Assert.Single(result.GetProperty("applied").EnumerateArray()).GetProperty("op_type").GetString());
        Assert.Equal(0, await Db(db => db.Set<SyncOperation>().CountAsync(o => o.EntityId == order)));
    }

    [Fact]
    public async Task A_note_changed_since_the_engineer_saw_it_is_a_conflict_and_is_kept()
    {
        await NoteAsync(pole1, "ghi chú A");
        await SendAsync("manager", HttpMethod.Put, $"/api/v1/assets/poles/{pole1}/note", new { note = "ghi chú B" }, HttpStatusCode.OK);

        var stale = await PushAsync("a", Op("pole_note", new { pole_id = pole1, note = "ghi chú C", base_note = "ghi chú A" }));
        var conflict = Assert.Single(stale.GetProperty("conflicts").EnumerateArray());
        Assert.Equal("NOTE_CHANGED", conflict.GetProperty("reason").GetString());
        Assert.Equal("ghi chú B", conflict.GetProperty("server_state").GetProperty("note").GetString());
        Assert.Equal("ghi chú B", await Db(db => db.Set<Pole>().IgnoreQueryFilters().Where(p => p.PoleId == pole1).Select(p => p.Note).SingleAsync()));

        var fresh = await PushAsync("a", Op("pole_note", new { pole_id = pole1, note = "ghi chú C", base_note = " ghi chú B " }));
        Assert.Single(fresh.GetProperty("applied").EnumerateArray());
        Assert.Equal("ghi chú C", await Db(db => db.Set<Pole>().IgnoreQueryFilters().Where(p => p.PoleId == pole1).Select(p => p.Note).SingleAsync()));
    }

    [Fact]
    public async Task A_bad_operation_is_rejected_on_its_own_and_a_bad_envelope_is_400()
    {
        var mismatched = Guid.NewGuid();
        var result = await PushAsync("a",
            Op("teleport", new { }),
            Op("fault_report", new { pole_id = pole1, fault_type = "lamp_out", note = "ngắn" }),
            Op("fault_report", new { client_op_id = mismatched, pole_id = pole1, fault_type = "lamp_out", note = "Đèn tắt hẳn lúc kiểm tra" }),
            Op("work_order_start", new { work_order_id = "WO-404404" }),
            Op("work_order_start", new { work_order_id = (string?)null, performed_at = DateTime.UtcNow.AddHours(1) }),
            Op("pole_note", new { pole_id = pole2, note = "Ghi chú hợp lệ" }));

        var rejected = result.GetProperty("rejected").EnumerateArray().Select(r => r.GetProperty("error").GetProperty("code").GetString()!).ToArray();
        Assert.Equal(["VALIDATION_FAILED", "VALIDATION_FAILED", "VALIDATION_FAILED", "VALIDATION_FAILED"], rejected);
        Assert.Equal("WORK_ORDER_NOT_FOUND", Assert.Single(result.GetProperty("conflicts").EnumerateArray()).GetProperty("reason").GetString());
        Assert.Equal("pole_note", Assert.Single(result.GetProperty("applied").EnumerateArray()).GetProperty("op_type").GetString());
        Assert.Equal(0, await Db(db => db.Set<Fault>().IgnoreQueryFilters().CountAsync(f => f.CommuneId == home)));

        var once = Guid.NewGuid();
        await SendAsync("a", HttpMethod.Post, Push, new { operations = new[] { Op("pole_note", new { pole_id = pole2, note = "x" }, once), Op("pole_note", new { pole_id = pole2, note = "y" }, once) } }, HttpStatusCode.BadRequest);
        await SendAsync("a", HttpMethod.Post, Push, new { operations = Enumerable.Range(0, 101).Select(_ => Op("pole_note", new { pole_id = pole2, note = "x" })).ToArray() }, HttpStatusCode.BadRequest);
    }

    /// <summary>A key already spent on one operation cannot carry another: rejected, and nothing of the second is written.</summary>
    [Fact]
    public async Task A_client_op_id_reused_for_a_different_operation_is_rejected_and_writes_nothing()
    {
        var key = Guid.NewGuid();
        await PushAsync("a", Op("pole_note", new { pole_id = pole1, note = "lần đầu" }, key));

        var reused = await PushAsync("a", Op("pole_note", new { pole_id = pole2, note = "lần hai" }, key));

        Assert.Equal("IDEMPOTENCY_CONFLICT", Assert.Single(reused.GetProperty("rejected").EnumerateArray()).GetProperty("error").GetProperty("code").GetString());
        Assert.Null(await Db(db => db.Set<Pole>().IgnoreQueryFilters().Where(p => p.PoleId == pole2).Select(p => p.Note).SingleAsync()));
    }

    [Fact]
    public async Task A_step_dated_before_the_order_was_assigned_or_in_the_future_is_rejected()
    {
        var order = await CreateOrderAsync("inspection", users["a"], road: road1);

        var result = await PushAsync("a",
            Op("work_order_start", new { work_order_id = order, performed_at = DateTime.UtcNow.AddDays(-2) }),
            Op("work_order_start", new { work_order_id = order, performed_at = DateTime.UtcNow.AddHours(1) }));

        var rejected = result.GetProperty("rejected").EnumerateArray().ToArray();
        Assert.Equal("performed_at", rejected[0].GetProperty("error").GetProperty("details").GetProperty("field").GetString());
        Assert.Equal("BLOCKED_BY_EARLIER_OP", rejected[1].GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(WorkOrderStatus.Assigned, await Db(db => db.Set<WorkOrder>().IgnoreQueryFilters().Where(w => w.WorkOrderId == order).Select(w => w.WoStatus).SingleAsync()));
    }

    /// <summary>
    /// 🔴 The door admits a role that may sync; each operation must STILL pass its own capability. Proven by a host
    /// whose authorization refuses only <c>ReportFaults</c>: the report is rejected while the lux reading goes through.
    /// </summary>
    [Fact]
    public async Task Each_operation_is_checked_against_its_own_capability_not_only_the_door()
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddTransient<IAuthorizationService>(provider => new RefusingOne(
                ActivatorUtilities.CreateInstance<DefaultAuthorizationService>(provider), LuxMapPolicies.ReportFaults))));
        using var client = await LoginAsync(host.CreateClient(), users["a"]);

        var response = await client.PostAsJsonAsync(Push, new
        {
            operations = new[]
            {
                Op("fault_report", new { pole_id = pole1, fault_type = "lamp_out", note = "Đèn tắt hẳn lúc kiểm tra tối nay" }),
                Op("lux_reading", new { pole_id = pole1, measured_at = DateTime.UtcNow, lux_value = 4.0, data_source = "field" }),
            },
        });
        var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ROLE_FORBIDDEN", Assert.Single(result.GetProperty("rejected").EnumerateArray()).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("lux_reading", Assert.Single(result.GetProperty("applied").EnumerateArray()).GetProperty("op_type").GetString());
    }

    [Fact]
    public async Task Only_a_field_engineer_may_sync()
    {
        foreach (var who in new[] { "manager", "superior" })
        {
            await ExpectErrorAsync(who, Bundle, HttpStatusCode.Forbidden, "ROLE_FORBIDDEN");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────

    private sealed class RefusingOne(IAuthorizationService inner, string refused) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => inner.AuthorizeAsync(user, resource, requirements);

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => policyName == refused ? Task.FromResult(AuthorizationResult.Failed()) : inner.AuthorizeAsync(user, resource, policyName);
    }

    private static object Op(string type, object payload, Guid? key = null)
        => new Dictionary<string, object?> { ["client_op_id"] = key ?? Guid.NewGuid(), ["op_type"] = type, ["payload"] = payload };

    private static Guid Key(object op) => (Guid)((Dictionary<string, object?>)op)["client_op_id"]!;

    /// <summary>One batch shares created_at, so the id rule decides: length first, then the text (CLAUDE.md section 0).</summary>
    private static string[] IdOrder(params string[] ids) => [.. ids.OrderBy(id => id.Length).ThenBy(id => id, StringComparer.Ordinal)];

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(x => x.GetString()!)];

    private async Task<HttpClient> LoginAsync(HttpClient client, AppUser user)
    {
        var token = await (await client.PostLoginAsync(user.Username, factory.AccountPassword)).ReadTokensAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
        return client;
    }

    private Task<JsonElement> PushAsync(string who, params object[] operations)
        => SendAsync(who, HttpMethod.Post, Push, new { operations }, HttpStatusCode.OK);

    private Task<JsonElement> GetAsync(string who, string url) => SendAsync(who, HttpMethod.Get, url, null, HttpStatusCode.OK);

    private async Task ExpectErrorAsync(string who, string url, HttpStatusCode status, string code)
        => Assert.Equal(code, (await SendAsync(who, HttpMethod.Get, url, null, status)).GetProperty("error").GetProperty("code").GetString());

    private async Task<JsonElement> SendAsync(string who, HttpMethod method, string url, object? body, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        var response = await clients[who].SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{who} {method} {url}: expected {(int)expected}, got {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<string> CreateOrderAsync(string kind, AppUser assignee, string[]? faults = null, string? road = null)
    {
        var created = await SendAsync("manager", HttpMethod.Post, "/api/v1/work-orders",
            new { task_kind = kind, title = "Sync test order", fault_ids = faults, segment_id = road, assigned_to = assignee.UserId }, HttpStatusCode.Created);
        return created.GetProperty("work_order_id").GetString()!;
    }

    private Task NoteAsync(string poleId, string note)
        => SendAsync("a", HttpMethod.Put, $"/api/v1/assets/poles/{poleId}/note", new { note }, HttpStatusCode.OK);

    private Task<string> FaultAsync(string poleId, FaultStatus status)
        => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var fault = new Fault
            {
                CommuneId = home, PoleId = poleId, SegmentId = road1, FaultStatus = status, FaultType = FaultType.LampOut,
                Severity = Severity.Medium, SourceChannel = SourceChannel.Cv, DataSource = DataSource.Simulated, DetectedAt = DateTime.UtcNow,
            };
            db.Add(fault);
            await db.SaveChangesAsync();
            return fault.FaultId;
        });

    private Task<(int Faults, int Lux, int Audits, int Replays)> CountsAsync(string order)
        => Db(async db => (
            await db.Set<Fault>().IgnoreQueryFilters().CountAsync(f => f.CommuneId == home),
            await db.Set<LuxReading>().IgnoreQueryFilters().CountAsync(l => l.CommuneId == home),
            await db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(e => e.CommuneId == home),
            await db.Set<SyncOperation>().CountAsync(o => o.UserId == users["a"].UserId)));
}
