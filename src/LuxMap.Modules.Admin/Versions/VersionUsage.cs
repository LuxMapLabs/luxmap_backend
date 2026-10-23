using System.Text.RegularExpressions;
using LuxMap.Shared.Versions;
using Npgsql;

namespace LuxMap.Modules.Admin.Versions;

/// <summary>
/// How many results each <see cref="VersionedResultColumn"/> attributes to one <c>artifact_version</c> (BE-34). Raw SQL on purpose, like
/// <c>StorageUsage</c>: the tables belong to modules Admin does not reference, and the count is system-wide — no commune filter applies.
/// </summary>
public sealed partial class VersionUsage
{
    private readonly NpgsqlDataSource dataSource;

    public VersionUsage(NpgsqlDataSource dataSource, IEnumerable<VersionedResultColumn> columns)
    {
        this.dataSource = dataSource;
        Columns = [.. columns];
        // Spliced into SQL: a registration that is not a plain lowercase identifier, or a parent nobody registered, stops startup.
        foreach (var c in Columns)
        {
            var names = new[] { c.Result, c.Table, c.Column, c.DataSourceColumn, c.ParentTable, c.ParentKey }.OfType<string>();
            if (!names.All(name => Identifier().IsMatch(name)))
                throw new InvalidOperationException($"Versioned result column {c} is not a plain identifier.");
            if (c.Reference == VersionReference.ViaParent && (c.ParentKey is null || ParentIdColumns(c).Count == 0))
                throw new InvalidOperationException($"Versioned result column {c} points at a parent with no registered id column.");
        }
    }

    public IReadOnlyList<VersionedResultColumn> Columns { get; }

    /// <summary>
    /// Every result kind registered, in registration order and summed across its columns — zero when nothing used the version, so
    /// "no result" reads differently from "not counted".
    /// </summary>
    public async Task<IReadOnlyList<ResultCount>> CountAsync(long versionId, string version, CancellationToken ct)
    {
        var counts = new Dictionary<string, (long Count, Dictionary<string, long>? BySource)>(StringComparer.Ordinal);
        foreach (var c in Columns)
            counts.TryAdd(c.Result, (0, c.DataSourceColumn is null ? null : new Dictionary<string, long>(StringComparer.Ordinal)));

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        foreach (var c in Columns)
        {
            var source = c.DataSourceColumn is null ? "NULL::text" : $"{c.DataSourceColumn}::text";
            await using var command = new NpgsqlCommand(
                $"SELECT {source}, count(*)::bigint FROM {c.Table} WHERE {Condition(c)} GROUP BY 1", connection);
            command.Parameters.AddWithValue("id", versionId);
            command.Parameters.AddWithValue("v", version);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var (count, bySource) = counts[c.Result];
                var n = reader.GetInt64(1);
                if (bySource is not null && !reader.IsDBNull(0))
                    bySource[reader.GetString(0)] = bySource.GetValueOrDefault(reader.GetString(0)) + n;
                counts[c.Result] = (count + n, bySource);
            }
        }

        return [.. counts.Select(pair => new ResultCount(pair.Key, pair.Value.Count, pair.Value.BySource))];
    }

    /// <summary>The one place each reference kind becomes SQL. A composed string is the version, or the version and "+" — not LIKE.</summary>
    private string Condition(VersionedResultColumn c) => c.Reference switch
    {
        VersionReference.Id => $"{c.Column} = @id",
        VersionReference.Text => $"{c.Column} = @v",
        VersionReference.ComposedText => $"({c.Column} = @v OR left({c.Column}, char_length(@v) + 1) = @v || '+')",
        VersionReference.ViaParent =>
            $"{c.Column} IN (SELECT {c.ParentKey} FROM {c.ParentTable} WHERE @id IN ({string.Join(", ", ParentIdColumns(c))}))",
        _ => throw new InvalidOperationException($"Unknown version reference {c.Reference}."),
    };

    private List<string> ParentIdColumns(VersionedResultColumn c)
        => [.. Columns.Where(p => p.Table == c.ParentTable && p.Reference == VersionReference.Id).Select(p => p.Column)];

    [GeneratedRegex("^[a-z_][a-z0-9_]*$")]
    private static partial Regex Identifier();
}

public sealed record ResultCount(string Result, long Count, IReadOnlyDictionary<string, long>? ByDataSource);
