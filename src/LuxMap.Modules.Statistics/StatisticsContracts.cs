using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Statistics;

/// <summary>A dimension a statistics row can be grouped by, in its wire spelling (<c>group_by=commune,segment</c>).</summary>
public enum StatisticsDimension
{
    Commune,
    Segment,
}

/// <summary>
/// <c>GET /statistics/fixture-status</c>. <c>data_source</c> is ALWAYS a grouping dimension, so no figure ever adds
/// up poles from two sources (drift ST-1).
/// </summary>
public sealed record FixtureStatusStatistics
{
    public required DateTime AsOf { get; init; }

    /// <summary>The dimensions the rows are grouped by, <c>data_source</c> first.</summary>
    public required IReadOnlyList<string> GroupBy { get; init; }

    public required IReadOnlyList<FixtureStatusRow> Rows { get; init; }
}

/// <summary>One group. <c>normal + dim + out + unknown = pole_count</c>.</summary>
public sealed record FixtureStatusRow
{
    public required DataSource DataSource { get; init; }

    /// <summary><c>null</c> unless grouped by <c>commune</c>.</summary>
    public required string? CommuneId { get; init; }

    /// <summary><c>null</c> unless grouped by <c>segment</c>.</summary>
    public required string? SegmentId { get; init; }

    public required int PoleCount { get; init; }
    public required int Normal { get; init; }
    public required int Dim { get; init; }
    public required int Out { get; init; }

    /// <summary>Includes the poles no sweep has ever covered — the map shows those as <c>unknown</c> too.</summary>
    public required int Unknown { get; init; }

    /// <summary>The part of <see cref="Unknown"/> never covered by a sweep. A subset, not a fifth bucket (drift ST-2).</summary>
    public required int NeverSurveyed { get; init; }
}

/// <summary><c>GET /statistics/repair-timeliness</c>: repair work orders finished in the nights <c>from</c>..<c>to</c>.</summary>
public sealed record RepairTimelinessStatistics
{
    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }
    public required DateTime AsOf { get; init; }
    public required IReadOnlyList<string> GroupBy { get; init; }
    public required IReadOnlyList<RepairTimelinessRow> Rows { get; init; }
}

/// <summary>One group. <c>on_time + late + no_due_date = completed</c>.</summary>
public sealed record RepairTimelinessRow
{
    /// <summary><c>null</c> unless grouped by <c>commune</c>.</summary>
    public required string? CommuneId { get; init; }

    public required int Completed { get; init; }

    /// <summary>Finished in a night on or before its <c>due_date</c>.</summary>
    public required int OnTime { get; init; }

    public required int Late { get; init; }

    /// <summary>Finished without a due date; left out of <see cref="OnTimeRate"/>.</summary>
    public required int NoDueDate { get; init; }

    /// <summary><c>on_time / (on_time + late)</c>, 4 decimals; <c>null</c> when nothing finished had a due date.</summary>
    public required double? OnTimeRate { get; init; }

    /// <summary>Repair work orders not finished whose due date is already behind tonight — as of now, whatever the window.</summary>
    public required int OpenOverdue { get; init; }
}

/// <summary>
/// <c>GET /statistics/warranty</c> (BE-31): poles with their lamp in service and its warranty, soonest expiry first.
/// The pagination keys of Contract section 0, plus the day and window the statuses were worked out against.
/// </summary>
public sealed record WarrantyList
{
    /// <summary>Today in the communes' zone — what <c>days_remaining</c> counts from.</summary>
    public required DateOnly Today { get; init; }

    /// <summary>A warranty ending within this many days of today is <c>expiring</c> (both ends included).</summary>
    public required int ExpiringWithinDays { get; init; }

    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int Total { get; init; }
    public required IReadOnlyList<WarrantyItem> Items { get; init; }
}

/// <summary>One pole. Flat on purpose: the same record is a CSV row.</summary>
public sealed record WarrantyItem
{
    public required string PoleId { get; init; }
    public required string SegmentId { get; init; }
    public required string CommuneId { get; init; }

    /// <summary>The POLE's provenance — the dimension every statistic splits by (drift ST-1).</summary>
    public required DataSource DataSource { get; init; }

    /// <summary><c>null</c> when the pole carries no lamp in service.</summary>
    public required string? FixtureId { get; init; }

    public required FixtureType? FixtureType { get; init; }
    public required int? LampWatt { get; init; }
    public required DateOnly? InstallDate { get; init; }

    /// <summary><c>null</c> = not on record (or no lamp) — <see cref="WarrantyStatus"/> is then <c>unknown</c>.</summary>
    public required DateOnly? WarrantyExpiry { get; init; }

    public required WarrantyStatus WarrantyStatus { get; init; }

    /// <summary>Days from today to the expiry: 0 = ends today, negative = expired; <c>null</c> when unknown.</summary>
    public required int? DaysRemaining { get; init; }
}
