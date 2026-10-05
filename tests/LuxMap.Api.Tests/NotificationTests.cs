using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Npgsql;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-27 — who is told what when a work order moves, and that each person reads only their own notices.
/// Survey and fault notices are pinned next to their flows (SurveyProcessingTests, SurveyPublicationTests,
/// FaultReportTests).
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public class NotificationTests(AssetImportFixture factory) : IAsyncLifetime
{
    private readonly Dictionary<string, AppUser> users = [];
    private readonly Dictionary<string, HttpClient> clients = [];
    private string home = null!;
    private string segment = null!;

    private Task<T> Db<T>(Func<LuxMapDbContext, Task<T>> work) => factory.QueryAsync(work);

    public async Task InitializeAsync()
    {
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var commune = new AdministrativeUnit { Name = "Notification test " + Guid.NewGuid() };
            db.Add(commune);
            await db.SaveChangesAsync();
            home = commune.CommuneId;
            foreach (var (key, role, locked) in new[]
            {
                ("manager", UserRole.Manager, false), ("manager2", UserRole.Manager, false), ("locked_manager", UserRole.Manager, true),
                ("a", UserRole.FieldEngineer, false), ("b", UserRole.FieldEngineer, false), ("superior", UserRole.Superior, false),
            })
            {
                var user = new AppUser
                {
                    Username = "ntf" + Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid", FullName = key,
                    PasswordHash = "", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", PasswordSetAt = DateTime.UtcNow, Role = role, IsLocked = locked,
                };
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, factory.AccountPassword);
                users[key] = user;
                db.Add(user);
            }
            await db.SaveChangesAsync();
            foreach (var user in users.Values) db.Add(new AppUserCommune { UserId = user.UserId, CommuneId = home });
            var road = new RoadSegment
            {
                CommuneId = home, SegmentName = "Notification test road", RoadClass = RoadClass.InterVillage, DataSource = DataSource.Simulated,
                Geom = new LineString([new Coordinate(108, 16), new Coordinate(108.01, 16.01)]) { SRID = 4326 }, LengthM = 100,
            };
            db.Add(road);
            await db.SaveChangesAsync();
            segment = road.SegmentId;
            return 0;
        });
        foreach (var (key, user) in users.Where(pair => !pair.Value.IsLocked))
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
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SET LOCAL luxmap.audit_purge = 'on'");
            foreach (var table in new[] { "audit_event", "notification", "work_order_fault", "work_order", "road_segment", "app_user_commune" })
            {
                var sql = $"DELETE FROM {table} WHERE commune_id = {{0}}";
                await db.Database.ExecuteSqlRawAsync(sql, home);
            }
            var ids = users.Values.Select(user => user.UserId).ToArray();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM refresh_token WHERE user_id = ANY({ids})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user WHERE user_id = ANY({ids})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM administrative_unit WHERE commune_id = {home}");
            await transaction.CommitAsync();
            return 0;
        });
        foreach (var client in clients.Values) client.Dispose();
    }

    [Fact]
    public async Task Assigning_tells_the_new_engineer_and_reassigning_or_unassigning_tells_the_old_one()
    {
        var id = await CreateAsync(users["a"].UserId);

        var assigned = Assert.Single(await NoticesAsync("a"));
        Assert.Equal("work_order_assigned", assigned.GetProperty("type").GetString());
        Assert.Equal($"Bạn được giao phiếu kiểm tra {id}", assigned.GetProperty("title").GetString());
        Assert.Equal($"{id} — Notification test order. Hạn 12/10/2026.", assigned.GetProperty("body").GetString());
        Assert.Equal("work_order", assigned.GetProperty("entity_type").GetString());
        Assert.Equal(id, assigned.GetProperty("entity_id").GetString());
        Assert.Matches(new Regex("^NTF-[0-9]{6,}$"), assigned.GetProperty("notification_id").GetString()!);
        Assert.EndsWith("Z", assigned.GetProperty("created_at").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, assigned.GetProperty("read_at").ValueKind);

        await SendAsync("manager", HttpMethod.Put, $"/api/v1/work-orders/{id}/assignee", new { assigned_to = users["b"].UserId });
        Assert.Equal(["work_order_assigned"], Types(await NoticesAsync("b")));
        Assert.Equal(["work_order_unassigned", "work_order_assigned"], Types(await NoticesAsync("a")));

        await SendAsync("manager", HttpMethod.Put, $"/api/v1/work-orders/{id}/assignee", new { assigned_to = (string?)null });
        Assert.Equal(["work_order_unassigned", "work_order_assigned"], Types(await NoticesAsync("b")));

        // Nobody is told about what they did themselves, and nobody else is told at all.
        Assert.Empty(await NoticesAsync("manager"));
        Assert.Empty(await NoticesAsync("superior"));
    }

    [Fact]
    public async Task Completing_tells_every_active_manager_of_the_commune_and_return_and_verify_tell_the_engineer()
    {
        var id = await CreateAsync(users["a"].UserId);
        await SendAsync("a", HttpMethod.Post, $"/api/v1/work-orders/{id}/start", new { });
        await SendAsync("a", HttpMethod.Post, $"/api/v1/work-orders/{id}/complete", new { report_note = "Đã kiểm tra xong tuyến" });

        foreach (var manager in new[] { "manager", "manager2" })
        {
            var notice = Assert.Single(await NoticesAsync(manager));
            Assert.Equal("work_order_completed", notice.GetProperty("type").GetString());
            Assert.Equal($"Phiếu kiểm tra {id} chờ nghiệm thu", notice.GetProperty("title").GetString());
        }
        Assert.False(await Db(db => db.Set<Notification>().IgnoreQueryFilters().AnyAsync(n => n.RecipientUserId == users["locked_manager"].UserId)));
        Assert.Empty(await NoticesAsync("superior"));

        await SendAsync("manager", HttpMethod.Post, $"/api/v1/work-orders/{id}/return", new { note = "Thiếu ảnh cột số 3" });
        var returned = (await NoticesAsync("a"))[0];
        Assert.Equal("work_order_returned", returned.GetProperty("type").GetString());
        Assert.EndsWith("Lý do: Thiếu ảnh cột số 3", returned.GetProperty("body").GetString(), StringComparison.Ordinal);

        await SendAsync("a", HttpMethod.Post, $"/api/v1/work-orders/{id}/complete", new { report_note = "Đã bổ sung ảnh cột số 3" });
        await SendAsync("manager2", HttpMethod.Post, $"/api/v1/work-orders/{id}/verify", new { note = "Đạt" });
        Assert.Equal(["work_order_verified", "work_order_returned", "work_order_assigned"], Types(await NoticesAsync("a")));
        Assert.Equal(["work_order_completed", "work_order_completed"], Types(await NoticesAsync("manager")));
    }

    [Fact]
    public async Task A_new_date_tells_the_assignee_a_new_title_does_not_and_cancelling_says_why()
    {
        var id = await CreateAsync(users["a"].UserId);

        await SendAsync("manager", HttpMethod.Patch, $"/api/v1/work-orders/{id}", new { title = "Renamed order" });
        Assert.Equal(["work_order_assigned"], Types(await NoticesAsync("a")));

        await SendAsync("manager", HttpMethod.Patch, $"/api/v1/work-orders/{id}", new { due_date = "2026-10-20" });
        var rescheduled = (await NoticesAsync("a"))[0];
        Assert.Equal("work_order_rescheduled", rescheduled.GetProperty("type").GetString());
        Assert.Equal($"{id} — Renamed order. Ngày làm: chưa đặt; hạn: 20/10/2026.", rescheduled.GetProperty("body").GetString());

        await SendAsync("manager", HttpMethod.Post, $"/api/v1/work-orders/{id}/cancel", new { note = "Trùng phiếu khác" });
        var cancelled = (await NoticesAsync("a"))[0];
        Assert.Equal("work_order_cancelled", cancelled.GetProperty("type").GetString());
        Assert.EndsWith("Lý do: Trùng phiếu khác", cancelled.GetProperty("body").GetString(), StringComparison.Ordinal);

        // An order nobody holds tells nobody when it is cancelled.
        var open = await CreateAsync(null);
        await SendAsync("manager", HttpMethod.Post, $"/api/v1/work-orders/{open}/cancel", new { note = "Không cần nữa" });
        Assert.False(await Db(db => db.Set<Notification>().IgnoreQueryFilters().AnyAsync(n => n.EntityId == open)));
    }

    [Fact]
    public async Task Each_person_reads_and_marks_only_their_own_and_marking_read_is_idempotent()
    {
        var first = await CreateAsync(users["a"].UserId);
        await CreateAsync(users["a"].UserId);
        var notices = await NoticesAsync("a");
        Assert.Equal(2, notices.Count);
        var target = notices[1].GetProperty("notification_id").GetString()!;
        Assert.Equal(first, notices[1].GetProperty("entity_id").GetString());

        var stranger = await clients["b"].PostAsync($"/api/v1/notifications/{target}/read", null);
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal("NOTIFICATION_NOT_FOUND", (await JsonAsync(stranger)).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(2, await UnreadAsync("a"));

        Assert.Equal(HttpStatusCode.NoContent, (await clients["a"].PostAsync($"/api/v1/notifications/{target}/read", null)).StatusCode);
        var readAt = (await NoticesAsync("a"))[1].GetProperty("read_at").GetString();
        Assert.NotNull(readAt);
        Assert.Equal(HttpStatusCode.NoContent, (await clients["a"].PostAsync($"/api/v1/notifications/{target}/read", null)).StatusCode);
        Assert.Equal(readAt, (await NoticesAsync("a"))[1].GetProperty("read_at").GetString());

        var page = await JsonAsync(await clients["a"].GetAsync("/api/v1/notifications?unread_only=true"));
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.Equal(1, page.GetProperty("unread_count").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await clients["a"].PostAsync("/api/v1/notifications/read-all", null)).StatusCode);
        Assert.Equal(0, await UnreadAsync("a"));
        Assert.Equal(2, (await NoticesAsync("a")).Count);
    }

    /// <summary>
    /// The phone and the browser mark the same notice read at once. Another connection holds the row and sets
    /// the FIRST read time; the request that waited behind it must keep that time, not write its own.
    /// </summary>
    [Fact]
    public async Task Marking_read_while_another_device_does_keeps_the_first_read_time()
    {
        await CreateAsync(users["a"].UserId);
        var id = (await NoticesAsync("a"))[0].GetProperty("notification_id").GetString()!;
        var first = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        await using var connection = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand("SELECT 1 FROM notification WHERE notification_id = $1 FOR UPDATE", connection, transaction))
        {
            hold.Parameters.AddWithValue(id);
            await hold.ExecuteScalarAsync();
        }
        var second = clients["a"].PostAsync($"/api/v1/notifications/{id}/read", null);
        await Task.Delay(500);
        Assert.False(second.IsCompleted, "The second device must wait for the row, not read past it.");
        await using (var mark = new NpgsqlCommand("UPDATE notification SET read_at = $1 WHERE notification_id = $2", connection, transaction))
        {
            mark.Parameters.AddWithValue(first);
            mark.Parameters.AddWithValue(id);
            await mark.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await second).StatusCode);
        Assert.Equal(first, await Db(db => db.Set<Notification>().IgnoreQueryFilters().Where(n => n.NotificationId == id).Select(n => n.ReadAt).SingleAsync()));
    }

    /// <summary>
    /// Verify and return race on a done order: one wins, the other's save rolls back — and its notice with
    /// it. A notice written outside the business save would leave the engineer told both.
    /// </summary>
    [Fact]
    public async Task The_losing_side_of_a_race_leaves_no_notice()
    {
        var id = await PlantDoneAsync();

        var verify = SendRawAsync("manager", HttpMethod.Post, $"/api/v1/work-orders/{id}/verify", new { note = "Đạt" });
        var reject = SendRawAsync("manager2", HttpMethod.Post, $"/api/v1/work-orders/{id}/return", new { note = "Làm lại" });
        var codes = (await Task.WhenAll(verify, reject)).Select(response => response.StatusCode).Order().ToArray();

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], codes);
        Assert.Single(await NoticesAsync("a"));
    }

    private async Task<string> CreateAsync(string? assignee)
    {
        var json = await JsonAsync(await SendAsync("manager", HttpMethod.Post, "/api/v1/work-orders", new
        {
            task_kind = "inspection", title = "Notification test order", segment_id = segment, assigned_to = assignee, due_date = "2026-10-12",
        }));
        return json.GetProperty("work_order_id").GetString()!;
    }

    private Task<string> PlantDoneAsync() => Db(async db =>
    {
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var now = DateTime.UtcNow;
        var order = new WorkOrder
        {
            CommuneId = home, TaskKind = TaskKind.Inspection, Title = "Race order", SegmentId = segment, CreatedBy = users["manager"].UserId,
            AssignedTo = users["a"].UserId, AssignedAt = now, StartedAt = now, CompletedAt = now, WoStatus = WorkOrderStatus.Done,
            ReportNote = "Planted as done", CreatedAt = now, UpdatedAt = now,
        };
        db.Add(order);
        await db.SaveChangesAsync();
        return order.WorkOrderId;
    });

    private async Task<HttpResponseMessage> SendAsync(string who, HttpMethod method, string path, object body)
    {
        var response = await SendRawAsync(who, method, path, body);
        Assert.True(response.IsSuccessStatusCode, $"{who} {method} {path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return response;
    }

    private Task<HttpResponseMessage> SendRawAsync(string who, HttpMethod method, string path, object body)
        => clients[who].SendAsync(new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) });

    /// <summary>The caller's notices, newest first, as the API returns them.</summary>
    private async Task<List<JsonElement>> NoticesAsync(string who)
    {
        var page = await JsonAsync(await clients[who].GetAsync("/api/v1/notifications?page_size=200"));
        return [.. page.GetProperty("items").EnumerateArray()];
    }

    private async Task<int> UnreadAsync(string who)
        => (await JsonAsync(await clients[who].GetAsync("/api/v1/notifications/unread-count"))).GetProperty("unread_count").GetInt32();

    private static string[] Types(IEnumerable<JsonElement> notices) => [.. notices.Select(notice => notice.GetProperty("type").GetString()!)];

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
