using System.Net;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;

namespace LuxMap.Modules.Faults;

/// <summary>The filters of <c>GET /faults</c>, already parsed and scope-checked.</summary>
public sealed record FaultListQuery
{
    public BoundingBox? Bbox { get; init; }
    public IReadOnlyList<FaultStatus>? Statuses { get; init; }
    public IReadOnlyList<Severity>? Severities { get; init; }
    public IReadOnlyList<FaultType>? FaultTypes { get; init; }
    public IReadOnlyList<SourceChannel>? SourceChannels { get; init; }

    /// <summary><c>null</c> = every source EXCEPT <c>calibration_rig</c> (section 1.6, BE-40 D-6).</summary>
    public IReadOnlyList<DataSource>? DataSources { get; init; }

    public string? PoleId { get; init; }
    public string? SegmentId { get; init; }
    public string? ClusterId { get; init; }

    /// <summary>Already narrowed by <c>CommuneFilter.Narrow</c>; <c>null</c> = the caller's whole scope.</summary>
    public IReadOnlyList<string>? CommuneIds { get; init; }

    public FaultSort Sort { get; init; } = FaultSort.Default;
}

public enum FaultSortKey
{
    Severity,
    PriorityScore,
    DetectedAt,
    UpdatedAt,
}

/// <summary>
/// The <c>sort</c> parameter: a key, optionally prefixed with <c>-</c> for descending (BE-40 D-7).
/// </summary>
/// <remarks>
/// The default is <c>-severity</c> (drift P-3): the Manager sets severity (P-2) while automatic
/// priority scoring is deferred (P-1). Within one severity the OLDEST fault comes first — it has
/// waited longest. <c>-priority_score</c> stays available for when CV-16 exists.
/// <para>
/// NULL <c>priority_score</c> sorts LAST in both directions — a fault CV-16 has not scored yet is
/// not the most urgent one, nor the least. Ties always break on
/// <c>created_at, length(fault_id), fault_id</c> ASCENDING, whatever the direction of the key, as
/// the mock does (<c>FAULT-0002</c> before <c>FAULT-0003</c> at 96.0).
/// </para>
/// </remarks>
public sealed record FaultSort(FaultSortKey Key, bool Descending)
{
    public static FaultSort Default { get; } = new(FaultSortKey.Severity, Descending: true);

    public static FaultSort Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Default;
        }

        var trimmed = raw.Trim();
        var descending = trimmed.StartsWith('-');
        var name = descending ? trimmed[1..] : trimmed;

        foreach (var key in Enum.GetValues<FaultSortKey>())
        {
            if (WireEnum.Name(key) == name)
            {
                return new FaultSort(key, descending);
            }
        }

        throw new LuxMapException(
            ErrorCodes.ValidationFailed,
            HttpStatusCode.BadRequest,
            $"'{trimmed}' is not a valid sort.",
            new Dictionary<string, object?>
            {
                ["sort"] = trimmed,
                ["allowed"] = Enum.GetValues<FaultSortKey>()
                    .SelectMany(key => new[] { WireEnum.Name(key), "-" + WireEnum.Name(key) })
                    .ToArray(),
            });
    }
}
