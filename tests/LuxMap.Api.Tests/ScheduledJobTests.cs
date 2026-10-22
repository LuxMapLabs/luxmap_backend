using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Modules.Telemetry;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Modules.Telemetry.Jobs;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Modules.WorkOrders.Jobs;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Persistence.Jobs;
using LuxMap.Persistence.Settings;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using Npgsql;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-26 — the lease runner and the two jobs on it: overdue work orders and silent devices.
/// </summary>
/// <remarks>
/// 🔴 Both jobs scan EVERY commune of the run's shared database. They are run here with a clock in 2001 and every row they should see is
/// planted in 2000–2001, so no other class's work order or device is ever a candidate — a notice written for one would break that class's
/// teardown (<c>work_order_overdue_notice</c> holds its order with <c>Restrict</c>).
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public class ScheduledJobTests(AssetImportFixture factory) : IAsyncLifetime
{
    private static readonly DateTime Night10 = new(2001, 3, 10, 5, 0, 0, DateTimeKind.Utc); // 12:00 in Ho Chi Minh City

    private readonly Dictionary<string, AppUser> users = [];
    private readonly List<string> jobNames = [];
    private string home = null!;
    private string other = null!;

    private Task<T> Db<T>(Func<LuxMapDbContext, Task<T>> work) => factory.QueryAsync(work);

    public async Task InitializeAsync()
    {
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var communes = new[] { "home", "other" }.Select(n => new AdministrativeUnit { Name = $"Job test {n} {Guid.NewGuid()}" }).ToArray();
            db.AddRange(communes);
            await db.SaveChangesAsync();
            (home, other) = (communes[0].CommuneId, communes[1].CommuneId);
            foreach (var (key, role, locked, commune) in new[]
            {
                ("manager", UserRole.Manager, false, home), ("manager2", UserRole.Manager, false, home),
                ("locked_manager", UserRole.Manager, true, home), ("other_manager", UserRole.Manager, false, other),
                ("engineer", UserRole.FieldEngineer, false, home),
            })
            {
                var user = new AppUser
                {
                    Username = "job" + Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid", FullName = key,
                    PasswordHash = "x", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", PasswordSetAt = DateTime.UtcNow, Role = role, IsLocked = locked,
                };
                users[key] = user;
                db.Add(user);
                await db.SaveChangesAsync();
                db.Add(new AppUserCommune { UserId = user.UserId, CommuneId = commune });
            }
            await db.SaveChangesAsync();
            return 0;
        });
    }

    public async Task DisposeAsync()
    {
        await Db(async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SET LOCAL luxmap.audit_purge = 'on'");
            foreach (var table in new[]
            {
                "audit_event", "notification", "fault", "work_order_overdue_notice", "work_order", "iot_node", "electrical_cabinet",
                "app_user_commune",
            })
            {
                var sql = $"DELETE FROM {table} WHERE commune_id = {{0}} OR commune_id = {{1}}";
                await db.Database.ExecuteSqlRawAsync(sql, home, other);
            }
            var ids = users.Values.Select(user => user.UserId).ToArray();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user WHERE user_id = ANY({ids})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM administrative_unit WHERE commune_id IN ({home}, {other})");
            var names = jobNames.ToArray();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM scheduled_job WHERE job_name = ANY({names})");
            await transaction.CommitAsync();
            return 0;
        });
    }

    // ---- the runner ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Only_one_of_many_simultaneous_claims_wins_and_only_its_token_can_finish_the_run()
    {
        var name = await NewJobAsync();
        var runner = Runner();

        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => runner.ClaimAsync([name], CancellationToken.None)));
        var won = Assert.Single(claims, c => c is not null)!.Value;

        Assert.False(await runner.CompleteAsync(name, Guid.NewGuid(), TimeSpan.FromHours(1), CancellationToken.None));
        Assert.True(await runner.CompleteAsync(name, won.Token, TimeSpan.FromHours(1), CancellationToken.None));
        Assert.Null(await runner.ClaimAsync([name], CancellationToken.None)); // next run is an hour away

        var row = await RowAsync(name);
        Assert.Null(row.LeaseToken);
        Assert.Equal(1, row.RunCount);
        Assert.InRange((row.NextRunAt - row.LastFinishedAt!.Value).TotalMinutes, 59.9, 60.1);
    }

    [Fact]
    public async Task An_expired_lease_is_taken_over_and_the_old_holder_is_fenced_out()
    {
        var name = await NewJobAsync();
        var runner = Runner();
        var first = (await runner.ClaimAsync([name], CancellationToken.None))!.Value;
        Assert.Null(await runner.ClaimAsync([name], CancellationToken.None)); // a live lease holds it

        await SqlAsync("UPDATE scheduled_job SET lease_expires_at = now() - interval '1 second' WHERE job_name = {0}", name);
        var second = (await runner.ClaimAsync([name], CancellationToken.None))!.Value;
        Assert.NotEqual(first.Token, second.Token);

        Assert.False(await runner.FailAsync(name, first.Token, TimeSpan.FromHours(1), "late", CancellationToken.None));
        Assert.True(await runner.CompleteAsync(name, second.Token, TimeSpan.FromHours(1), CancellationToken.None));
        Assert.Null((await RowAsync(name)).LastError);
    }

    [Fact]
    public async Task A_failure_is_kept_and_retried_sooner_than_the_interval_and_the_next_success_clears_it()
    {
        var name = await NewJobAsync();
        var runner = Runner();
        var claim = (await runner.ClaimAsync([name], CancellationToken.None))!.Value;
        Assert.True(await runner.FailAsync(name, claim.Token, TimeSpan.FromHours(6), new string('x', 1500), CancellationToken.None));

        var failed = await RowAsync(name);
        Assert.Equal(1000, failed.LastError!.Length);
        Assert.Equal(0, failed.RunCount);
        Assert.InRange((failed.NextRunAt - failed.LastFinishedAt!.Value).TotalMinutes, 4.9, 5.1); // Jobs:RetrySeconds, not 6 h

        await SqlAsync("UPDATE scheduled_job SET next_run_at = now() WHERE job_name = {0}", name);
        claim = (await runner.ClaimAsync([name], CancellationToken.None))!.Value;
        Assert.True(await runner.CompleteAsync(name, claim.Token, TimeSpan.FromHours(6), CancellationToken.None));
        Assert.Null((await RowAsync(name)).LastError);
    }

    [Fact]
    public async Task Failures_in_a_row_are_counted_and_the_last_one_stays_dated_after_a_success()
    {
        var name = await NewJobAsync();
        var runner = Runner();
        for (var i = 0; i < 2; i++)
        {
            await SqlAsync("UPDATE scheduled_job SET next_run_at = now() WHERE job_name = {0}", name);
            var failing = (await runner.ClaimAsync([name], CancellationToken.None))!.Value;
            Assert.True(await runner.FailAsync(name, failing.Token, TimeSpan.FromHours(1), "boom", CancellationToken.None));
        }

        var failed = await RowAsync(name);
        Assert.Equal(2, failed.ConsecutiveFailures);
        Assert.Equal(failed.LastFinishedAt, failed.LastFailedAt);

        await SqlAsync("UPDATE scheduled_job SET next_run_at = now() WHERE job_name = {0}", name);
        var claim = (await runner.ClaimAsync([name], CancellationToken.None))!.Value;
        Assert.True(await runner.CompleteAsync(name, claim.Token, TimeSpan.FromHours(1), CancellationToken.None));

        // BE-35 (D-4): the success clears the error and the count, but a job that failed an hour ago still shows when.
        var recovered = await RowAsync(name);
        Assert.Equal(0, recovered.ConsecutiveFailures);
        Assert.Null(recovered.LastError);
        Assert.Equal(failed.LastFailedAt, recovered.LastFailedAt);
        Assert.Equal(1, recovered.RunCount);
    }

    [Fact]
    public async Task The_worker_runs_due_jobs_records_a_failure_and_runs_the_job_again()
    {
        var job = new FlakyJob("test_" + Guid.NewGuid().ToString("N")[..20]);
        jobNames.Add(job.Name);
        var options = new ScheduledJobOptions { PollSeconds = 1, RetrySeconds = 1 };
        var worker = new ScheduledJobWorker(new ScheduledJobRunner(Npgsql(), options), [job], options, TimeProvider.System,
            NullLogger<ScheduledJobWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await job.SecondRun.Task.WaitAsync(TimeSpan.FromSeconds(30));
            // The job signalled; the runner's own completion is a later statement. Stopping now could cancel it (review F-2).
            await UntilAsync(async () => await RowAsync(job.Name) is { RunCount: 1, LeaseToken: null, LastError: null });
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Matches($"^job:{job.Name}:[0-9a-f]{{32}}$", job.Correlations[0]);
        Assert.NotEqual(job.Correlations[0], job.Correlations[1]); // a new lease, a new correlation
        Assert.Equal(DateTimeKind.Utc, job.Clocks[0].Kind);
        Assert.Equal(0, job.Clocks[0].Ticks % 10); // microseconds, as stored
    }

    [Fact]
    public async Task A_database_unavailable_at_startup_does_not_end_the_worker()
    {
        var job = new FlakyJob("test_" + Guid.NewGuid().ToString("N")[..20], failFirst: false);
        jobNames.Add(job.Name);
        var options = new ScheduledJobOptions { PollSeconds = 1 };
        // A 1-second command timeout, and the table held under an exclusive lock: the first ensure fails like a database that is down.
        var connection = new NpgsqlConnectionStringBuilder(TestDatabase.EnsureReady().ConnectionString) { CommandTimeout = 1 };
        await using var impatient = NpgsqlDataSource.Create(connection.ConnectionString);
        var worker = new ScheduledJobWorker(new ScheduledJobRunner(impatient, options), [job], options, TimeProvider.System,
            NullLogger<ScheduledJobWorker>.Instance);

        await using (var holder = await Npgsql().OpenConnectionAsync())
        {
            await using var transaction = await holder.BeginTransactionAsync();
            await using (var lockTable = new NpgsqlCommand("LOCK TABLE scheduled_job IN ACCESS EXCLUSIVE MODE", holder, transaction))
                await lockTable.ExecuteNonQueryAsync();
            await worker.StartAsync(CancellationToken.None);
            await Task.Delay(TimeSpan.FromSeconds(2.5)); // at least one ensure has timed out
            Assert.False(worker.ExecuteTask!.IsCompleted);
            Assert.Empty(job.Correlations);
            await transaction.RollbackAsync();
        }

        try
        {
            await job.SecondRun.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await UntilAsync(async () => await RowAsync(job.Name) is { RunCount: 1, LeaseToken: null });
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    // ---- overdue work orders ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Overdue_is_judged_by_the_agenda_night_not_the_calendar_date()
    {
        var due9 = await PlantOrderAsync(WorkOrderStatus.Assigned, new(2001, 3, 9));
        var due8 = await PlantOrderAsync(WorkOrderStatus.Assigned, new(2001, 3, 8));

        // 11:59:59 on the 10th in Ho Chi Minh City is still the night of the 9th: the 9th is not over yet.
        await OverdueAsync(Night10.AddSeconds(-1));
        Assert.Equal([due8], await NoticedAsync());

        await OverdueAsync(Night10);
        Assert.Equal([due8, due9], await NoticedAsync());
    }

    [Fact]
    public async Task Only_open_assigned_and_in_progress_orders_with_a_due_date_are_late()
    {
        var late = new List<string>();
        foreach (var status in Enum.GetValues<WorkOrderStatus>())
        {
            var id = await PlantOrderAsync(status, new(2001, 3, 1));
            if (status is WorkOrderStatus.Open or WorkOrderStatus.Assigned or WorkOrderStatus.InProgress) late.Add(id);
        }
        await PlantOrderAsync(WorkOrderStatus.Assigned, due: null);

        await OverdueAsync(Night10);

        Assert.Equal(late.Order(StringComparer.Ordinal), (await NoticedAsync()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_assignee_and_every_active_manager_of_the_commune_are_told_once()
    {
        var assigned = await PlantOrderAsync(WorkOrderStatus.InProgress, new(2001, 3, 1));
        var open = await PlantOrderAsync(WorkOrderStatus.Open, new(2001, 3, 1));

        await OverdueAsync(Night10);
        await OverdueAsync(Night10.AddHours(1));

        var notices = await NotificationsAsync();
        Assert.Equal(["engineer", "manager", "manager2"], Recipients(notices, assigned));
        Assert.Equal(["manager", "manager2"], Recipients(notices, open)); // nobody assigned, nobody locked, no other commune
        var notice = notices.First(n => n.EntityId == assigned);
        Assert.Equal(NotificationType.WorkOrderOverdue, notice.Type);
        Assert.Equal(NotificationEntityType.WorkOrder, notice.EntityType);
        Assert.Equal($"Phiếu kiểm tra {assigned} đã quá hạn", notice.Title);
        Assert.Equal($"{assigned} — Job test order. Hạn 01/03/2001 đã qua mà phiếu chưa xong.", notice.Body);
    }

    [Fact]
    public async Task A_new_due_date_is_reported_again_and_a_reassignment_is_not()
    {
        var id = await PlantOrderAsync(WorkOrderStatus.Assigned, new(2001, 3, 1));
        await OverdueAsync(Night10);

        await UpdateOrderAsync(id, wo => wo.AssignedTo = users["manager"].UserId);
        await OverdueAsync(Night10.AddHours(1));
        Assert.Single(await NoticesFor(id, "engineer"));

        await UpdateOrderAsync(id, wo => wo.DueDate = new DateOnly(2001, 3, 5));
        await OverdueAsync(Night10.AddHours(2));
        Assert.Equal([new DateOnly(2001, 3, 1), new DateOnly(2001, 3, 5)],
            await Db(db => db.Set<WorkOrderOverdueNotice>().IgnoreQueryFilters().Where(n => n.WorkOrderId == id)
                .OrderBy(n => n.DueDate).Select(n => n.DueDate).ToListAsync()));
        Assert.Equal(2, (await NotificationsAsync()).Count(n => n.EntityId == id && n.RecipientUserId == users["manager2"].UserId));
    }

    [Fact]
    public async Task Simultaneous_runs_tell_each_person_once_and_never_touch_the_work_order()
    {
        var ids = new List<string>();
        for (var i = 0; i < 5; i++) ids.Add(await PlantOrderAsync(WorkOrderStatus.Assigned, new(2001, 3, 1)));
        var before = await Db(db => db.Set<WorkOrder>().IgnoreQueryFilters().AsNoTracking().Where(w => ids.Contains(w.WorkOrderId))
            .Select(w => new { w.WorkOrderId, w.UpdatedAt, w.Version }).ToListAsync());

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => OverdueAsync(Night10)));

        Assert.Equal(15, (await NotificationsAsync()).Count); // 5 orders × (engineer + 2 managers)
        var after = await Db(db => db.Set<WorkOrder>().IgnoreQueryFilters().AsNoTracking().Where(w => ids.Contains(w.WorkOrderId))
            .Select(w => new { w.WorkOrderId, w.UpdatedAt, w.Version }).ToListAsync());
        Assert.Equal(before.OrderBy(x => x.WorkOrderId), after.OrderBy(x => x.WorkOrderId));
        Assert.False(await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().AnyAsync(a => a.CommuneId == home)));
    }

    // ---- silent devices -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_silent_device_raises_one_node_offline_fault_dated_when_it_crossed_the_threshold()
    {
        var last = new DateTime(2001, 3, 10, 1, 0, 0, DateTimeKind.Utc);
        var (node, cabinet) = await PlantNodeAsync(last);

        await OfflineAsync(Night10);
        await OfflineAsync(Night10.AddHours(1));

        var fault = Assert.Single(await FaultsAsync());
        Assert.Equal(FaultType.NodeOffline, fault.FaultType);
        Assert.Equal(FaultStatus.Detected, fault.FaultStatus);
        Assert.Equal(SourceChannel.Iot, fault.SourceChannel);
        Assert.Equal(DataSource.Simulated, fault.DataSource);
        Assert.Equal(Severity.Medium, fault.Severity);
        Assert.Equal(node, fault.OriginNodeId);
        Assert.Equal(last.AddHours(1), fault.DetectedAt); // the iot_offline_after_minutes setting, not when the job ran
        Assert.Equal((16.05, 108.25), (fault.Lat, fault.Lng)); // the cabinet's point
        Assert.Null(fault.PoleId);
        Assert.Null(fault.SegmentId);
        Assert.Null(fault.ReportedBy);
        Assert.Contains(node, fault.Note, StringComparison.Ordinal);

        var audit = Assert.Single(await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().Where(a => a.EntityId == fault.FaultId).ToListAsync()));
        Assert.Equal((AuditActorKind.Iot, AuditAction.Created), (audit.ActorKind, audit.Action));
        Assert.Null(audit.ActorUserId);
        Assert.StartsWith("job:test:", audit.CorrelationId, StringComparison.Ordinal);

        var notices = await NotificationsAsync();
        Assert.Equal(["manager", "manager2"], Recipients(notices, fault.FaultId));
        Assert.All(notices, n => Assert.Equal(NotificationType.FaultReported, n.Type));
        Assert.Equal($"Sự cố mới {fault.FaultId}: Thiết bị mất kết nối", notices[0].Title);
        Assert.Equal($"Thiết bị {node} ở tủ {cabinet} không báo về quá 60 phút. Sự cố chờ Quản lý xử lý.", notices[0].Body);
    }

    [Fact]
    public async Task The_job_agrees_with_the_map_status_on_both_sides_of_the_threshold_and_ignores_never_reported()
    {
        var offlineAfter = (await Db(db => SystemSettingValues.ReadAsync(db, default))).IotOfflineAfter;
        var atThreshold = Night10 - offlineAfter;
        var past = atThreshold.AddTicks(-10); // one microsecond more
        // The job's SQL restates StatusAt (EF cannot translate a method); pinned against the map's own rule on both sides.
        Assert.Equal(NodeStatus.Online, NodeSilence.StatusAt(atThreshold, Night10, offlineAfter));
        Assert.Equal(NodeStatus.Offline, NodeSilence.StatusAt(past, Night10, offlineAfter));
        await PlantNodeAsync(atThreshold);
        var (offline, _) = await PlantNodeAsync(past);
        await PlantNodeAsync(null); // never_reported: not yet in service

        await OfflineAsync(Night10);

        Assert.Equal([offline], (await FaultsAsync()).Select(f => f.OriginNodeId));
    }

    [Fact]
    public async Task A_rejected_fault_is_not_raised_again_for_the_same_silence_but_a_new_silence_is_new()
    {
        var (node, _) = await PlantNodeAsync(new DateTime(2001, 3, 10, 1, 0, 0, DateTimeKind.Utc));
        await OfflineAsync(Night10);
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var fault = await db.Set<Fault>().IgnoreQueryFilters().SingleAsync(f => f.OriginNodeId == node);
            fault.FaultStatus = FaultStatus.Rejected;
            await db.SaveChangesAsync();
            return 0;
        });

        await OfflineAsync(Night10.AddHours(1));
        Assert.Single(await FaultsAsync());

        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var device = await db.Set<IotNode>().IgnoreQueryFilters().SingleAsync(n => n.NodeId == node);
            device.LastReportAt = Night10.AddHours(2); // it came back, then fell silent again
            await db.SaveChangesAsync();
            return 0;
        });
        await OfflineAsync(Night10.AddHours(4));

        var first = new DateTime(2001, 3, 10, 2, 0, 0, DateTimeKind.Utc);
        Assert.Equal([first, Night10.AddHours(3)], (await FaultsAsync()).Select(f => f.DetectedAt).Order());
    }

    [Fact]
    public async Task The_job_reads_the_threshold_from_the_setting_at_every_pass()
    {
        var last = Night10.AddMinutes(-45);
        await PlantNodeAsync(last);

        await OfflineAsync(Night10); // 45 minutes silent, under the seeded 60
        Assert.Empty(await FaultsAsync());

        await factory.WithSettingAsync(SystemSettingKeys.IotOfflineAfterMinutes, 30, () => OfflineAsync(Night10));

        var fault = Assert.Single(await FaultsAsync());
        Assert.Equal(last.AddMinutes(30), fault.DetectedAt);
        Assert.Contains("quá 30 phút", fault.Note, StringComparison.Ordinal);
    }

    /// <summary>
    /// BE-33 D-8: <c>detected_at</c> is last report + threshold, so a threshold changed in the middle of a silence moves it — and a
    /// duplicate check on the exact <c>detected_at</c> (and the unique index) would let the same silence in a second time.
    /// </summary>
    [Fact]
    public async Task Changing_the_threshold_in_the_middle_of_a_silence_does_not_raise_it_twice()
    {
        var last = new DateTime(2001, 3, 10, 1, 0, 0, DateTimeKind.Utc);
        await PlantNodeAsync(last);
        await OfflineAsync(Night10);

        await factory.WithSettingAsync(SystemSettingKeys.IotOfflineAfterMinutes, 30, () => OfflineAsync(Night10.AddMinutes(1)));
        await factory.WithSettingAsync(SystemSettingKeys.IotOfflineAfterMinutes, 120, () => OfflineAsync(Night10.AddMinutes(2)));

        var fault = Assert.Single(await FaultsAsync());
        Assert.Equal(last.AddHours(1), fault.DetectedAt);
    }

    [Fact]
    public async Task Simultaneous_runs_raise_one_fault_per_silence()
    {
        for (var i = 0; i < 4; i++) await PlantNodeAsync(new DateTime(2001, 3, 10, 1, i, 0, DateTimeKind.Utc));

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => OfflineAsync(Night10)));

        Assert.Equal(4, (await FaultsAsync()).Count);
        Assert.Equal(8, (await NotificationsAsync()).Count);
        Assert.Equal(4, await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(a => a.CommuneId == home)));
    }

    [Fact]
    public async Task The_device_foreign_key_keeps_its_own_index_beside_the_partial_unique_one()
    {
        var indexes = await Db(db => db.Database.SqlQueryRaw<string>(
            "SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'fault' AND indexname LIKE '%origin_node%'").ToListAsync());
        Assert.Contains(indexes, i => i.Contains("ix_fault_origin_node_id_commune_id", StringComparison.Ordinal) && !i.Contains("WHERE"));
        Assert.Contains(indexes, i => i.Contains("UNIQUE INDEX ux_fault_origin_node_id_detected_at", StringComparison.Ordinal)
            && i.Contains("WHERE (fault_type = 'node_offline'::text)", StringComparison.Ordinal));
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------------------

    private NpgsqlDataSource Npgsql() => factory.Services.GetRequiredService<NpgsqlDataSource>();

    private ScheduledJobRunner Runner() => new(Npgsql(), new ScheduledJobOptions());

    private async Task<string> NewJobAsync()
    {
        var name = "test_" + Guid.NewGuid().ToString("N")[..20];
        jobNames.Add(name);
        await Runner().EnsureAsync([name], CancellationToken.None);
        return name;
    }

    private Task<ScheduledJob> RowAsync(string name)
        => Db(db => db.Set<ScheduledJob>().AsNoTracking().SingleAsync(j => j.JobName == name));

    private static async Task UntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition did not hold within 30 seconds.");
            await Task.Delay(100);
        }
    }

    private Task SqlAsync(string sql, params object[] args) => Db(db => db.Database.ExecuteSqlRawAsync(sql, args));

    private T Job<T>() where T : IScheduledJob => factory.Services.GetServices<IScheduledJob>().OfType<T>().Single();

    private Task OverdueAsync(DateTime now) => Job<WorkOrderOverdueJob>().RunAsync(now, "job:test:" + Guid.NewGuid().ToString("N"), CancellationToken.None);

    private Task OfflineAsync(DateTime now) => Job<NodeOfflineJob>().RunAsync(now, "job:test:" + Guid.NewGuid().ToString("N"), CancellationToken.None);

    private Task<string> PlantOrderAsync(WorkOrderStatus status, DateOnly? due) => Db(async db =>
    {
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var at = new DateTime(2001, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var assigned = status is not (WorkOrderStatus.Open or WorkOrderStatus.Cancelled);
        var started = status is WorkOrderStatus.InProgress or WorkOrderStatus.Done or WorkOrderStatus.Verified;
        var completed = status is WorkOrderStatus.Done or WorkOrderStatus.Verified;
        var wo = new WorkOrder
        {
            CommuneId = home, TaskKind = TaskKind.Inspection, Title = "Job test order", WoStatus = status, CreatedBy = users["manager"].UserId,
            AssignedTo = assigned ? users["engineer"].UserId : null, AssignedAt = assigned ? at : null, DueDate = due,
            StartedAt = started ? at : null, CompletedAt = completed ? at : null, ReportNote = completed ? "xong" : null,
            ClosedAt = status is WorkOrderStatus.Verified or WorkOrderStatus.Cancelled ? at : null, CreatedAt = at, UpdatedAt = at,
        };
        db.Add(wo);
        await db.SaveChangesAsync();
        return wo.WorkOrderId;
    });

    private Task UpdateOrderAsync(string id, Action<WorkOrder> change) => Db(async db =>
    {
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        change(await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(w => w.WorkOrderId == id));
        await db.SaveChangesAsync();
        return 0;
    });

    /// <summary>A device on a cabinet of its own (one device per cabinet, <c>ux_iot_node_cabinet_id</c>).</summary>
    private Task<(string Node, string Cabinet)> PlantNodeAsync(DateTime? lastReport) => Db(async db =>
    {
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var box = new ElectricalCabinet
        {
            CabinetName = "Job test cabinet", CommuneId = home, DataSource = LuxMap.Shared.Contracts.Enums.DataSource.Simulated,
            Geom = new Point(108.25, 16.05) { SRID = 4326 },
        };
        db.Add(box);
        await db.SaveChangesAsync();
        var node = new IotNode
        {
            CommuneId = home, CabinetId = box.CabinetId, DataSource = LuxMap.Shared.Contracts.Enums.DataSource.Simulated, LastReportAt = lastReport,
            CreatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        db.Add(node);
        await db.SaveChangesAsync();
        return (node.NodeId, box.CabinetId);
    });

    private Task<List<string>> NoticedAsync() => Db(db => db.Set<WorkOrderOverdueNotice>().IgnoreQueryFilters()
        .Where(n => n.CommuneId == home).OrderBy(n => n.DueDate).ThenBy(n => n.WorkOrderId.Length).ThenBy(n => n.WorkOrderId)
        .Select(n => n.WorkOrderId).ToListAsync());

    private Task<List<Notification>> NotificationsAsync() => Db(db => db.Set<Notification>().IgnoreQueryFilters()
        .Where(n => n.CommuneId == home || n.CommuneId == other).ToListAsync());

    private async Task<List<Notification>> NoticesFor(string id, string user)
        => [.. (await NotificationsAsync()).Where(n => n.EntityId == id && n.RecipientUserId == users[user].UserId)];

    private Task<List<Fault>> FaultsAsync() => Db(db => db.Set<Fault>().IgnoreQueryFilters().AsNoTracking()
        .Where(f => f.CommuneId == home || f.CommuneId == other).ToListAsync());

    private string[] Recipients(IEnumerable<Notification> notices, string entityId)
        => [.. notices.Where(n => n.EntityId == entityId)
            .Select(n => users.Single(u => u.Value.UserId == n.RecipientUserId).Key).Order(StringComparer.Ordinal)];

    /// <summary>Fails its first run and signals the second; or, with <paramref name="failFirst"/> false, signals the first.</summary>
    private sealed class FlakyJob(string name, bool failFirst = true) : IScheduledJob
    {
        public List<string> Correlations { get; } = [];
        public List<DateTime> Clocks { get; } = [];
        public TaskCompletionSource SecondRun { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Name { get; } = name;
        public TimeSpan Interval => TimeSpan.FromHours(1);

        public Task RunAsync(DateTime now, string correlationId, CancellationToken ct)
        {
            Correlations.Add(correlationId);
            Clocks.Add(now);
            if (failFirst && Correlations.Count == 1) throw new InvalidOperationException("first run fails");
            SecondRun.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
