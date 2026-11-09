using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public class WorkOrderBatchTests(AssetImportFixture factory, ITestOutputHelper output) : IAsyncLifetime
{
    private readonly Dictionary<string, AppUser> users = [];
    private readonly Dictionary<string, HttpClient> clients = [];
    private string home = null!;
    private string foreign = null!;
    private string segment = null!;
    private const string Route = "/api/v1/work-orders";

    private Task<T> Db<T>(Func<LuxMapDbContext, Task<T>> work) => factory.QueryAsync(work);

    public async Task InitializeAsync()
    {
        output.WriteLine("Creating isolated work-order accounts and communes.");
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var communes = new[] { new AdministrativeUnit { Name = "WO batch test home " + Guid.NewGuid() }, new AdministrativeUnit { Name = "WO batch test foreign " + Guid.NewGuid() } };
            db.AddRange(communes);
            await db.SaveChangesAsync();
            home = communes[0].CommuneId; foreign = communes[1].CommuneId;
            foreach (var (key, role) in new[] { ("manager", UserRole.Manager), ("a", UserRole.FieldEngineer), ("b", UserRole.FieldEngineer), ("superior", UserRole.Superior), ("admin", UserRole.SystemAdmin), ("foreign", UserRole.FieldEngineer) })
            {
                var user = new AppUser
                {
                    Username = "wobatch" + Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid", FullName = key,
                    PasswordHash = "", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", PasswordSetAt = DateTime.UtcNow, Role = role, HasSystemWideScope = role == UserRole.SystemAdmin,
                };
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, factory.AccountPassword);
                users[key] = user; db.Add(user);
            }
            await db.SaveChangesAsync();
            foreach (var user in users.Values)
            {
                db.Add(new AppUserCommune { UserId = user.UserId, CommuneId = user == users["foreign"] ? foreign : home });
            }
            var roads = new[] { home, foreign }.Select(c => new RoadSegment
            {
                CommuneId = c, SegmentName = "WO batch test road", RoadClass = RoadClass.InterVillage, DataSource = DataSource.Simulated,
                Geom = new LineString([new Coordinate(108, 16), new Coordinate(108.01, 16.01)]) { SRID = 4326 }, LengthM = 100,
            }).ToArray();
            db.AddRange(roads); await db.SaveChangesAsync(); segment = roads[0].SegmentId;
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
            foreach (var table in new[] { "notification", "work_order_fault", "work_order_segment", "repair_evidence", "fault", "work_order_pole_photo", "work_order", "fault_cluster", "fixture", "pole", "road_segment" })
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

    private Task<string> Fault(FaultStatus status = FaultStatus.Detected)
        => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var f = new Fault { CommuneId = home, SegmentId = segment, Lat = 16, Lng = 108,
                FaultStatus = status, FaultType = FaultType.LampOut, Severity = Severity.Medium,
                SourceChannel = SourceChannel.Cv, DataSource = DataSource.Simulated, DetectedAt = DateTime.UtcNow };
            db.Add(f); await db.SaveChangesAsync(); return f.FaultId;
        });

    private object Item(string title, string? fault = null, string? assigned = null) => new
    {
        task_kind = "inspection", title, fault_ids = fault is null ? null : new[] { fault },
        segment_id = fault is null ? segment : null, assigned_to = assigned,
        scheduled_date = "2026-11-10", due_date = "2026-11-11",
    };

    private async Task<JsonElement> Batch(object body, int status, string? code = null, int? index = null, string who = "manager")
    {
        using var response = await clients[who].PostAsJsonAsync(Route + "/batch", body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == status, text);
        Assert.Null(response.Headers.Location);
        var json = JsonDocument.Parse(text).RootElement.Clone();
        if (code is not null) Assert.Equal(code, json.GetProperty("error").GetProperty("code").GetString());
        if (index is not null) Assert.Equal(index, json.GetProperty("error").GetProperty("details").GetProperty("index").GetInt32());
        return json;
    }

    private Task AssertNoWrites() => Db(async db =>
    {
        Assert.False(await db.Set<WorkOrder>().IgnoreQueryFilters().AnyAsync(x => x.CommuneId == home));
        Assert.False(await db.Set<WorkOrderFault>().IgnoreQueryFilters().AnyAsync(x => x.CommuneId == home));
        Assert.False(await db.Set<AuditEvent>().IgnoreQueryFilters().AnyAsync(x => x.CommuneId == home));
        Assert.False(await db.Set<Notification>().IgnoreQueryFilters().AnyAsync(x => x.CommuneId == home));
        return 0;
    });

    [Fact]
    public async Task Three_items_preserve_order_details_audits_and_assignment_notices()
    {
        var first = await Fault();
        var second = await Fault();
        var json = await Batch(new { items = new object[]
        {
            Item("First inspection", first, users["a"].UserId),
            Item("Second inspection", second),
            new { task_kind = "survey", title = "Third survey", commune_id = home, segment_ids = new[] { segment },
                assigned_to = users["b"].UserId, scheduled_date = "2026-11-10", due_date = "2026-11-11" },
        } }, 201);
        Assert.Equal(3, json.GetProperty("count").GetInt32());
        var items = json.GetProperty("created_items").EnumerateArray().ToArray();
        Assert.Equal(3, items.Length);
        var titles = new[] { "First inspection", "Second inspection", "Third survey" };
        var assignees = new[] { users["a"].UserId, null, users["b"].UserId };
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            var id = item.GetProperty("work_order_id").GetString()!;
            Assert.Equal(titles[i], item.GetProperty("title").GetString());
            Assert.Equal(assignees[i], item.GetProperty("assigned_to").GetString());
            Assert.Equal(i == 1 ? "open" : "assigned", item.GetProperty("wo_status").GetString());
            Assert.Equal("2026-11-10", item.GetProperty("scheduled_date").GetString());
            Assert.Equal("2026-11-11", item.GetProperty("due_date").GetString());
            Assert.Equal(i == 0 ? new[] { first } : i == 1 ? new[] { second } : [],
                item.GetProperty("fault_ids").EnumerateArray().Select(x => x.GetString()!).ToArray());
            if (i == 2) Assert.Equal(new[] { segment }, item.GetProperty("segment_ids").EnumerateArray().Select(x => x.GetString()!).ToArray());
            using var get = await clients["manager"].GetAsync(Route + "/" + id);
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            Assert.Equal(item.GetRawText(), (await get.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
            await Db(async db =>
            {
                var audit = Assert.Single(await db.Set<AuditEvent>().IgnoreQueryFilters().Where(x => x.EntityId == id).ToListAsync());
                Assert.Equal(AuditAction.Created, audit.Action);
                Assert.Equal(AuditEntityType.WorkOrder, audit.EntityType);
                var notices = await db.Set<Notification>().IgnoreQueryFilters().Where(x => x.EntityId == id).ToListAsync();
                if (assignees[i] is null) Assert.Empty(notices);
                else
                {
                    var notice = Assert.Single(notices);
                    Assert.Equal(assignees[i], notice.RecipientUserId);
                    Assert.Equal(NotificationType.WorkOrderAssigned, notice.Type);
                }
                return 0;
            });
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Missing_fault_at_any_index_rolls_back_orders_links_audits_and_notices(int index)
    {
        var faults = new[] { await Fault(), await Fault(), await Fault() };
        // Derive an absent ID without assuming a sequence value.
        var missing = faults[0] + "000000000";
        var items = faults.Select((f, i) => Item("Rollback " + i, i == index ? missing : f, users["a"].UserId)).ToArray();
        var json = await Batch(new { items }, 404, "FAULT_NOT_FOUND", index);
        Assert.Equal(missing, json.GetProperty("error").GetProperty("details").GetProperty("fault_ids")[0].GetString());
        await AssertNoWrites();
    }

    [Fact]
    public async Task Survey_rows_and_notices_are_also_rolled_back_when_a_later_item_fails()
    {
        await Batch(new { items = new object[]
        {
            new { task_kind = "survey", title = "Rollback survey", commune_id = home,
                segment_ids = new[] { segment }, assigned_to = users["a"].UserId },
            new { task_kind = "inspection", title = "Invalid schedule", segment_id = segment,
                scheduled_date = "2026-11-12", due_date = "2026-11-11" },
        } }, 400, "VALIDATION_FAILED", 1);
        await AssertNoWrites();
        await Db(async db =>
        {
            Assert.False(await db.Set<WorkOrderSegment>().IgnoreQueryFilters().AnyAsync(x => x.CommuneId == home));
            return 0;
        });
    }

    [Fact]
    public async Task Duplicate_fault_between_items_rolls_back_everything()
    {
        var fault = await Fault();
        var json = await Batch(new { items = new[] { Item("First", fault), Item("Second", fault) } },
            409, "FAULT_ALREADY_IN_WORK_ORDER", 1);
        Assert.Equal(fault, json.GetProperty("error").GetProperty("details").GetProperty("fault_id").GetString());
        await AssertNoWrites();
    }

    [Fact]
    public async Task Existing_link_conflicts_and_single_create_keeps_location_and_shape()
    {
        var fault = await Fault();
        using var single = await clients["manager"].PostAsJsonAsync(Route, Item("Existing", fault));
        Assert.Equal(HttpStatusCode.Created, single.StatusCode);
        var json = await single.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetProperty("work_order_id").GetString();
        Assert.Equal(Route + "/" + id, single.Headers.Location!.ToString());
        Assert.Equal("open", json.GetProperty("wo_status").GetString());
        var error = await Batch(new { items = new[] { Item("Conflict", fault) } }, 409, "FAULT_ALREADY_IN_WORK_ORDER", 0);
        Assert.Equal(id, error.GetProperty("error").GetProperty("details").GetProperty("work_order_id").GetString());
        await Db(async db =>
        {
            Assert.Equal(1, await db.Set<WorkOrder>().IgnoreQueryFilters().CountAsync(x => x.CommuneId == home));
            Assert.Equal(1, await db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(x => x.CommuneId == home));
            return 0;
        });
    }

    [Fact]
    public async Task Ineligible_status_and_foreign_assignee_keep_the_single_create_errors()
    {
        var fault = await Fault(FaultStatus.Verified);
        await Batch(new { items = new[] { Item("Ineligible", fault) } }, 409, "FAULT_STATUS_NOT_ELIGIBLE", 0);
        var json = await Batch(new { items = new[] { Item("Foreign", assigned: users["foreign"].UserId) } },
            409, "ASSIGNEE_NOT_ELIGIBLE", 0);
        Assert.Equal(users["foreign"].UserId, json.GetProperty("error").GetProperty("details").GetProperty("assigned_to").GetString());
        await AssertNoWrites();
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Items_must_be_present_and_contain_one_to_fifty_elements(int count)
    {
        object body = count == -2 ? new { items = (object[]?)null }
            : count == -1 ? new { } : new { items = Enumerable.Range(0, count).Select(i => Item("Item " + i)).ToArray() };
        var json = await Batch(body, 400, "VALIDATION_FAILED");
        Assert.Equal("items", json.GetProperty("error").GetProperty("details").GetProperty("field").GetString());
        await AssertNoWrites();
    }

    [Fact]
    public async Task Fifty_items_are_allowed()
    {
        var json = await Batch(new { items = Enumerable.Range(0, 50).Select(i => Item("Item " + i)).ToArray() }, 201);
        Assert.Equal(50, json.GetProperty("count").GetInt32());
        Assert.Equal(50, json.GetProperty("created_items").GetArrayLength());
    }

    [Theory]
    [InlineData("null", "VALIDATION_FAILED")]
    [InlineData("owned", "SERVER_OWNED_FIELD")]
    [InlineData("schedule", "VALIDATION_FAILED")]
    [InlineData("title", "VALIDATION_FAILED")]
    [InlineData("kind", "VALIDATION_FAILED")]
    public async Task Element_validation_runs_in_service_and_names_index(string invalid, string code)
    {
        object? bad = invalid switch
        {
            "null" => null,
            "owned" => new { task_kind = "inspection", title = "Owned", segment_id = segment, work_order_id = "client-value" },
            "schedule" => new { task_kind = "inspection", title = "Schedule", segment_id = segment, scheduled_date = "2026-11-12", due_date = "2026-11-11" },
            "title" => new { task_kind = "inspection", title = "", segment_id = segment },
            _ => new { title = "Missing kind", segment_id = segment },
        };
        var json = await Batch(new { items = new[] { Item("Before", assigned: users["a"].UserId), bad } }, 400, code, 1);
        if (invalid == "schedule") Assert.Equal("scheduled_date", json.GetProperty("error").GetProperty("details").GetProperty("field").GetString());
        await AssertNoWrites();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("superior")]
    [InlineData("admin")]
    public async Task Only_managers_can_create_batches(string who)
        => await Batch(new { items = new[] { Item("Forbidden") } }, 403, "ROLE_FORBIDDEN", who: who);

    [Fact]
    public async Task No_token_is_unauthorized()
    {
        using var anonymous = factory.CreateClient();
        using var denied = await anonymous.PostAsJsonAsync(Route + "/batch", new { items = new[] { Item("Anonymous") } });
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task Reversed_fault_batches_have_one_winner_and_one_conflict_for_twenty_rounds()
    {
        for (var round = 0; round < 20; round++)
        {
            var first = await Fault();
            var second = await Fault();
            var forward = new { items = new[] { Item("Forward first", first), Item("Forward second", second) } };
            var reverse = new { items = new[] { Item("Reverse second", second), Item("Reverse first", first) } };
            var responses = await Task.WhenAll(clients["manager"].PostAsJsonAsync(Route + "/batch", forward),
                clients["manager"].PostAsJsonAsync(Route + "/batch", reverse));
            try
            {
                Assert.Equal(new[] { 201, 409 }, responses.Select(x => (int)x.StatusCode).Order().ToArray());
                var loser = await responses.Single(x => x.StatusCode == HttpStatusCode.Conflict).Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("FAULT_ALREADY_IN_WORK_ORDER", loser.GetProperty("error").GetProperty("code").GetString());
                Assert.Equal(0, loser.GetProperty("error").GetProperty("details").GetProperty("index").GetInt32());
                await Db(async db =>
                {
                    var links = await db.Set<WorkOrderFault>().IgnoreQueryFilters().Where(x => x.FaultId == first || x.FaultId == second).ToListAsync();
                    Assert.Equal(2, links.Count);
                    Assert.Equal(2, links.Select(x => x.WorkOrderId).Distinct().Count());
                    return 0;
                });
            }
            finally { foreach (var response in responses) response.Dispose(); }
        }
    }
}
