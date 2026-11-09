using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.WorkOrders;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace LuxMap.Api.Tests;

// Reuses WorkOrderTests' isolated per-test WO communes/accounts, AssetDatabaseCollection and FK-safe teardown.
public partial class WorkOrderTests
{
    private const string PreviewRoute = Route + "/auto-dispatch/preview";

    // EF does not promise that user "a" is inserted (so numbered) before "b": tie-breaks are by user_id, so tests name the engineers by ID order.
    private AppUser Eng(string key) => key switch
    {
        "lower" => new[] { users["a"], users["b"] }.OrderBy(u => u.UserId.Length).ThenBy(u => u.UserId, StringComparer.Ordinal).First(),
        "higher" => new[] { users["a"], users["b"] }.OrderBy(u => u.UserId.Length).ThenBy(u => u.UserId, StringComparer.Ordinal).Last(),
        _ => users[key],
    };

    private async Task WithDispatchClient(Func<HttpClient, Task> test, string who = "manager", string instant = "2026-11-12T10:00:00Z")
    {
        // JwtBearer validates against the REAL clock while AccessTokenIssuer uses TimeProvider, so a token minted under the
        // fake clock would carry a future nbf and be refused: sign in on the real-clock host, then use the token on the fake one.
        using var realClient = factory.CreateClient();
        var token = await (await realClient.PostLoginAsync(users[who].Username, factory.AccountPassword)).ReadTokensAsync();
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("WorkOrders:AutoDispatch:LeadDays", "2");
            builder.UseSetting("WorkOrders:AutoDispatch:QuotaPerDay", "2");
            builder.UseSetting("WorkOrders:AutoDispatch:MaxPolesPerOrder", "8");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture)));
            });
        });
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
        await test(client);
    }

    private static async Task<JsonElement> Preview(HttpClient client, object? body = null)
    {
        using var response = body is null ? await client.PostAsync(PreviewRoute, null) : await client.PostAsJsonAsync(PreviewRoute, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [Fact]
    public async Task Auto_dispatch_reads_only_is_repeatable_and_matches_the_seven_pole_example()
    {
        var ids = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            var pole = await PlantPole((7 - i) / 10d, road: i < 4 ? segment : foreignSegment, commune: home);
            ids.Add(await PlantPoleFault(pole, FaultStatus.Detected));
        }
        // The inter-commune road geometry is readable for ordering, but its foreign name is not exposed.
        async Task<long[]> Counts() => await Db(async db =>
        {
            return new long[] {
                await db.Set<WorkOrder>().IgnoreQueryFilters().LongCountAsync(w => w.CommuneId == home),
                await db.Set<WorkOrderFault>().IgnoreQueryFilters().LongCountAsync(w => w.CommuneId == home),
                await db.Set<AuditEvent>().IgnoreQueryFilters().LongCountAsync(w => w.CommuneId == home),
                await db.Set<Notification>().IgnoreQueryFilters().LongCountAsync(w => w.CommuneId == home),
            };
        });
        await WithDispatchClient(async client =>
        {
            var before = await Counts();
            var first = await Preview(client);
            var second = await Preview(client, new { commune_id = home });
            Assert.Equal(first.GetRawText(), second.GetRawText());
            Assert.Equal(before, await Counts());
            Assert.Equal("2026-11-14", first.GetProperty("start_date").GetString());
            Assert.Equal(7, first.GetProperty("fault_count").GetInt32());
            var proposals = first.GetProperty("proposals").EnumerateArray().ToArray();
            Assert.Equal(2, proposals.Length);
            Assert.Equal($"Kiểm tra 4 cột sự cố - WO test road", proposals[0].GetProperty("title").GetString());
            Assert.Equal($"Kiểm tra 3 cột sự cố - {foreignSegment}", proposals[1].GetProperty("title").GetString());
            Assert.Equal(Eng("lower").UserId, proposals[0].GetProperty("assigned_to").GetString());
            Assert.Equal(Eng("higher").UserId, proposals[1].GetProperty("assigned_to").GetString());
            Assert.All(proposals, p => Assert.Equal("2026-11-14", p.GetProperty("scheduled_date").GetString()));
            Assert.Equal(ids.Count, proposals.Sum(p => p.GetProperty("fault_ids").GetArrayLength()));
        });
    }

    [Fact]
    public async Task Auto_dispatch_filters_status_active_links_and_engineer_eligibility()
    {
        var active = await Fault();
        var released = await Fault();
        var order = await Plant(WorkOrderStatus.Assigned);
        var noSegment = await Fault();
        foreach (var status in Enum.GetValues<FaultStatus>().Where(x => x != FaultStatus.Detected)) await Fault(status);
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            db.AddRange(new WorkOrderFault { WorkOrderId = order, FaultId = active, CommuneId = home, LinkedAt = DateTime.UtcNow.AddDays(-1) },
                new WorkOrderFault { WorkOrderId = order, FaultId = released, CommuneId = home, LinkedAt = DateTime.UtcNow.AddDays(-1), ReleasedAt = DateTime.UtcNow });
            var f = await db.Set<Fault>().IgnoreQueryFilters().SingleAsync(f => f.FaultId == noSegment);
            f.SegmentId = null;
            var a = await db.Set<AppUser>().SingleAsync(u => u.UserId == users["a"].UserId);
            a.IsLocked = true;
            // Move B to a different commune; neither engineer is eligible for home now.
            var link = await db.Set<AppUserCommune>().SingleAsync(l => l.UserId == users["b"].UserId);
            db.Remove(link);
            await db.SaveChangesAsync();
            db.Add(new AppUserCommune { UserId = users["b"].UserId, CommuneId = foreign });
            await db.SaveChangesAsync();
            return 0;
        });
        await WithDispatchClient(async client =>
        {
            var json = await Preview(client);
            Assert.Equal(1, json.GetProperty("fault_count").GetInt32());
            var p = Assert.Single(json.GetProperty("proposals").EnumerateArray());
            Assert.Equal(released, Assert.Single(p.GetProperty("fault_ids").EnumerateArray()).GetString());
            Assert.Equal(JsonValueKind.Null, p.GetProperty("assigned_to").ValueKind);
            Assert.Equal("NO_ELIGIBLE_ENGINEER", p.GetProperty("unassigned_reason").GetString());
            var skipped = Assert.Single(json.GetProperty("skipped").EnumerateArray());
            Assert.Equal(noSegment, skipped.GetProperty("fault_id").GetString());
            Assert.Equal("NO_SEGMENT", skipped.GetProperty("reason").GetString());
        });
    }

    [Theory]
    [InlineData(8, 1, 8)]
    [InlineData(9, 2, 1)]
    [InlineData(16, 2, 8)]
    [InlineData(17, 3, 1)]
    public async Task Auto_dispatch_chunks_in_actual_road_order_without_splitting_a_pole(int count, int orders, int last)
    {
        var poles = new Dictionary<int, string>();
        for (var i = count; i >= 1; i--)
        {
            poles[i] = await PlantPole(i / 100d);
            await PlantPoleFault(poles[i], FaultStatus.Detected);
        }
        await PlantPoleFault(poles[8], FaultStatus.Detected);
        await WithDispatchClient(async client =>
        {
            var p = (await Preview(client)).GetProperty("proposals").EnumerateArray().ToArray();
            Assert.Equal(orders, p.Length);
            Assert.Equal(last, p[^1].GetProperty("pole_count").GetInt32());
            Assert.Equal(Enumerable.Range(1, 8).Select(i => poles[i]), p[0].GetProperty("pole_ids").EnumerateArray().Select(x => x.GetString()));
            Assert.Equal(9, p[0].GetProperty("fault_ids").GetArrayLength());
            if (orders > 1) Assert.EndsWith($"(ca 1/{orders})", p[0].GetProperty("title").GetString());
        });
    }

    [Theory]
    [InlineData("survey", "assigned", "a", "b")]
    [InlineData("repair", "done", "a", "b")]
    [InlineData("survey", "cancelled", "lower", "lower")]
    public async Task Auto_dispatch_load_includes_every_kind_and_done_but_excludes_cancelled(
        string kind, string status, string loaded, string expected)
    {
        await Fault();
        var date = new DateOnly(2026, 11, 14);
        for (var i = 0; i < 2; i++)
        {
            var id = await Plant(WorkOrderStatus.Assigned, Eng(loaded).UserId);
            await Db(async db =>
            {
                using var system = db.EnterUnscopedSystemWriteBackdoor();
                var w = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(w => w.WorkOrderId == id);
                w.TaskKind = kind == "survey" ? TaskKind.Survey : TaskKind.Repair;
                w.WoStatus = status == "cancelled" ? WorkOrderStatus.Cancelled : status == "done" ? WorkOrderStatus.Done : WorkOrderStatus.Assigned;
                if (w.TaskKind == TaskKind.Survey) w.SegmentId = null;
                if (w.WoStatus == WorkOrderStatus.Done)
                {
                    w.StartedAt = DateTime.UtcNow.AddHours(-2);
                    w.CompletedAt = DateTime.UtcNow.AddHours(-1);
                    w.ReportNote = "Auto dispatch load test";
                }
                if (w.WoStatus == WorkOrderStatus.Cancelled) w.ClosedAt = DateTime.UtcNow;
                w.ScheduledDate = date;
                await db.SaveChangesAsync();
                return 0;
            });
        }
        await WithDispatchClient(async client =>
        {
            var p = Assert.Single((await Preview(client)).GetProperty("proposals").EnumerateArray());
            Assert.Equal(Eng(expected).UserId, p.GetProperty("assigned_to").GetString());
        });
    }

    [Fact]
    public async Task Auto_dispatch_reports_overflow_at_two_thousand_candidates()
    {
        var faultIds = await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var faults = Enumerable.Range(0, 2001).Select(i => new Fault
            {
                CommuneId = home, SegmentId = segment, Lat = 16, Lng = 108,
                FaultStatus = FaultStatus.Detected, FaultType = FaultType.LampOut,
                Severity = Severity.Low, SourceChannel = SourceChannel.FieldReport,
                DataSource = DataSource.Simulated,
                DetectedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(i),
            }).ToArray();
            db.AddRange(faults);
            await db.SaveChangesAsync();
            return faults.Select(f => f.FaultId).ToArray();
        });
        await WithDispatchClient(async client =>
        {
            var json = await Preview(client);
            Assert.Equal(2000, json.GetProperty("fault_count").GetInt32());
            Assert.Equal(250, json.GetProperty("order_count").GetInt32());
            var skipped = Assert.Single(json.GetProperty("skipped").EnumerateArray());
            Assert.Equal(faultIds[^1], skipped.GetProperty("fault_id").GetString());
            Assert.Equal("LIMIT_EXCEEDED", skipped.GetProperty("reason").GetString());
        });
    }

    [Theory]
    [InlineData(-5, "higher")] // Monday of this week loads the lower-ID engineer, so the other wins.
    [InlineData(-6, "lower")] // Sunday belongs to the preceding week: tie, lowest user_id wins.
    [InlineData(2, "lower")]  // Following Monday belongs to the next week.
    [InlineData(null, "lower")] // No scheduled date is not load.
    public async Task Auto_dispatch_counts_only_the_monday_to_sunday_week_and_scheduled_orders(int? offset, string expected)
    {
        await Fault();
        var id = await Plant(WorkOrderStatus.Assigned, Eng("lower").UserId);
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var w = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(w => w.WorkOrderId == id);
            w.ScheduledDate = offset is { } days ? new DateOnly(2026, 11, 14).AddDays(days) : null;
            await db.SaveChangesAsync();
            return 0;
        });
        await WithDispatchClient(async client =>
        {
            var p = Assert.Single((await Preview(client)).GetProperty("proposals").EnumerateArray());
            Assert.Equal(Eng(expected).UserId, p.GetProperty("assigned_to").GetString());
        });
    }

    [Theory]
    [InlineData("2026-11-12T10:00:00Z", "2026-11-14")]
    [InlineData("2026-11-13T10:00:00Z", "2026-11-15")]
    public async Task Auto_dispatch_never_skips_weekends(string instant, string date)
    {
        await Fault();
        await WithDispatchClient(async client =>
        {
            var json = await Preview(client);
            Assert.Equal(date, json.GetProperty("start_date").GetString());
            Assert.Equal(date, json.GetProperty("proposals")[0].GetProperty("scheduled_date").GetString());
        }, instant: instant);
    }

    [Fact]
    public async Task Auto_dispatch_enforces_existing_capability_and_commune_scope_and_validates_body()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(PreviewRoute, null)).StatusCode);
        foreach (var who in new[] { "a", "superior", "admin" })
        {
            using var response = await clients[who].PostAsync(PreviewRoute, null);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("ROLE_FORBIDDEN", json.GetProperty("error").GetProperty("code").GetString());
        }
        await WithDispatchClient(async client =>
        {
            foreach (var body in new object[] { new { commune_id = (string?)null }, new { commune_id = "" }, new { } }) await Preview(client, body);
            using var foreignResponse = await client.PostAsJsonAsync(PreviewRoute, new { commune_id = foreign });
            Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);
            Assert.Equal("COMMUNE_FORBIDDEN", (await foreignResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString());
            using var bad = await client.PostAsJsonAsync(PreviewRoute, new { commune_id = 123 });
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Equal("VALIDATION_FAILED", (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString());
        });
    }
}
