using LuxMap.Shared.Authorization;

namespace LuxMap.Modules.WorkOrders.Entities;

/// <summary>What the photo copy job decided for one evidence photo (POLE-GPS-PHOTO PGP-4, PGP-6).</summary>
public enum PhotoCopyOutcome
{
    Copied,
    /// <summary>The pole's gallery already showed its maximum number of photos.</summary>
    SkippedLimit,
    /// <summary>Above the asset photo pixel cap; evidence has none.</summary>
    SkippedPixels,
    /// <summary>The stored original is gone or cannot be read as a JPEG.</summary>
    MissingObject,
}

/// <summary>
/// The ledger of the photo copy job (POLE-GPS-PHOTO PGP-4): one row per evidence photo of a verified <c>pole_gps_update</c> order once
/// the job has dealt with it, whether or not it became a pole asset photo. The primary key IS the rule — each photo is handled once,
/// and a skipped photo is not met again on every run. Lives in WorkOrders so that Assets never points back at work orders; not audited
/// itself (the copied asset photo carries the audit event).
/// </summary>
public sealed class WorkOrderPhotoCopy : ICommuneScoped
{
    public required string EvidenceId { get; set; }

    public required string WorkOrderId { get; set; }

    public required string PoleId { get; set; }

    public required string CommuneId { get; set; }

    public PhotoCopyOutcome Outcome { get; set; }

    /// <summary>The pole asset photo made from it; set exactly when <see cref="Outcome"/> is <see cref="PhotoCopyOutcome.Copied"/>.</summary>
    public string? PhotoId { get; set; }

    public DateTime DecidedAt { get; set; }
}
