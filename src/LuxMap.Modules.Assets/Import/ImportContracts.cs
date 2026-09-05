namespace LuxMap.Modules.Assets.Import;

/// <summary>Which of the four asset files is being imported. Order matters — see <see cref="ImportKindExtensions"/>.</summary>
public enum ImportKind
{
    Segments,
    Feeders,
    Poles,
    Fixtures,
}

public static class ImportKindExtensions
{
    /// <summary>
    /// The order the four files must be loaded in, dictated by foreign keys rather than preference:
    /// <c>pole.segment_id</c> is NOT NULL, <c>pole.feeder_id</c> must exist if filled, and
    /// <c>fixture.pole_id</c> is NOT NULL.
    /// </summary>
    public static IReadOnlyList<ImportKind> LoadOrder { get; } =
        [ImportKind.Segments, ImportKind.Feeders, ImportKind.Poles, ImportKind.Fixtures];
}

/// <summary>
/// One thing wrong with one line of the file.
/// </summary>
/// <param name="Row">1-based line number IN THE FILE, so the person can open the file and look.</param>
/// <param name="Column">The offending column, or <c>null</c> when the problem is the row as a whole.</param>
public sealed record ImportRowError(int Row, string? Column, string Message);

/// <summary>
/// The result of an import — NOT the Contract error shape, and deliberately so.
/// </summary>
/// <remarks>
/// ⚠️ This shape is not in Contract v1.1; registered as drift. Two decisions are worth stating.
/// <para>
/// <b>It answers 200, not 4xx.</b> The valid rows were really written, so the request succeeded; a
/// body under <c>{ error: … }</c> would claim otherwise. There is precedent:
/// <c>POST /lux-readings</c> answers 200 on a repeated <c>client_op_id</c>, which Contract section
/// 5.8 calls normal behaviour rather than an error. 207 was considered and rejected — it appears
/// nowhere in the Contract and nowhere in this codebase.
/// </para>
/// <para>
/// <b><see cref="Rows"/> is an ARRAY, never a dictionary keyed by line number.</b> A dictionary
/// makes no ordering promise, and its keys would be numbers-as-strings where <c>"10"</c> sorts
/// before <c>"9"</c> — so the list a person reads would jump about for no visible reason.
/// </para>
/// </remarks>
/// <param name="Inserted">Rows that created a new asset.</param>
/// <param name="Updated">Rows that matched an existing <c>(commune_id, external_ref)</c> and overwrote it.</param>
/// <param name="Failed">Rows rejected in validation. Nothing was written for them.</param>
/// <param name="TotalErrors">Every error found, which can exceed <see cref="Rows"/> — one row may break several rules.</param>
/// <param name="Truncated">True when <see cref="Rows"/> was cut at <see cref="ImportResult.MaxReportedErrors"/>.</param>
public sealed record ImportResult(
    int Inserted,
    int Updated,
    int Failed,
    int TotalErrors,
    bool Truncated,
    IReadOnlyList<ImportRowError> Rows)
{
    /// <summary>
    /// A person fixing a spreadsheet works through the first handful of mistakes and re-uploads;
    /// nobody reads the 400th. The cap keeps a wrong-delimiter file — which fails every single row —
    /// from returning a body larger than the upload was.
    /// </summary>
    public const int MaxReportedErrors = 100;

    public static ImportResult From(int inserted, int updated, int failedRows, IReadOnlyList<ImportRowError> errors)
        => new(
            inserted,
            updated,
            failedRows,
            errors.Count,
            errors.Count > MaxReportedErrors,
            [.. errors.Take(MaxReportedErrors)]);
}
