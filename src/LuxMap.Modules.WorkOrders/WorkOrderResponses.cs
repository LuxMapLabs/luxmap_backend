using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.WorkOrders;

public class WorkOrderItem
{
    public required string WorkOrderId { get; init; }
    public required string Title { get; init; }
    public required string CommuneId { get; init; }
    public TaskKind TaskKind { get; init; }
    public string? SegmentId { get; init; }
    public string? ClusterId { get; init; }
    public required string[] FaultIds { get; init; }
    public WorkOrderStatus WoStatus { get; init; }
    public string? AssignedTo { get; init; }
    public double? PriorityScore { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateOnly? DueDate { get; init; }
    public DateOnly? ScheduledDate { get; init; }
}

public sealed class WorkOrderDetail : WorkOrderItem
{
    public string? Note { get; init; }
    public string? ReviewNote { get; init; }
    public string? ReportNote { get; init; }
    public string? MaterialsNote { get; init; }
    public string? MaterialsUsed { get; init; }
    public required string CreatedBy { get; init; }
    public DateTime? AssignedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public DateTime? ClosedAt { get; init; }
    public bool? AssigneeEligible { get; init; }
    public required string[] AllowedActions { get; init; }
    public required WorkOrderFaultDetail[] Faults { get; init; }
}

public sealed record WorkOrderLocation(double Lat, double Lng);
public sealed record WorkOrderFaultDetail(string FaultId, string? PoleId, string? SegmentId,
    WorkOrderLocation Location, FaultType FaultType, FaultStatus FaultStatus, Severity Severity,
    InspectionOutcome? InspectionOutcome);
public sealed record WorkOrderAssignee(string UserId, string FullName);
