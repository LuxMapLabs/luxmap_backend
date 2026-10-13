using LuxMap.Modules.Citizen.Entities;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Citizen;

/// <summary>One item of the citizen report queue (CITIZEN-QR P2c, drift PC-*). Never the reporter's position (D-10).</summary>
public record CitizenReportItem
{
    public required string ReportId { get; init; }
    public required string PoleId { get; init; }
    public required string CommuneId { get; init; }

    /// <summary>The pole's: a report about a testbed pole is never field data.</summary>
    public required DataSource DataSource { get; init; }

    /// <summary>The night the first send arrived in (12:00 Asia/Ho_Chi_Minh boundary) — the grouping window.</summary>
    public required DateOnly NightOf { get; init; }

    public required CitizenReportStatus Status { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime LastSubmittedAt { get; init; }

    public required int SubmissionCount { get; init; }

    /// <summary>Photos kept across every send. Counted for every reader, even one who cannot open them.</summary>
    public required int PhotoCount { get; init; }

    /// <summary>The distinct kinds citizens chose, in the order of the form.</summary>
    public required IReadOnlyList<CitizenReportKind> Kinds { get; init; }

    /// <summary>At least one send shared a position more than <c>Citizen:FarFromPoleMeters</c> from the pole.</summary>
    public required bool FarFromPole { get; init; }

    /// <summary>The fault it became (<c>accepted</c>) or joined (<c>merged</c>); null otherwise.</summary>
    public required string? FaultId { get; init; }

    public required string? DecidedBy { get; init; }
    public required DateTime? DecidedAt { get; init; }

    /// <summary>The Manager's words: the reason of a rejection, or an optional note on accept / merge.</summary>
    public required string? DecisionNote { get; init; }
}

/// <summary><c>GET /citizen-reports/{id}</c>: the item, the pole it is about, and every send.</summary>
public sealed record CitizenReportDetail : CitizenReportItem
{
    public required CitizenReportPole Pole { get; init; }

    /// <summary>Open faults on the pole (detected, confirmed, in_progress) — the candidates for <c>merge</c>.</summary>
    public required IReadOnlyList<CitizenReportOpenFault> OpenFaults { get; init; }

    /// <summary>Oldest first.</summary>
    public required IReadOnlyList<CitizenSubmissionItem> Submissions { get; init; }

    /// <summary>
    /// What THIS caller can do now: <c>accept</c>, <c>merge</c>, <c>reject</c> — so the web app disables a button before it is pressed.
    /// Empty for a decided report and for a reader without <c>ReviewCitizenReports</c>.
    /// </summary>
    public required IReadOnlyList<string> AllowedActions { get; init; }

    /// <summary>
    /// The fault types <c>accept</c> takes for this report (D-17): <c>lamp_out</c> when a send said the lamp is out, <c>lamp_dim</c> when
    /// one said it is dim. Empty when no send said either.
    /// </summary>
    public required IReadOnlyList<FaultType> AcceptableFaultTypes { get; init; }
}

/// <summary>The pole a report is about, as it stands now.</summary>
public sealed record CitizenReportPole
{
    public required string PoleId { get; init; }
    public required string SegmentId { get; init; }
    public required ReportPoint Location { get; init; }

    /// <summary>From the last published survey; <c>unknown</c> when none covered the pole.</summary>
    public required FixtureStatus FixtureStatus { get; init; }

    public required DateTime? LastSeenAt { get; init; }
}

public sealed record ReportPoint(double Lat, double Lng);

public sealed record CitizenReportOpenFault
{
    public required string FaultId { get; init; }

    /// <summary>The effective type: the Manager's override when there is one.</summary>
    public required FaultType FaultType { get; init; }

    public required FaultStatus FaultStatus { get; init; }
    public required Severity Severity { get; init; }
    public required SourceChannel SourceChannel { get; init; }
    public required DateTime DetectedAt { get; init; }
}

/// <summary>One send. The text is a citizen's: show it as plain text, never as markup.</summary>
public sealed record CitizenSubmissionItem
{
    public required string SubmissionId { get; init; }
    public required CitizenReportKind Kind { get; init; }
    public required string? Description { get; init; }
    public required bool FarFromPole { get; init; }

    /// <summary>Photos came with the send but were not kept: <c>over_quota</c> or <c>over_budget</c>.</summary>
    public required PhotosDroppedReason? PhotosDropped { get; init; }

    public required DateTime ReceivedAt { get; init; }
    public required int PhotoCount { get; init; }

    /// <summary>Only for a caller who may open them (<c>ReviewCitizenReports</c>); empty otherwise — never a link that answers 403.</summary>
    public required IReadOnlyList<CitizenPhotoItem> Photos { get; init; }
}

public sealed record CitizenPhotoItem
{
    public required string PhotoId { get; init; }
    public required int Position { get; init; }
    public required string ThumbnailUrl { get; init; }
    public required string OriginalUrl { get; init; }

    /// <summary>The model's hint (D-9); null until it ran, or when the model is unavailable.</summary>
    public required CitizenPhotoAiHint? AiHint { get; init; }
}

/// <summary>A hint, never a decision (CQ-5). <c>inconclusive</c> says nothing either way — daytime photos land there.</summary>
public sealed record CitizenPhotoAiHint
{
    public required CitizenPhotoHint Label { get; init; }
    public required double? Confidence { get; init; }
    public required string ArtifactVersion { get; init; }
    public required DateTime CheckedAt { get; init; }
}

/// <summary><c>POST /citizen-reports/{id}/accept</c>.</summary>
public sealed record AcceptCitizenReportRequest
{
    /// <summary>
    /// Required: <c>submission_count</c> of the report as the Manager saw it. Citizens may send more while it is open; a different count
    /// is 409 <c>CITIZEN_REPORT_CHANGED</c>, so a decision is never made on content nobody read.
    /// </summary>
    public int? SubmissionCount { get; init; }

    /// <summary><c>lamp_out</c> or <c>lamp_dim</c>.</summary>
    public FaultType? FaultType { get; init; }

    /// <summary>Defaults to <c>medium</c>.</summary>
    public Severity? Severity { get; init; }

    /// <summary>Optional, up to 1000 characters; kept on the report and as the fault's <c>review_note</c>.</summary>
    public string? Note { get; init; }
}

/// <summary><c>POST /citizen-reports/{id}/merge</c>.</summary>
public sealed record MergeCitizenReportRequest
{
    /// <summary>
    /// Required: <c>submission_count</c> of the report as the Manager saw it. Citizens may send more while it is open; a different count
    /// is 409 <c>CITIZEN_REPORT_CHANGED</c>, so a decision is never made on content nobody read.
    /// </summary>
    public int? SubmissionCount { get; init; }

    public string? FaultId { get; init; }

    /// <summary>Optional, up to 1000 characters.</summary>
    public string? Note { get; init; }
}

/// <summary><c>POST /citizen-reports/{id}/reject</c>.</summary>
public sealed record RejectCitizenReportRequest
{
    /// <summary>
    /// Required: <c>submission_count</c> of the report as the Manager saw it. Citizens may send more while it is open; a different count
    /// is 409 <c>CITIZEN_REPORT_CHANGED</c>, so a decision is never made on content nobody read.
    /// </summary>
    public int? SubmissionCount { get; init; }

    /// <summary>Required, 1..1000 characters: why it is not a fault.</summary>
    public string? Reason { get; init; }
}
