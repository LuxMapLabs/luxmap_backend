using LuxMap.Shared.Versions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-34 — every column through which a result names a version is registered as a <see cref="VersionedResultColumn"/>, so a new
/// results table cannot drop out of <c>GET /admin/artifact-versions/{id}/usage</c> silently. Read against the live schema, the way
/// <c>StoredObjectColumnTests</c> guards the storage totals.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class VersionedResultColumnTests(AssetImportFixture fixture)
{
    /// <summary>Columns that name a version but are deliberately not counted, each with its reason.</summary>
    private static readonly Dictionary<string, string> NotCounted = new(StringComparer.Ordinal)
    {
        ["work_order_pole_photo.artifact_version"] = "the same photo as work_order_pole_photo.model_version, with '+{rules}' behind it",
    };

    [Fact]
    public async Task Every_foreign_key_to_the_registry_and_every_version_text_column_is_registered()
    {
        var registered = fixture.Services.GetServices<VersionedResultColumn>()
            .Where(c => c.Reference != VersionReference.ViaParent)
            .Select(c => $"{c.Table}.{c.Column}").ToHashSet(StringComparer.Ordinal);

        var inSchema = await fixture.QueryAsync(db => db.Database.SqlQueryRaw<string>("""
            SELECT c.conrelid::regclass::text || '.' || a.attname AS "Value"
            FROM pg_constraint c JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY (c.conkey)
            WHERE c.contype = 'f' AND c.confrelid = 'artifact_version'::regclass
            UNION
            SELECT table_name || '.' || column_name FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name <> 'artifact_version'
              AND (column_name LIKE '%model_version' OR column_name LIKE '%artifact_version')
            """).ToListAsync());

        var missing = inSchema.Where(column => !registered.Contains(column) && !NotCounted.ContainsKey(column)).Order().ToList();
        Assert.True(missing.Count == 0, $"Version columns with no VersionedResultColumn registration: {string.Join(", ", missing)}");
        Assert.All(NotCounted.Keys, column => Assert.Contains(column, inSchema));
    }

    /// <summary>A result reached through its parent (an observation through its run) points at a parent that is itself registered.</summary>
    [Fact]
    public void Every_through_parent_registration_has_a_registered_parent()
    {
        var columns = fixture.Services.GetServices<VersionedResultColumn>().ToList();

        Assert.All(columns.Where(c => c.Reference == VersionReference.ViaParent), child =>
            Assert.Contains(columns, parent => parent.Table == child.ParentTable && parent.Reference == VersionReference.Id));
    }
}
