using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.WorkOrders.Entities;

public enum TaskKind { Inspection, Repair, Survey }
public enum InspectionOutcome { FaultPresent, FaultAbsent, Inconclusive }

public class WorkOrder : ICommuneScoped, IAssigneeScoped, IAudited
{
    public string WorkOrderId { get; set; } = null!;
    public required string CommuneId { get; set; }
    public TaskKind TaskKind { get; set; }
    public required string Title { get; set; }
    public WorkOrderStatus WoStatus { get; set; }
    public string? SegmentId { get; set; }
    public string? ClusterId { get; set; }

    /// <summary>The work order this one was created from by follow-up; NULL for a root (drift FR-2).</summary>
    public string? ParentWorkOrderId { get; set; }

    /// <summary>
    /// The root of the chain this work order belongs to; NULL when it IS the root (drift FR-2).
    /// </summary>
    /// <remarks>
    /// The API's <c>case_id</c> is <c>root_work_order_id ?? work_order_id</c>. Stored NULL for a root
    /// rather than pointing at itself, so every existing writer — the mock seeder, test fixtures —
    /// keeps creating valid roots without knowing this column exists.
    /// </remarks>
    public string? RootWorkOrderId { get; set; }
    public string? AssignedTo { get; set; }
    public DateTime? AssignedAt { get; set; }
    public required string CreatedBy { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateOnly? ScheduledDate { get; set; }
    public string? Note { get; set; }
    public string? ReviewNote { get; set; }
    public string? ReportNote { get; set; }

    /// <summary>What the Manager plans to bring — free text, set on create and PATCH (drift FR-3).</summary>
    public string? MaterialsNote { get; set; }

    /// <summary>What the assigned engineer actually used — free text, set on complete (drift FR-3).</summary>
    /// <remarks>A separate field from <see cref="MaterialsNote"/> so the report never overwrites the plan.</remarks>
    public string? MaterialsUsed { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public uint Version { get; set; }
}

public class WorkOrderFault : ICommuneScoped, IAudited
{
    public required string WorkOrderId { get; set; }
    public required string FaultId { get; set; }
    public required string CommuneId { get; set; }
    public DateTime LinkedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public InspectionOutcome? InspectionOutcome { get; set; }
}

/// <summary>Ordered survey targets; access through the scoped parent work order.</summary>
public class WorkOrderSegment : ICommuneScoped, IAudited
{
    public required string WorkOrderId { get; set; }
    public int Position { get; set; }
    public required string SegmentId { get; set; }
    public required string CommuneId { get; set; }
}
