using System.Text.Json;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;

namespace LuxMap.Persistence.Audit;

/// <summary>Trusted service input, never bound directly from an HTTP request.</summary>
public sealed record AuditChange(
    DateTime OccurredAt,
    AuditActorKind ActorKind,
    string? ActorUserId,
    UserRole? ActorRole,
    string CommuneId,
    AuditEntityType EntityType,
    string EntityId,
    AuditAction Action,
    object? BeforeState,
    object? AfterState,
    string? Note = null);

public interface IAuditTrail
{
    /// <summary>Stages one event. The caller saves it together with its business changes.</summary>
    void Record(AuditChange change);
}

public sealed class AuditTrail(LuxMapDbContext db, ICorrelationIdAccessor correlation) : IAuditTrail
{
    public void Record(AuditChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        db.Set<AuditEvent>().Add(new AuditEvent
        {
            OccurredAt = change.OccurredAt,
            ActorKind = change.ActorKind,
            ActorUserId = change.ActorUserId,
            ActorRole = change.ActorRole,
            CommuneId = change.CommuneId,
            EntityType = change.EntityType,
            EntityId = change.EntityId,
            Action = change.Action,
            BeforeState = Serialize(change.BeforeState),
            AfterState = Serialize(change.AfterState),
            Note = change.Note,
            CorrelationId = correlation.CorrelationId,
        });
    }

    private static string? Serialize(object? state)
        => state is null ? null : JsonSerializer.Serialize(state, LuxMapJsonOptions.Default);
}
