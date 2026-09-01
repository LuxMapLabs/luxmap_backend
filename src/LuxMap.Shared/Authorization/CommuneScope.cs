namespace LuxMap.Shared.Authorization;

/// <summary>
/// The commune scope of the current request, derived from the signed JWT. NEVER taken from client
/// input.
/// </summary>
/// <param name="IsSystemWide">Contract section 7: administrators carry <c>["*"]</c> and see everything.</param>
/// <param name="CommuneIds">Empty when unauthenticated or when the claim is missing — meaning NOTHING is visible.</param>
public sealed record CommuneScope(bool IsSystemWide, IReadOnlyList<string> CommuneIds)
{
    /// <summary>
    /// The safe default: no scope at all. Used when unauthenticated, when the <c>commune_ids</c>
    /// claim is absent, and when it is present but empty — none of the three may ever be read as
    /// "no restriction".
    /// </summary>
    public static readonly CommuneScope Empty = new(false, []);

    public static CommuneScope SystemWide { get; } = new(true, []);

    public static CommuneScope ForCommunes(IEnumerable<string> communeIds)
        => new(false, [.. communeIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)]);

    /// <summary>Administrators see everything; everyone else sees exactly the communes in the claim.</summary>
    public bool Allows(string communeId)
        => IsSystemWide || CommuneIds.Contains(communeId, StringComparer.Ordinal);
}

/// <summary>
/// Reads the commune scope of the current request. Business and persistence code go through this
/// instead of touching <c>HttpContext</c> — the same pattern as <c>ICorrelationIdAccessor</c> in BE-04.
/// </summary>
public interface ICommuneScopeAccessor
{
    CommuneScope Scope { get; }
}
