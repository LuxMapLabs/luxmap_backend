using LuxMap.Modules.WorkOrders.Entities;
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

    /// <summary><c>null</c> for the unassigned group, or when not grouped by <c>segment</c>.</summary>
    public required string? SegmentId { get; init; }

    public required int PoleCount { get; init; }
    public required int Normal { get; init; }
    public required int Dim { get; init; }
    public required int Out { get; init; }

    /// <summary>Includes the poles no sweep has ever covered — the map shows those as <c>unknown</c> too.</summary>
    public required int Unknown { get; init; }

    /// <summary>The part of <see cref="Unknown"/> never covered by a sweep. A subset, not a fifth bucket (drift ST-2).</summary>
    public required int NeverSurveyed { get; init; }

    /// <summary>Poles in this group with no assigned segment; included in pole_count and the status buckets.</summary>
    public required int UnassignedSegmentPoleCount { get; init; }
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
    public required string? SegmentId { get; init; }
    public required string CommuneId { get; init; }

    /// <summary>The POLE's provenance — the dimension every statistic splits by (drift ST-1).</summary>
    public required DataSource DataSource { get; init; }

    /// <summary><c>null</c> when the pole carries no lamp in service.</summary>
    public required string? FixtureId { get; init; }

    public required FixtureType? FixtureType { get; init; }
    public required int? LampWatt { get; init; }
    public required int? LampCount { get; init; }
    public required DateOnly? InstallDate { get; init; }

    /// <summary><c>null</c> = not on record (or no lamp) — <see cref="WarrantyStatus"/> is then <c>unknown</c>.</summary>
    public required DateOnly? WarrantyExpiry { get; init; }

    public required WarrantyStatus WarrantyStatus { get; init; }

    /// <summary>Days from today to the expiry: 0 = ends today, negative = expired; <c>null</c> when unknown.</summary>
    public required int? DaysRemaining { get; init; }
}

/// <summary>Dimensions accepted by work-order counts only; the existing statistics keep their own dimensions.</summary>
public enum WorkOrderStatisticsDimension { Commune, TaskKind }

/// <summary><c>GET /statistics/work-orders</c>: raw work-order counts by status (drift ST-12).</summary>
public sealed record WorkOrderStatusStatistics
{
    public required DateTime AsOf { get; init; }

    /// <summary>Requested dimensions in input order, or empty when ungrouped.</summary>
    public required IReadOnlyList<string> GroupBy { get; init; }

    public required IReadOnlyList<WorkOrderStatusRow> Rows { get; init; }
}

/// <summary>One group. The six status counts sum to <see cref="Total"/>.</summary>
public sealed record WorkOrderStatusRow
{
    /// <summary><c>null</c> unless grouped by commune.</summary>
    public required string? CommuneId { get; init; }

    /// <summary><c>null</c> unless grouped by task_kind.</summary>
    public required TaskKind? TaskKind { get; init; }

    public required int Open { get; init; }
    public required int Assigned { get; init; }
    public required int InProgress { get; init; }
    public required int Done { get; init; }
    public required int Verified { get; init; }
    public required int Cancelled { get; init; }
    public required int Total { get; init; }
}

/// <summary>Grouping dimensions specific to citizen feedback (ST-13).</summary>
public enum CitizenFeedbackDimension
{
    Commune,
    Segment,
}

/// <summary>Citizen queue items by status; data_source is always the first grouping dimension (ST-13).</summary>
public sealed record CitizenFeedbackStatistics
{
    public required DateTime AsOf { get; init; }
    public required IReadOnlyList<string> GroupBy { get; init; }
    public required IReadOnlyList<CitizenFeedbackRow> Rows { get; init; }
}

/// <summary>One nonempty group of CitizenReport items, never individual submissions.</summary>
public sealed record CitizenFeedbackRow
{
    public required DataSource DataSource { get; init; }

    /// <summary>Null unless grouped by commune.</summary>
    public required string? CommuneId { get; init; }

    /// <summary>Null unless grouped by segment; when grouped, null means poles without an assigned segment.</summary>
    public required string? SegmentId { get; init; }

    public required int Pending { get; init; }
    public required int Accepted { get; init; }
    public required int Merged { get; init; }
    public required int Rejected { get; init; }
    public required int Total { get; init; }

    /// <summary>(accepted + merged + rejected) / total, rounded to 4 decimals away from zero; never null.</summary>
    public required double ResolutionRate { get; init; }
}

/// <summary>What the citizens of one report said, strongest first: <c>lamp_out</c> beats <c>lamp_dim</c> beats the rest (ST-14).</summary>
public enum CitizenClaim
{
    LampOut,
    LampDim,

    /// <summary>Pole damaged or something else — nothing the ON / OFF model can confirm.</summary>
    Other,
}

/// <summary>
/// <c>GET /statistics/citizen-screening</c> (ST-14): how the ON / OFF model's photo hints screen citizen QR reports. Reads the
/// hints already stored; never runs the model. One block per <c>data_source</c>, so no figure adds two sources up (ST-1).
/// </summary>
public sealed record CitizenScreeningStatistics
{
    public required DateTime AsOf { get; init; }

    /// <summary>The <c>night_of</c> bounds asked for; <c>null</c> when that side was open.</summary>
    public required DateOnly? From { get; init; }

    public required DateOnly? To { get; init; }

    /// <summary>Only sources with at least one report, in enum order.</summary>
    public required IReadOnlyList<CitizenScreeningSource> Sources { get; init; }
}

/// <summary>Every block of one <c>data_source</c>. Counts are of reports (queue items), never of single sends.</summary>
public sealed record CitizenScreeningSource
{
    public required DataSource DataSource { get; init; }

    public required int Reports { get; init; }

    /// <summary>Sends across those reports.</summary>
    public required int Submissions { get; init; }

    /// <summary>Reports with at least one send shared from farther than <c>Citizen:FarFromPoleMeters</c> from the pole.</summary>
    public required int FarFromPole { get; init; }

    public required CitizenScreeningStatusCounts ByStatus { get; init; }

    public required CitizenScreeningVerdictCounts ByVerdict { get; init; }

    /// <summary>Only claims with reports, in enum order.</summary>
    public required IReadOnlyList<CitizenScreeningClaim> ByClaim { get; init; }

    /// <summary>Citizens said the lamp is out, the model saw it lit — suspected false report. Dim + lit is not one.</summary>
    public required int Contradicted { get; init; }

    public required CitizenScreeningAgreement Agreement { get; init; }

    /// <summary>Only nights with reports, oldest first.</summary>
    public required IReadOnlyList<CitizenScreeningNight> ByNight { get; init; }

    /// <summary>The 10 most reported poles: reports, then sends, descending.</summary>
    public required IReadOnlyList<CitizenScreeningPole> TopPoles { get; init; }

    /// <summary>The hint versions behind these figures (BE-34). More than one means two models are mixed.</summary>
    public required IReadOnlyList<CitizenScreeningVersion> ArtifactVersions { get; init; }
}

/// <summary>The four queue statuses; they add up to <c>reports</c>.</summary>
public sealed record CitizenScreeningStatusCounts
{
    public required int Pending { get; init; }
    public required int Accepted { get; init; }
    public required int Merged { get; init; }
    public required int Rejected { get; init; }
}

/// <summary>
/// The model's verdict on a report, from the hints of its kept photos. The five buckets are disjoint and add up to the reports.
/// </summary>
public sealed record CitizenScreeningVerdictCounts
{
    /// <summary>At least one photo <c>lamp_off</c>, none <c>lamp_on</c>.</summary>
    public required int CvOff { get; init; }

    /// <summary>At least one photo <c>lamp_on</c>, none <c>lamp_off</c>.</summary>
    public required int CvOn { get; init; }

    /// <summary>Photos were analyzed but all said <c>inconclusive</c>, or some said on and some off.</summary>
    public required int Inconclusive { get; init; }

    /// <summary>Photos kept, none analyzed yet — hints run when a Manager first opens the report.</summary>
    public required int NotAnalyzed { get; init; }

    /// <summary>No photo kept.</summary>
    public required int NoPhoto { get; init; }
}

public sealed record CitizenScreeningClaim
{
    public required CitizenClaim Claim { get; init; }
    public required int Reports { get; init; }
    public required CitizenScreeningVerdictCounts ByVerdict { get; init; }
}

/// <summary>
/// How often the model's verdict matched the Manager's decision, on DECIDED reports whose citizens said the lamp is out and whose
/// verdict is <c>cv_off</c> or <c>cv_on</c>. "Upheld" = accepted or merged. A match rate, not precision / recall: the Manager's
/// decision is not ground truth. Rates are 0..1, 4 decimals, <c>null</c> when their denominator is 0.
/// </summary>
public sealed record CitizenScreeningAgreement
{
    /// <summary>Reports counted here: the sum of the four cells.</summary>
    public required int Decided { get; init; }

    public required int CvOffUpheld { get; init; }
    public required int CvOffRejected { get; init; }
    public required int CvOnUpheld { get; init; }
    public required int CvOnRejected { get; init; }

    /// <summary><c>(cv_off_upheld + cv_on_rejected) / decided</c>.</summary>
    public required double? AgreementRate { get; init; }

    /// <summary>Of the reports the model saw dark, the share the Manager upheld.</summary>
    public required double? CvOffUpheldRate { get; init; }

    /// <summary>Of the reports the model saw lit, the share the Manager rejected.</summary>
    public required double? CvOnRejectedRate { get; init; }
}

public sealed record CitizenScreeningNight
{
    public required DateOnly NightOf { get; init; }
    public required int Reports { get; init; }
    public required int CvOff { get; init; }
    public required int CvOn { get; init; }
    public required int Inconclusive { get; init; }
    public required int NotAnalyzed { get; init; }
    public required int NoPhoto { get; init; }
    public required int Contradicted { get; init; }
}

public sealed record CitizenScreeningPole
{
    public required string PoleId { get; init; }

    /// <summary>The pole's segment now; <c>null</c> when unassigned.</summary>
    public required string? SegmentId { get; init; }

    public required int Reports { get; init; }
    public required int Submissions { get; init; }
    public required int Contradicted { get; init; }
    public required DateOnly LastNightOf { get; init; }
}

public sealed record CitizenScreeningVersion
{
    public required string ArtifactVersion { get; init; }

    /// <summary>Photos in these reports carrying a hint of this version.</summary>
    public required int Photos { get; init; }
}
