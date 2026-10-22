using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LuxMap.Modules.Admin.Monitoring;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Jobs;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Serialization;
using LuxMap.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-35 — the system admin's monitoring page (<c>GET /admin/system/status</c>), the orphan check and the low-disk mail.
/// </summary>
/// <remarks>
/// <para>
/// In <see cref="AssetDatabaseCollection"/> because every class that writes sweeps is: the survey counts are compared before and after
/// planting, which holds only while nobody else writes sweeps.
/// </para>
/// <para>
/// The storage sums and the orphan check run on a PROBE table this class creates and drops, registered as a
/// <see cref="StoredObjectColumn"/> — the real photo tables are written by classes outside the collection, so their totals move.
/// <c>storage_reconciliation</c> and <c>storage_alert</c> are written by this class only.
/// </para>
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class SystemMonitoringTests(AssetImportFixture factory) : IAsyncLifetime
{
    private readonly string probe = "be35_probe_" + Guid.NewGuid().ToString("N")[..12];

    private NpgsqlDataSource Source => factory.Services.GetRequiredService<NpgsqlDataSource>();

    private Task<T> Db<T>(Func<LuxMapDbContext, Task<T>> work) => factory.QueryAsync(work);

    public async Task InitializeAsync()
    {
        await ExecuteAsync($"CREATE TABLE {probe} (object_key text, byte_count bigint, thumbnail_key text, thumbnail_bytes bigint)");
        await ExecuteAsync("DELETE FROM storage_alert");
        await ExecuteAsync("DELETE FROM storage_reconciliation");
    }

    public async Task DisposeAsync()
    {
        await ExecuteAsync($"DROP TABLE IF EXISTS {probe}");
        await ExecuteAsync("DELETE FROM storage_alert");
        await ExecuteAsync("DELETE FROM storage_reconciliation");
    }

    // ---- job state -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_job_is_running_stalled_failing_or_ok_in_that_order_of_precedence()
    {
        var now = new DateTime(2001, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var jobs = new ScheduledJobOptions(); // poll 15 s + lease 300 s
        ScheduledJob Row(int nextInSeconds, int failures = 0, int? leaseInSeconds = null) => new()
        {
            JobName = "x", NextRunAt = now.AddSeconds(nextInSeconds), ConsecutiveFailures = failures,
            LastFailedAt = failures > 0 ? now : null,
            LeaseToken = leaseInSeconds is null ? null : Guid.NewGuid(), LeaseExpiresAt = leaseInSeconds is { } l ? now.AddSeconds(l) : null,
        };

        Assert.Equal(JobState.Stalled, SystemStatusService.Classify(null, now, jobs)); // registered, the runner never made its row
        Assert.Equal(JobState.Ok, SystemStatusService.Classify(Row(60), now, jobs));
        Assert.Equal(JobState.Ok, SystemStatusService.Classify(Row(-315), now, jobs)); // due exactly a poll and a lease ago: not yet
        Assert.Equal(JobState.Stalled, SystemStatusService.Classify(Row(-316), now, jobs));
        Assert.Equal(JobState.Failing, SystemStatusService.Classify(Row(60, failures: 2), now, jobs));
        Assert.Equal(JobState.Stalled, SystemStatusService.Classify(Row(-316, failures: 2), now, jobs)); // nothing runs: worse than failing
        Assert.Equal(JobState.Running, SystemStatusService.Classify(Row(-400, failures: 2, leaseInSeconds: 30), now, jobs));
        Assert.Equal(JobState.Stalled, SystemStatusService.Classify(Row(-400, leaseInSeconds: -1), now, jobs)); // a dead holder
    }

    // ---- the page --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_system_admin_reads_every_registered_job_every_bucket_the_disk_and_the_database()
    {
        var client = await factory.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");
        var response = await client.GetAsync("/api/v1/admin/system/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        var registered = factory.Services.GetServices<IScheduledJob>().Select(job => job.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Contains("storage_capacity", registered);
        Assert.Contains("storage_reconciliation", registered);
        var jobs = root.GetProperty("jobs").EnumerateArray().ToList();
        Assert.Equal(registered, jobs.Select(job => job.GetProperty("name").GetString()!).ToArray());
        Assert.All(jobs, job => Assert.Contains(job.GetProperty("state").GetString(), new[] { "ok", "failing", "stalled", "running" }));

        var storage = root.GetProperty("storage");
        Assert.Equal(["survey", "evidence", "video", "citizen"],
            storage.GetProperty("buckets").EnumerateArray().Select(b => b.GetProperty("bucket").GetString()!).ToArray());
        var disk = storage.GetProperty("disk");
        Assert.True(disk.GetProperty("total_bytes").GetInt64() > 0);
        Assert.InRange(disk.GetProperty("free_ratio").GetDouble(), 0, 1);
        Assert.True(storage.GetProperty("database_bytes").GetInt64() > 0);

        var byStatus = root.GetProperty("survey_processing").GetProperty("by_status");
        Assert.Equal(["not_started", "queued", "processing", "succeeded", "failed"], byStatus.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.EndsWith("Z", root.GetProperty("generated_at").GetString());
    }

    [Fact]
    public async Task Failed_and_stuck_sweeps_are_counted_and_the_failed_run_is_listed_newest_first()
    {
        var before = await StatusAsync();
        var (failedSweep, stuckSweep) = await PlantSweepsAsync();
        var after = await StatusAsync();

        Assert.Equal(before.SurveyProcessing.ByStatus.Failed + 1, after.SurveyProcessing.ByStatus.Failed);
        Assert.Equal(before.SurveyProcessing.ByStatus.Processing + 1, after.SurveyProcessing.ByStatus.Processing);
        Assert.Equal(before.SurveyProcessing.Stuck + 1, after.SurveyProcessing.Stuck);
        var newest = after.SurveyProcessing.RecentFailures[0];
        Assert.Equal((failedSweep, "VIDEO_TIMEOUT", "frames", 3), (newest.SweepId, newest.ErrorCode, newest.Stage, newest.Attempt));
        Assert.DoesNotContain(after.SurveyProcessing.RecentFailures, failure => failure.SweepId == stuckSweep);
        Assert.InRange(after.SurveyProcessing.RecentFailures.Count, 1, SystemStatusService.RecentFailureCount);
    }

    // ---- what the rows hold ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Every_object_key_column_in_the_database_is_registered_and_every_registration_is_a_real_column()
    {
        var actual = await ColumnsAsync("""
            SELECT table_name || '.' || column_name FROM information_schema.columns
            WHERE table_schema = 'public' AND (column_name LIKE '%object_key' OR column_name LIKE '%thumbnail_key')
                AND table_name NOT LIKE 'be35\_probe\_%'
            """);
        var registered = factory.Services.GetServices<StoredObjectColumn>().ToList();
        Assert.Equal(actual.Order(StringComparer.Ordinal), registered.Select(c => $"{c.Table}.{c.KeyColumn}").Order(StringComparer.Ordinal));

        var bytes = await ColumnsAsync("SELECT table_name || '.' || column_name FROM information_schema.columns WHERE table_schema = 'public'");
        Assert.All(registered, c => Assert.Contains($"{c.Table}.{c.BytesColumn}", bytes));
    }

    [Fact]
    public async Task Totals_count_originals_and_thumbnails_per_bucket_and_skip_rows_without_a_key()
    {
        await ExecuteAsync($"INSERT INTO {probe} VALUES ('a', 100, 'a-t', 10), ('b', 200, 'b-t', 20), (NULL, 999, 'c-t', 30)");
        var usage = new StorageUsage(Source,
            [StoredObjectColumn.Original(StorageBucket.Citizen, probe), StoredObjectColumn.Thumbnail(StorageBucket.Citizen, probe)]);

        var totals = await usage.TotalsAsync(CancellationToken.None);

        Assert.Equal((5L, 360L), totals[StorageBucket.Citizen]); // 2 originals (300) + 3 thumbnails (60); the keyless 999 is not stored
        Assert.Equal((0L, 0L), totals[StorageBucket.Survey]);
        Assert.Equal(4, totals.Count);
    }

    [Fact]
    public void A_registration_that_is_not_a_plain_identifier_is_refused()
        => Assert.Throws<InvalidOperationException>(() =>
            new StorageUsage(Source, [new StoredObjectColumn(StorageBucket.Video, "survey_raw_file; DROP TABLE pole", "object_key", "byte_count")]));

    // ---- the orphan check ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Orphans_past_the_grace_period_and_missing_objects_are_reported_and_nothing_is_deleted()
    {
        var now = DateTime.UtcNow;
        await ExecuteAsync($"INSERT INTO {probe} (object_key, byte_count) VALUES ('kept', 1), ('missing', 2)");
        var store = new FakeInventory(StorageBucket.Citizen,
            [new("kept", 1, now.AddDays(-3)), new("old-orphan", 40, now.AddHours(-25)), new("fresh", 7, now.AddHours(-23))]);

        await Reconciliation(store).RunAsync(now, "test", CancellationToken.None);

        var row = await ReconciliationAsync(StorageBucket.Citizen);
        Assert.Equal((3L, 48L), (row.StoreObjectCount, row.StoreBytes));
        Assert.Equal((1L, 40L, 1L), (row.OrphanCount, row.OrphanBytes, row.MissingCount)); // "fresh" may be an upload in flight
        Assert.Equal(["old-orphan"], row.OrphanSample);
        Assert.Equal(["missing"], row.MissingSample);
        Assert.Equal(3, store.Listed); // listed, never touched
        Assert.Equal(4, await Db(db => db.Set<StorageReconciliation>().CountAsync())); // every bucket checked
    }

    [Fact]
    public async Task A_second_run_overwrites_the_bucket_row_and_samples_stop_at_twenty()
    {
        var now = DateTime.UtcNow;
        var old = now.AddDays(-2);
        var job = Reconciliation(new FakeInventory(StorageBucket.Citizen, [.. Enumerable.Range(0, 25).Select(i => new ListedObject($"o{i}", 1, old))]));

        await job.RunAsync(now.AddMinutes(-1), "first", CancellationToken.None);
        await job.RunAsync(now, "second", CancellationToken.None);

        var row = await ReconciliationAsync(StorageBucket.Citizen);
        Assert.Equal(now, row.CheckedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(25, row.OrphanCount);
        Assert.Equal(StorageReconciliationJob.SampleSize, row.OrphanSample.Length);
        Assert.Equal(1, await Db(db => db.Set<StorageReconciliation>().CountAsync(r => r.Bucket == StorageBucket.Citizen)));
    }

    [Fact]
    public async Task A_row_committed_while_the_store_is_being_listed_is_not_reported_missing()
    {
        // Real order: the object is written, THEN its row commits. Here the listing has already passed the object when the row lands.
        var now = DateTime.UtcNow;
        var store = new FakeInventory(StorageBucket.Citizen, [new("a", 1, now.AddDays(-2)), new("b", 1, now.AddDays(-2))])
        {
            AfterFirst = () => ExecuteAsync($"INSERT INTO {probe} (object_key, byte_count) VALUES ('late', 1)"),
        };

        await Reconciliation(store).RunAsync(now, "race", CancellationToken.None);

        Assert.Equal(0, (await ReconciliationAsync(StorageBucket.Citizen)).MissingCount);
    }

    // ---- low disk --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Low_disk_mails_every_active_system_admin_once_per_spell()
    {
        var (disk, mail, job) = Capacity();
        var admin = await SeedAdminEmailAsync();
        var t0 = DateTime.UtcNow;

        disk.Ratio = 0.10;
        await job.RunAsync(t0, "1", CancellationToken.None);
        await job.RunAsync(t0.AddMinutes(15), "2", CancellationToken.None);
        Assert.Single(mail.To(admin));
        Assert.Contains("10", mail.To(admin)[0].Subject);
        Assert.All(await RecipientRolesAsync(mail), role => Assert.Equal(UserRole.SystemAdmin, role));

        disk.Ratio = 0.40; // back above: the spell closes
        await job.RunAsync(t0.AddMinutes(30), "3", CancellationToken.None);
        Assert.Equal(0, await OpenAlertsAsync());

        disk.Ratio = 0.05; // a new spell is a new mail
        await job.RunAsync(t0.AddMinutes(45), "4", CancellationToken.None);
        Assert.Equal(2, mail.To(admin).Count);
        Assert.Equal(2, await Db(db => db.Set<StorageAlert>().CountAsync()));
    }

    [Fact]
    public async Task A_failed_send_is_retried_by_the_next_run()
    {
        var (disk, mail, job) = Capacity();
        var admin = await SeedAdminEmailAsync();
        disk.Ratio = 0.10;
        mail.Fail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.RunAsync(DateTime.UtcNow, "down", CancellationToken.None));
        Assert.Null((await Db(db => db.Set<StorageAlert>().SingleAsync())).NotifiedAt);

        mail.Fail = false;
        await job.RunAsync(DateTime.UtcNow, "up", CancellationToken.None);
        Assert.Single(mail.To(admin));
    }

    [Fact]
    public async Task A_run_that_started_before_the_spell_opened_can_still_mail_and_close_it()
    {
        // Codex (BE-35 review): two instances snapshot the clock at different times. The later one opens the spell; the earlier one,
        // still running, must not stamp notified_at / closed_at BEFORE opened_at (ck_storage_alert_order).
        var (disk, mail, job) = Capacity();
        var admin = await SeedAdminEmailAsync();
        var earlier = DateTime.UtcNow;
        var later = earlier.AddMinutes(1);
        disk.Ratio = 0.10;
        mail.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => job.RunAsync(later, "opens", CancellationToken.None));

        mail.Fail = false;
        await job.RunAsync(earlier, "mails", CancellationToken.None);
        Assert.Single(mail.To(admin));

        disk.Ratio = 0.40;
        await job.RunAsync(earlier, "closes", CancellationToken.None);
        var spell = await Db(db => db.Set<StorageAlert>().AsNoTracking().SingleAsync());
        Assert.True(spell.NotifiedAt >= spell.OpenedAt && spell.ClosedAt >= spell.OpenedAt);
    }

    [Fact]
    public async Task Simultaneous_checks_open_one_spell_and_mail_each_admin_once()
    {
        var (disk, mail, job) = Capacity();
        var admin = await SeedAdminEmailAsync();
        disk.Ratio = 0.01;
        var now = DateTime.UtcNow;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => job.RunAsync(now, $"race-{i}", CancellationToken.None)));

        Assert.Single(mail.To(admin));
        Assert.Equal(1, await OpenAlertsAsync());
    }

    [Fact]
    public void The_real_probe_measures_the_disk_holding_the_path()
    {
        var space = new DriveDiskProbe(new MonitoringOptions { DiskPath = Path.GetTempPath() }).Measure();

        Assert.True(space.TotalBytes > 0);
        Assert.InRange(space.FreeBytes, 0, space.TotalBytes);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------------------

    private StorageReconciliationJob Reconciliation(IObjectInventory store) => new(
        new StorageUsage(Source, [StoredObjectColumn.Original(StorageBucket.Citizen, probe)]), store, Source, new MonitoringOptions(),
        NullLogger<StorageReconciliationJob>.Instance);

    private (FakeDisk Disk, RecordingEmailSender Mail, StorageCapacityJob Job) Capacity()
    {
        var disk = new FakeDisk();
        var mail = new RecordingEmailSender();
        var job = new StorageCapacityJob(disk, Source, factory.Services.GetRequiredService<JobContextFactory>(), mail, new MonitoringOptions(),
            NullLogger<StorageCapacityJob>.Instance);
        return (disk, mail, job);
    }

    /// <summary>Over HTTP as the seeded admin: outside a request the commune scope is empty and the query filters hide every sweep.</summary>
    private async Task<SystemStatusResponse> StatusAsync()
    {
        var client = await factory.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");
        var json = new JsonSerializerOptions();
        LuxMapJsonOptions.Configure(json);
        return (await client.GetFromJsonAsync<SystemStatusResponse>("/api/v1/admin/system/status", json))!;
    }

    /// <summary>Survey rows are immutable: what is planted here stays for the run, in a commune and under an account of its own.</summary>
    private Task<(string Failed, string Stuck)> PlantSweepsAsync() => Db(async db =>
    {
        using var seed = db.EnterUnscopedSystemWriteBackdoor();
        var commune = new AdministrativeUnit { Name = "monitoring " + Guid.NewGuid().ToString("N") };
        db.Add(commune);
        var person = new AppUser
        {
            Username = "monitoring" + Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid", FullName = "Monitoring",
            PasswordHash = "unused", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", PasswordSetAt = DateTime.UtcNow, Role = UserRole.Manager,
        };
        db.Add(person);
        await db.SaveChangesAsync();
        var order = new WorkOrder
        {
            CommuneId = commune.CommuneId, CreatedBy = person.UserId, AssignedTo = person.UserId, AssignedAt = DateTime.UtcNow,
            TaskKind = TaskKind.Survey, WoStatus = WorkOrderStatus.InProgress, Title = "Monitoring", StartedAt = DateTime.UtcNow,
        };
        db.Add(order);
        var versions = new[] { "association_algorithm", "clock_algorithm" }.Select(c => new ArtifactVersion
            { Component = c, Version = Guid.NewGuid().ToString("N"), ArtifactHash = new('a', 64), CreatedAt = DateTime.UtcNow }).ToArray();
        db.AddRange(versions);
        await db.SaveChangesAsync();

        SurveySweep Sweep(SweepStatus status, SweepProcessingStatus processing) => new()
        {
            WorkOrderId = order.WorkOrderId, CommuneId = commune.CommuneId, CapturedBy = person.UserId, ClientOpId = Guid.NewGuid(),
            BootSessionId = Guid.NewGuid(), CreateRequestHash = new('a', 64), UtcAnchor = DateTime.UtcNow, EndedElapsedNs = 10_000_000_000L,
            DataSource = DataSource.Simulated, Status = status, ProcessingStatus = processing,
        };
        var failed = Sweep(SweepStatus.Failed, SweepProcessingStatus.Failed);
        failed.ProcessingAttempt = 3;
        var stuck = Sweep(SweepStatus.Processing, SweepProcessingStatus.Processing);
        stuck.ProcessingAttempt = 1;
        stuck.ProcessingLeaseOwner = Guid.NewGuid();
        stuck.ProcessingLeaseExpiresAt = DateTime.UtcNow.AddMinutes(-5);
        db.AddRange(failed, stuck);
        await db.SaveChangesAsync();

        // Finished in the future: the newest failure of the run, whatever the other classes planted.
        var at = DateTime.UtcNow.AddYears(50);
        db.Add(new SurveyProcessingRun
        {
            SweepId = failed.SweepId, CommuneId = commune.CommuneId, Attempt = 3, LeaseOwner = Guid.NewGuid(), LeaseExpiresAt = at,
            InputHash = new('b', 64), SettingsSnapshot = "{}", AlgorithmVersionId = versions[0].VersionId, ClockVersionId = versions[1].VersionId,
            ResultState = "failed", Stage = "frames", ErrorCode = "VIDEO_TIMEOUT", StartedAt = at.AddSeconds(-10), FinishedAt = at,
        });
        await db.SaveChangesAsync();
        return (failed.SweepId, stuck.SweepId);
    });

    private Task<StorageReconciliation> ReconciliationAsync(StorageBucket bucket)
        => Db(db => db.Set<StorageReconciliation>().AsNoTracking().SingleAsync(row => row.Bucket == bucket));

    private Task<int> OpenAlertsAsync() => Db(db => db.Set<StorageAlert>().CountAsync(alert => alert.ClosedAt == null));

    private Task<string> SeedAdminEmailAsync()
        => Db(db => db.Set<AppUser>().Where(user => user.Username == "admin").Select(user => user.Email).SingleAsync());

    /// <summary>The roles behind the addresses mailed — of the accounts still there: other classes delete the admins they create.</summary>
    private Task<List<UserRole>> RecipientRolesAsync(RecordingEmailSender mail) => Db(db =>
    {
        var addresses = mail.Addresses.ToArray();
        return db.Set<AppUser>().Where(user => addresses.Contains(user.Email)).Select(user => user.Role).ToListAsync();
    });

    private async Task<List<string>> ColumnsAsync(string sql)
    {
        await using var command = Source.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var command = Source.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FakeDisk : IDiskProbe
    {
        public double Ratio { get; set; } = 0.5;

        public DiskSpace Measure() => new(40_000_000_000, (long)(40_000_000_000 * Ratio));
    }

    private sealed class FakeInventory(StorageBucket bucket, IReadOnlyList<ListedObject> objects) : IObjectInventory
    {
        public int Listed { get; private set; }

        public Func<Task>? AfterFirst { get; init; }

        public async IAsyncEnumerable<ListedObject> ListAsync(StorageBucket listed, [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (listed != bucket) yield break;
            foreach (var item in objects)
            {
                Listed++;
                yield return item;
                if (Listed == 1 && AfterFirst is { } after) await after();
            }
        }
    }
}
