using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Citizen.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using System.Net;
using System.Text;
using System.Text.Json;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-28 / ST-12 — fixture-status, repair-timeliness and work-order statistics. Every class run gets its own two
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
            foreach (var (key, role) in new[] { ("manager", UserRole.Manager), ("engineer", UserRole.FieldEngineer), ("superior", UserRole.Superior), ("foreign_superior", UserRole.Superior) })
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
            foreach (var (key, user) in users)
                db.Add(new AppUserCommune { UserId = user.UserId, CommuneId = key == "foreign_superior" ? foreign : home });
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
            foreach (var table in new[] { "citizen_submission", "citizen_report", "qr_code", "work_order_segment", "work_order", "fault", "pole", "road_segment" })
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
        Assert.Equal(new[] { "commune_id", "data_source", "dim", "never_surveyed", "normal", "out", "pole_count", "segment_id", "unassigned_segment_pole_count", "unknown" },
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
        var foreignBody = await Get("foreign_superior", "fixture-status?group_by=segment,commune");
        var rows = body.GetProperty("rows").EnumerateArray().Concat(foreignBody.GetProperty("rows").EnumerateArray())
            .Select(r => (r.GetProperty("commune_id").GetString(), r.GetProperty("segment_id").GetString(), r.GetProperty("pole_count").GetInt32()))
            .ToArray();
        // Expected order follows the ID rule (length, then ordinal) — never insert order: EF does not keep the
        // AddRange order when the database generates keys (docs/agents/traps/assets.md, BE-REVIEW-02 7b).
        var expected = new (string?, string?, int)[] { (home, road, 2), (home, otherRoad, 1), (foreign, foreignRoad, 1) }
            .OrderBy(r => r.Item1!.Length).ThenBy(r => r.Item1, StringComparer.Ordinal)
            .ThenBy(r => r.Item2!.Length).ThenBy(r => r.Item2, StringComparer.Ordinal).ToArray();
        // Each account receives its own ordered response; concatenating responses has no global order.
        Assert.Equal(expected.Where(r => r.Item1 == home), rows.Where(r => r.Item1 == home));
        Assert.Equal(expected.Where(r => r.Item1 == foreign), rows.Where(r => r.Item1 == foreign));
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
    [InlineData("work-orders?group_by=segment")]
    [InlineData("work-orders?format=xml")]
    [InlineData("work-orders?from=30/09/2026")]
    [InlineData("work-orders?to=2026-02-30")]
    [InlineData("fixture-status?group_by=task_kind")]
    [InlineData("repair-timeliness?group_by=task_kind")]
    [InlineData("fixture-status?group_by=road")]
    [InlineData("fixture-status?data_source=testbed")]
    [InlineData("repair-timeliness?group_by=segment")]
    [InlineData("repair-timeliness?from=2026-09-30&to=2026-09-01")]
    [InlineData("repair-timeliness?from=30/09/2026")]
    [InlineData("repair-timeliness?to=9999-12-31")]
    [InlineData("repair-timeliness?to=0001-01-01")]
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
        // Both edges of the window from the inside: the first hour of the night of `from`, and the morning
        // after `to` — still the night of 30/09, so it counts (on time against a due date of 30/09).
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Done, new DateOnly(2026, 9, 1), Local(2026, 9, 1, 12));
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Done, new DateOnly(2026, 9, 30), Local(2026, 10, 1, 11));

        var body = await Get("superior", $"repair-timeliness?commune_id={home}&from=2026-09-01&to=2026-09-30");

        Assert.Equal("2026-09-01", body.GetProperty("from").GetString());
        Assert.Equal("2026-09-30", body.GetProperty("to").GetString());
        var row = Assert.Single(body.GetProperty("rows").EnumerateArray());
        Assert.Equal(6, row.GetProperty("completed").GetInt32());
        Assert.Equal(4, row.GetProperty("on_time").GetInt32());
        Assert.Equal(1, row.GetProperty("late").GetInt32());
        Assert.Equal(1, row.GetProperty("no_due_date").GetInt32());
        Assert.Equal(0.8, row.GetProperty("on_time_rate").GetDouble());
        Assert.Equal(0, row.GetProperty("open_overdue").GetInt32());
    }

    /// <summary>A repair due TONIGHT is not overdue yet; one due last night is. Tonight follows the 12:00 local rule.</summary>
    [Fact]
    public async Task A_repair_due_tonight_is_not_overdue_and_one_due_last_night_is()
    {
        var tonight = Tonight();
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Assigned, tonight, null);
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Assigned, tonight.AddDays(-1), null);

        var body = await Get("superior", $"repair-timeliness?commune_id={home}");

        // Skip only if the request itself crossed 12:00 local, where "tonight" legitimately moves.
        if (Tonight() != tonight) return;
        Assert.Equal(1, Assert.Single(body.GetProperty("rows").EnumerateArray()).GetProperty("open_overdue").GetInt32());
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
        var foreignBody = await Get("foreign_superior", "repair-timeliness?group_by=commune");
        var rows = body.GetProperty("rows").EnumerateArray().Concat(foreignBody.GetProperty("rows").EnumerateArray())
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

        var tonight = Tonight();
        Assert.Equal(tonight.ToString("yyyy-MM-dd"), body.GetProperty("to").GetString());
        Assert.Equal(tonight.AddDays(-29).ToString("yyyy-MM-dd"), body.GetProperty("from").GetString());
        Assert.Empty(body.GetProperty("rows").EnumerateArray());
    }

    // ---- work-orders (ST-12) ------------------------------------------------------------------------------

    [Fact]
    public async Task Work_order_counts_include_all_kinds_and_six_statuses_without_an_outer_total()
    {
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Open, null, null);
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Assigned, null, null);
        await PlantOrder(home, TaskKind.Inspection, WorkOrderStatus.InProgress, null, null);
        await PlantOrder(home, TaskKind.Inspection, WorkOrderStatus.Done, null, Local(2026, 9, 1, 20));
        await PlantOrder(home, TaskKind.Survey, WorkOrderStatus.Verified, null, Local(2026, 9, 1, 20));
        await PlantOrder(home, TaskKind.Survey, WorkOrderStatus.Cancelled, null, null);

        var body = await Get("superior", "work-orders");
        Assert.Equal(new[] { "as_of", "group_by", "rows" }, body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.EndsWith("Z", body.GetProperty("as_of").GetString());
        Assert.Empty(Strings(body.GetProperty("group_by")));
        var row = Assert.Single(body.GetProperty("rows").EnumerateArray());
        AssertOrderRow(row, null, null, 1, 1, 1, 1, 1, 1, 6);
        Assert.Equal(new[] { "assigned", "cancelled", "commune_id", "done", "in_progress", "open", "task_kind", "total", "verified" },
            row.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));

        var grouped = await Get("superior", "work-orders?group_by=task_kind");
        Assert.Equal(["task_kind"], Strings(grouped.GetProperty("group_by")));
        var rows = grouped.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(3, rows.Length);
        AssertOrderRow(rows[0], null, "repair", 1, 1, 0, 0, 0, 0, 2);
        AssertOrderRow(rows[1], null, "inspection", 0, 0, 1, 1, 0, 0, 2);
        AssertOrderRow(rows[2], null, "survey", 0, 0, 0, 0, 1, 1, 2);
    }

    [Fact]
    public async Task Work_order_groups_cover_two_visible_communes_and_preserve_requested_dimension_order()
    {
        await PlantOrder(home, TaskKind.Survey, WorkOrderStatus.Open, null, null);
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Cancelled, null, null);
        await PlantOrder(foreign, TaskKind.Inspection, WorkOrderStatus.Assigned, null, null);
        await PlantOrder(foreign, TaskKind.Repair, WorkOrderStatus.Open, null, null);
        // A signed legacy token can still name two communes (ACCT-1); never create an invalid two-commune account.
        using var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<LuxMap.Modules.Identity.Auth.AccessTokenIssuer>()
            .Issue(users["manager"], [home, foreign]).Token;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        foreach (var dimensions in new[] { "commune,task_kind", "task_kind,commune" })
        {
            var response = await client.GetAsync("/api/v1/statistics/work-orders?group_by=" + dimensions);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(dimensions.Split(','), Strings(body.GetProperty("group_by")));
            var rows = body.GetProperty("rows").EnumerateArray().ToArray();
            var communes = new[] { home, foreign }.OrderBy(c => c.Length).ThenBy(c => c, StringComparer.Ordinal).ToArray();
            Assert.Equal(4, rows.Length);
            for (var i = 0; i < communes.Length; i++)
            {
                if (communes[i] == home)
                {
                    AssertOrderRow(rows[i * 2], home, "repair", 0, 0, 0, 0, 0, 1, 1);
                    AssertOrderRow(rows[i * 2 + 1], home, "survey", 1, 0, 0, 0, 0, 0, 1);
                }
                else
                {
                    AssertOrderRow(rows[i * 2], foreign, "repair", 1, 0, 0, 0, 0, 0, 1);
                    AssertOrderRow(rows[i * 2 + 1], foreign, "inspection", 0, 1, 0, 0, 0, 0, 1);
                }
            }
        }
        var narrowed = await client.GetAsync($"/api/v1/statistics/work-orders?commune_id={home}&commune_id={foreign}");
        Assert.Equal(HttpStatusCode.OK, narrowed.StatusCode);
        var total = JsonDocument.Parse(await narrowed.Content.ReadAsStringAsync()).RootElement.GetProperty("rows")[0].GetProperty("total").GetInt32();
        Assert.Equal(4, total);
    }

    [Fact]
    public async Task Work_order_dates_are_inclusive_and_either_bound_excludes_unscheduled_orders()
    {
        foreach (var date in new DateOnly?[] { null, new(2026, 8, 31), new(2026, 9, 1), new(2026, 9, 30), new(2026, 10, 1) })
            await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Open, null, null, scheduled: date);
        foreach (var (query, count) in new[]
        {
            ("", 5), ("?from=2026-09-01&to=2026-09-30", 2),
            ("?from=2026-09-01", 3), ("?to=2026-09-30", 3),
            ("?from=2026-09-01&to=2026-09-01", 1),
            ("?from=2000-01-01&to=2099-12-31", 4),
        })
        {
            var body = await Get("manager", "work-orders" + query);
            AssertOrderRow(Assert.Single(body.GetProperty("rows").EnumerateArray()), null, null, count, 0, 0, 0, 0, 0, count);
        }
    }

    [Theory]
    [InlineData("from=2026-09-30&to=2026-09-01", "from")]
    [InlineData("from=1999-12-31", "from")]
    [InlineData("to=2100-01-01", "to")]
    [InlineData("from=9999-12-31", "from")]
    [InlineData("to=0001-01-01", "to")]
    public async Task Work_order_date_validation_names_the_invalid_field(string query, string field)
    {
        var response = await clients["superior"].GetAsync("/api/v1/statistics/work-orders?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error");
        Assert.Equal(ErrorCodes.ValidationFailed, error.GetProperty("code").GetString());
        Assert.Equal(field, error.GetProperty("details").GetProperty("field").GetString());
    }

    [Fact]
    public async Task Work_order_segment_filter_matches_direct_and_survey_targets_without_multiplying_orders()
    {
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Open, null, null);
        await PlantOrder(home, TaskKind.Survey, WorkOrderStatus.Assigned, null, null, targets: [road, otherRoad]);
        var first = await Get("superior", $"work-orders?segment_id=%20{road}%20");
        AssertOrderRow(Assert.Single(first.GetProperty("rows").EnumerateArray()), null, null, 1, 1, 0, 0, 0, 0, 2);
        var second = await Get("superior", $"work-orders?segment_id={otherRoad}");
        AssertOrderRow(Assert.Single(second.GetProperty("rows").EnumerateArray()), null, null, 0, 1, 0, 0, 0, 0, 1);
        var all = await Get("superior", "work-orders?segment_id=%20%20");
        AssertOrderRow(Assert.Single(all.GetProperty("rows").EnumerateArray()), null, null, 1, 1, 0, 0, 0, 0, 2);
        var empty = await Get("superior", "work-orders?segment_id=SEG-no-orders");
        Assert.Empty(empty.GetProperty("rows").EnumerateArray());
    }

    [Fact]
    public async Task Work_order_statistics_enforce_scope_and_refuse_field_engineers()
    {
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Open, null, null);
        await PlantOrder(foreign, TaskKind.Survey, WorkOrderStatus.Cancelled, null, null);
        var body = await Get("manager", "work-orders?group_by=commune");
        AssertOrderRow(Assert.Single(body.GetProperty("rows").EnumerateArray()), home, null, 1, 0, 0, 0, 0, 0, 1);
        var refused = await clients["manager"].GetAsync($"/api/v1/statistics/work-orders?commune_id={foreign}");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains(ErrorCodes.CommuneForbidden, await refused.Content.ReadAsStringAsync());
        var engineer = await clients["engineer"].GetAsync("/api/v1/statistics/work-orders");
        Assert.Equal(HttpStatusCode.Forbidden, engineer.StatusCode);
        Assert.Contains(ErrorCodes.RoleForbidden, await engineer.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Work_order_csv_uses_the_json_columns_and_raw_counts()
    {
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Open, null, null);
        var response = await clients["superior"].GetAsync("/api/v1/statistics/work-orders?format=csv&group_by=task_kind");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("work-orders-", response.Content.Headers.ContentDisposition!.FileNameStar);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, bytes[..3]);
        Assert.Equal("commune_id,task_kind,open,assigned,in_progress,done,verified,cancelled,total\r\n,repair,1,0,0,0,0,0,1\r\n",
            System.Text.Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public async Task Empty_work_order_statistics_have_no_synthetic_zero_row()
    {
        var body = await Get("superior", "work-orders");
        Assert.Empty(body.GetProperty("rows").EnumerateArray());
    }

    private static void AssertOrderRow(JsonElement row, string? commune, string? kind,
        int open, int assigned, int inProgress, int done, int verified, int cancelled, int total)
    {
        Assert.Equal(commune, row.GetProperty("commune_id").GetString());
        Assert.Equal(kind, row.GetProperty("task_kind").GetString());
        Assert.Equal(new[] { open, assigned, inProgress, done, verified, cancelled, total },
            new[] { "open", "assigned", "in_progress", "done", "verified", "cancelled", "total" }.Select(key => row.GetProperty(key).GetInt32()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repair_segment_filters_finished_and_overdue_orders_without_counting_unassigned(bool byCommune)
    {
        var due = new DateOnly(2026, 9, 10);
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Done, due, Local(2026, 9, 10, 22));
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Verified, due, Local(2026, 9, 11, 22));
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Done, null, Local(2026, 9, 12, 22));
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Open, Tonight().AddDays(-1), null);
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Done, due, Local(2026, 9, 10, 22), otherRoad);
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Assigned, Tonight().AddDays(-1), null, otherRoad);
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Done, due, Local(2026, 9, 11, 22), unassigned: true);
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.InProgress, Tonight().AddDays(-1), null, unassigned: true);

        var path = $"repair-timeliness?from=2026-09-01&to=2026-09-30&group_by={(byCommune ? "commune" : "")}";
        var filtered = await Get("superior", path + $"&segment_id=%20{road}%20");
        var row = Assert.Single(filtered.GetProperty("rows").EnumerateArray());
        AssertTimeliness(row, completed: 3, onTime: 1, late: 1, noDueDate: 1, overdue: 1, rate: 0.5);
        Assert.Equal(byCommune ? home : null, row.GetProperty("commune_id").GetString());

        var other = await Get("superior", path + $"&segment_id={otherRoad}");
        AssertTimeliness(Assert.Single(other.GetProperty("rows").EnumerateArray()), 1, 1, 0, 0, 1, 1);
        foreach (var suffix in new[] { "", "&segment_id=", "&segment_id=%20%20%20" })
        {
            var all = await Get("superior", path + suffix);
            AssertTimeliness(Assert.Single(all.GetProperty("rows").EnumerateArray()), 5, 2, 2, 1, 3, 0.5);
        }
    }

    [Fact]
    public async Task Repair_segment_without_orders_or_outside_scope_returns_no_rows()
    {
        await PlantOrder(home, TaskKind.Repair, WorkOrderStatus.Done, new DateOnly(2026, 9, 10), Local(2026, 9, 10, 22));
        await PlantOrder(foreign, TaskKind.Repair, WorkOrderStatus.Done, new DateOnly(2026, 9, 10), Local(2026, 9, 10, 22));
        await PlantOrder(foreign, TaskKind.Repair, WorkOrderStatus.Open, Tonight().AddDays(-1), null);
        var path = "repair-timeliness?from=2026-09-01&to=2026-09-30";
        // Prove the foreign orders exist and are visible to their own commune.
        var visible = await Get("foreign_superior", path + $"&segment_id={foreignRoad}");
        AssertTimeliness(Assert.Single(visible.GetProperty("rows").EnumerateArray()), 1, 1, 0, 0, 1, 1);
        foreach (var segment in new[] { otherRoad, "SEG-nonexistent", foreignRoad })
        {
            var body = await Get("superior", path + $"&segment_id={segment}");
            Assert.Empty(body.GetProperty("rows").EnumerateArray());
        }
    }

    private static void AssertTimeliness(JsonElement row, int completed, int onTime, int late, int noDueDate, int overdue, double rate)
    {
        Assert.Equal((completed, onTime, late, noDueDate, overdue),
            (row.GetProperty("completed").GetInt32(), row.GetProperty("on_time").GetInt32(),
                row.GetProperty("late").GetInt32(), row.GetProperty("no_due_date").GetInt32(), row.GetProperty("open_overdue").GetInt32()));
        Assert.Equal(rate, row.GetProperty("on_time_rate").GetDouble());
    }

    // ---- citizen-feedback (ST-13) -----------------------------------------------------------------------

    [Fact]
    public async Task Citizen_items_count_each_status_once_and_keep_sources_separate()
    {
        await PlantCitizen(home, road, DataSource.Field, new(2020, 1, 1), CitizenReportStatus.Pending, submissions: 2);
        foreach (var status in new[] { CitizenReportStatus.Accepted, CitizenReportStatus.Accepted,
                     CitizenReportStatus.Merged, CitizenReportStatus.Rejected })
            await PlantCitizen(home, road, DataSource.Field, new(2026, 9, 10), status);
        await PlantCitizen(home, road, DataSource.PublicImagery, new(2026, 9, 10), CitizenReportStatus.Pending);
        await PlantCitizen(home, road, DataSource.Simulated, new(2026, 9, 10), CitizenReportStatus.Pending);

        var body = await Get("superior", "citizen-feedback");
        Assert.Equal(["data_source"], Strings(body.GetProperty("group_by")));
        Assert.EndsWith("Z", body.GetProperty("as_of").GetString());
        var rows = body.GetProperty("rows").EnumerateArray().ToArray();
        // Each provenance remains a separate row, in enum order.
        Assert.Equal(["field", "public_imagery", "simulated"], rows.Select(r => r.GetProperty("data_source").GetString()));
        AssertCitizenRow(rows[0], 1, 2, 1, 1, 5, 0.8);
        AssertCitizenRow(rows[1], 1, 0, 0, 0, 1, 0);
        AssertCitizenRow(rows[2], 1, 0, 0, 0, 1, 0);
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("commune_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("segment_id").ValueKind);
        Assert.Equal(new[] { "accepted", "commune_id", "data_source", "merged", "pending", "rejected", "resolution_rate", "segment_id", "total" },
            rows[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Citizen_segment_groups_keep_unassigned_poles_last_without_losing_the_commune_total()
    {
        await PlantCitizen(home, road, DataSource.Field, new(2026, 9, 10), CitizenReportStatus.Pending);
        await PlantCitizen(home, otherRoad, DataSource.Field, new(2026, 9, 10), CitizenReportStatus.Rejected);
        await PlantCitizen(home, null, DataSource.Field, new(2026, 9, 10), CitizenReportStatus.Pending);
        var rows = (await Get("superior", "citizen-feedback?group_by=segment")).GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(3, rows.Length);
        Assert.Equal(new[] { road, otherRoad }.OrderBy(x => x.Length).ThenBy(x => x, StringComparer.Ordinal),
            rows.Take(2).Select(r => r.GetProperty("segment_id").GetString()));
        Assert.Equal(JsonValueKind.Null, rows[2].GetProperty("segment_id").ValueKind);
        AssertCitizenRow(rows[2], 1, 0, 0, 0, 1, 0);
        Assert.Equal(3, rows.Sum(r => r.GetProperty("total").GetInt32()));
        Assert.Equal(3, (await Get("superior", "citizen-feedback")).GetProperty("rows")[0].GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Citizen_groups_cover_two_visible_communes_and_preserve_requested_dimension_order()
    {
        await PlantCitizen(home, road, DataSource.Field, new(2026, 9, 10), CitizenReportStatus.Pending);
        await PlantCitizen(foreign, foreignRoad, DataSource.Field, new(2026, 9, 10), CitizenReportStatus.Rejected);
        // Legacy multi-commune JWT, without changing the one-commune account rule (ACCT-1).
        using var scope = factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<AccessTokenIssuer>().Issue(users["superior"], [home, foreign]);
        clients["superior"].DefaultRequestHeaders.Authorization = new("Bearer", token.Token);
        var body = await Get("superior", $"citizen-feedback?group_by=commune,segment&commune_id={home}&commune_id={foreign}");
        Assert.Equal(["data_source", "commune_id", "segment_id"], Strings(body.GetProperty("group_by")));
        var rows = body.GetProperty("rows").EnumerateArray().ToArray();
        var expected = new (string?, string?)[] { (home, road), (foreign, foreignRoad) }
            .OrderBy(x => x.Item1!.Length).ThenBy(x => x.Item1, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, rows.Select(r => (r.GetProperty("commune_id").GetString(), r.GetProperty("segment_id").GetString())));
        Assert.Equal([1, 1], rows.Select(r => r.GetProperty("total").GetInt32()));
        var reversed = await Get("superior", "citizen-feedback?group_by=segment,commune");
        Assert.Equal(["data_source", "segment_id", "commune_id"], Strings(reversed.GetProperty("group_by")));
    }

    [Fact]
    public async Task Citizen_calibration_rig_requires_an_explicit_source_filter()
    {
        await PlantCitizen(home, road, DataSource.Field, new(2026, 9, 10), CitizenReportStatus.Pending);
        await PlantCitizen(home, road, DataSource.CalibrationRig, new(2026, 9, 10), CitizenReportStatus.Rejected);
        var rows = (await Get("superior", "citizen-feedback")).GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal("field", Assert.Single(rows).GetProperty("data_source").GetString());
        var rig = Assert.Single((await Get("superior", "citizen-feedback?data_source=calibration_rig")).GetProperty("rows").EnumerateArray());
        Assert.Equal("calibration_rig", rig.GetProperty("data_source").GetString());
        AssertCitizenRow(rig, 0, 0, 0, 1, 1, 1);
        var both = await Get("superior", "citizen-feedback?data_source=calibration_rig,field");
        // Alphabetic sorting would put calibration_rig first; enum sorting puts field first.
        Assert.Equal(["field", "calibration_rig"], both.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("data_source").GetString()));
    }

    [Fact]
    public async Task Citizen_date_bounds_use_night_of_inclusively_with_no_default_window()
    {
        // Plant time is now; night_of deliberately differs, so created_at filtering cannot pass.
        foreach (var day in new[] { 9, 10, 11, 12 })
            await PlantCitizen(home, road, DataSource.Field, new(2026, 9, day), CitizenReportStatus.Pending);
        var path = "citizen-feedback?from=2026-09-10&to=2026-09-11";
        AssertCitizenRow(Assert.Single((await Get("superior", path)).GetProperty("rows").EnumerateArray()), 2, 0, 0, 0, 2, 0);
        Assert.Equal(3, (await Get("superior", "citizen-feedback?from=2026-09-10")).GetProperty("rows")[0].GetProperty("total").GetInt32());
        Assert.Equal(3, (await Get("superior", "citizen-feedback?to=2026-09-11")).GetProperty("rows")[0].GetProperty("total").GetInt32());
        Assert.Equal(4, (await Get("superior", "citizen-feedback")).GetProperty("rows")[0].GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("from=1999-12-31", "from")]
    [InlineData("from=2100-01-01", "from")]
    [InlineData("to=1999-12-31", "to")]
    [InlineData("to=2100-01-01", "to")]
    [InlineData("from=2026-09-11&to=2026-09-10", "from")]
    [InlineData("format=xml", "format")]
    public async Task Citizen_invalid_bounds_and_format_name_the_field(string query, string field)
    {
        var response = await clients["superior"].GetAsync("/api/v1/statistics/citizen-feedback?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error");
        Assert.Equal(ErrorCodes.ValidationFailed, error.GetProperty("code").GetString());
        Assert.Equal(field, error.GetProperty("details").GetProperty("field").GetString());
    }

    [Theory]
    [InlineData("from=10/09/2026")]
    [InlineData("to=invalid")]
    [InlineData("group_by=data_source")]
    [InlineData("group_by=task_kind")]
    [InlineData("data_source=testbed")]
    public async Task Citizen_malformed_queries_are_validation_errors(string query)
    {
        var response = await clients["superior"].GetAsync("/api/v1/statistics/citizen-feedback?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ErrorCodes.ValidationFailed, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Citizen_segment_filter_uses_the_pole_and_trims_blank_values()
    {
        await PlantCitizen(home, road, DataSource.Field, new(2026, 9, 10), CitizenReportStatus.Pending);
        await PlantCitizen(home, road, DataSource.Field, new(2026, 9, 11), CitizenReportStatus.Pending);
        await PlantCitizen(home, null, DataSource.Field, new(2026, 9, 10), CitizenReportStatus.Pending);
        var row = Assert.Single((await Get("superior", $"citizen-feedback?segment_id=%20{road}%20")).GetProperty("rows").EnumerateArray());
        AssertCitizenRow(row, 2, 0, 0, 0, 2, 0);
        Assert.Equal(3, (await Get("superior", "citizen-feedback?segment_id=%20%20")).GetProperty("rows")[0].GetProperty("total").GetInt32());
        Assert.Empty((await Get("superior", $"citizen-feedback?segment_id={otherRoad}")).GetProperty("rows").EnumerateArray());
    }

    [Fact]
    public async Task Citizen_statistics_enforce_commune_scope_and_read_statistics_capability()
    {
        await PlantCitizen(home, road, DataSource.Field, new(2026, 9, 10), CitizenReportStatus.Pending);
        await PlantCitizen(foreign, foreignRoad, DataSource.Field, new(2026, 9, 10), CitizenReportStatus.Rejected);
        AssertCitizenRow(Assert.Single((await Get("manager", "citizen-feedback")).GetProperty("rows").EnumerateArray()), 1, 0, 0, 0, 1, 0);
        foreach (var (who, path, code) in new[]
                 {
                     ("manager", $"citizen-feedback?commune_id={foreign}", ErrorCodes.CommuneForbidden),
                     ("engineer", "citizen-feedback", ErrorCodes.RoleForbidden),
                 })
        {
            var response = await clients[who].GetAsync("/api/v1/statistics/" + path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains(code, await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Citizen_csv_has_json_columns_bom_crlf_and_rounded_rate()
    {
        await PlantCitizen(home, road, DataSource.Field, new(2026, 9, 10), CitizenReportStatus.Pending);
        await PlantCitizen(home, road, DataSource.Field, new(2026, 9, 11), CitizenReportStatus.Pending);
        await PlantCitizen(home, road, DataSource.Field, new(2026, 9, 12), CitizenReportStatus.Rejected);
        Assert.Equal(0.3333, (await Get("superior", "citizen-feedback")).GetProperty("rows")[0].GetProperty("resolution_rate").GetDouble());
        var response = await clients["superior"].GetAsync("/api/v1/statistics/citizen-feedback?format=csv");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, bytes.Take(3));
        Assert.Equal("data_source,commune_id,segment_id,pending,accepted,merged,rejected,total,resolution_rate\r\n"
            + "field,,,2,0,0,1,3,0.3333\r\n", Encoding.UTF8.GetString(bytes[3..]));
    }

    private static void AssertCitizenRow(JsonElement row, int pending, int accepted, int merged, int rejected, int total, double rate)
    {
        Assert.Equal((pending, accepted, merged, rejected, total),
            (row.GetProperty("pending").GetInt32(), row.GetProperty("accepted").GetInt32(), row.GetProperty("merged").GetInt32(),
                row.GetProperty("rejected").GetInt32(), row.GetProperty("total").GetInt32()));
        Assert.Equal(rate, row.GetProperty("resolution_rate").GetDouble());
    }

    private Task PlantCitizen(string commune, string? segment, DataSource source, DateOnly night,
        CitizenReportStatus status, int submissions = 0)
        => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var now = DateTime.UtcNow;
            var pole = new Pole { CommuneId = commune, SegmentId = segment, DataSource = source, Geom = new Point(108, 16) { SRID = 4326 } };
            db.Add(pole);
            await db.SaveChangesAsync();
            Fault? fault = null;
            if (status is CitizenReportStatus.Accepted or CitizenReportStatus.Merged)
            {
                fault = new Fault
                {
                    PoleId = pole.PoleId, CommuneId = commune, SegmentId = segment, DataSource = source,
                    FaultType = FaultType.LampOut, FaultStatus = FaultStatus.Detected, Severity = Severity.Medium,
                    SourceChannel = SourceChannel.Cv, DetectedAt = now, CreatedAt = now, UpdatedAt = now,
                };
                db.Add(fault);
                await db.SaveChangesAsync();
            }
            var report = new CitizenReport
            {
                PoleId = pole.PoleId, CommuneId = commune, DataSource = source, NightOf = night, Status = status,
                CreatedAt = now, LastSubmittedAt = now, FaultId = fault?.FaultId,
                DecidedBy = status == CitizenReportStatus.Pending ? null : users["manager"].UserId,
                DecidedAt = status == CitizenReportStatus.Pending ? null : now,
                DecisionNote = status == CitizenReportStatus.Rejected ? "Stats rejection" : null,
            };
            db.Add(report);
            await db.SaveChangesAsync();
            if (submissions > 0)
            {
                var qr = new QrCode
                {
                    PoleId = pole.PoleId, CommuneId = commune, Code = Guid.NewGuid().ToString("N")[..16],
                    CreatedBy = users["manager"].UserId, CreatedAt = now,
                };
                db.Add(qr);
                await db.SaveChangesAsync();
                for (var i = 0; i < submissions; i++)
                    db.Add(new CitizenSubmission
                    {
                        ReportId = report.ReportId, PoleId = pole.PoleId, CommuneId = commune, QrId = qr.QrId,
                        ClientOpId = Guid.NewGuid(), RequestHash = new string('a', 64), Kind = CitizenReportKind.LampOut, ReceivedAt = now,
                    });
                await db.SaveChangesAsync();
            }
            return 0;
        });

    // ---- helpers ------------------------------------------------------------------------------------------

    /// <summary>Tonight by the agenda's rule, worked out independently: local date, minus one before 12:00.</summary>
    private static DateOnly Tonight()
    {
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh")).DateTime;
        return DateOnly.FromDateTime(local.Hour < 12 ? local.AddDays(-1) : local);
    }

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

    private Task PlantOrder(string commune, TaskKind kind, WorkOrderStatus status, DateOnly? due, DateTime? completed,
        string? segment = null, bool unassigned = false, DateOnly? scheduled = null, string[]? targets = null)
        => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var now = DateTime.UtcNow;
            var assigned = status is not (WorkOrderStatus.Open or WorkOrderStatus.Cancelled);
            var order = new WorkOrder
            {
                CommuneId = commune, TaskKind = kind, Title = "Stats order", WoStatus = status, DueDate = due, ScheduledDate = scheduled,
                SegmentId = unassigned || kind == TaskKind.Survey ? null : segment ?? (commune == home ? road : foreignRoad),
                CreatedBy = users["manager"].UserId, CreatedAt = now, UpdatedAt = now,
                AssignedTo = assigned ? users["engineer"].UserId : null, AssignedAt = assigned ? now : null,
                StartedAt = status is WorkOrderStatus.InProgress or WorkOrderStatus.Done or WorkOrderStatus.Verified ? now : null,
                CompletedAt = completed, ReportNote = completed is null ? null : "Planted as finished",
                ClosedAt = status is WorkOrderStatus.Verified or WorkOrderStatus.Cancelled ? now : null,
            };
            db.Add(order);
            await db.SaveChangesAsync();
            if (targets is not null)
            {
                db.AddRange(targets.Select((segment, position) => new WorkOrderSegment
                {
                    WorkOrderId = order.WorkOrderId, CommuneId = commune, SegmentId = segment, Position = position,
                }));
                await db.SaveChangesAsync();
            }
            return 0;
        });
}
