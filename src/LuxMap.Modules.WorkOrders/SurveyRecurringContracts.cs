using System.Text.Json;

namespace LuxMap.Modules.WorkOrders;

public sealed class CreateSurveyRecurringRequest
{
    public string? CommuneId { get; init; }
    public string? SegmentId { get; init; }
    public JsonElement DayOfWeek { get; init; }
    public string? AssignedTo { get; init; }
    public string? Note { get; init; }
    public JsonElement ScheduleId { get; init; }
    public JsonElement StartsOnWeek { get; init; }
    public JsonElement IsActive { get; init; }
    public JsonElement CreatedBy { get; init; }
    public JsonElement FirstOccurrenceDate { get; init; }
}

public sealed class PatchSurveyRecurringRequest
{
    public JsonElement IsActive { get; init; }
    public JsonElement AssignedTo { get; init; }
    public JsonElement Note { get; init; }
    public JsonElement DayOfWeek { get; init; }
    public JsonElement SegmentId { get; init; }
    public JsonElement CommuneId { get; init; }
    public JsonElement ScheduleId { get; init; }
    public JsonElement StartsOnWeek { get; init; }
    public JsonElement CreatedBy { get; init; }
    public JsonElement FirstOccurrenceDate { get; init; }
}

public sealed record SurveyRecurringItem(string ScheduleId, string CommuneId, string SegmentId,
    string? SegmentName, short DayOfWeek, string? AssignedTo, string? AssignedToName, bool IsActive,
    string? Note, DateOnly StartsOnWeek, DateOnly FirstOccurrenceDate, DateTime CreatedAt,
    string CreatedBy, DateTime UpdatedAt);
