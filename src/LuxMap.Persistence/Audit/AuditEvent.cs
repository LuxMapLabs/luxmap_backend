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
public enum AuditActorKind { User, Cv, Iot }
public enum AuditEntityType { WorkOrder }
public enum AuditAction
{
    Created, Assigned, Reassigned, Unassigned, Started, Completed, Verified,
    Returned, Cancelled, DetailsChanged,
}

/// <summary>Marks business entities whose writes require one audit event per SaveChanges.</summary>
public interface IAudited;
