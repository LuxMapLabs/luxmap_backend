using System.Net;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-28 — <c>/statistics/fixture-status</c> and <c>/statistics/repair-timeliness</c>. Every class run gets its own two
/// communes, so each figure below is an EXACT count of what the test planted — "figures match a manual count" is the
/// ticket's acceptance criterion.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class StatisticsTests(AssetImportFixture factory) : IAsyncLifetime
{
    private readonly Dictionary<string, AppUser> users = [];
    private readonly Dictionary<string, HttpClient> clients = [];
    private string home = null!;
    private string foreign = null!;
    private string road = null!;
    private string otherRoad = null!;
    private string foreignRoad = null!;

    private Task<T> Db<T>(Func<LuxMapDbContext, Task<T>> work) => factory.QueryAsync(work);

    public async Task InitializeAsync()
    {
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var communes = new[] { new AdministrativeUnit { Name = "Stats home " + Guid.NewGuid() }, new AdministrativeUnit { Name = "Stats foreign " + Guid.NewGuid() } };
            db.AddRange(communes);
            await db.SaveChangesAsync();
            home = communes[0].CommuneId; foreign = communes[1].CommuneId;
            foreach (var (key, role) in new[] { ("manager", UserRole.Manager), ("engineer", UserRole.FieldEngineer), ("superior", UserRole.Superior) })
            {
                var user = new AppUser
                {
                    Username = "st" + Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid", FullName = key,
                    PasswordHash = "", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", PasswordSetAt = DateTime.UtcNow, Role = role,
                };
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, factory.AccountPassword);
                users[key] = user; db.Add(user);
            }
            await db.SaveChangesAsync();
            foreach (var user in users.Values)
            {
                db.Add(new AppUserCommune { UserId = user.UserId, CommuneId = home });
                if (user.Role == UserRole.Superior) db.Add(new AppUserCommune { UserId = user.UserId, CommuneId = foreign });
            }
            var roads = new[] { home, home, foreign }.Select(c => new RoadSegment
            {
                CommuneId = c, SegmentName = "Stats road", RoadClass = RoadClass.InterVillage, DataSource = DataSource.Simulated,
                Geom = new LineString([new Coordinate(108, 16), new Coordinate(108.01, 16.01)]) { SRID = 4326 }, LengthM = 100,
            }).ToArray();
            db.AddRange(roads);
            await db.SaveChangesAsync();
            road = roads[0].SegmentId; otherRoad = roads[1].SegmentId; foreignRoad = roads[2].SegmentId;
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
            foreach (var table in new[] { "work_order", "pole", "road_segment" })
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

    // ---- fixture-status ---------------------------------------------------------------------------------

    [Fact]
    public async Task Every_status_is_counted_and_never_surveyed_is_part_of_unknown_not_a_fifth_bucket()
    {
        await PlantPoles(home, road, DataSource.Field,
            FixtureStatus.Normal, FixtureStatus.Normal, FixtureStatus.Normal, FixtureStatus.Dim, FixtureStatus.Dim,
            FixtureStatus.Out, FixtureStatus.Unknown, null, null);
        await PlantPoles(home, road, DataSource.PublicImagery, FixtureStatus.Normal);

        var body = await Get("superior", $"fixture-status?commune_id={home}");

        Assert.Equal(["data_source"], Strings(body.GetProperty("group_by")));
        var rows = body.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        AssertRow(rows[0], "field", poles: 9, normal: 3, dim: 2, @out: 1, unknown: 3, neverSurveyed: 2);
        AssertRow(rows[1], "public_imagery", poles: 1, normal: 1, dim: 0, @out: 0, unknown: 0, neverSurveyed: 0);
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("commune_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("segment_id").ValueKind);
        Assert.Equal(new[] { "commune_id", "data_source", "dim", "never_surveyed", "normal", "out", "pole_count", "segment_id", "unknown" },
            rows[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_calibration_rig_is_left_out_unless_asked_for_by_name_as_on_the_map()
    {
        await PlantPoles(home, road, DataSource.Field, FixtureStatus.Normal);
        await PlantPoles(home, road, DataSource.CalibrationRig, FixtureStatus.Out, FixtureStatus.Dim);

        var byDefault = await Get("superior", $"fixture-status?commune_id={home}");
        Assert.Equal(["field"], byDefault.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("data_source").GetString()));

        var rig = (await Get("superior", $"fixture-status?commune_id={home}&data_source=calibration_rig")).GetProperty("rows")[0];
        AssertRow(rig, "calibration_rig", poles: 2, normal: 0, dim: 1, @out: 1, unknown: 0, neverSurveyed: 0);
    }

    [Fact]
    public async Task Grouping_by_commune_and_segment_gives_one_row_per_group_in_id_order()
    {
        await PlantPoles(home, road, DataSource.Field, FixtureStatus.Out, FixtureStatus.Normal);
        await PlantPoles(home, otherRoad, DataSource.Field, FixtureStatus.Dim);
        await PlantPoles(foreign, foreignRoad, DataSource.Field, (FixtureStatus?)null);

        var body = await Get("superior", "fixture-status?group_by=segment,commune");

        Assert.Equal(["data_source", "commune_id", "segment_id"], Strings(body.GetProperty("group_by")));
        var rows = body.GetProperty("rows").EnumerateArray()
            .Select(r => (r.GetProperty("commune_id").GetString(), r.GetProperty("segment_id").GetString(), r.GetProperty("pole_count").GetInt32()))
            .ToArray();
        // Expected order follows the ID rule (length, then ordinal) — never insert order: EF does not keep the
        // AddRange order when the database generates keys (CLAUDE.md, BE-REVIEW-02 7b).
        var expected = new (string?, string?, int)[] { (home, road, 2), (home, otherRoad, 1), (foreign, foreignRoad, 1) }
            .OrderBy(r => r.Item1!.Length).ThenBy(r => r.Item1, StringComparer.Ordinal)
            .ThenBy(r => r.Item2!.Length).ThenBy(r => r.Item2, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, rows);
    }

    [Fact]
    public async Task A_manager_counts_only_their_communes_and_naming_another_one_is_403()
    {
        await PlantPoles(home, road, DataSource.Field, FixtureStatus.Normal);
        await PlantPoles(foreign, foreignRoad, DataSource.Field, FixtureStatus.Out, FixtureStatus.Out);

        // The manager's scope is the home commune only; without commune_id the query filter still keeps them there.
        var rows = (await Get("manager", "fixture-status?group_by=commune")).GetProperty("rows").EnumerateArray().ToArray();
        var row = Assert.Single(rows);
        Assert.Equal(home, row.GetProperty("commune_id").GetString());
        Assert.Equal(1, row.GetProperty("pole_count").GetInt32());

        var refused = await clients["manager"].GetAsync($"/api/v1/statistics/fixture-status?commune_id={foreign}");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains(ErrorCodes.CommuneForbidden, await refused.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("fixture-status?group_by=road")]
    [InlineData("fixture-status?data_source=testbed")]
    [InlineData("repair-timeliness?group_by=segment")]
    [InlineData("repair-timeliness?from=2026-09-30&to=2026-09-01")]
    [InlineData("repair-timeliness?from=30/09/2026")]
    public async Task A_malformed_query_is_a_400(string query)
    {
        var response = await clients["superior"].GetAsync("/api/v1/statistics/" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ErrorCodes.ValidationFailed, await response.Content.ReadAsStringAsync());
    }

    // ---- repair-timeliness ------------------------------------------------------------------------------

    /// <summary>
    /// Local time is UTC+7, and a night runs from 12:00 to 12:00. The plants sit on both sides of midnight AND of
    /// noon, so a calendar-date rule and a night rule give different answers.
    /// </summary>
    [Fact]
    public async Task On_time_is_decided_by_the_night_of_completion_not_the_calendar_date()
    {
        var due = new DateOnly(2026, 9, 10);
        // 01:00 on the 11th, local: still the night of the 10th → on time.
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Done, due, Local(2026, 9, 11, 1));
        // 13:00 on the 11th, local: the night of the 11th → late.
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Verified, due, Local(2026, 9, 11, 13));
        // Earlier than due, verified → on time.
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Verified, due, Local(2026, 9, 8, 22));
        // No due date: counted as completed, outside the rate.
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Done, null, Local(2026, 9, 12, 20));
        // Not repairs, not finished, or outside the window: none of these count.
        await PlantOrder(home, TaskKind.Inspection, WorkOrderStatus.Done, due, Local(2026, 9, 20, 20));
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Cancelled, due, null);
        // The database allows a cancelled order to carry a completion time; it still never counts as finished.
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Cancelled, due, Local(2026, 9, 9, 20));
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Done, due, Local(2026, 9, 1, 11)); // night of 31/08
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Done, due, Local(2026, 10, 1, 12)); // night of 01/10

        var body = await Get("superior", $"repair-timeliness?commune_id={home}&from=2026-09-01&to=2026-09-30");

        Assert.Equal("2026-09-01", body.GetProperty("from").GetString());
        Assert.Equal("2026-09-30", body.GetProperty("to").GetString());
        var row = Assert.Single(body.GetProperty("rows").EnumerateArray());
        Assert.Equal(4, row.GetProperty("completed").GetInt32());
        Assert.Equal(2, row.GetProperty("on_time").GetInt32());
        Assert.Equal(1, row.GetProperty("late").GetInt32());
        Assert.Equal(1, row.GetProperty("no_due_date").GetInt32());
        Assert.Equal(0.6667, row.GetProperty("on_time_rate").GetDouble());
        Assert.Equal(0, row.GetProperty("open_overdue").GetInt32());
    }

    [Fact]
    public async Task Open_repairs_past_their_due_night_are_overdue_and_the_rate_is_null_with_nothing_due()
    {
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.InProgress, new DateOnly(2026, 1, 1), null);
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Open, new DateOnly(2026, 1, 2), null);
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Assigned, new DateOnly(2099, 1, 1), null);
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Open, null, null);
        await PlantOrder(home, TaskKind.Inspection, WorkOrderStatus.Open, new DateOnly(2026, 1, 1), null);
        await PlantOrder(foreign, TaskKind.Repair, WorkOrderStatus.Open, new DateOnly(2026, 1, 1), null);

        var body = await Get("superior", "repair-timeliness?group_by=commune");

        Assert.Equal(["commune_id"], Strings(body.GetProperty("group_by")));
        var rows = body.GetProperty("rows").EnumerateArray()
            .ToDictionary(r => r.GetProperty("commune_id").GetString()!);
        Assert.Equal(2, rows[home].GetProperty("open_overdue").GetInt32());
        Assert.Equal(1, rows[foreign].GetProperty("open_overdue").GetInt32());
        Assert.Equal(0, rows[home].GetProperty("completed").GetInt32());
        Assert.Equal(JsonValueKind.Null, rows[home].GetProperty("on_time_rate").ValueKind);
    }

    [Fact]
    public async Task Without_bounds_the_window_is_the_last_thirty_nights_ending_tonight()
    {
        var body = await Get("superior", $"repair-timeliness?commune_id={home}");

        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"));
        var tonight = DateOnly.FromDateTime(local.DateTime.Hour < 12 ? local.DateTime.AddDays(-1) : local.DateTime);
        Assert.Equal(tonight.ToString("yyyy-MM-dd"), body.GetProperty("to").GetString());
        Assert.Equal(tonight.AddDays(-29).ToString("yyyy-MM-dd"), body.GetProperty("from").GetString());
        Assert.Empty(body.GetProperty("rows").EnumerateArray());
    }

    // ---- helpers ------------------------------------------------------------------------------------------

    private static DateTime Local(int year, int month, int day, int hour)
        => new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Utc).AddHours(-7);

    private async Task<JsonElement> Get(string who, string path)
    {
        var response = await clients[who].GetAsync("/api/v1/statistics/" + path);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path}: {(int)response.StatusCode} {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(x => x.GetString()!).ToArray();

    private static void AssertRow(JsonElement row, string source, int poles, int normal, int dim, int @out, int unknown, int neverSurveyed)
    {
        Assert.Equal(source, row.GetProperty("data_source").GetString());
        Assert.Equal(
            (poles, normal, dim, @out, unknown, neverSurveyed),
            (row.GetProperty("pole_count").GetInt32(), row.GetProperty("normal").GetInt32(), row.GetProperty("dim").GetInt32(),
                row.GetProperty("out").GetInt32(), row.GetProperty("unknown").GetInt32(), row.GetProperty("never_surveyed").GetInt32()));
    }

    /// <summary>One pole per status; <c>null</c> plants a pole with NO status row (never surveyed).</summary>
    private Task PlantPoles(string commune, string segment, DataSource source, params FixtureStatus?[] statuses)
        => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            foreach (var status in statuses)
            {
                var pole = new Pole { CommuneId = commune, SegmentId = segment, DataSource = source, Geom = new Point(108, 16) { SRID = 4326 } };
                db.Add(pole);
                await db.SaveChangesAsync();
                if (status is not { } known) continue;
                db.Add(new PoleCurrentStatus
                {
                    PoleId = pole.PoleId, CommuneId = commune, FixtureStatus = known,
                    StatusConfidence = known == FixtureStatus.Unknown ? null : 0.9,
                    LastSeenAt = known == FixtureStatus.Unknown ? null : new DateTime(2026, 10, 3, 14, 0, 0, DateTimeKind.Utc),
                    UpdatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
            return 0;
        });

    private Task PlantOrder(string commune, TaskKind kind, WorkOrderStatus status, DateOnly? due, DateTime? completed)
        => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var now = DateTime.UtcNow;
            var assigned = status is not (WorkOrderStatus.Open or WorkOrderStatus.Cancelled);
            db.Add(new WorkOrder
            {
                CommuneId = commune, TaskKind = kind, Title = "Stats order", WoStatus = status, DueDate = due,
                SegmentId = kind == TaskKind.Survey ? null : (commune == home ? road : foreignRoad),
                CreatedBy = users["manager"].UserId, CreatedAt = now, UpdatedAt = now,
                AssignedTo = assigned ? users["engineer"].UserId : null, AssignedAt = assigned ? now : null,
                StartedAt = status is WorkOrderStatus.InProgress or WorkOrderStatus.Done or WorkOrderStatus.Verified ? now : null,
                CompletedAt = completed, ReportNote = completed is null ? null : "Planted as finished",
                ClosedAt = status is WorkOrderStatus.Verified or WorkOrderStatus.Cancelled ? now : null,
            });
            await db.SaveChangesAsync();
            return 0;
        });
}
