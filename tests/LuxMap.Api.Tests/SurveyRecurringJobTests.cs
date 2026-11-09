using System.Globalization;
using LuxMap.Modules.WorkOrders;
using LuxMap.Modules.WorkOrders.Jobs;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Persistence.Audit;
using LuxMap.Persistence.Jobs;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace LuxMap.Api.Tests;

public partial class WorkOrderTests
{
    private static async Task RunRecurring(IServiceProvider services, FakeTimeProvider clock, string? correlation = null, RecurringLog? logger = null)
    {
        logger ??= new RecurringLog();
        var job = new SurveyRecurringGenerateJob(services.GetRequiredService<JobContextFactory>(),
            services.GetRequiredService<WorkOrderAgendaOptions>(), logger);
        Assert.Equal("survey_recurring_generate", job.Name);
        Assert.Equal(TimeSpan.FromMinutes(15), job.Interval);
        await job.RunAsync(clock.GetUtcNow().UtcDateTime, correlation ?? "job:survey_recurring_generate:" + Guid.NewGuid().ToString("N"), default);
        Assert.DoesNotContain(logger.Levels, level => level >= LogLevel.Error);
    }

    private Task<List<WorkOrder>> RecurringOrders() => Db(db => db.Set<WorkOrder>().IgnoreQueryFilters()
        .Where(w => w.CommuneId == home && w.TaskKind == TaskKind.Survey).OrderBy(w => w.ScheduledDate).ToListAsync());

    private Task ChangeRecurring(string id, Action<SurveyRecurringSchedule> change) => Db(async db =>
    {
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var schedule = await db.Set<SurveyRecurringSchedule>().IgnoreQueryFilters().SingleAsync(x => x.ScheduleId == id);
        change(schedule);
        return await db.SaveChangesAsync();
    });

    [Fact]
    public async Task Recurring_job_stages_order_receipt_audit_and_assigned_notice_once_and_is_visible_in_agenda()
    {
        await WithRecurringHost(async (client, clock, services) =>
        {
            var schedule = await NewRecurring(client, assigned: users["a"].UserId, note: "Survey notes");
            var id = schedule.GetProperty("schedule_id").GetString()!;
            clock.SetUtcNow(DateTimeOffset.Parse("2025-11-06T09:59:59Z", CultureInfo.InvariantCulture));
            await RunRecurring(services, clock);
            Assert.Empty(await RecurringOrders());
            clock.Advance(TimeSpan.FromSeconds(1));
            var correlation = "job:survey_recurring_generate:" + Guid.NewGuid().ToString("N");
            await RunRecurring(services, clock, correlation);
            await RunRecurring(services, clock);
            var order = Assert.Single(await RecurringOrders());
            Assert.Equal("Khảo sát toàn tuyến - WO test road", order.Title);
            Assert.Equal("Survey notes", order.Note);
            Assert.Equal(users["manager"].UserId, order.CreatedBy);
            Assert.Equal(users["a"].UserId, order.AssignedTo);
            Assert.Equal(WorkOrderStatus.Assigned, order.WoStatus);
            Assert.Equal(new DateOnly(2025, 11, 12), order.ScheduledDate);
            Assert.Equal(order.ScheduledDate, order.DueDate);
            await Db(async db =>
            {
                var receipt = await db.Set<SurveyRecurringOccurrence>().IgnoreQueryFilters().SingleAsync(x => x.ScheduleId == id);
                Assert.Equal(order.WorkOrderId, receipt.WorkOrderId);
                Assert.Equal(new DateOnly(2025, 11, 12), receipt.ScheduledDate);
                Assert.Equal(home, receipt.CommuneId);
                var target = await db.Set<WorkOrderSegment>().IgnoreQueryFilters().SingleAsync(x => x.WorkOrderId == order.WorkOrderId);
                Assert.Equal(segment, target.SegmentId);
                Assert.Equal(0, target.Position);
                var audit = await db.Set<AuditEvent>().IgnoreQueryFilters().SingleAsync(x => x.CorrelationId == correlation);
                Assert.Equal(AuditAction.Created, audit.Action);
                Assert.Equal(AuditActorKind.System, audit.ActorKind);
                Assert.Null(audit.ActorUserId);
                Assert.Equal(order.WorkOrderId, audit.EntityId);
                var notice = await db.Set<Notification>().IgnoreQueryFilters().SingleAsync(x => x.EntityId == order.WorkOrderId);
                Assert.Equal(NotificationType.WorkOrderAssigned, notice.Type);
                Assert.Equal(users["a"].UserId, notice.RecipientUserId);
                return 0;
            });
            var list = await Send("manager", "GET", "?task_kind=survey", null, 200);
            Assert.Equal(order.WorkOrderId, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("work_order_id").GetString());
            var agenda = await AgendaOf("a", "?night_of=2025-11-12");
            Assert.Equal([order.WorkOrderId], Groups(agenda).SelectMany(OrderIds));
            Assert.Contains("scheduled_tonight", Strings(Assert.Single(Groups(agenda)).GetProperty("work_orders")[0], "flags"));
            await RecurringSend(client, "DELETE", "/" + id, null, 204);
            Assert.Single(await RecurringOrders());
            Assert.Equal(0, await Db(db => db.Set<SurveyRecurringOccurrence>().IgnoreQueryFilters().CountAsync(x => x.ScheduleId == id)));
        });
    }

    [Fact]
    public async Task Recurring_two_concurrent_jobs_commit_only_one_order_receipt_audit_and_notice()
    {
        await WithRecurringHost(async (client, clock, services) =>
        {
            await NewRecurring(client, assigned: users["a"].UserId);
            clock.SetUtcNow(DateTimeOffset.Parse("2025-11-06T10:00:00Z", CultureInfo.InvariantCulture));
            var before = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(x => x.CommuneId == home && x.EntityType == AuditEntityType.WorkOrder));
            await Task.WhenAll(RunRecurring(services, clock), RunRecurring(services, clock));
            var order = Assert.Single(await RecurringOrders());
            Assert.Equal(1, await Db(db => db.Set<SurveyRecurringOccurrence>().IgnoreQueryFilters().CountAsync(x => x.CommuneId == home)));
            Assert.Equal(before + 1, await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(x => x.CommuneId == home && x.EntityType == AuditEntityType.WorkOrder)));
            Assert.Equal(1, await Db(db => db.Set<Notification>().IgnoreQueryFilters().CountAsync(x => x.EntityId == order.WorkOrderId)));
        });
    }

    [Fact]
    public async Task Recurring_manual_survey_suppresses_the_week_until_cancelled_but_cancelled_generated_order_never_regenerates()
    {
        await WithRecurringHost(async (client, clock, services) =>
        {
            await NewRecurring(client);
            var manual = await Send("manager", "POST", "", new { task_kind = "survey", title = "Manual same week",
                commune_id = home, segment_ids = new[] { segment }, scheduled_date = "2025-11-14" }, 201);
            var manualId = manual.GetProperty("work_order_id").GetString()!;
            clock.SetUtcNow(DateTimeOffset.Parse("2025-11-06T10:00:00Z", CultureInfo.InvariantCulture));
            await RunRecurring(services, clock);
            Assert.Single(await RecurringOrders());
            Assert.Equal(0, await Db(db => db.Set<SurveyRecurringOccurrence>().IgnoreQueryFilters().CountAsync(x => x.CommuneId == home)));
            await Send("manager", "POST", "/" + manualId + "/cancel", new { note = "Cancel manual visit" }, 200);
            await RunRecurring(services, clock);
            var generated = Assert.Single(await RecurringOrders(), x => x.WorkOrderId != manualId);
            await Send("manager", "POST", "/" + generated.WorkOrderId + "/cancel", new { note = "Do not regenerate" }, 200);
            await RunRecurring(services, clock);
            Assert.Equal(2, (await RecurringOrders()).Count);
            Assert.Equal(1, await Db(db => db.Set<SurveyRecurringOccurrence>().IgnoreQueryFilters().CountAsync(x => x.CommuneId == home)));
        });
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("future")]
    [InlineData("past")]
    public async Task Recurring_job_skips_inactive_future_starts_and_past_days(string reason)
    {
        await WithRecurringHost(async (client, clock, services) =>
        {
            var id = (await NewRecurring(client)).GetProperty("schedule_id").GetString()!;
            if (reason == "inactive") await RecurringSend(client, "PATCH", "/" + id, new { is_active = false }, 200);
            if (reason == "future") await ChangeRecurring(id, s => s.StartsOnWeek = new(2025, 11, 17));
            clock.SetUtcNow(DateTimeOffset.Parse(reason == "past" ? "2025-11-13T09:59:59Z" : "2025-11-06T10:00:00Z", CultureInfo.InvariantCulture));
            await RunRecurring(services, clock);
            Assert.Empty(await RecurringOrders());
        });
    }

    [Theory]
    [InlineData("2025-11-06T09:59:59Z", 1)]
    [InlineData("2025-11-06T10:00:00Z", 2)]
    [InlineData("2025-11-09T17:00:00Z", 1)] // Monday midnight local: current week only.
    public async Task Recurring_job_generates_sundays_in_the_exact_local_window(string instant, int expected)
    {
        await WithRecurringHost(async (client, clock, services) =>
        {
            var id = (await NewRecurring(client, day: 7)).GetProperty("schedule_id").GetString()!;
            await ChangeRecurring(id, s => s.StartsOnWeek = new(2025, 11, 3));
            clock.SetUtcNow(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture));
            await RunRecurring(services, clock);
            var dates = (await RecurringOrders()).Select(w => w.ScheduledDate).ToArray();
            Assert.Equal(expected, dates.Length);
            if (expected == 2) Assert.Equal([new DateOnly(2025, 11, 9), new DateOnly(2025, 11, 16)], dates);
            else Assert.Equal(instant == "2025-11-09T17:00:00Z" ? new DateOnly(2025, 11, 16) : new DateOnly(2025, 11, 9), dates[0]);
        });
    }

    [Fact]
    public async Task Recurring_locked_preferred_engineer_falls_back_to_lowest_week_load_and_counts_new_orders()
    {
        await WithRecurringHost(async (client, clock, services) =>
        {
            var secondRoad = await PlantRoad(0.1);
            await NewRecurring(client, assigned: users["a"].UserId);
            await NewRecurring(client, road: secondRoad, assigned: users["a"].UserId);
            await Db(async db =>
            {
                var user = await db.Set<AppUser>().SingleAsync(x => x.UserId == users["a"].UserId);
                user.IsLocked = true;
                return await db.SaveChangesAsync();
            });
            clock.SetUtcNow(DateTimeOffset.Parse("2025-11-06T10:00:00Z", CultureInfo.InvariantCulture));
            await RunRecurring(services, clock);
            var orders = await RecurringOrders();
            Assert.Equal(2, orders.Count);
            Assert.All(orders, w => Assert.Equal(users["b"].UserId, w.AssignedTo));
            Assert.All(orders, w => Assert.StartsWith("Tự động sinh từ lịch khảo sát định kỳ SSCH-", w.Note));
        });
    }

    [Fact]
    public async Task Recurring_fallback_uses_week_then_day_load_ignores_quota_and_reserves_each_generated_order()
    {
        await WithRecurringHost(async (client, clock, services) =>
        {
            var secondRoad = await PlantRoad(0.1);
            await NewRecurring(client);
            await NewRecurring(client, road: secondRoad);
            // Equal weekly counts; lower ID already has two orders on the requested date (over quota).
            for (var i = 0; i < 2; i++)
            {
                var onDate = await Plant(WorkOrderStatus.Assigned, assigned: Eng("lower").UserId);
                await SetDates(onDate, new(2025, 11, 12), new(2025, 11, 12));
                var otherDay = await Plant(WorkOrderStatus.Assigned, assigned: Eng("higher").UserId);
                await SetDates(otherDay, new(2025, 11, 10), new(2025, 11, 10));
            }
            clock.SetUtcNow(DateTimeOffset.Parse("2025-11-06T10:00:00Z", CultureInfo.InvariantCulture));
            await RunRecurring(services, clock);
            var orders = (await RecurringOrders()).OrderBy(w => w.CreatedAt).ThenBy(w => w.WorkOrderId.Length).ThenBy(w => w.WorkOrderId).ToArray();
            Assert.Equal(2, orders.Length);
            Assert.Equal(Eng("higher").UserId, orders[0].AssignedTo); // equal week, fewer today
            Assert.Equal(Eng("lower").UserId, orders[1].AssignedTo); // fewer this week, despite two today
            Assert.All(orders, w => Assert.Equal(new DateOnly(2025, 11, 12), w.ScheduledDate));
        });
    }

    [Fact]
    public async Task Recurring_without_any_eligible_engineer_stays_open_with_no_assigned_notification()
    {
        await WithRecurringHost(async (client, clock, services) =>
        {
            await NewRecurring(client);
            await Db(async db =>
            {
                var ids = new[] { users["a"].UserId, users["b"].UserId };
                foreach (var user in await db.Set<AppUser>().Where(u => ids.Contains(u.UserId)).ToListAsync()) user.IsLocked = true;
                return await db.SaveChangesAsync();
            });
            clock.SetUtcNow(DateTimeOffset.Parse("2025-11-06T10:00:00Z", CultureInfo.InvariantCulture));
            var logger = new RecurringLog();
            await RunRecurring(services, clock, logger: logger);
            Assert.Contains(LogLevel.Warning, logger.Levels);
            var order = Assert.Single(await RecurringOrders());
            Assert.Equal(WorkOrderStatus.Open, order.WoStatus);
            Assert.Null(order.AssignedTo);
            Assert.Null(order.AssignedAt);
            Assert.Equal(0, await Db(db => db.Set<Notification>().IgnoreQueryFilters().CountAsync(x => x.EntityId == order.WorkOrderId)));
        });
    }
    private sealed class RecurringLog : ILogger<SurveyRecurringGenerateJob>
    {
        public List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }

}
