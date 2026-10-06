using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.WorkOrders;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LuxMap.Shared.Storage;
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
                    PasswordHash = "", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", PasswordSetAt = DateTime.UtcNow, Role = role, HasSystemWideScope = role == UserRole.SystemAdmin,
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
            foreach (var table in new[] { "notification", "work_order_fault", "work_order_segment", "repair_evidence", "work_order", "fault", "fault_cluster", "fixture", "pole", "road_segment" })
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
        foreach (var client in photoClients.Values) client.Dispose();
        if (photoHost is not null) await photoHost.DisposeAsync();
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

    [Fact]
    public async Task An_unknown_enum_name_is_a_400_that_names_no_internal_type()
    {
        using var body = JsonDocument.Parse("""{"task_kind":"bogus","title":"Invalid enum"}""");
        var json = await Send("manager", "POST", "", body.RootElement, 400, "VALIDATION_FAILED");
        Assert.Equal("The value is not valid for this field.", json.GetProperty("error").GetProperty("details").GetProperty("$.task_kind")[0].GetString());
        Assert.DoesNotContain("LuxMap.", json.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("999")]
    [InlineData("\"1\"")]
    [InlineData("\"999\"")]
    public async Task Numeric_task_kind_is_a_validation_error_at_json_binding(string value)
    {
        // A real nullable enum body property. PATCH /faults uses JsonElement plus its own
        // validation, so it would not prove that the global converter rejects integers.
        using var body = JsonDocument.Parse($$"""{"task_kind":{{value}},"title":"Invalid enum"}""");
        var correlation = Guid.NewGuid().ToString();
        var json = await Send("manager", "POST", "", body.RootElement, 400, "VALIDATION_FAILED", correlation: correlation);

        Assert.Equal(["error"], json.EnumerateObject().Select(x => x.Name).ToArray());
        var error = json.GetProperty("error");
        Assert.Equal(["code", "details", "message"], error.EnumerateObject().Select(x => x.Name).Order().ToArray());
        Assert.Equal("The submitted payload is invalid.", error.GetProperty("message").GetString());
        var details = error.GetProperty("details");
        Assert.Equal(correlation, details.GetProperty("correlation_id").GetString());
        Assert.Equal(JsonValueKind.Array, details.GetProperty("$.task_kind").ValueKind);
        Assert.DoesNotContain("System.", json.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("LuxMap.", json.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("stack", json.GetRawText(), StringComparison.OrdinalIgnoreCase);
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
        await PlantAfterPhoto(id);
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
        // Drift FR-2a (C): verifying an inspection confirms what the engineer found present.
        Assert.Equal(FaultStatus.Confirmed, await Db(db => db.Set<Fault>().IgnoreQueryFilters().Where(x => x.FaultId == inspectFault).Select(x => x.FaultStatus).SingleAsync()));
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
        await PlantAfterPhoto(id);
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

    /// <summary>Drift FR-2 + FR-2a: inspection → verified → follow-up repair, one case_id for the chain.</summary>
    [Fact]
    public async Task A_verified_inspection_confirms_present_faults_and_follows_up_into_a_repair_on_one_case()
    {
        var (present, absent, unsure) = (await Fault(), await Fault(), await Fault());
        var inspect = await VerifiedInspectionAsync((present, "fault_present"), (absent, "fault_absent"), (unsure, "inconclusive"));

        var statuses = await Db(db => db.Set<Fault>().IgnoreQueryFilters()
            .Where(x => x.FaultId == present || x.FaultId == absent || x.FaultId == unsure)
            .ToDictionaryAsync(x => x.FaultId, x => new { x.FaultStatus, x.ConfirmedBy }));
        Assert.Equal(FaultStatus.Confirmed, statuses[present].FaultStatus);
        Assert.Equal(users["manager"].UserId, statuses[present].ConfirmedBy);
        Assert.Equal(FaultStatus.Detected, statuses[absent].FaultStatus);
        Assert.Equal(FaultStatus.Detected, statuses[unsure].FaultStatus);
        var verified = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters()
            .SingleAsync(x => x.EntityId == inspect && x.Action == AuditAction.Verified));
        Assert.Contains(present, verified.AfterState!);

        var parent = await Send("manager", "GET", "/" + inspect, null, 200);
        Assert.Contains("follow_up", parent.GetProperty("allowed_actions").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(inspect, parent.GetProperty("case_id").GetString());
        Assert.Equal(JsonValueKind.Null, parent.GetProperty("parent_work_order_id").ValueKind);

        var repair = await Send("manager", "POST", "/" + inspect + "/follow-up",
            new { task_kind = "repair", assigned_to = users["a"].UserId, note = "Go and fix it" }, 201);
        var repairId = repair.GetProperty("work_order_id").GetString()!;
        await PlantAfterPhoto(repairId);
        Assert.Equal("repair", repair.GetProperty("task_kind").GetString());
        Assert.Equal(inspect, repair.GetProperty("parent_work_order_id").GetString());
        Assert.Equal(inspect, repair.GetProperty("case_id").GetString());
        Assert.Equal(parent.GetProperty("title").GetString(), repair.GetProperty("title").GetString());
        Assert.Equal([present], repair.GetProperty("fault_ids").EnumerateArray().Select(x => x.GetString()!).ToArray());

        var chain = await Send("manager", "GET", $"?case_id={inspect}", null, 200);
        Assert.Equal(
            new[] { inspect, repairId }.Order(),
            chain.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("work_order_id").GetString()!).Order());

        await Send("a", "POST", "/" + repairId + "/start", new { }, 200);
        await Send("a", "POST", "/" + repairId + "/complete", new { report_note = "Lamp head replaced" }, 200);
        await Send("manager", "POST", "/" + repairId + "/verify", new { }, 200);
        await Send("manager", "POST", "/" + repairId + "/follow-up", new { task_kind = "repair" }, 409, "INVALID_STATE_TRANSITION");
    }

    [Fact]
    public async Task Follow_up_is_refused_until_there_is_a_verified_parent_with_something_to_carry()
    {
        var fault = await Fault();
        var open = await Create("inspection", [fault], users["a"].UserId);
        await Send("manager", "POST", "/" + open + "/follow-up", new { task_kind = "repair" }, 409, "INVALID_STATE_TRANSITION");
        await Send("a", "POST", "/" + open + "/follow-up", new { task_kind = "repair" }, 403, "ROLE_FORBIDDEN");

        var nothing = await VerifiedInspectionAsync((await Fault(), "fault_absent"));
        await Send("manager", "POST", "/" + nothing + "/follow-up", new { task_kind = "repair" }, 409, "NOTHING_TO_FOLLOW_UP");

        var (one, two) = (await Fault(), await Fault());
        var parent = await VerifiedInspectionAsync((one, "fault_present"), (two, "fault_present"));
        await Send("manager", "POST", "/" + parent + "/follow-up", new { task_kind = "inspection" }, 409, "INVALID_STATE_TRANSITION");
        await Send("manager", "POST", "/" + parent + "/follow-up", new { task_kind = "repair", fault_ids = new[] { fault } }, 400, "VALIDATION_FAILED");

        // Splitting the next step: two repairs from one inspection, each with its own share, one case.
        var first = await Send("manager", "POST", "/" + parent + "/follow-up", new { task_kind = "repair", fault_ids = new[] { one } }, 201);
        var second = await Send("manager", "POST", "/" + parent + "/follow-up", new { task_kind = "repair", fault_ids = new[] { two } }, 201);
        Assert.Equal(parent, first.GetProperty("case_id").GetString());
        Assert.Equal(parent, second.GetProperty("case_id").GetString());
        await Send("manager", "POST", "/" + parent + "/follow-up", new { task_kind = "repair", fault_ids = new[] { one } }, 409, "FAULT_ALREADY_IN_WORK_ORDER");
    }

    [Fact]
    public async Task The_table_refuses_a_half_linked_chain_and_a_parent_from_another_commune()
    {
        var root = await Plant(WorkOrderStatus.Verified);
        var child = await Plant(WorkOrderStatus.Open);
        var half = "UPDATE work_order SET parent_work_order_id = {0} WHERE work_order_id = {1}";
        var incomplete = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => Db(db => db.Database.ExecuteSqlRawAsync(half, root, child)));
        Assert.Equal("ck_work_order_chain_complete", incomplete.ConstraintName);

        var foreignRoot = await Plant(WorkOrderStatus.Verified, commune: foreign);
        var cross = "UPDATE work_order SET parent_work_order_id = {0}, root_work_order_id = {0} WHERE work_order_id = {1}";
        var refused = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => Db(db => db.Database.ExecuteSqlRawAsync(cross, foreignRoot, child)));
        Assert.Equal("23503", refused.SqlState);
    }

    /// <summary>Creates an inspection over the faults, runs it to verified with the given outcomes.</summary>
    private async Task<string> VerifiedInspectionAsync(params (string Fault, string Outcome)[] outcomes)
    {
        var id = await Create("inspection", outcomes.Select(x => x.Fault).ToArray(), users["a"].UserId);
        await Send("a", "POST", "/" + id + "/start", new { }, 200);
        await Send("a", "POST", "/" + id + "/complete", new { report_note = "Inspection finished",
            fault_outcomes = outcomes.Select(x => new { fault_id = x.Fault, outcome = x.Outcome }).ToArray() }, 200);
        await Send("manager", "POST", "/" + id + "/verify", new { }, 200);
        return id;
    }

    private Task<int> AuditCount(string id) => Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(x => x.EntityId == id));

    /// <remarks>
    /// Since BE-19 creation locks the fault rows (FaultLocks), so the second request waits and then
    /// meets the first one's link in its own pre-check — the unique index stays as the backstop.
    /// </remarks>
    [Fact]
    public async Task Concurrent_creates_on_one_fault_serialize_and_the_loser_leaves_nothing()
    {
        var fault = await Fault();
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => clients["manager"].PostAsJsonAsync(Route,
            new { task_kind = "inspection", title = "Race", fault_ids = new[] { fault } })));
        Assert.Equal(new[] { 201, 409 }, responses.Select(x => (int)x.StatusCode).Order());
        var loser = responses.Single(x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Contains("FAULT_ALREADY_IN_WORK_ORDER", await loser.Content.ReadAsStringAsync());
        Assert.Equal(1, await Db(db => db.Set<WorkOrder>().IgnoreQueryFilters().CountAsync(x => x.CommuneId == home)));
        Assert.Equal(1, await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(x => x.CommuneId == home)));
    }

    /// <summary>
    /// The race Codex found (BE-19): a fault rejected while a work order is being created over it.
    /// Holding the row lock from outside, rejecting it, then releasing must make the waiting create
    /// re-read the fault and refuse it — not insert a link to a rejected fault.
    /// </summary>
    [Fact]
    public async Task Creation_waits_for_the_fault_lock_and_rereads_the_status_it_validates()
    {
        var fault = await Fault();
        await using var scope = factory.Services.CreateAsyncScope();
        var holder = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        await using var transaction = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlRawAsync("SELECT 1 FROM fault WHERE fault_id = {0} FOR UPDATE", fault);

        var create = clients["manager"].PostAsJsonAsync(Route, new { task_kind = "inspection", title = "Blocked", fault_ids = new[] { fault } });
        await Task.Delay(500);
        Assert.False(create.IsCompleted, "Creation should be waiting on the fault row lock.");

        await holder.Database.ExecuteSqlRawAsync("UPDATE fault SET fault_status = 'rejected' WHERE fault_id = {0}", fault);
        await transaction.CommitAsync();

        var response = await create;
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("FAULT_STATUS_NOT_ELIGIBLE", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await Db(db => db.Set<WorkOrderFault>().IgnoreQueryFilters().CountAsync(x => x.FaultId == fault)));
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

    // ---- GET /work-orders/{id}/poles (drift WO-12) --------------------------------------------------

    /// <summary>A pole at <paramref name="along"/> (0..1) of a road starting <paramref name="origin"/> degrees off the test road, so road order differs from insert order.</summary>
    private Task<string> PlantPole(double along, string? road = null, string? commune = null,
        FixtureStatus? status = null, double? confidence = null, bool lamp = false, bool sensitive = false, double origin = 0)
        => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var owner = commune ?? home;
            var pole = new Pole { CommuneId = owner, SegmentId = road ?? segment, DataSource = DataSource.Simulated,
                NearSensitivePoi = sensitive, Geom = new Point(108 + origin + 0.01 * along, 16 + origin + 0.01 * along) { SRID = 4326 } };
            db.Add(pole); await db.SaveChangesAsync();
            if (status is { } known)
                db.Add(new PoleCurrentStatus { PoleId = pole.PoleId, CommuneId = owner, FixtureStatus = known, StatusConfidence = confidence,
                    LastSeenAt = known == FixtureStatus.Unknown ? null : new DateTime(2026, 10, 3, 14, 0, 0, DateTimeKind.Utc), UpdatedAt = DateTime.UtcNow });
            if (lamp)
                db.Add(new Fixture { PoleId = pole.PoleId, CommuneId = owner, FixtureType = FixtureType.LedRoadLamp, PowerSource = PowerSource.Grid,
                    LampWatt = 90, InstallDate = new DateOnly(2025, 1, 1), DataSource = DataSource.Simulated });
            await db.SaveChangesAsync();
            return pole.PoleId;
        });

    private Task<string> PlantPoleFault(string poleId, FaultStatus status, string? reportedOn = null)
        => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var pole = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(x => x.PoleId == poleId);
            var fault = new Fault { CommuneId = pole.CommuneId, SegmentId = reportedOn ?? pole.SegmentId, PoleId = poleId, Lat = 16, Lng = 108,
                FaultStatus = status, FaultType = FaultType.LampOut, Severity = Severity.Medium, SourceChannel = SourceChannel.Cv,
                DataSource = DataSource.Simulated, DetectedAt = DateTime.UtcNow };
            db.Add(fault); await db.SaveChangesAsync(); return fault.FaultId;
        });

    private async Task<JsonElement> PolesOf(string id, string who = "manager", string query = "", int expected = 200, string? error = null)
        => await Send(who, "GET", $"/{id}/poles{query}", null, expected, error);

    [Fact]
    public async Task An_orders_poles_come_in_road_order_with_the_status_the_last_survey_left_before_any_visit()
    {
        // Planted OUT of road order, so the answer cannot be the insert order or the id order.
        var far = await PlantPole(0.8, status: FixtureStatus.Out, confidence: 0.9, lamp: true, sensitive: true);
        var near = await PlantPole(0.2, status: FixtureStatus.Dim, confidence: 0.8);
        var middle = await PlantPole(0.5); // never covered by a sweep
        await PlantPoleFault(near, FaultStatus.Confirmed);
        await PlantPoleFault(near, FaultStatus.Resolved); // closed: not counted
        // POLE-NOTE: what the engineer reads before going out.
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var pole = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == far);
            pole.Note = "Trước cổng chợ";
            return await db.SaveChangesAsync();
        });
        var id = await Create();

        var body = await PolesOf(id);

        Assert.Equal(3, body.GetProperty("total").GetInt32());
        var items = body.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal([near, middle, far], items.Select(x => x.GetProperty("pole_id").GetString()!));
        Assert.Equal([1, 2, 3], items.Select(x => x.GetProperty("position").GetInt32()));
        Assert.All(items, x => Assert.Equal(segment, x.GetProperty("segment_id").GetString()));

        Assert.Equal(["dim", "unknown", "out"], items.Select(x => x.GetProperty("fixture_status").GetString()!));
        Assert.Equal(0.8, items[0].GetProperty("status_confidence").GetDouble(), 6);
        Assert.Equal("2026-10-03T14:00:00Z", items[0].GetProperty("last_seen_at").GetString());
        // unknown means no published sweep: no confidence, no time.
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("status_confidence").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("last_seen_at").ValueKind);

        Assert.Equal([1, 0, 0], items.Select(x => x.GetProperty("open_fault_count").GetInt32()));
        Assert.Equal([false, false, true], items.Select(x => x.GetProperty("near_sensitive_poi").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("lamp_watt").ValueKind);
        Assert.Equal(90, items[2].GetProperty("lamp_watt").GetInt32());
        Assert.Equal("led_road_lamp", items[2].GetProperty("fixture_type").GetString());
        Assert.Equal(16.008, items[2].GetProperty("location").GetProperty("lat").GetDouble(), 6);

        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("note").ValueKind);
        Assert.Equal("Trước cổng chợ", items[2].GetProperty("note").GetString());
    }

    [Fact]
    public async Task A_survey_order_lists_its_segments_in_order_then_along_each_road()
    {
        var other = await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var road = new RoadSegment { CommuneId = home, SegmentName = "WO second road", RoadClass = RoadClass.InterVillage,
                DataSource = DataSource.Simulated, LengthM = 100, Geom = new LineString([new Coordinate(108.1, 16.1), new Coordinate(108.11, 16.11)]) { SRID = 4326 } };
            db.Add(road); await db.SaveChangesAsync(); return road.SegmentId;
        });
        var onFirst = await PlantPole(0.4);
        var onOther = await PlantPole(0.1, road: other, origin: 0.1);
        var response = await Send("manager", "POST", "", new { task_kind = "survey", title = "Survey the poles", commune_id = home,
            segment_ids = new[] { other, segment }, assigned_to = users["a"].UserId }, 201);
        var id = response.GetProperty("work_order_id").GetString()!;

        // The engineer the order is assigned to sees it too — this is what they open on the phone.
        var body = await PolesOf(id, who: "a");

        var items = body.GetProperty("items").EnumerateArray().ToArray();
        // `other` was listed first on the order, so its pole comes first even though it was planted second.
        Assert.Equal([onOther, onFirst], items.Select(x => x.GetProperty("pole_id").GetString()!));
        Assert.Equal([1, 1], items.Select(x => x.GetProperty("position").GetInt32())); // position restarts per segment
    }

    [Fact]
    public async Task A_repair_created_from_faults_lists_the_poles_carrying_them_with_the_fault_ids()
    {
        var offRoad = await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var road = new RoadSegment { CommuneId = home, SegmentName = "WO fault road", RoadClass = RoadClass.InterVillage,
                DataSource = DataSource.Simulated, LengthM = 100, Geom = new LineString([new Coordinate(108.2, 16.2), new Coordinate(108.21, 16.21)]) { SRID = 4326 } };
            db.Add(road); await db.SaveChangesAsync(); return road.SegmentId;
        });
        // Planted in REVERSE road order on a road the order does not list: only the faults tie these poles to it.
        var beyond = await PlantPole(0.7, road: offRoad, origin: 0.2);
        var pole = await PlantPole(0.3, road: offRoad, status: FixtureStatus.Out, confidence: 0.9, origin: 0.2);
        var fault = await PlantPoleFault(pole, FaultStatus.Confirmed, reportedOn: segment);
        var beyondFault = await PlantPoleFault(beyond, FaultStatus.Confirmed, reportedOn: segment);
        var response = await Send("manager", "POST", "", new { task_kind = "repair", title = "Repair those poles", fault_ids = new[] { fault, beyondFault } }, 201);
        var id = response.GetProperty("work_order_id").GetString()!;

        var items = (await PolesOf(id)).GetProperty("items").EnumerateArray().ToArray();

        // Road order on the fault-only road too, not insert order or id order.
        Assert.Equal([pole, beyond], items.Select(x => x.GetProperty("pole_id").GetString()!));
        Assert.Equal([1, 2], items.Select(x => x.GetProperty("position").GetInt32()));
        Assert.Equal([fault], items[0].GetProperty("work_order_fault_ids").EnumerateArray().Select(x => x.GetString()!));
        Assert.Equal("out", items[0].GetProperty("fixture_status").GetString());
    }

    [Fact]
    public async Task Road_order_survives_a_road_owned_by_a_commune_outside_the_callers_scope()
    {
        // The home engineer's poles stand on a road the FOREIGN commune owns (inter_commune); the commune
        // filter hides that road from them, but the order along it must still be the road's.
        var beyond = await PlantPole(0.7, road: foreignSegment);
        var pole = await PlantPole(0.3, road: foreignSegment);
        var faults = new[] { await PlantPoleFault(pole, FaultStatus.Confirmed, reportedOn: segment), await PlantPoleFault(beyond, FaultStatus.Confirmed, reportedOn: segment) };
        var id = await Create("repair", faults, assigned: users["a"].UserId);

        var items = (await PolesOf(id, who: "a")).GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal([pole, beyond], items.Select(x => x.GetProperty("pole_id").GetString()!));
        Assert.Equal([1, 2], items.Select(x => x.GetProperty("position").GetInt32()));
    }

    [Fact]
    public async Task Paging_keeps_road_positions_and_the_total()
    {
        foreach (var along in new[] { 0.1, 0.2, 0.3 }) await PlantPole(along);
        var id = await Create();

        var first = await PolesOf(id, query: "?page=1&page_size=2");
        var second = await PolesOf(id, query: "?page=2&page_size=2");

        Assert.Equal(3, first.GetProperty("total").GetInt32());
        Assert.Equal([1, 2], first.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("position").GetInt32()));
        Assert.Equal([3], second.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("position").GetInt32()));
    }

    [Fact]
    public async Task A_caller_sees_only_poles_of_their_own_communes_on_a_shared_road()
    {
        var mine = await PlantPole(0.2);
        var theirs = await PlantPole(0.4, commune: foreign); // a neighbouring commune's pole on the same road
        var id = await Plant(WorkOrderStatus.InProgress, assigned: users["a"].UserId);

        var engineer = (await PolesOf(id, who: "a")).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("pole_id").GetString()!);
        var manager = (await PolesOf(id)).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("pole_id").GetString()!);

        Assert.Equal([mine], engineer);
        Assert.Equal([mine, theirs], manager); // the manager is scoped to both communes
    }

    [Fact]
    public async Task An_order_the_caller_cannot_see_is_404_exactly_like_one_that_does_not_exist()
    {
        await PlantPole(0.2);
        var foreignOrder = await Plant(WorkOrderStatus.Open, commune: foreign);
        var someoneElses = await Plant(WorkOrderStatus.InProgress, assigned: users["a"].UserId);

        await PolesOf(foreignOrder, who: "a", expected: 404, error: "WORK_ORDER_NOT_FOUND");
        await PolesOf(someoneElses, who: "b", expected: 404, error: "WORK_ORDER_NOT_FOUND");
        await PolesOf("WO-9999999", expected: 404, error: "WORK_ORDER_NOT_FOUND");
    }

    // ---- BE-24: photos of a work order ------------------------------------------------------------

    /// <summary>A repair cannot be completed without an after photo (BE-24); tests about other rules plant one.</summary>
    private Task PlantAfterPhoto(string workOrderId)
        => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var order = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(x => x.WorkOrderId == workOrderId);
            db.Add(new RepairEvidence { WorkOrderId = workOrderId, CommuneId = order.CommuneId, Kind = EvidenceKind.After,
                CapturedAt = DateTime.UtcNow, Lat = 16, Lng = 108, ObjectKey = "original/planted.jpg", ThumbnailKey = "thumb/planted.jpg",
                ByteCount = 1, ThumbnailBytes = 1, UploadedBy = order.AssignedTo ?? users["a"].UserId });
            await db.SaveChangesAsync(); return 0;
        });

    private readonly PhotoStore photos = new();
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>? photoHost;
    private readonly Dictionary<string, HttpClient> photoClients = [];

    private async Task<HttpClient> PhotoClient(string who)
    {
        photoHost ??= factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IObjectStore>(photos))));
        if (photoClients.TryGetValue(who, out var known)) return known;
        var client = photoHost.CreateClient();
        var token = await (await client.PostLoginAsync(users[who].Username, factory.AccountPassword)).ReadTokensAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
        return photoClients[who] = client;
    }

    private static byte[] Jpeg(byte shade = 90) => PhotoStore.Jpeg(shade);

    private async Task<(int Status, JsonElement Body)> Upload(string who, string id, string kind, byte[]? bytes = null,
        string capturedAt = "2026-10-04T13:30:00Z", string lat = "16.0001", string lng = "108.0001", string? clientOpId = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes ?? Jpeg());
        file.Headers.ContentType = new("image/jpeg");
        form.Add(file, "file", "photo.jpg");
        form.Add(new StringContent(kind), "kind");
        form.Add(new StringContent(capturedAt), "captured_at");
        form.Add(new StringContent(lat), "lat");
        form.Add(new StringContent(lng), "lng");
        if (clientOpId is not null) form.Add(new StringContent(clientOpId), "client_op_id");
        var response = await (await PhotoClient(who)).PostAsync($"{Route}/{id}/evidence", form);
        var text = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static string? Code(JsonElement body) => body.ValueKind == JsonValueKind.Object && body.TryGetProperty("error", out var e) ? e.GetProperty("code").GetString() : null;

    private async Task<string> StartedRepair()
    {
        var id = await Create("repair", [await Fault(FaultStatus.Confirmed)], users["a"].UserId);
        await Send("a", "POST", "/" + id + "/start", new { }, 200);
        return id;
    }

    [Fact]
    public async Task A_repair_needs_an_after_photo_before_it_can_be_completed()
    {
        var id = await StartedRepair();
        await Send("a", "POST", "/" + id + "/complete", new { report_note = "The repair is complete" }, 409, "AFTER_EVIDENCE_REQUIRED");

        Assert.Equal(201, (await Upload("a", id, "before")).Status);
        // A BEFORE photo is not proof of the repair.
        await Send("a", "POST", "/" + id + "/complete", new { report_note = "The repair is complete" }, 409, "AFTER_EVIDENCE_REQUIRED");

        Assert.Equal(201, (await Upload("a", id, "after")).Status);
        await Send("a", "POST", "/" + id + "/complete", new { report_note = "The repair is complete" }, 200);
    }

    [Fact]
    public async Task Photos_are_listed_in_capture_order_and_served_through_the_api_byte_for_byte()
    {
        var id = await StartedRepair();
        var original = Jpeg(17);
        var (status, after) = await Upload("a", id, "after", original, capturedAt: "2026-10-04T13:40:00Z");
        Assert.Equal(201, status);
        var (_, before) = await Upload("a", id, "before", capturedAt: "2026-10-04T13:10:00+07:00"); // offset normalised to UTC

        var list = await Send("manager", "GET", "/" + id + "/evidence", null, 200);
        var items = list.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal([before.GetProperty("evidence_id").GetString(), after.GetProperty("evidence_id").GetString()],
            items.Select(x => x.GetProperty("evidence_id").GetString()));
        Assert.Equal("2026-10-04T06:10:00Z", items[0].GetProperty("captured_at").GetString());
        Assert.Equal(["before", "after"], items.Select(x => x.GetProperty("kind").GetString()!));

        var evidenceId = after.GetProperty("evidence_id").GetString()!;
        Assert.Matches("^EVD-[0-9]{4,}$", evidenceId);
        Assert.Equal($"/api/v1/evidence/{evidenceId}/thumbnail", after.GetProperty("thumbnail_url").GetString());
        var manager = await PhotoClient("manager");
        var stored = await manager.GetByteArrayAsync(after.GetProperty("original_url").GetString());
        Assert.Equal(original, stored); // never re-encoded (BE-11 rule 4)
        var thumbnail = await manager.GetAsync(after.GetProperty("thumbnail_url").GetString());
        Assert.Equal("image/jpeg", thumbnail.Content.Headers.ContentType?.MediaType);
        Assert.NotEqual(original, await thumbnail.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Labels_follow_the_kind_of_order()
    {
        var repair = await StartedRepair();
        var (status, body) = await Upload("a", repair, "observation");
        Assert.Equal((400, "EVIDENCE_KIND_NOT_ALLOWED"), (status, Code(body)));

        var inspection = await Create("inspection", [await Fault(FaultStatus.Detected)], users["a"].UserId);
        await Send("a", "POST", "/" + inspection + "/start", new { }, 200);
        (status, body) = await Upload("a", inspection, "before");
        Assert.Equal((400, "EVIDENCE_KIND_NOT_ALLOWED"), (status, Code(body)));
        Assert.Equal(201, (await Upload("a", inspection, "observation")).Status);
    }

    [Fact]
    public async Task Only_the_assigned_engineer_uploads_and_only_while_the_order_is_in_progress()
    {
        var notStarted = await Create("repair", [await Fault(FaultStatus.Confirmed)], users["a"].UserId);
        var (status, body) = await Upload("a", notStarted, "before");
        Assert.Equal((409, "WORK_ORDER_NOT_IN_PROGRESS"), (status, Code(body)));

        var id = await StartedRepair();
        (status, body) = await Upload("b", id, "before"); // another engineer of the same commune
        Assert.Equal((404, "WORK_ORDER_NOT_FOUND"), (status, Code(body)));
        (status, _) = await Upload("manager", id, "before"); // managers verify; they do not take the photos
        Assert.Equal(403, status);
        Assert.Equal(0, await Db(db => db.Set<RepairEvidence>().IgnoreQueryFilters().CountAsync(x => x.WorkOrderId == id || x.WorkOrderId == notStarted)));
    }

    [Fact]
    public async Task Another_engineer_cannot_list_or_open_the_photos()
    {
        var id = await StartedRepair();
        var (_, photo) = await Upload("a", id, "before");

        await Send("b", "GET", "/" + id + "/evidence", null, 404, "WORK_ORDER_NOT_FOUND");
        var response = await (await PhotoClient("b")).GetAsync(photo.GetProperty("thumbnail_url").GetString());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("EVIDENCE_NOT_FOUND", Code(JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement));
    }

    [Theory]
    [InlineData("png")]
    [InlineData("corrupt jpeg")]
    public async Task A_file_that_is_not_a_jpeg_is_refused_by_its_bytes_and_nothing_is_written(string what)
    {
        var id = await StartedRepair();
        byte[] bytes = what == "png"
            ? [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00]
            : [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x13, 0x37, 0xDE, 0xAD]; // passes the signature, not the decoder

        var (status, body) = await Upload("a", id, "before", bytes);

        Assert.Equal((415, "UNSUPPORTED_IMAGE_FORMAT"), (status, Code(body)));
        Assert.Equal(0, await Db(db => db.Set<RepairEvidence>().IgnoreQueryFilters().CountAsync(x => x.WorkOrderId == id)));
    }

    [Theory]
    [InlineData("captured_at", "not a time")]
    [InlineData("captured_at", "13:30")]
    [InlineData("captured_at", "2026-10-04")]
    [InlineData("lat", "91")]
    [InlineData("lng", "NaN")]
    [InlineData("kind", "selfie")]
    public async Task Malformed_fields_are_400_before_anything_is_stored(string field, string value)
    {
        var id = await StartedRepair();
        var (status, body) = field switch
        {
            "captured_at" => await Upload("a", id, "before", capturedAt: value),
            "lat" => await Upload("a", id, "before", lat: value),
            "lng" => await Upload("a", id, "before", lng: value),
            _ => await Upload("a", id, value),
        };
        Assert.Equal((400, "VALIDATION_FAILED"), (status, Code(body)));
        Assert.Equal(0, await Db(db => db.Set<RepairEvidence>().IgnoreQueryFilters().CountAsync(x => x.WorkOrderId == id)));
    }

    [Theory]
    [InlineData("reassigned")]
    [InlineData("cancelled")]
    public async Task An_order_changed_while_the_photo_uploads_is_checked_again_before_the_row_is_saved(string change)
    {
        var id = await StartedRepair();
        photos.DuringWrite = () => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var order = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(x => x.WorkOrderId == id);
            if (change == "reassigned") order.AssignedTo = users["b"].UserId;
            else { order.WoStatus = WorkOrderStatus.Cancelled; order.ClosedAt = DateTime.UtcNow; order.ReviewNote = "Called off"; }
            await db.SaveChangesAsync(); return 0;
        });
        try
        {
            var (status, body) = await Upload("a", id, "before");
            Assert.Equal(change == "reassigned" ? (404, "WORK_ORDER_NOT_FOUND") : (409, "WORK_ORDER_NOT_IN_PROGRESS"), (status, Code(body)));
        }
        finally { photos.DuringWrite = null; }
        Assert.Equal(0, await Db(db => db.Set<RepairEvidence>().IgnoreQueryFilters().CountAsync(x => x.WorkOrderId == id)));
    }

    [Fact]
    public async Task An_overlapping_retry_gets_the_photo_back_even_after_the_order_was_completed()
    {
        var id = await StartedRepair();
        var key = Guid.NewGuid();
        string? firstId = null;
        // While this request writes its image, the overlapping first attempt commits the photo and the engineer
        // completes the order. This request must then answer the replay, not "not in progress".
        photos.DuringWrite = () => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var order = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(x => x.WorkOrderId == id);
            var first = new RepairEvidence { WorkOrderId = id, CommuneId = order.CommuneId, Kind = EvidenceKind.After, CapturedAt = DateTime.UtcNow,
                Lat = 16, Lng = 108, ObjectKey = "original/first.jpg", ThumbnailKey = "thumb/first.jpg", ByteCount = 1, ThumbnailBytes = 1,
                UploadedBy = users["a"].UserId, ClientOpId = key };
            db.Add(first);
            order.WoStatus = WorkOrderStatus.Done; order.CompletedAt = DateTime.UtcNow; order.ReportNote = "The repair is complete";
            await db.SaveChangesAsync(); firstId = first.EvidenceId; return 0;
        });
        try
        {
            var (status, body) = await Upload("a", id, "after", clientOpId: key.ToString());
            Assert.Equal(200, status);
            Assert.Equal(firstId, body.GetProperty("evidence_id").GetString());
        }
        finally { photos.DuringWrite = null; }
        Assert.Equal(1, await Db(db => db.Set<RepairEvidence>().IgnoreQueryFilters().CountAsync(x => x.WorkOrderId == id)));
    }

    [Fact]
    public async Task A_retry_with_the_same_client_op_id_returns_the_same_photo()
    {
        var id = await StartedRepair();
        var key = Guid.NewGuid().ToString();

        var first = await Upload("a", id, "after", clientOpId: key);
        var retry = await Upload("a", id, "after", clientOpId: key);
        var changed = await Upload("a", id, "before", clientOpId: key);

        Assert.Equal(201, first.Status);
        Assert.Equal(200, retry.Status);
        Assert.Equal(first.Body.GetProperty("evidence_id").GetString(), retry.Body.GetProperty("evidence_id").GetString());
        Assert.Equal((409, "IDEMPOTENCY_CONFLICT"), (changed.Status, Code(changed.Body)));
        Assert.Equal(1, await Db(db => db.Set<RepairEvidence>().IgnoreQueryFilters().CountAsync(x => x.WorkOrderId == id)));
    }

    // ---- GET /work-orders/agenda (BE-25) --------------------------------------------------------------

    private async Task<JsonElement> AgendaOf(string who, string query = "", int expected = 200, string? error = null)
        => await Send(who, "GET", $"/agenda{query}", null, expected, error);

    private static JsonElement[] Groups(JsonElement agenda) => agenda.GetProperty("groups").EnumerateArray().ToArray();
    private static string[] OrderIds(JsonElement group) => group.GetProperty("work_orders").EnumerateArray().Select(x => x.GetProperty("work_order_id").GetString()!).ToArray();
    private static string[] Strings(JsonElement item, string property) => item.GetProperty(property).EnumerateArray().Select(x => x.GetString()!).ToArray();

    /// <summary>A home road <paramref name="origin"/> degrees north-east of the test road.</summary>
    private Task<string> PlantRoad(double origin) => Db(async db =>
    {
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var road = new RoadSegment { CommuneId = home, SegmentName = "WO agenda road", RoadClass = RoadClass.InterVillage, DataSource = DataSource.Simulated,
            LengthM = 100, Geom = new LineString([new Coordinate(108 + origin, 16 + origin), new Coordinate(108.01 + origin, 16.01 + origin)]) { SRID = 4326 } };
        db.Add(road); await db.SaveChangesAsync(); return road.SegmentId;
    });

    private Task SetDates(string id, DateOnly? scheduled, DateOnly? due) => Db(async db =>
    {
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var wo = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(x => x.WorkOrderId == id);
        wo.ScheduledDate = scheduled; wo.DueDate = due;
        return await db.SaveChangesAsync();
    });

    /// <summary>A fault the agenda must place without help from a road: no pole, no segment unless a pole gives one.</summary>
    private Task<string> RoadlessFault(double? lat, double? lng, string? pole = null) => Db(async db =>
    {
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var f = new Fault { CommuneId = home, PoleId = pole, Lat = lat, Lng = lng, FaultStatus = FaultStatus.Confirmed, FaultType = FaultType.LampOut,
            Severity = Severity.Medium, SourceChannel = SourceChannel.Cv, DataSource = DataSource.Simulated, DetectedAt = DateTime.UtcNow };
        db.Add(f); await db.SaveChangesAsync(); return f.FaultId;
    });

    // Independent measurements: PostGIS directly, not the code under test.
    private Task<double> RoadMetres(string road, double lat, double lng) => Db(db => db.Database.SqlQuery<double>(
        $"SELECT ST_Distance(ST_Transform(geom, 3405), ST_Transform(ST_SetSRID(ST_MakePoint({lng}, {lat}), 4326), 3405)) AS \"Value\" FROM road_segment WHERE segment_id = {road}").SingleAsync());
    private Task<double> PointMetres(double lat1, double lng1, double lat2, double lng2) => Db(db => db.Database.SqlQuery<double>(
        $"SELECT ST_Distance(ST_Transform(ST_SetSRID(ST_MakePoint({lng1}, {lat1}), 4326), 3405), ST_Transform(ST_SetSRID(ST_MakePoint({lng2}, {lat2}), 4326), 3405)) AS \"Value\"").SingleAsync());

    [Fact]
    public async Task Tonights_agenda_lists_the_engineers_open_orders_once_with_why_and_only_counts_later_nights()
    {
        var started = await Plant(WorkOrderStatus.InProgress);
        var tonight = await Plant(WorkOrderStatus.Assigned);
        var late = await Plant(WorkOrderStatus.Assigned);
        var anyNight = await Plant(WorkOrderStatus.Assigned);
        var later = await Plant(WorkOrderStatus.Assigned);
        await Plant(WorkOrderStatus.Done);
        await Plant(WorkOrderStatus.Open);
        await Plant(WorkOrderStatus.Assigned, assigned: users["b"].UserId);
        await SetDates(started, new DateOnly(2026, 10, 8), null); // started counts even when planned for later
        await SetDates(tonight, new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 10));
        await SetDates(late, new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 5));
        await SetDates(later, new DateOnly(2026, 10, 9), null);

        var body = await AgendaOf("a", "?night_of=2026-10-06");

        Assert.Equal("2026-10-06", body.GetProperty("night_of").GetString());
        Assert.Equal(users["a"].UserId, body.GetProperty("assigned_to").GetString());
        Assert.Equal(1, body.GetProperty("upcoming_count").GetInt32());
        var group = Assert.Single(Groups(body));
        Assert.Equal(segment, group.GetProperty("segment_id").GetString());
        Assert.Equal("WO test road", group.GetProperty("segment_name").GetString());
        Assert.Equal(home, group.GetProperty("commune_id").GetString());
        Assert.Equal(16, group.GetProperty("location").GetProperty("lat").GetDouble(), 9); // the road's start without `near`
        Assert.Equal(JsonValueKind.Null, group.GetProperty("distance_m").ValueKind);

        // In progress first, then overdue, then by due date — a dated order before an undated one.
        Assert.Equal([started, late, tonight, anyNight], OrderIds(group));
        var orders = group.GetProperty("work_orders").EnumerateArray().ToArray();
        Assert.Equal(["in_progress"], Strings(orders[0], "flags"));
        Assert.Equal(["carried_over", "overdue"], Strings(orders[1], "flags"));
        Assert.Equal(["scheduled_tonight"], Strings(orders[2], "flags"));
        Assert.Equal(["unscheduled"], Strings(orders[3], "flags"));
        Assert.Equal([segment], Strings(orders[0], "segment_ids"));
        // The list shape is the listing's own.
        Assert.Equal("inspection", orders[0].GetProperty("task_kind").GetString());
        Assert.Equal("in_progress", orders[0].GetProperty("wo_status").GetString());
        Assert.Equal(started, orders[0].GetProperty("case_id").GetString());
    }

    [Fact]
    public async Task A_survey_appears_once_under_its_first_road_and_near_puts_the_nearest_road_first()
    {
        var nearRoad = await PlantRoad(0.2);
        var survey = (await Send("manager", "POST", "", new { task_kind = "survey", title = "Survey both roads", commune_id = home,
            segment_ids = new[] { nearRoad, segment }, assigned_to = users["a"].UserId }, 201)).GetProperty("work_order_id").GetString()!;
        var inspection = await Plant(WorkOrderStatus.Assigned);

        // Without a position, roads come in id order: the test road was created first.
        Assert.Equal([segment, nearRoad], Groups(await AgendaOf("a", "?night_of=2026-10-06")).Select(x => x.GetProperty("segment_id").GetString()!));

        var groups = Groups(await AgendaOf("a", "?night_of=2026-10-06&near=16.2,108.2"));

        Assert.Equal([nearRoad, segment], groups.Select(x => x.GetProperty("segment_id").GetString()!));
        Assert.Equal([survey], OrderIds(groups[0])); // once, under its FIRST road, although it also covers the second
        Assert.Equal([inspection], OrderIds(groups[1]));
        Assert.Equal([nearRoad, segment], Strings(groups[0].GetProperty("work_orders")[0], "segment_ids"));
        Assert.Equal(await RoadMetres(nearRoad, 16.2, 108.2), groups[0].GetProperty("distance_m").GetDouble(), 6);
        Assert.Equal(await RoadMetres(segment, 16.2, 108.2), groups[1].GetProperty("distance_m").GetDouble(), 6);
        Assert.True(groups[1].GetProperty("distance_m").GetDouble() > 20_000); // metres, never degrees
        // The far road's point to head for is its end nearest the engineer, not its start.
        Assert.Equal(16.01, groups[1].GetProperty("location").GetProperty("lat").GetDouble(), 6);
        Assert.Equal(108.01, groups[1].GetProperty("location").GetProperty("lng").GetDouble(), 6);
    }

    [Fact]
    public async Task An_engineer_gets_only_their_own_agenda_and_everyone_else_must_name_an_engineer_of_their_communes()
    {
        var mine = await Plant(WorkOrderStatus.Assigned);
        var stranger = await Db(async db =>
        {
            var user = new AppUser { Username = "wo" + Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid", FullName = "stranger",
                PasswordHash = "x", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", PasswordSetAt = DateTime.UtcNow, Role = UserRole.FieldEngineer };
            db.Add(user); await db.SaveChangesAsync(); return user; // a field engineer of NO commune
        });
        users["stranger"] = stranger;

        await AgendaOf("a", $"?assigned_to={users["b"].UserId}", 404, "USER_NOT_FOUND");
        Assert.Equal([mine], Groups(await AgendaOf("a", "?assigned_to=me")).SelectMany(OrderIds));
        Assert.Equal([mine], Groups(await AgendaOf("a", $"?assigned_to={users["a"].UserId}")).SelectMany(OrderIds));
        Assert.Empty(Groups(await AgendaOf("b"))); // b sees nothing of a's

        await AgendaOf("manager", "", 400, "VALIDATION_FAILED");
        foreach (var who in new[] { "manager", "superior", "admin" })
            Assert.Equal([mine], Groups(await AgendaOf(who, $"?assigned_to={users["a"].UserId}")).SelectMany(OrderIds));
        await AgendaOf("manager", $"?assigned_to={users["superior"].UserId}", 404, "USER_NOT_FOUND"); // not a field engineer
        await AgendaOf("manager", $"?assigned_to={stranger.UserId}", 404, "USER_NOT_FOUND");      // in none of my communes
        await AgendaOf("manager", "?assigned_to=USR-9999999", 404, "USER_NOT_FOUND");
    }

    [Fact]
    public async Task Without_night_of_the_agenda_is_for_the_current_night_in_the_communes_time_zone()
    {
        var options = new WorkOrderAgendaOptions();
        var before = options.NightOf(DateTimeOffset.UtcNow);
        var body = await AgendaOf("a");
        var after = options.NightOf(DateTimeOffset.UtcNow);

        Assert.Contains(DateOnly.ParseExact(body.GetProperty("night_of").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture), new[] { before, after });
    }

    [Fact]
    public async Task An_order_on_a_fault_with_no_pole_and_no_road_is_grouped_by_commune_at_the_faults_spot()
    {
        var fault = await RoadlessFault(16.3, 108.3);
        var id = await Create("repair", [fault], assigned: users["a"].UserId);

        var group = Assert.Single(Groups(await AgendaOf("a", "?night_of=2026-10-06&near=16.31,108.3")));

        Assert.Equal(JsonValueKind.Null, group.GetProperty("segment_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, group.GetProperty("segment_name").ValueKind);
        Assert.Equal(home, group.GetProperty("commune_id").GetString());
        Assert.Equal(16.3, group.GetProperty("location").GetProperty("lat").GetDouble(), 9);
        Assert.Equal(await PointMetres(16.31, 108.3, 16.3, 108.3), group.GetProperty("distance_m").GetDouble(), 6);
        var item = group.GetProperty("work_orders")[0];
        Assert.Equal(id, item.GetProperty("work_order_id").GetString());
        Assert.Empty(Strings(item, "segment_ids"));
        Assert.Equal(108.3, item.GetProperty("location").GetProperty("lng").GetDouble(), 9);
    }

    [Fact]
    public async Task A_road_of_a_commune_outside_the_callers_scope_lends_it_no_name_and_no_place()
    {
        // A home pole on the FOREIGN commune's road (inter_commune), carrying a fault with no road of its own.
        var pole = await PlantPole(0.3, road: foreignSegment);
        var fault = await RoadlessFault(null, null, pole);
        await Create("repair", [fault], assigned: users["a"].UserId);

        var engineer = Assert.Single(Groups(await AgendaOf("a", "?night_of=2026-10-06")));
        var manager = Assert.Single(Groups(await AgendaOf("manager", $"?night_of=2026-10-06&assigned_to={users["a"].UserId}")));

        Assert.Equal(foreignSegment, engineer.GetProperty("segment_id").GetString());
        Assert.Equal(JsonValueKind.Null, engineer.GetProperty("segment_name").ValueKind);
        Assert.Equal(home, engineer.GetProperty("commune_id").GetString());
        Assert.Equal(16.003, engineer.GetProperty("location").GetProperty("lat").GetDouble(), 9); // the pole, not the road
        Assert.Equal("WO test road", manager.GetProperty("segment_name").GetString()); // the manager covers both communes
        Assert.Equal(foreign, manager.GetProperty("commune_id").GetString());
    }

    [Fact]
    public async Task A_date_that_is_not_yyyy_mm_dd_is_a_400_never_another_day()
    {
        // en-US parsing used to read 06/10/2026 as 10 June: another month's work, no error.
        foreach (var (path, field) in new[] { ("/agenda?night_of=06/10/2026", "night_of"), ("/agenda?night_of=2026-10-06T00:00:00", "night_of"),
                     ("?scheduled_from=06/10/2026", "scheduled_from"), ("?scheduled_to=2026-6-1", "scheduled_to") })
        {
            var body = await Send("a", "GET", path, null, 400, "VALIDATION_FAILED");
            Assert.True(body.GetProperty("error").GetProperty("details").TryGetProperty(field, out _), body.GetRawText());
        }
        Assert.Equal("2026-10-06", (await AgendaOf("a", "?night_of=2026-10-06")).GetProperty("night_of").GetString());
    }

    [Fact]
    public async Task A_position_that_is_not_lat_comma_lng_on_the_globe_is_a_400()
    {
        foreach (var near in new[] { "abc", "16.2", "16,2,3", "91,108", "16,181", "NaN,108", "16,Infinity" })
            await AgendaOf("a", "?near=" + Uri.EscapeDataString(near), 400, "VALIDATION_FAILED");
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
