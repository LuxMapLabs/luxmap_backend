using System.Net;
using System.Text.Json;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Faults;

/// <summary>Body of <c>PATCH /faults/{id}</c>. Every field keeps "absent" apart from "null".</summary>
public sealed class PatchFaultRequest
{
    /// <summary>Required (Contract section 5.4). The current status means "no transition" (D-9).</summary>
    public JsonElement FaultStatus { get; init; }

    /// <summary><c>lamp_out</c> / <c>lamp_dim</c>; null clears the override (D-4).</summary>
    public JsonElement OverrideFaultType { get; init; }

    /// <summary>The Manager's priority (drift P-2).</summary>
    public JsonElement Severity { get; init; }

    /// <summary>Stored as <c>review_note</c>; null or blank clears it (D-6).</summary>
    public JsonElement Note { get; init; }
}

/// <summary>A Manager's review of one fault (BE-19): confirm, reject, reclassify, severity, note.</summary>
public sealed class FaultReviewService(
    LuxMapDbContext db,
    ICurrentActorAccessor actor,
    IAuditTrail audit,
    FaultTransitions transitions,
    IActiveWorkOrderLookup workOrders,
    FaultQueryService query)
{
    private string ActorId => actor.UserId
        ?? throw new LuxMapException(ErrorCodes.Unauthenticated, HttpStatusCode.Unauthorized, "Authentication required.");

    public async Task<FaultItem> ReviewAsync(string id, PatchFaultRequest request, CancellationToken ct)
    {
        // 400 before any lookup: a malformed body says nothing about which faults exist.
        if (!OptionalJson.Present(request.FaultStatus) || request.FaultStatus.ValueKind != JsonValueKind.String)
        {
            throw OptionalJson.Invalid("fault_status");
        }

        var target = WireEnum.Parse<FaultStatus>(request.FaultStatus.GetString()!, "fault_status");
        var reclassify = OptionalJson.Present(request.OverrideFaultType);
        var typeText = OptionalJson.Text(request.OverrideFaultType, "override_fault_type");
        var overrideType = typeText is null ? (FaultType?)null : WireEnum.Parse<FaultType>(typeText, "override_fault_type");
        if (overrideType is { } requested && !FaultReviewRules.Reclassifiable.Contains(requested))
        {
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "override_fault_type moves a fault between lamp_out and lamp_dim only.",
                new Dictionary<string, object?> { ["override_fault_type"] = typeText,
                    ["allowed"] = FaultReviewRules.Reclassifiable.Select(WireEnum.Name).ToArray() });
        }

        var setSeverity = OptionalJson.Present(request.Severity);
        var severity = setSeverity
            ? WireEnum.Parse<Severity>(OptionalJson.Text(request.Severity, "severity", nullable: false)!, "severity")
            : (Severity?)null;
        var setNote = OptionalJson.Present(request.Note);
        var note = OptionalJson.Text(request.Note, "note") is { } text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await FaultLocks.LockAsync(db, [id], ct);

        // Scoped lookup: a fault outside the caller's communes is the same 404 as one that does not exist.
        var fault = await db.Set<Fault>().FirstOrDefaultAsync(x => x.FaultId == id, ct)
            ?? throw Error("FAULT_NOT_FOUND", HttpStatusCode.NotFound, ("fault_id", id));

        var repair = await workOrders.ActiveRepairsAsync([id], ct);
        if (repair.TryGetValue(id, out var repairId))
        {
            throw Error("FAULT_IN_ACTIVE_REPAIR", HttpStatusCode.Conflict, ("fault_id", id), ("work_order_id", repairId));
        }

        var transition = target != fault.FaultStatus;
        if (transition && !FaultReviewRules.IsReviewTransition(fault.FaultStatus, target))
        {
            throw Error("INVALID_STATE_TRANSITION", HttpStatusCode.Conflict, ("fault_id", id),
                ("fault_status", fault.FaultStatus), ("requested", target),
                ("allowed_actions", FaultReviewRules.AllowedActions(fault, heldByRepair: false)));
        }

        var nextOverride = reclassify ? (overrideType == fault.FaultType ? null : overrideType) : fault.OverrideFaultType;
        if (reclassify && nextOverride != fault.OverrideFaultType && !FaultReviewRules.Reclassifiable.Contains(fault.EffectiveType))
        {
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "Only a lamp_out or lamp_dim fault can be reclassified.",
                new Dictionary<string, object?> { ["fault_type"] = WireEnum.Name(fault.EffectiveType) });
        }

        var nextSeverity = severity ?? fault.Severity;
        var nextNote = setNote ? note : fault.ReviewNote;
        var edited = nextOverride != fault.OverrideFaultType || nextSeverity != fault.Severity || nextNote != fault.ReviewNote;

        if (!transition && !edited)
        {
            return await query.ItemAsync(id, ct) ?? throw Error("FAULT_NOT_FOUND", HttpStatusCode.NotFound, ("fault_id", id));
        }

        // Edits without a transition are for faults still under review; a closed fault is history.
        if (!transition && !FaultStatusSets.IsOpen(fault.FaultStatus))
        {
            throw Error("INVALID_STATE_TRANSITION", HttpStatusCode.Conflict, ("fault_id", id),
                ("fault_status", fault.FaultStatus), ("allowed_actions", Array.Empty<string>()));
        }

        var before = Snapshot(fault);
        var now = UtcMicrosecondClock.UtcNow();
        fault.OverrideFaultType = nextOverride;
        fault.Severity = nextSeverity;
        fault.ReviewNote = nextNote;
        if (transition)
        {
            transitions.Apply(fault, target, now, ActorId);
        }
        else
        {
            fault.UpdatedAt = now;
        }

        var action = !transition ? AuditAction.DetailsChanged
            : target == FaultStatus.Confirmed ? AuditAction.Confirmed : AuditAction.Rejected;
        audit.Record(new(now, AuditActorKind.User, ActorId, actor.Role, fault.CommuneId,
            AuditEntityType.Fault, fault.FaultId, action, before, Snapshot(fault), nextNote));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The transaction is aborted: roll back before anything else touches the connection.
            await transaction.RollbackAsync(ct);
            throw Error("CONCURRENT_MODIFICATION", HttpStatusCode.Conflict, ("fault_id", id));
        }

        await transaction.CommitAsync(ct);
        return await query.ItemAsync(id, ct) ?? throw Error("FAULT_NOT_FOUND", HttpStatusCode.NotFound, ("fault_id", id));
    }

    private static LuxMapException Error(string code, HttpStatusCode status, params (string Key, object? Value)[] details)
        => new(code, status, code.Replace('_', ' '), details.ToDictionary(x => x.Key, x => x.Value));

    // Only an explicit snapshot crosses the audit boundary, never the EF entity.
    private static FaultSnapshot Snapshot(Fault fault)
        => new(fault.FaultId, fault.FaultStatus, fault.FaultType, fault.OverrideFaultType, fault.Severity,
            fault.ReviewNote, fault.ConfirmedBy, fault.ConfirmedAt, fault.UpdatedAt);

    private sealed record FaultSnapshot(string FaultId, FaultStatus FaultStatus, FaultType FaultType,
        FaultType? OverrideFaultType, Severity Severity, string? ReviewNote, string? ConfirmedBy,
        DateTime? ConfirmedAt, DateTime UpdatedAt);
}
