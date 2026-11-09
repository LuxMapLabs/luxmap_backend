using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;

namespace LuxMap.Modules.WorkOrders.Entities;

public sealed class SurveyRecurringSchedule : ICommuneScoped, IAudited
{
    public string ScheduleId { get; set; } = null!;
    public required string CommuneId { get; set; }
    public required string SegmentId { get; set; }
    public short DayOfWeek { get; set; }
    public string? AssignedTo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateOnly StartsOnWeek { get; set; }
    public string? Note { get; set; }
    public required string CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Durable generation receipt: cancelling the order never deletes this row.</summary>
public sealed class SurveyRecurringOccurrence : ICommuneScoped
{
    public required string ScheduleId { get; set; }
    public DateOnly ScheduledDate { get; set; }
    public required string CommuneId { get; set; }
    public required string WorkOrderId { get; set; }
    public DateTime CreatedAt { get; set; }
}
