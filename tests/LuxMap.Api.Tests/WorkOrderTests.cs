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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public class WorkOrderTests(AssetImportFixture factory, ITestOutputHelper output) : IAsyncLifetime
{
    private readonly Dictionary<string, AppUser> users = [];
    private readonly Dictionary<string, HttpClient> clients = [];
    private string home = null!;
    private string foreign = null!;
    private string segment = null!;
    private string foreignSegment = null!;
    private const string Route = "/api/v1/work-orders";

    private Task<T> Db<T>(Func<LuxMapDbContext, Task<T>> work) => factory.QueryAsync(work);

    public async Task InitializeAsync()
    {
        output.WriteLine("Creating isolated work-order accounts and communes.");
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var communes = new[] { new AdministrativeUnit { Name = "WO test home " + Guid.NewGuid() }, new AdministrativeUnit { Name = "WO test foreign " + Guid.NewGuid() } };
            db.AddRange(communes);
            await db.SaveChangesAsync();
            home = communes[0].CommuneId; foreign = communes[1].CommuneId;
            foreach (var (key, role) in new[] { ("manager", UserRole.Manager), ("a", UserRole.FieldEngineer), ("b", UserRole.FieldEngineer), ("superior", UserRole.Superior), ("admin", UserRole.SystemAdmin) })
            {
                var user = new AppUser
                {
                    Username = "wo" + Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid", FullName = key,
                    PasswordHash = "", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", Role = role, HasSystemWideScope = role == UserRole.SystemAdmin,
                };
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, factory.AccountPassword);
                users[key] = user; db.Add(user);
            }
            await db.SaveChangesAsync();
            foreach (var user in users.Values)
            {
                db.Add(new AppUserCommune { UserId = user.UserId, CommuneId = home });
                if (user.Role is UserRole.Manager or UserRole.Superior) db.Add(new AppUserCommune { UserId = user.UserId, CommuneId = foreign });
            }
            var roads = new[] { home, foreign }.Select(c => new RoadSegment
            {
                CommuneId = c, SegmentName = "WO test road", RoadClass = RoadClass.InterVillage, DataSource = DataSource.Simulated,
                Geom = new LineString([new Coordinate(108, 16), new Coordinate(108.01, 16.01)]) { SRID = 4326 }, LengthM = 100,
            }).ToArray();
            db.AddRange(roads); await db.SaveChangesAsync(); segment = roads[0].SegmentId; foreignSegment = roads[1].SegmentId;
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
            foreach (var table in new[] { "work_order_fault", "work_order", "fault", "fault_cluster", "road_segment" })
            {
                var sql = $"DELETE FROM {table} WHERE commune_id = {{0}} OR commune_id = {{1}}";
                await db.Database.ExecuteSqlRawAsync(sql, home, foreign);
            }
            var ids = users.Values.Select(x => x.UserId).ToArray();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user WHERE user_id = ANY({ids})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM administrative_unit WHERE commune_id = {home} OR commune_id = {foreign}");
            return 0;
        });
        foreach (var client in clients.Values) client.Dispose();
    }

    private async Task<JsonElement> Send(string who, string method, string path, object? body, int expected, string? error = null, int auditExpected = 1, string? correlation = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), Route + path);
        if (body is not null) request.Content = JsonContent.Create(body);
        correlation ??= Guid.NewGuid().ToString();
        request.Headers.Add("X-Correlation-Id", correlation);
        var response = await clients[who].SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expected, $"{who} {method} {path}: expected {expected}, got {(int)response.StatusCode}: {text}");
        var json = JsonDocument.Parse(text).RootElement.Clone();
        if (error is not null) Assert.Equal(error, json.GetProperty("error").GetProperty("code").GetString());
        var events = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().Where(x => x.CorrelationId == correlation).ToListAsync());
        if (expected >= 400 || method == "GET") Assert.Empty(events);
        else Assert.Equal(auditExpected, events.Count);
        return json;
    }

    private Task<string> Fault(FaultStatus status = FaultStatus.Detected, string? commune = null, string? road = null, double? priority = null)
        => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var f = new Fault { CommuneId = commune ?? home, SegmentId = road ?? segment, Lat = 16, Lng = 108,
                FaultStatus = status, FaultType = FaultType.LampOut, Severity = Severity.Medium,
                SourceChannel = SourceChannel.Cv, DataSource = DataSource.Simulated, DetectedAt = DateTime.UtcNow, PriorityScore = priority };
            db.Add(f); await db.SaveChangesAsync(); return f.FaultId;
        });

    private async Task<string> Create(string kind = "inspection", string[]? faults = null, string? assigned = null)
    {
        var response = await Send("manager", "POST", "", new { task_kind = kind, title = "Work order test", fault_ids = faults,
            segment_id = faults is null ? segment : null, assigned_to = assigned }, 201);
        return response.GetProperty("work_order_id").GetString()!;
    }

    private Task<string> Plant(WorkOrderStatus state, string? assigned = null, string? commune = null, string? explicitId = null, DateTime? created = null)
        => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var now = created ?? DateTime.UtcNow;
            var wo = new WorkOrder { CommuneId = commune ?? home, Title = "Planted work order", CreatedBy = users["manager"].UserId,
                TaskKind = TaskKind.Inspection, WoStatus = state, CreatedAt = now, UpdatedAt = now,
                SegmentId = commune == foreign ? foreignSegment : segment,
                AssignedTo = state == WorkOrderStatus.Open ? null : assigned ?? users["a"].UserId,
                AssignedAt = state == WorkOrderStatus.Open ? null : now,
                StartedAt = state is WorkOrderStatus.InProgress or WorkOrderStatus.Done or WorkOrderStatus.Verified ? now : null,
                CompletedAt = state is WorkOrderStatus.Done or WorkOrderStatus.Verified ? now : null,
                ReportNote = state is WorkOrderStatus.Done or WorkOrderStatus.Verified ? "Planted report" : null,
                ClosedAt = state is WorkOrderStatus.Verified or WorkOrderStatus.Cancelled ? now : null };
            if (explicitId is not null) wo.WorkOrderId = explicitId;
            db.Add(wo); await db.SaveChangesAsync(); return wo.WorkOrderId;
        });

    [Fact]
    public async Task Transition_http_literal_six_by_nine_table()
    {
        string[] actions = ["assign", "reassign", "unassign", "edit", "start", "complete", "verify", "return", "cancel"];
        bool[][] table = [
            [true,true,true,true,false,false,false,false,true],
            [true,true,true,true,true,false,false,false,true],
            [true,true,true,true,false,true,false,false,true],
            [false,false,false,false,false,false,true,true,false],
            [false,false,false,false,false,false,false,false,false],
            [false,false,false,false,false,false,false,false,false]];
        WorkOrderStatus[] states = [WorkOrderStatus.Open, WorkOrderStatus.Assigned, WorkOrderStatus.InProgress, WorkOrderStatus.Done, WorkOrderStatus.Verified, WorkOrderStatus.Cancelled];
        for (var s = 0; s < states.Length; s++)
        for (var a = 0; a < actions.Length; a++)
        {
            var id = await Plant(states[s]);
            var action = actions[a];
            var who = action is "start" or "complete" ? "a" : "manager";
            var path = action is "assign" or "reassign" or "unassign" ? "assignee" : action;
            var method = path == "assignee" ? "PUT" : action == "edit" ? "PATCH" : "POST";
            object body = action switch
            {
                "assign" or "reassign" => new { assigned_to = users["b"].UserId },
                "unassign" => new { assigned_to = (string?)null },
                "edit" => new { title = "Edited" },
                "complete" => new { report_note = "Field work is complete" },
                _ => new { note = "Reviewed by manager" },
            };
            // An unassigned WO is outside every engineer's scope, before state validation.
            var expected = states[s] == WorkOrderStatus.Open && who == "a" ? 404 : table[s][a] ? 200 : 409;
            await Send(who, method, "/" + id + (action == "edit" ? "" : "/" + path), body, expected,
                expected == 404 ? "WORK_ORDER_NOT_FOUND" : expected == 409 ? "INVALID_STATE_TRANSITION" : null,
                auditExpected: states[s] == WorkOrderStatus.Open && action == "unassign" ? 0 : 1);
        }
    }

    [Fact]
    public async Task Actor_and_commune_filters_are_combined_per_request_and_metadata_is_closed()
    {
        var mine = await Plant(WorkOrderStatus.Assigned);
        var other = await Plant(WorkOrderStatus.Assigned, users["b"].UserId);
        var elsewhere = await Plant(WorkOrderStatus.Assigned, users["a"].UserId, foreign);
        foreach (var id in new[] { other, elsewhere })
        {
            await Send("a", "GET", "/" + id, null, 404, "WORK_ORDER_NOT_FOUND");
            await Send("a", "POST", "/" + id + "/start", new { }, 404, "WORK_ORDER_NOT_FOUND");
            await Send("a", "POST", "/" + id + "/complete", new { report_note = "Sufficient field report" }, 404, "WORK_ORDER_NOT_FOUND");
        }
        foreach (var who in new[] { "manager", "superior", "admin", "a" }) await Send(who, "GET", "/" + mine, null, 200);
        foreach (var who in new[] { "manager", "superior", "admin" }) await Send(who, "POST", "/" + mine + "/start", new { }, 403, "ROLE_FORBIDDEN");
        foreach (var who in new[] { "superior", "admin" }) await Send(who, "POST", "", new { }, 403, "ROLE_FORBIDDEN");
        var list = await Send("a", "GET", "?assigned_to=me", null, 200);
        Assert.Equal(new[] { mine }, list.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("work_order_id").GetString()));
        var empty = await Send("a", "GET", "?assigned_to=" + users["b"].UserId, null, 200);
        Assert.Equal(0, empty.GetProperty("total").GetInt32());
        await Send("a", "GET", "?commune_id=" + foreign, null, 403, "COMMUNE_FORBIDDEN");
        await Send("a", "GET", "/" + mine, null, 200); // after admin: cached filter must not inherit '*'.
        var endpoints = factory.Services.GetService(typeof(Microsoft.AspNetCore.Routing.EndpointDataSource)) as Microsoft.AspNetCore.Routing.EndpointDataSource;
        foreach (var endpoint in endpoints!.Endpoints.OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>().Where(x => x.RoutePattern.RawText!.Contains("work-orders")))
            Assert.Null(endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>());
    }

    [Fact]
    public async Task Eligibility_scope_and_server_owned_fields()
    {
        foreach (var status in Enum.GetValues<FaultStatus>())
        foreach (var kind in new[] { "inspection", "repair" })
        {
            var fault = await Fault(status);
            var accepted = status is FaultStatus.Confirmed or FaultStatus.InProgress || (kind == "inspection" && status == FaultStatus.Detected);
            await Send("manager", "POST", "", new { task_kind = kind, title = "Eligibility", fault_ids = new[] { fault } }, accepted ? 201 : 409,
                accepted ? null : "FAULT_STATUS_NOT_ELIGIBLE");
        }
        var homeFault = await Fault(); var foreignFault = await Fault(commune: foreign);
        await Send("manager", "POST", "", new { task_kind = "inspection", title = "Mixed", fault_ids = new[] { homeFault, foreignFault } }, 409, "CROSS_COMMUNE_REFERENCE");
        await Send("manager", "POST", "", new { task_kind = "inspection", title = "Invalid", fault_ids = new[] { "FAULT-missing" } }, 404, "FAULT_NOT_FOUND");
        await Send("manager", "POST", "", new { task_kind = "inspection", title = "Own field", segment_id = segment, commune_id = home }, 400, "SERVER_OWNED_FIELD");
        // Remove the manager's second commune, then obtain a fresh token for an actual out-of-scope lookup.
        await Db(async db => { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user_commune WHERE user_id = {users["manager"].UserId} AND commune_id = {foreign}"); return 0; });
        var token = await (await clients["manager"].PostLoginAsync(users["manager"].Username, factory.AccountPassword)).ReadTokensAsync();
        clients["manager"].DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
        await Send("manager", "POST", "", new { task_kind = "inspection", title = "Foreign", fault_ids = new[] { foreignFault } }, 404, "FAULT_NOT_FOUND");
    }

    [Fact]
    public async Task Assignee_eligibility_uses_database_and_has_identical_error_details()
    {
        var id = await Create();
        var target = users["b"].UserId;
        string? reference = null;
        for (var reason = 0; reason < 4; reason++)
        {
            await Db(async db =>
            {
                var user = await db.Set<AppUser>().SingleAsync(x => x.UserId == target);
                user.Role = reason == 0 ? UserRole.Superior : UserRole.FieldEngineer;
                user.IsLocked = reason == 1;
                if (reason == 2) await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user_commune WHERE user_id = {target} AND commune_id = {home}");
                if (reason == 3) db.Remove(user);
                await db.SaveChangesAsync(); return 0;
            });
            var error = await Send("manager", "PUT", "/" + id + "/assignee", new { assigned_to = target }, 409, "ASSIGNEE_NOT_ELIGIBLE");
            var details = error.GetProperty("error").GetProperty("details");
            Assert.Equal(target, details.GetProperty("assigned_to").GetString());
            var keys = string.Join(",", details.EnumerateObject().Select(x => x.Name).Order());
            reference ??= keys; Assert.Equal(reference, keys);
        }
        await Send("manager", "PUT", "/" + id + "/assignee", new { }, 400, "VALIDATION_FAILED");
        var choices = await Send("manager", "GET", "/assignees?commune_id=" + home, null, 200);
        Assert.Equal(new[] { users["a"].UserId }, choices.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("user_id").GetString()));
        var running = await Plant(WorkOrderStatus.InProgress);
        await Send("manager", "PUT", "/" + running + "/assignee", new { assigned_to = users["a"].UserId }, 200, auditExpected: 0);
        // A still-valid JWT does not make a removed DB assignment eligible for re-assignment.
        await Db(async db => { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user_commune WHERE user_id = {users["a"].UserId} AND commune_id = {home}"); return 0; });
        await Send("manager", "PUT", "/" + id + "/assignee", new { assigned_to = users["a"].UserId }, 409, "ASSIGNEE_NOT_ELIGIBLE");
        var detail = await Send("manager", "GET", "/" + running, null, 200);
        Assert.False(detail.GetProperty("assignee_eligible").GetBoolean());
    }

    [Theory]
    [InlineData("superior")]
    [InlineData("locked")]
    [InlineData("other_commune")]
    [InlineData("missing")]
    public async Task Create_rejects_ineligible_assignee_without_order_or_audit(string reason)
    {
        var existing = await Plant(WorkOrderStatus.Open);
        var target = users["b"].UserId;
        await Db(async db =>
        {
            var user = await db.Set<AppUser>().SingleAsync(x => x.UserId == target);
            switch (reason)
            {
                case "superior": user.Role = UserRole.Superior; break;
                case "locked": user.IsLocked = true; break;
                case "other_commune":
                    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user_commune WHERE user_id = {target} AND commune_id = {home}");
                    db.Add(new AppUserCommune { UserId = target, CommuneId = foreign });
                    break;
                case "missing": db.Remove(user); break;
            }
            await db.SaveChangesAsync(); return 0;
        });
        // Error details include the request correlation ID; keep it identical for this comparison.
        var correlation = Guid.NewGuid().ToString();
        var put = await Send("manager", "PUT", "/" + existing + "/assignee",
            new { assigned_to = target }, 409, "ASSIGNEE_NOT_ELIGIBLE", correlation: correlation);
        var post = await Send("manager", "POST", "", new
        {
            task_kind = "inspection", title = "Rejected assignment", segment_id = segment, assigned_to = target,
        }, 409, "ASSIGNEE_NOT_ELIGIBLE", correlation: correlation);
        Assert.Equal(put.GetProperty("error").GetProperty("details").GetRawText(),
            post.GetProperty("error").GetProperty("details").GetRawText());
        var orders = await Db(db => db.Set<WorkOrder>().IgnoreQueryFilters()
            .Where(x => x.CommuneId == home || x.CommuneId == foreign).Select(x => x.WorkOrderId).ToListAsync());
        Assert.Equal(new[] { existing }, orders);
        Assert.Empty(await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters()
            .Where(x => x.CommuneId == home || x.CommuneId == foreign).ToListAsync()));
    }

    [Fact]
    public async Task Repair_propagation_and_inspection_outcomes_and_release()
    {
        var fault = await Fault(FaultStatus.Confirmed);
        var id = await Create("repair", [fault], users["a"].UserId);
        await Send("a", "POST", "/" + id + "/start", new { }, 200);
        await Send("a", "POST", "/" + id + "/complete", new { report_note = "The repair is complete" }, 200);
        Assert.Equal(FaultStatus.InProgress, await Db(db => db.Set<Fault>().IgnoreQueryFilters().Where(x => x.FaultId == fault).Select(x => x.FaultStatus).SingleAsync()));
        await Send("manager", "POST", "/" + id + "/return", new { note = "Please check once more" }, 200);
        var completed = await Send("a", "POST", "/" + id + "/complete", new { report_note = "Second check is complete" }, 200);
        await Send("manager", "POST", "/" + id + "/verify", new { }, 200);
        var saved = await Db(db => db.Set<Fault>().IgnoreQueryFilters().SingleAsync(x => x.FaultId == fault));
        Assert.Equal(FaultStatus.Verified, saved.FaultStatus);
        Assert.Equal(users["a"].UserId, saved.ResolvedBy);
        Assert.Equal(completed.GetProperty("completed_at").GetDateTime(), saved.ResolvedAt);
        var inspectFault = await Fault();
        var inspect = await Create("inspection", [inspectFault], users["a"].UserId);
        await Send("a", "POST", "/" + inspect + "/start", new { }, 200);
        await Send("a", "POST", "/" + inspect + "/complete", new { report_note = "The lamp is present" }, 400, "VALIDATION_FAILED");
        var done = await Send("a", "POST", "/" + inspect + "/complete", new { report_note = "The fault is present", fault_outcomes = new[] { new { fault_id = inspectFault, outcome = "fault_present" } } }, 200);
        Assert.Equal("fault_present", done.GetProperty("faults")[0].GetProperty("inspection_outcome").GetString());
        await Send("manager", "POST", "/" + inspect + "/verify", new { }, 200);
        Assert.Equal(FaultStatus.Detected, await Db(db => db.Set<Fault>().IgnoreQueryFilters().Where(x => x.FaultId == inspectFault).Select(x => x.FaultStatus).SingleAsync()));
        await Create("inspection", [inspectFault]);
        var cancelledFault = await Fault(FaultStatus.InProgress);
        var cancelled = await Create("repair", [cancelledFault], users["a"].UserId);
        await Send("manager", "POST", "/" + cancelled + "/cancel", new { note = "Hand over the repair" }, 200);
        await Create("repair", [cancelledFault]);
    }

    [Fact]
    public async Task Patch_null_noop_audit_content_and_reassignment()
    {
        var id = await Create(assigned: users["a"].UserId);
        var created = await Send("manager", "GET", "/" + id, null, 200);
        var count = await AuditCount(id);
        var noop = await Send("manager", "PATCH", "/" + id, new { title = "Work order test" }, 200, auditExpected: 0);
        Assert.Equal(created.GetProperty("updated_at").GetString(), noop.GetProperty("updated_at").GetString());
        await Send("manager", "PUT", "/" + id + "/assignee", new { assigned_to = users["a"].UserId }, 200, auditExpected: 0);
        Assert.Equal(count, await AuditCount(id));
        await Send("manager", "PATCH", "/" + id, new { due_date = "2026-12-02", scheduled_date = "2026-12-01" }, 200);
        var kept = await Send("manager", "PATCH", "/" + id, new { title = "Changed" }, 200);
        Assert.Equal("2026-12-02", kept.GetProperty("due_date").GetString());
        var cleared = await Send("manager", "PATCH", "/" + id, new { due_date = (string?)null }, 200);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("due_date").ValueKind);
        await Send("manager", "PATCH", "/" + id, new { title = (string?)null }, 400);
        await Send("manager", "PATCH", "/" + id, new { }, 400);
        await Send("manager", "PATCH", "/" + id, new { wo_status = "done" }, 400);
        await Send("a", "POST", "/" + id + "/start", new { }, 200);
        var reassigned = await Send("manager", "PUT", "/" + id + "/assignee", new { assigned_to = users["b"].UserId }, 200);
        Assert.Equal("assigned", reassigned.GetProperty("wo_status").GetString());
        await Send("a", "GET", "/" + id, null, 404, "WORK_ORDER_NOT_FOUND");
        var audit = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().Where(x => x.EntityId == id).OrderBy(x => x.AuditId).ToListAsync());
        Assert.Equal(new[] { AuditAction.Created, AuditAction.DetailsChanged, AuditAction.DetailsChanged, AuditAction.DetailsChanged, AuditAction.Started, AuditAction.Reassigned }, audit.Select(x => x.Action));
        Assert.All(audit, x => { Assert.Equal(AuditActorKind.User, x.ActorKind); Assert.Equal(home, x.CommuneId); Assert.NotEmpty(x.CorrelationId); Assert.DoesNotContain("password", x.AfterState!, StringComparison.OrdinalIgnoreCase); });
        Assert.Equal(users["manager"].UserId, audit[0].ActorUserId);
        Assert.Equal(UserRole.Manager, audit[0].ActorRole);
        Assert.Contains("Work order test", audit[0].AfterState!);
        Assert.NotNull(audit[^1].BeforeState);
    }

    /// <summary>
    /// Drift FR-3: the Manager's plan and the engineer's report are two fields, so completing the
    /// work order never overwrites what the Manager wrote.
    /// </summary>
    [Fact]
    public async Task Materials_plan_and_materials_used_are_kept_apart()
    {
        var fault = await Fault(FaultStatus.Confirmed);
        var created = await Send("manager", "POST", "", new { task_kind = "repair", title = "Materials test",
            fault_ids = new[] { fault }, assigned_to = users["a"].UserId, materials_note = "  2 LED 100W, 1 driver  " }, 201);
        var id = created.GetProperty("work_order_id").GetString()!;
        Assert.Equal("2 LED 100W, 1 driver", created.GetProperty("materials_note").GetString());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("materials_used").ValueKind);

        await Send("manager", "PATCH", "/" + id, new { materials_note = "2 LED 100W, 1 driver" }, 200, auditExpected: 0);
        var kept = await Send("manager", "PATCH", "/" + id, new { title = "Materials test renamed" }, 200);
        Assert.Equal("2 LED 100W, 1 driver", kept.GetProperty("materials_note").GetString());
        var changed = await Send("manager", "PATCH", "/" + id, new { materials_note = "1 LED 100W" }, 200);
        Assert.Equal("1 LED 100W", changed.GetProperty("materials_note").GetString());
        var cleared = await Send("manager", "PATCH", "/" + id, new { materials_note = (string?)null }, 200);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("materials_note").ValueKind);
        var restored = await Send("manager", "PATCH", "/" + id, new { materials_note = "1 LED 100W" }, 200);
        Assert.Equal("1 LED 100W", restored.GetProperty("materials_note").GetString());
        await Send("manager", "PATCH", "/" + id, new { materials_note = 5 }, 400, "VALIDATION_FAILED");

        await Send("a", "POST", "/" + id + "/start", new { }, 200);
        var done = await Send("a", "POST", "/" + id + "/complete",
            new { report_note = "Replaced the lamp head", materials_used = " 1 LED 100W, 2 m cable " }, 200);
        Assert.Equal("1 LED 100W, 2 m cable", done.GetProperty("materials_used").GetString());
        Assert.Equal("1 LED 100W", done.GetProperty("materials_note").GetString());

        var completed = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters()
            .SingleAsync(x => x.EntityId == id && x.Action == AuditAction.Completed));
        Assert.Contains("1 LED 100W, 2 m cable", completed.AfterState!);

        await Send("manager", "POST", "/" + id + "/return", new { note = "Please check the cable" }, 200);
        var again = await Send("a", "POST", "/" + id + "/complete", new { report_note = "Cable checked, all good", materials_used = "   " }, 200);
        Assert.Equal(JsonValueKind.Null, again.GetProperty("materials_used").ValueKind);
        Assert.Equal("1 LED 100W", again.GetProperty("materials_note").GetString());
    }

    [Theory]
    [InlineData("materials_note", "ck_work_order_materials_note_not_blank")]
    [InlineData("materials_used", "ck_work_order_materials_used_not_blank")]
    public async Task The_table_refuses_blank_materials_text(string column, string constraint)
    {
        var id = await Plant(WorkOrderStatus.Open);
        var sql = $"UPDATE work_order SET {column} = '  ' WHERE work_order_id = {{0}}";
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => Db(db => db.Database.ExecuteSqlRawAsync(sql, id)));
        Assert.Equal("23514", error.SqlState);
        Assert.Equal(constraint, error.ConstraintName);
    }

    private Task<int> AuditCount(string id) => Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(x => x.EntityId == id));

    [Fact]
    public async Task Link_race_rolls_back_losing_order_and_audit()
    {
        var fault = await Fault();
        var barrier = new SaveBarrier(fault, create: true);
        await using var raceFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<LuxMapDbContext>((_, options) => options.AddInterceptors(barrier))));
        using var client = raceFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = clients["manager"].DefaultRequestHeaders.Authorization;
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(Route,
            new { task_kind = "inspection", title = "Race", fault_ids = new[] { fault } })));
        Assert.Equal(new[] { 201, 409 }, responses.Select(x => (int)x.StatusCode).Order());
        var loser = responses.Single(x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Contains("FAULT_ALREADY_IN_WORK_ORDER", await loser.Content.ReadAsStringAsync());
        Assert.Equal(1, await Db(db => db.Set<WorkOrder>().IgnoreQueryFilters().CountAsync(x => x.CommuneId == home)));
        Assert.Equal(1, await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(x => x.CommuneId == home)));
    }

    [Fact]
    public async Task Schedule_api_database_and_inclusive_filter()
    {
        var id = await Create();
        await Send("manager", "PATCH", "/" + id, new { scheduled_date = "2026-12-02", due_date = "2026-12-01" }, 400);
        await Send("manager", "PATCH", "/" + id, new { scheduled_date = "2026-12-02", due_date = "2026-12-03" }, 200);
        var result = await Send("manager", "GET", "?scheduled_from=2026-12-02&scheduled_to=2026-12-02", null, 200);
        Assert.Equal(1, result.GetProperty("total").GetInt32());
        await Db(async db =>
        {
            var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE work_order SET scheduled_date = DATE '2026-12-04' WHERE work_order_id = {id}"));
            Assert.Equal("23514", error.SqlState); return 0;
        });
    }

    [Fact]
    public async Task Listing_orders_numeric_ids_and_parses_snake_case()
    {
        var now = DateTime.UtcNow;
        // The width boundary is fixed; check LIVE availability before using it. No sequence rewind.
        await Db(async db =>
        {
            Assert.False(await db.Set<WorkOrder>().IgnoreQueryFilters().AnyAsync(x => x.WorkOrderId == "WO-9999" || x.WorkOrderId == "WO-10000"));
            await using var tx = await db.Database.BeginTransactionAsync();
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            foreach (var id in new[] { "WO-9999", "WO-10000" }) db.Add(new WorkOrder { WorkOrderId = id, CommuneId = home,
                Title = "Width boundary", CreatedBy = users["manager"].UserId, WoStatus = WorkOrderStatus.Open,
                TaskKind = TaskKind.Inspection, SegmentId = segment, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync(); await tx.CommitAsync(); return 0;
        });
        var list = await Send("manager", "GET", "?commune_id=" + home, null, 200);
        Assert.Equal(new[] { "WO-10000", "WO-9999" }, list.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("work_order_id").GetString()));
        var running = await Plant(WorkOrderStatus.InProgress);
        var filtered = await Send("manager", "GET", "?wo_status=in_progress&task_kind=inspection", null, 200);
        Assert.Equal(new[] { running }, filtered.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("work_order_id").GetString()));
    }

    [Fact]
    public async Task Derived_fields_use_highest_priority_and_live_max()
    {
        var first = await Fault(priority: 1); var second = await Fault(road: foreignSegment, priority: 9);
        var id = await Create(faults: [first, second]);
        var detail = await Send("manager", "GET", "/" + id, null, 200);
        Assert.Equal(foreignSegment, detail.GetProperty("segment_id").GetString());
        Assert.Equal(9, detail.GetProperty("priority_score").GetDouble());
        await Db(async db => { using var system = db.EnterUnscopedSystemWriteBackdoor(); var f = await db.Set<Fault>().IgnoreQueryFilters().SingleAsync(x => x.FaultId == first); f.PriorityScore = 20; await db.SaveChangesAsync(); return 0; });
        detail = await Send("manager", "GET", "/" + id, null, 200);
        Assert.Equal(20, detail.GetProperty("priority_score").GetDouble());
        Assert.Equal(foreignSegment, detail.GetProperty("segment_id").GetString());
    }
    [Fact]
    public async Task Audit_required_on_real_work_order_and_link_and_backdoor_is_explicit()
    {
        var id = await Create();
        foreach (var count in new[] { 0, 2 })
        await Db(async db =>
        {
            var wo = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(x => x.WorkOrderId == id);
            wo.Title = "Changed without one audit";
            for (var i = 0; i < count; i++) db.Add(new AuditEvent { CommuneId = home, EntityId = id, CorrelationId = "guard", AfterState = "{}" });
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            Assert.Contains("exactly one", error.Message);
            return 0;
        });
        var fault = await Fault();
        await Db(async db =>
        {
            db.Add(new WorkOrderFault { WorkOrderId = id, CommuneId = home, FaultId = fault, LinkedAt = DateTime.UtcNow });
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            using var backdoor = db.EnterUnscopedSystemWriteBackdoor();
            await db.SaveChangesAsync(); return 0;
        });
    }

    [Fact]
    public async Task Partial_unique_index_rejects_active_duplicates_but_preserves_released_history()
    {
        var fault = await Fault();
        var first = await Create(faults: [fault]);
        var second = await Create();
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            db.Add(new WorkOrderFault { WorkOrderId = second, FaultId = fault, CommuneId = home, LinkedAt = DateTime.UtcNow });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal("ux_work_order_fault_fault_id_active", Assert.IsType<Npgsql.PostgresException>(error.InnerException).ConstraintName);
            return 0;
        });
        await Send("manager", "POST", "/" + first + "/cancel", new { note = "Release the fault" }, 200);
        await Create(faults: [fault]);
        Assert.Equal(2, await Db(db => db.Set<WorkOrderFault>().IgnoreQueryFilters().CountAsync(x => x.FaultId == fault)));
    }

    [Fact]
    public async Task Concurrency_verify_return_has_one_success_one_conflict_and_one_audit()
    {
        var id = await Plant(WorkOrderStatus.Done);
        var barrier = new SaveBarrier(id);
        await using var raceFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<LuxMapDbContext>((_, options) => options.AddInterceptors(barrier))));
        using var client = raceFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = clients["manager"].DefaultRequestHeaders.Authorization;
        var responses = await Task.WhenAll(client.PostAsJsonAsync(Route + "/" + id + "/verify", new { note = "Accepted" }),
            client.PostAsJsonAsync(Route + "/" + id + "/return", new { note = "Check again" }));
        Assert.Equal(new[] { 200, 409 }, responses.Select(x => (int)x.StatusCode).Order());
        Assert.Contains("CONCURRENT_MODIFICATION", await responses.Single(x => x.StatusCode == HttpStatusCode.Conflict).Content.ReadAsStringAsync());
        Assert.Equal(1, await AuditCount(id));
        Assert.Equal(2, barrier.Arrivals);
    }

    [Fact]
    public async Task Fault_xmin_rejects_a_stale_write()
    {
        var id = await Fault();
        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        using var firstSystem = first.EnterUnscopedSystemWriteBackdoor();
        using var secondSystem = second.EnterUnscopedSystemWriteBackdoor();
        var a = await first.Set<Fault>().IgnoreQueryFilters().SingleAsync(x => x.FaultId == id);
        var b = await second.Set<Fault>().IgnoreQueryFilters().SingleAsync(x => x.FaultId == id);
        a.Note = "First decision"; b.Note = "Stale decision";
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    private sealed class SaveBarrier(string id, bool create = false) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        public int Arrivals => arrivals;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (create ? data.Context!.ChangeTracker.Entries<WorkOrderFault>().Any(x => x.Entity.FaultId == id && x.State == EntityState.Added)
                : data.Context!.ChangeTracker.Entries<WorkOrder>().Any(x => x.Entity.WorkOrderId == id && x.State == EntityState.Modified))
            {
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }

}
