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
}
public enum AuditEntityType { WorkOrder, Fault, SurveySweep, LightingRequest, LightingCommand }
public enum AuditAction
{
    Created, Assigned, Reassigned, Unassigned, Started, Completed, Verified,
    Returned, Cancelled, DetailsChanged,

    /// <summary>A Manager's review decision on a fault (BE-19): detected → confirmed / rejected.</summary>
    Confirmed, Rejected, Submitted,

    /// <summary>LIGHT-CTRL 3.5: a Manager's press, then each command's life — and a device report on an already closed command.</summary>
    Requested, Delivered, Applied, Failed, Expired, Superseded, Reported,
}

/// <summary>Marks business entities whose writes require one audit event per SaveChanges.</summary>
public interface IAudited;
