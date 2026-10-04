using LuxMap.Shared.Authorization;

namespace LuxMap.Modules.WorkOrders.Entities;

/// <summary>
/// Photo label. <c>before</c>/<c>after</c> belong to a REPAIR (the lamp as found, then as fixed);
/// <c>observation</c> belongs to an INSPECTION, which fixes nothing and so has no before or after (drift EV-1).
/// </summary>
public enum EvidenceKind { Before, After, Observation }

/// <summary>
/// A photo an engineer took on a work order (BE-24) or attached to a fault they reported (BE-41). A separate stream from survey frames (BE-11): it is
/// for people to look at, never input to CV scoring — phone cameras set their own exposure.
/// </summary>
/// <remarks>
/// The object is written first and the row second (BE-11 rule 3). Rows are never edited or deleted by the
/// API: a photo is a record of what was seen. Commune-scoped as a query root (the image endpoints read it by
/// id), but visibility ALSO needs the parent work order, which carries the assignee scope — every read
/// goes through the order first.
/// </remarks>
public class RepairEvidence : ICommuneScoped
{
    public string EvidenceId { get; set; } = null!;
    /// <summary>The work order the photo belongs to — or null when it belongs to a reported fault (BE-41).</summary>
    public string? WorkOrderId { get; set; }

    /// <summary>The reported fault the photo belongs to (BE-41, drift EV-2). Exactly one of the two parents is set.</summary>
    public string? FaultId { get; set; }
    public required string CommuneId { get; set; }
    public EvidenceKind Kind { get; set; }

    /// <summary>When the phone took the photo, as the client says (UTC).</summary>
    public DateTime CapturedAt { get; set; }

    public double Lat { get; set; }
    public double Lng { get; set; }
    public required string ObjectKey { get; set; }
    public required string ThumbnailKey { get; set; }

    /// <summary>Bytes actually written, not a client-declared length (BE-35 capacity report).</summary>
    public long ByteCount { get; set; }

    public long ThumbnailBytes { get; set; }
    public required string UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; }

    /// <summary>Optional retry key from the phone: the same key from the same user returns the same photo.</summary>
    public Guid? ClientOpId { get; set; }
}
