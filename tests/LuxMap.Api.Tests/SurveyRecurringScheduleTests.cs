using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Audit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace LuxMap.Api.Tests;

// Shares the per-test isolated communes/accounts and FK-safe teardown in WorkOrderTests.
public partial class WorkOrderTests
{
    private const string RecurringRoute = "/api/v1/survey-schedules/recurring";

    private async Task WithRecurringHost(Func<HttpClient, FakeTimeProvider, IServiceProvider, Task> test,
        string who = "manager", string instant = "2025-11-04T03:00:00Z", string[]? communes = null)
    {
        using var realClient = factory.CreateClient();
        var tokens = await (await realClient.PostLoginAsync(users[who].Username, factory.AccountPassword)).ReadTokensAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture));
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", communes is null ? tokens.AccessToken
            : factory.Services.GetRequiredService<AccessTokenIssuer>().Issue(users[who], communes).Token);
        await test(client, clock, host.Services);
    }

    private async Task<JsonElement> RecurringSend(HttpClient client, string method, string suffix, object? body,
        int expected, string? code = null)
    {
        var correlation = "recurring-" + Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(new HttpMethod(method), RecurringRoute + suffix);
        request.Headers.Add("X-Correlation-Id", correlation);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expected, $"{method} {suffix}: {response.StatusCode}: {text}");
        var events = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().Where(x => x.CorrelationId == correlation).ToArrayAsync());
        if (expected >= 400 || method == "GET") Assert.Empty(events);
        else
        {
            var audit = Assert.Single(events);
            Assert.Equal(AuditEntityType.SurveyRecurringSchedule, audit.EntityType);
            Assert.Equal(users["manager"].UserId, audit.ActorUserId);
        }
        if (expected == 204) { Assert.Empty(text); return default; }
        var json = JsonDocument.Parse(text).RootElement.Clone();
        if (code is not null) Assert.Equal(code, json.GetProperty("error").GetProperty("code").GetString());
        return json;
    }

    private Task<JsonElement> NewRecurring(HttpClient client, int day = 3, string? road = null, string? assigned = null, string? note = null)
        => RecurringSend(client, "POST", "", new { commune_id = home, segment_id = road ?? segment,
            day_of_week = day, assigned_to = assigned, note }, 201);

    [Fact]
    public async Task Recurring_create_patch_reenable_and_delete_have_one_audit_and_correct_calendar()
    {
        await WithRecurringHost(async (client, clock, _) =>
        {
            var created = await NewRecurring(client, assigned: users["a"].UserId, note: "Weekly road");
            var id = created.GetProperty("schedule_id").GetString()!;
            Assert.StartsWith("SSCH-", id);
            Assert.Equal("2025-11-10", created.GetProperty("starts_on_week").GetString());
            Assert.Equal("2025-11-12", created.GetProperty("first_occurrence_date").GetString());
            Assert.Equal("2025-11-04T03:00:00Z", created.GetProperty("created_at").GetString());
            Assert.Equal("WO test road", created.GetProperty("segment_name").GetString());
            Assert.Equal("a", created.GetProperty("assigned_to_name").GetString());
            Assert.Equal(users["manager"].UserId, created.GetProperty("created_by").GetString());
            var disabled = await RecurringSend(client, "PATCH", "/" + id, new { is_active = false }, 200);
            Assert.False(disabled.GetProperty("is_active").GetBoolean());
            clock.SetUtcNow(DateTimeOffset.Parse("2025-11-07T03:00:00Z", CultureInfo.InvariantCulture));
            var enabled = await RecurringSend(client, "PATCH", "/" + id,
                new { is_active = true, assigned_to = users["b"].UserId, note = (string?)null }, 200);
            Assert.Equal("2025-11-17", enabled.GetProperty("starts_on_week").GetString());
            Assert.Equal("2025-11-19", enabled.GetProperty("first_occurrence_date").GetString());
            Assert.Equal(users["b"].UserId, enabled.GetProperty("assigned_to").GetString());
            Assert.Equal(JsonValueKind.Null, enabled.GetProperty("note").ValueKind);
            clock.Advance(TimeSpan.FromDays(30));
            var unchanged = await RecurringSend(client, "PATCH", "/" + id, new { is_active = true, assigned_to = (string?)null }, 200);
            Assert.Equal("2025-11-17", unchanged.GetProperty("starts_on_week").GetString());
            Assert.Equal(JsonValueKind.Null, unchanged.GetProperty("assigned_to").ValueKind);
            var list = await RecurringSend(client, "GET", "?commune_id=" + home, null, 200);
            Assert.Equal("2025-11-19", Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("first_occurrence_date").GetString());
            await RecurringSend(client, "DELETE", "/" + id, null, 204);
            await RecurringSend(client, "DELETE", "/" + id, null, 404, "SURVEY_SCHEDULE_NOT_FOUND");
        });
    }

    [Theory]
    [InlineData("\"3\"")]
    [InlineData("0")]
    [InlineData("8")]
    [InlineData("3.5")]
    [InlineData("null")]
    public async Task Recurring_invalid_weekday_names_the_field(string day)
    {
        await WithRecurringHost(async (client, _, _) =>
        {
            using var body = JsonDocument.Parse($$"""{"commune_id":"{{home}}","segment_id":"{{segment}}","day_of_week":{{day}}}""");
            var error = await RecurringSend(client, "POST", "", body.RootElement, 400, "VALIDATION_FAILED");
            Assert.Equal("day_of_week", error.GetProperty("error").GetProperty("details").GetProperty("field").GetString());
        });
    }

    [Fact]
    public async Task Recurring_rejects_server_owned_immutable_empty_and_invalid_fields()
    {
        await WithRecurringHost(async (client, _, _) =>
        {
            foreach (var field in new[] { "schedule_id", "starts_on_week", "is_active", "created_by", "first_occurrence_date" })
            {
                using var body = JsonDocument.Parse($$"""{"commune_id":"{{home}}","segment_id":"{{segment}}","day_of_week":3,"{{field}}":null}""");
                await RecurringSend(client, "POST", "", body.RootElement, 400, "SERVER_OWNED_FIELD");
            }
            var id = (await NewRecurring(client)).GetProperty("schedule_id").GetString()!;
            foreach (var field in new[] { "commune_id", "segment_id", "day_of_week", "schedule_id", "starts_on_week", "created_by", "first_occurrence_date" })
            {
                using var body = JsonDocument.Parse($$"""{"{{field}}":null}""");
                await RecurringSend(client, "PATCH", "/" + id, body.RootElement, 400, "SERVER_OWNED_FIELD");
            }
            foreach (var body in new object[] { new { }, new { is_active = "true" }, new { is_active = (bool?)null }, new { assigned_to = 1 }, new { note = 1 }, new { note = new string('x', 1001) } })
                await RecurringSend(client, "PATCH", "/" + id, body, 400, "VALIDATION_FAILED");
            await RecurringSend(client, "POST", "", new { commune_id = home, segment_id = segment, day_of_week = 4, note = new string('x', 1001) }, 400, "VALIDATION_FAILED");
            await RecurringSend(client, "POST", "", new { commune_id = home, segment_id = segment, day_of_week = 3 }, 409, "SURVEY_SCHEDULE_EXISTS");
            await RecurringSend(client, "PATCH", "/" + id, new { assigned_to = users["manager"].UserId }, 409, "ASSIGNEE_NOT_ELIGIBLE");
            await RecurringSend(client, "PATCH", "/missing", new { is_active = false }, 404, "SURVEY_SCHEDULE_NOT_FOUND");
        });
    }

    [Fact]
    public async Task Recurring_enforces_commune_assets_and_eligibility()
    {
        await WithRecurringHost(async (client, _, _) =>
        {
            await RecurringSend(client, "POST", "", new { commune_id = foreign, segment_id = foreignSegment, day_of_week = 3 }, 403, "COMMUNE_FORBIDDEN");
            await RecurringSend(client, "GET", "?commune_id=" + foreign, null, 403, "COMMUNE_FORBIDDEN");
            foreach (var road in new[] { foreignSegment, "SEG-missing" })
                await RecurringSend(client, "POST", "", new { commune_id = home, segment_id = road, day_of_week = 3 }, 404, "ASSET_NOT_FOUND");
            foreach (var assigned in new[] { users["superior"].UserId, "USR-missing" })
                await RecurringSend(client, "POST", "", new { commune_id = home, segment_id = segment, day_of_week = 3, assigned_to = assigned }, 409, "ASSIGNEE_NOT_ELIGIBLE");
            await Db(async db =>
            {
                var user = await db.Set<AppUser>().SingleAsync(x => x.UserId == users["b"].UserId);
                user.IsLocked = true;
                return await db.SaveChangesAsync();
            });
            await RecurringSend(client, "POST", "", new { commune_id = home, segment_id = segment,
                day_of_week = 3, assigned_to = users["b"].UserId }, 409, "ASSIGNEE_NOT_ELIGIBLE");
            await RecurringSend(client, "POST", "", new { commune_id = "", segment_id = segment, day_of_week = 3 }, 400, "VALIDATION_FAILED");
            await RecurringSend(client, "POST", "", new { commune_id = home, segment_id = "", day_of_week = 3 }, 400, "VALIDATION_FAILED");

        });
        await WithRecurringHost(async (client, _, _) =>
        {
            await RecurringSend(client, "POST", "", new { commune_id = home, segment_id = foreignSegment, day_of_week = 3 }, 409, "CROSS_COMMUNE_REFERENCE");
            await Db(async db =>
            {
                using var system = db.EnterUnscopedSystemWriteBackdoor();
                var road = await db.Set<RoadSegment>().IgnoreQueryFilters().SingleAsync(x => x.SegmentId == foreignSegment);
                road.RoadClass = RoadClass.InterCommune;
                return await db.SaveChangesAsync();
            });
            await NewRecurring(client, road: foreignSegment);
            await RecurringSend(client, "POST", "", new { commune_id = foreign, segment_id = foreignSegment, day_of_week = 4 }, 201);
        }, communes: [home, foreign]);
        await WithRecurringHost(async (client, _, _) =>
        {
            var list = await RecurringSend(client, "GET", "", null, 200);
            Assert.Equal(home, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("commune_id").GetString());
            var id = await Db(db => db.Set<SurveyRecurringSchedule>().IgnoreQueryFilters().Where(x => x.CommuneId == foreign).Select(x => x.ScheduleId).SingleAsync());
            await RecurringSend(client, "PATCH", "/" + id, new { is_active = false }, 404, "SURVEY_SCHEDULE_NOT_FOUND");
            await RecurringSend(client, "DELETE", "/" + id, null, 404, "SURVEY_SCHEDULE_NOT_FOUND");
        });
    }

    [Theory]
    [InlineData("a")]
    [InlineData("superior")]
    [InlineData("admin")]
    public async Task Recurring_write_policy_is_manager_only_and_superior_can_read(string who)
    {
        await WithRecurringHost(async (client, _, _) =>
        {
            await RecurringSend(client, "GET", "?commune_id=" + home, null, 200);
            await RecurringSend(client, "POST", "", new { }, 403, "ROLE_FORBIDDEN");
            await RecurringSend(client, "PATCH", "/missing", new { is_active = false }, 403, "ROLE_FORBIDDEN");
            await RecurringSend(client, "DELETE", "/missing", null, 403, "ROLE_FORBIDDEN");
        }, who);
    }

    [Fact]
    public async Task Recurring_requires_authentication()
    {
        using var client = factory.CreateClient();
        foreach (var method in new[] { "GET", "POST", "PATCH", "DELETE" })
            await RecurringSend(client, method, method is "PATCH" or "DELETE" ? "/missing" : "", method is "POST" or "PATCH" ? new { } : null, 401, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task Recurring_list_pages_and_orders_ids_across_the_width_boundary()
    {
        string[] ids = ["SSCH-9999", "SSCH-10000", "SSCH-10001"];
        await Db(async db =>
        {
            Assert.False(await db.Set<SurveyRecurringSchedule>().IgnoreQueryFilters().AnyAsync(x => ids.Contains(x.ScheduleId)));
            await using var transaction = await db.Database.BeginTransactionAsync();
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var stamp = new DateTime(2025, 11, 4, 3, 0, 0, DateTimeKind.Utc);
            for (var index = 0; index < ids.Length; index++)
                db.Add(new SurveyRecurringSchedule { ScheduleId = ids[index], CommuneId = home, SegmentId = segment,
                    DayOfWeek = (short)(index + 1), StartsOnWeek = new(2025, 11, 10), CreatedAt = stamp, UpdatedAt = stamp,
                    CreatedBy = users["manager"].UserId });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return 0;
        });
        await WithRecurringHost(async (client, _, _) =>
        {
            var first = await RecurringSend(client, "GET", "?commune_id=" + home + "&page_size=2", null, 200);
            Assert.Equal(3, first.GetProperty("total").GetInt32());
            Assert.Equal(1, first.GetProperty("page").GetInt32());
            Assert.Equal(2, first.GetProperty("page_size").GetInt32());
            Assert.Equal(ids[..2], first.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("schedule_id").GetString()));
            var second = await RecurringSend(client, "GET", "?page=2&page_size=2", null, 200);
            Assert.Equal(ids[2], Assert.Single(second.GetProperty("items").EnumerateArray()).GetProperty("schedule_id").GetString());
        });
    }

}
