using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Citizen.Entities;

/// <summary>What a citizen says is wrong — the four choices of the public form (D-17).</summary>
public enum CitizenReportKind
{
    LampOut,
    LampDim,
    PoleDamaged,
    Other,
}

/// <summary>Where a report stands in the Manager's queue. P2b only ever writes <c>pending</c>; the decisions are P2c.</summary>
public enum CitizenReportStatus
{
    Pending,
    Accepted,
    Merged,
    Rejected,
}

/// <summary>Why a submission's photos were not kept — the text always is (CITIZEN-QR review P1 #5, D-8).</summary>
public enum PhotosDroppedReason
{
    /// <summary>This code already carried its photo allowance for the night.</summary>
    OverQuota,

    /// <summary>Citizen photos already used the day's storage budget.</summary>
    OverBudget,
}

/// <summary>
/// One item of the Manager's queue (CITIZEN-QR P2b, D-6): everything citizens sent about ONE pole in ONE night, while still
/// pending. Each send is its own <see cref="CitizenSubmission"/> — grouping never merges their content away.
/// </summary>
/// <remarks>
/// <para>
/// <c>ux_citizen_report_pole_id_night_of_pending</c> keeps one pending item per pole and night; a send for a pole whose item was
/// already decided starts a new item. Filing is serialised on the pole row, like issuing a code.
/// </para>
/// <para>
/// <see cref="DataSource"/> is the POLE's (D-5 / ST-1): reports about a testbed pole never count as field data.
/// </para>
/// </remarks>
public sealed class CitizenReport : ICommuneScoped, IAudited
{
    public string ReportId { get; set; } = null!;

    public required string PoleId { get; set; }

    public required string CommuneId { get; set; }

    public DataSource DataSource { get; set; }

    /// <summary>The night the first send arrived in (<c>WorkOrderAgendaOptions.NightOf</c>, D-7): the grouping window, nothing more.</summary>
    public DateOnly NightOf { get; set; }

    public CitizenReportStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>When the newest send was added; the queue reads newest first.</summary>
    public DateTime LastSubmittedAt { get; set; }

    // Decision columns, written by P2c. Present now so the CHECK that ties them to the status exists from the first row.
    public string? FaultId { get; set; }

    public string? DecidedBy { get; set; }

    public DateTime? DecidedAt { get; set; }

    public string? DecisionNote { get; set; }
}

/// <summary>One send through the public form (CITIZEN-QR P2b, D-6). Never edited, never merged into another.</summary>
/// <remarks>
/// The idempotency key is bound to the code AND the content (<see cref="RequestHash"/>): the same key with the same code and
/// content replays; anything else under that key is 409. <see cref="QrId"/> records which code was used — never the code itself.
/// </remarks>
public sealed class CitizenSubmission : ICommuneScoped, IAudited
{
    public string SubmissionId { get; set; } = null!;

    public required string ReportId { get; set; }

    public required string QrId { get; set; }

    public required string PoleId { get; set; }

    public required string CommuneId { get; set; }

    public Guid ClientOpId { get; set; }

    public required string RequestHash { get; set; }

    public CitizenReportKind Kind { get; set; }

    /// <summary>Plain text, at most 1000 characters. Shown to Managers as text only — never as markup.</summary>
    public string? Description { get; set; }

    /// <summary>The phone's position, if the citizen shared it. Both or neither.</summary>
    public double? ReporterLat { get; set; }

    public double? ReporterLng { get; set; }

    /// <summary>The shared position is more than <c>Citizen:FarFromPoleMeters</c> from the pole — a flag, never a refusal (D-8).</summary>
    public bool FarFromPole { get; set; }

    /// <summary>Set when photos came with the send but were not kept.</summary>
    public PhotosDroppedReason? PhotosDropped { get; set; }

    public DateTime ReceivedAt { get; set; }
}

/// <summary>A photo kept from a submission — metadata already stripped (D-10). Reached only through its submission.</summary>
public sealed class CitizenPhoto
{
    public string PhotoId { get; set; } = null!;

    public required string SubmissionId { get; set; }

    /// <summary>1..3, in the order the form sent them.</summary>
    public int Position { get; set; }

    public required string ObjectKey { get; set; }

    public required string ThumbnailKey { get; set; }

    public long ByteCount { get; set; }

    public long ThumbnailBytes { get; set; }

    public DateTime ReceivedAt { get; set; }
}
