using LuxMap.Persistence.Conventions;
using LuxMap.Persistence.Jobs;
using LuxMap.Shared.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace LuxMap.Modules.Admin.Monitoring;

/// <summary>
/// Once a day, each bucket's keys in the store against the keys its rows hold (BE-35, D-6) — the reconciliation the "object first, row
/// second" rule leaves owed (<c>docs/agents/traps/media-storage.md</c>, rule 3). Reports; never deletes.
/// </summary>
/// <remarks>
/// 🔴 <b>The rows are read BEFORE the store is listed.</b> A row is committed only after its object is written, so every key read first
/// is already in the store when the listing starts: "missing" cannot be a race. The other way round — an object written after the rows
/// were read — looks like an orphan, which is what <see cref="MonitoringOptions.OrphanGrace"/> absorbs.
/// </remarks>
public sealed class StorageReconciliationJob(
    StorageUsage usage, IObjectInventory inventory, NpgsqlDataSource dataSource, MonitoringOptions options,
    ILogger<StorageReconciliationJob> log) : IScheduledJob
{
    public const int SampleSize = 20;

    public string Name => "storage_reconciliation";

    public TimeSpan Interval => TimeSpan.FromDays(1);

    public async Task RunAsync(DateTime now, string correlationId, CancellationToken ct)
    {
        foreach (var bucket in Enum.GetValues<StorageBucket>())
        {
            var result = await CheckAsync(bucket, now, ct);
            await SaveAsync(result, ct);
            if (result.OrphanCount > 0 || result.MissingCount > 0)
                log.LogWarning("Bucket {Bucket}: {Orphans} orphan objects ({OrphanBytes} bytes), {Missing} missing ({Correlation}).",
                    bucket, result.OrphanCount, result.OrphanBytes, result.MissingCount, correlationId);
        }
    }

    private async Task<StorageReconciliation> CheckAsync(StorageBucket bucket, DateTime now, CancellationToken ct)
    {
        var known = await usage.KeysAsync(bucket, ct);
        var found = new HashSet<string>(StringComparer.Ordinal);
        var result = new StorageReconciliation { Bucket = bucket, CheckedAt = now };
        var orphans = new List<string>();
        var settled = now - options.OrphanGrace;
        await foreach (var item in inventory.ListAsync(bucket, ct))
        {
            result.StoreObjectCount++;
            result.StoreBytes += item.Bytes;
            if (known.Contains(item.Key))
            {
                found.Add(item.Key);
            }
            else if (item.LastModifiedUtc < settled)
            {
                result.OrphanCount++;
                result.OrphanBytes += item.Bytes;
                if (orphans.Count < SampleSize) orphans.Add(item.Key);
            }
        }

        var missing = known.Where(key => !found.Contains(key)).ToList();
        result.MissingCount = missing.Count;
        result.OrphanSample = [.. orphans];
        result.MissingSample = [.. missing.Take(SampleSize)];
        return result;
    }

    /// <summary>One row per bucket, overwritten: two instances checking at once both write a correct result, and the last one stays.</summary>
    private async Task SaveAsync(StorageReconciliation row, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO storage_reconciliation (bucket, checked_at, store_object_count, store_bytes, orphan_count, orphan_bytes,
                missing_count, orphan_sample, missing_sample)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            ON CONFLICT (bucket) DO UPDATE SET checked_at = EXCLUDED.checked_at, store_object_count = EXCLUDED.store_object_count,
                store_bytes = EXCLUDED.store_bytes, orphan_count = EXCLUDED.orphan_count, orphan_bytes = EXCLUDED.orphan_bytes,
                missing_count = EXCLUDED.missing_count, orphan_sample = EXCLUDED.orphan_sample, missing_sample = EXCLUDED.missing_sample
            """);
        command.Parameters.Add(new NpgsqlParameter { Value = ContractEnum.ToDbValue(row.Bucket) });
        command.Parameters.Add(new NpgsqlParameter { Value = row.CheckedAt, NpgsqlDbType = NpgsqlDbType.TimestampTz });
        foreach (var value in new[] { row.StoreObjectCount, row.StoreBytes, row.OrphanCount, row.OrphanBytes, row.MissingCount })
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        command.Parameters.Add(new NpgsqlParameter { Value = row.OrphanSample, NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter { Value = row.MissingSample, NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text });
        await command.ExecuteNonQueryAsync(ct);
    }
}
