using LuxMap.Shared.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LuxMap.Persistence.Jobs;

/// <summary>Section <c>Jobs</c>: whether this instance runs background jobs, and how often it looks for a due one.</summary>
public sealed record ScheduledJobOptions
{
    public const string SectionName = "Jobs";

    public bool Enabled { get; init; } = true;

    public int PollSeconds { get; init; } = 15;

    /// <summary>How long a claimed run may hold its job before another instance may take it.</summary>
    public int LeaseSeconds { get; init; } = 300;

    /// <summary>The wait after a FAILED run (capped by the job's own interval).</summary>
    public int RetrySeconds { get; init; } = 300;

    public void Validate()
    {
        if (PollSeconds is < 1 or > 3600 || LeaseSeconds is < 10 or > 86_400 || RetrySeconds is < 1 or > 86_400)
            throw new InvalidOperationException($"{SectionName} has an out-of-range setting.");
    }
}

/// <summary>
/// The lease protocol on <c>scheduled_job</c> (BE-26, D-2). Every time comparison is the DATABASE's <c>now()</c> — claim and fence agree on
/// one clock, whatever the instances' clocks say (Codex P1a §4). Raw SQL on purpose: the table is system bookkeeping (no commune, not
/// audited), and a claim must be ONE statement so two instances cannot both win it.
/// </summary>
public sealed class ScheduledJobRunner(NpgsqlDataSource dataSource, ScheduledJobOptions options)
{
    /// <summary>A row for every registered job; an existing row keeps its schedule.</summary>
    public async Task EnsureAsync(IEnumerable<string> names, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            "INSERT INTO scheduled_job (job_name, next_run_at, run_count) SELECT unnest($1::text[]), now(), 0 ON CONFLICT (job_name) DO NOTHING");
        command.Parameters.Add(new NpgsqlParameter { Value = names.ToArray() });
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Takes ONE due job whose lease is free or expired, under a new token; null when nothing is due.</summary>
    public async Task<(string Name, Guid Token)?> ClaimAsync(IReadOnlyCollection<string> registered, CancellationToken ct)
    {
        var token = Guid.NewGuid();
        await using var command = dataSource.CreateCommand("""
            UPDATE scheduled_job SET lease_token = $1, lease_expires_at = now() + make_interval(secs => $2), last_started_at = now()
            WHERE job_name = (
                SELECT job_name FROM scheduled_job
                WHERE job_name = ANY($3) AND next_run_at <= now() AND (lease_expires_at IS NULL OR lease_expires_at < now())
                ORDER BY next_run_at, job_name
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING job_name
            """);
        command.Parameters.Add(new NpgsqlParameter { Value = token });
        command.Parameters.Add(new NpgsqlParameter { Value = (double)options.LeaseSeconds });
        command.Parameters.Add(new NpgsqlParameter { Value = registered.ToArray() });
        return await command.ExecuteScalarAsync(ct) is string name ? (name, token) : null;
    }

    /// <summary>Success: next run after <paramref name="interval"/>. A run whose lease was taken over writes nothing (the fence).</summary>
    public Task<bool> CompleteAsync(string name, Guid token, TimeSpan interval, CancellationToken ct)
        => FinishAsync(name, token, interval, error: null, ct);

    /// <summary>Failure: kept in <c>last_error</c>, retried after <c>Jobs:RetrySeconds</c> (or the interval, if shorter).</summary>
    public Task<bool> FailAsync(string name, Guid token, TimeSpan interval, string error, CancellationToken ct)
        => FinishAsync(name, token, TimeSpan.FromSeconds(Math.Min(interval.TotalSeconds, options.RetrySeconds)),
            error.Length > 1000 ? error[..1000] : error, ct);

    private async Task<bool> FinishAsync(string name, Guid token, TimeSpan wait, string? error, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE scheduled_job SET next_run_at = now() + make_interval(secs => $3), lease_token = NULL, lease_expires_at = NULL,
                last_finished_at = now(), last_error = $4, run_count = run_count + CASE WHEN $4 IS NULL THEN 1 ELSE 0 END
            WHERE job_name = $1 AND lease_token = $2
            """);
        command.Parameters.Add(new NpgsqlParameter { Value = name });
        command.Parameters.Add(new NpgsqlParameter { Value = token });
        command.Parameters.Add(new NpgsqlParameter { Value = wait.TotalSeconds });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)error ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }
}

/// <summary>
/// Runs the registered <see cref="IScheduledJob"/>s (BE-26). Polls every <c>Jobs:PollSeconds</c>; claims and runs due jobs one at a time
/// until none is due. Off with <c>Jobs:Enabled = false</c> (the test hosts) — a job can then be run directly.
/// </summary>
public sealed class ScheduledJobWorker(
    ScheduledJobRunner runner, IEnumerable<IScheduledJob> jobs, ScheduledJobOptions options, TimeProvider clock,
    ILogger<ScheduledJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        var byName = jobs.ToDictionary(job => job.Name, StringComparer.Ordinal);
        if (byName.Count == 0) return;

        // Inside the retry loop: a database down at startup must not end the worker — or, with the default
        // BackgroundServiceExceptionBehavior.StopHost, the whole API. Claims wait until the rows exist.
        var ensured = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!ensured)
                {
                    await runner.EnsureAsync(byName.Keys, stoppingToken);
                    ensured = true;
                }

                while (await runner.ClaimAsync(byName.Keys, stoppingToken) is { } claim)
                {
                    await RunOneAsync(byName[claim.Name], claim.Token, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Background job runner iteration failed; the next poll retries.");
            }

            await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), stoppingToken);
        }
    }

    private async Task RunOneAsync(IScheduledJob job, Guid token, CancellationToken ct)
    {
        var correlation = $"job:{job.Name}:{token:N}";
        try
        {
            await job.RunAsync(UtcMicrosecondClock.UtcNow(clock), correlation, ct);
            if (!await runner.CompleteAsync(job.Name, token, job.Interval, ct))
                logger.LogWarning("Job {Job} finished after its lease was taken over ({Correlation}).", job.Name, correlation);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the lease expires and another run picks the job up
        }
        catch (Exception error)
        {
            logger.LogError(error, "Job {Job} failed ({Correlation}).", job.Name, correlation);
            await runner.FailAsync(job.Name, token, job.Interval, error.GetType().Name + ": " + error.Message, ct);
        }
    }
}
