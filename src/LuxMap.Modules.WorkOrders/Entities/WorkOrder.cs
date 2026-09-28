using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.WorkOrders.Entities;

public enum TaskKind { Inspection, Repair }
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
    public string? AssignedTo { get; set; }
    public DateTime? AssignedAt { get; set; }
    public required string CreatedBy { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateOnly? ScheduledDate { get; set; }
    public string? Note { get; set; }
    public string? ReviewNote { get; set; }
    public string? ReportNote { get; set; }
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
