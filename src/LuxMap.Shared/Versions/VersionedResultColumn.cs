namespace LuxMap.Shared.Versions;

/// <summary>How a column names the <c>artifact_version</c> behind a result (BE-34).</summary>
public enum VersionReference
{
    /// <summary>The column holds <c>artifact_version.version_id</c>.</summary>
    Id,

    /// <summary>The column holds the version string itself (a photo's <c>model_version</c>).</summary>
    Text,

    /// <summary>
    /// The column holds the version string, or <c>"{version}+{rules}"</c> when rules were applied on top of the model (a photo's
    /// AI hint, a fault raised from one). Matched as exactly the version, or the version followed by <c>+</c> — never by LIKE.
    /// </summary>
    ComposedText,

    /// <summary>
    /// The row carries no version of its own but a key to a parent that does (a pole observation through its run): counted when
    /// its parent's <see cref="Id"/> columns name the version.
    /// </summary>
    ViaParent,
}

/// <summary>
/// One column through which a result names the version that produced it (BE-34, D-8 of Phase 1). The module that owns the table
/// registers it as a singleton; <c>GET /admin/artifact-versions/{id}/usage</c> counts the rows of every registration without
/// referencing the module — the same pattern as <c>StoredObjectColumn</c> (BE-35).
/// </summary>
/// <remarks>
/// Identifiers, never input: they are spliced into SQL and checked at startup. <c>VersionedResultColumnTests</c> fails when a
/// foreign key to <c>artifact_version</c>, or a <c>*model_version</c> / <c>*artifact_version</c> column, has no registration.
/// </remarks>
/// <param name="Result">The name the usage endpoint reports the count under (<c>survey_run</c>, <c>fault</c>…).</param>
/// <param name="DataSourceColumn">The row's <c>data_source</c> column, when it has one: the count is then split by it.</param>
/// <param name="ParentTable">For <see cref="VersionReference.ViaParent"/>: the table <paramref name="Column"/> points at.</param>
/// <param name="ParentKey">For <see cref="VersionReference.ViaParent"/>: the parent's key column.</param>
public sealed record VersionedResultColumn(
    string Result, string Table, string Column, VersionReference Reference, string? DataSourceColumn = null,
    string? ParentTable = null, string? ParentKey = null)
{
    public static VersionedResultColumn Through(string result, string table, string column, string parentTable, string parentKey,
        string? dataSourceColumn = null)
        => new(result, table, column, VersionReference.ViaParent, dataSourceColumn, parentTable, parentKey);
}
