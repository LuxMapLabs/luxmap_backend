using System.Text.RegularExpressions;
using LuxMap.Shared.Storage;
using Npgsql;

namespace LuxMap.Modules.Admin.Monitoring;

/// <summary>
/// What the rows say is in each bucket (BE-35): adds up the <see cref="StoredObjectColumn"/>s the modules registered. Raw SQL on purpose:
/// the tables belong to modules Admin does not reference, and the totals are system-wide — no commune filter applies to a byte count.
/// </summary>
public sealed partial class StorageUsage
{
    private readonly NpgsqlDataSource dataSource;

    public StorageUsage(NpgsqlDataSource dataSource, IEnumerable<StoredObjectColumn> columns)
    {
        this.dataSource = dataSource;
        Columns = [.. columns];
        // The names are spliced into SQL: a registration that is not a plain lowercase identifier stops startup.
        foreach (var column in Columns)
        {
            if (!Identifier().IsMatch(column.Table) || !Identifier().IsMatch(column.KeyColumn) || !Identifier().IsMatch(column.BytesColumn))
                throw new InvalidOperationException($"Stored object column {column} is not a plain identifier.");
        }
    }

    public IReadOnlyList<StoredObjectColumn> Columns { get; }

    /// <summary>Objects and bytes per bucket; a bucket nothing registered still appears, at zero.</summary>
    public async Task<IReadOnlyDictionary<StorageBucket, (long Objects, long Bytes)>> TotalsAsync(CancellationToken ct)
    {
        var totals = Enum.GetValues<StorageBucket>().ToDictionary(bucket => bucket, _ => (0L, 0L));
        if (Columns.Count == 0) return totals;

        var sql = string.Join(" UNION ALL ", Columns.Select((c, i) =>
            $"SELECT {i} AS n, count({c.KeyColumn})::bigint, (coalesce(sum({c.BytesColumn}) FILTER (WHERE {c.KeyColumn} IS NOT NULL), 0))::bigint FROM {c.Table}"));
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var bucket = Columns[reader.GetInt32(0)].Bucket;
            var (objects, bytes) = totals[bucket];
            totals[bucket] = (objects + reader.GetInt64(1), bytes + reader.GetInt64(2));
        }

        return totals;
    }

    /// <summary>Every key the rows hold in <paramref name="bucket"/>.</summary>
    public async Task<HashSet<string>> KeysAsync(StorageBucket bucket, CancellationToken ct)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in Columns.Where(c => c.Bucket == bucket))
        {
            await using var command = dataSource.CreateCommand(
                $"SELECT {column.KeyColumn} FROM {column.Table} WHERE {column.KeyColumn} IS NOT NULL");
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) keys.Add(reader.GetString(0));
        }

        return keys;
    }

    public async Task<long> DatabaseBytesAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("SELECT pg_database_size(current_database())");
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]*$")]
    private static partial Regex Identifier();
}
