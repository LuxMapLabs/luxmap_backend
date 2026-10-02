using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LuxMap.Modules.WorkOrders.Entities;

namespace LuxMap.Modules.WorkOrders;

public sealed class CreateWorkOrderRequest
{
    [Required] public TaskKind? TaskKind { get; init; }
    [Required, StringLength(200, MinimumLength = 1)] public string Title { get; init; } = null!;
    public string[]? FaultIds { get; init; }
    public string? SegmentId { get; init; }
    /// <summary>SELF-SIGNED BE-15: ordered survey routes; commune_id is the explicit anchor.</summary>
    public string[]? SegmentIds { get; init; }
    public string? AssignedTo { get; init; }
    public DateOnly? DueDate { get; init; }
    public DateOnly? ScheduledDate { get; init; }
    public string? Note { get; init; }
    public string? MaterialsNote { get; init; }
    public JsonElement WorkOrderId { get; init; }
    public JsonElement CommuneId { get; init; }
    public JsonElement WoStatus { get; init; }
    public JsonElement ClusterId { get; init; }
    public JsonElement PriorityScore { get; init; }
}

public sealed class PatchWorkOrderRequest
{
    public JsonElement Title { get; init; }
    public JsonElement DueDate { get; init; }
    public JsonElement ScheduledDate { get; init; }
    public JsonElement MaterialsNote { get; init; }
    public JsonElement CommuneId { get; init; }
    public JsonElement WoStatus { get; init; }
    public JsonElement AssignedTo { get; init; }
    public JsonElement FaultIds { get; init; }
    public JsonElement TaskKind { get; init; }
    public JsonElement SegmentId { get; init; }
    public JsonElement SegmentIds { get; init; }
}

public sealed class AssignWorkOrderRequest
{
    public JsonElement AssignedTo { get; init; }
}

public sealed class CompleteWorkOrderRequest
{
    [Required] public string ReportNote { get; init; } = null!;
    public string? MaterialsUsed { get; init; }
    public JsonElement FaultOutcomes { get; init; }
}

/// <summary>Body of <c>POST /work-orders/{id}/follow-up</c>; the targets come from the parent (drift FR-2).</summary>
public sealed class FollowUpWorkOrderRequest
{
    [Required] public TaskKind? TaskKind { get; init; }
    /// <summary>Defaults to the parent's title.</summary>
    [StringLength(200, MinimumLength = 1)] public string? Title { get; init; }
    /// <summary>A subset of the parent's fault_present faults, to split the next step; defaults to all of them.</summary>
    public string[]? FaultIds { get; init; }
    public string? AssignedTo { get; init; }
    public DateOnly? DueDate { get; init; }
    public DateOnly? ScheduledDate { get; init; }
    public string? Note { get; init; }
    public string? MaterialsNote { get; init; }
}

public sealed class ReviewWorkOrderRequest
{
    public string? Note { get; init; }
}

public sealed record FaultOutcomeRequest(string FaultId, InspectionOutcome? Outcome);
