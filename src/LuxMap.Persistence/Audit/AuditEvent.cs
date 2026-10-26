using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Persistence.Audit;

/// <summary>One business operation; immutable after insertion. No public audit endpoint yet.</summary>
public sealed class AuditEvent : ICommuneScoped
{
    public long AuditId { get; set; }
    public DateTime OccurredAt { get; set; }
    public AuditActorKind ActorKind { get; set; }
    public string? ActorUserId { get; set; }
    public UserRole? ActorRole { get; set; }
    public required string CommuneId { get; set; }
    public AuditEntityType EntityType { get; set; }
    public required string EntityId { get; set; }
    public AuditAction Action { get; set; }
    public string? BeforeState { get; set; }
    public string? AfterState { get; set; }
    public string? Note { get; set; }
    public required string CorrelationId { get; set; }
}

// Internal storage enums, deliberately outside Shared.Contracts.Enums.
public enum AuditActorKind
{
    User, Cv, Iot,

    /// <summary>The server itself, acting on time alone — a lighting command that expired (LIGHT-CTRL 3.5).</summary>
    System,

    /// <summary>
    /// Someone without an account, through a pole's QR code (CITIZEN-QR D-15). Never a user, never a role — a citizen has neither
    /// (D-R1); what identifies the send is the submission row the event names.
    /// </summary>
    Public,
}
public enum AuditEntityType { WorkOrder, Fault, SurveySweep, LightingRequest, LightingCommand, QrCode, CitizenReport, Pole }
public enum AuditAction
{
    CancellationRequested, CancellationRejected,
    Created, Assigned, Reassigned, Unassigned, Started, Completed, Verified,
    Returned, Cancelled, DetailsChanged,

    /// <summary>A Manager's review decision on a fault (BE-19): detected → confirmed / rejected.</summary>
    Confirmed, Rejected, Submitted,

    /// <summary>LIGHT-CTRL 3.5: a Manager's press, then each command's life — and a device report on an already closed command.</summary>
    Requested, Delivered, Applied, Failed, Expired, Superseded, Reported,

    /// <summary>CITIZEN-QR: a Manager withdrew a QR code — replaced by a new one, or taken out of use.</summary>
    Revoked,

    /// <summary>CITIZEN-QR P2c: a Manager turned a citizen report into a new fault, or attached it to an open one.</summary>
    Accepted, Merged,

    /// <summary>CITIZEN-QR P2c: the ON / OFF model gave its hint on a citizen report's photos (actor <c>cv</c>) — a suggestion, not a decision.</summary>
    Analyzed,

    /// <summary>POLE-LOCATION: a pole was moved to a corrected position — the old and new coordinates are the before / after.</summary>
    Relocated,
}

/// <summary>Marks business entities whose writes require one audit event per SaveChanges.</summary>
public interface IAudited;
