using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.WorkOrders;

public sealed record BatchCreateWorkOrdersResponse(int Count, WorkOrderDetail[] CreatedItems);

/// <summary>A record so a richer view (the agenda, BE-25) can extend it through the copy constructor instead of re-listing fields.</summary>
public record WorkOrderItem
{
    public required string WorkOrderId { get; init; }
    public required string Title { get; init; }
    public required string CommuneId { get; init; }
    public TaskKind TaskKind { get; init; }
    public string? SegmentId { get; init; }
    public string? ClusterId { get; init; }
    /// <summary>The work order this one followed up; NULL for the first step of a chain (drift FR-2).</summary>
    public string? ParentWorkOrderId { get; init; }
    /// <summary>The ID of the chain's first work order — the same for every step (drift FR-2).</summary>
    public required string CaseId { get; init; }
    public required string[] FaultIds { get; init; }
    /// <summary>The poles of a pole_gps_update order, in the order they were sent; empty for other kinds (WO-POLE-GPS PG-4).</summary>
    public required string[] PoleIds { get; init; }
    public WorkOrderStatus WoStatus { get; init; }
    public CancellationRequestStatus CancellationRequestStatus { get; init; }
    public string? CancellationRequestNote { get; init; }
    public DateTime? CancellationRequestedAt { get; init; }
    public string? CancellationRejectNote { get; init; }
    public DateTime? CancellationRejectedAt { get; init; }
    public string? AssignedTo { get; init; }
    public double? PriorityScore { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateOnly? DueDate { get; init; }
    public DateOnly? ScheduledDate { get; init; }
}

public sealed record WorkOrderDetail : WorkOrderItem
{
    /// <summary>SELF-SIGNED BE-15, pending FW: ordered survey targets.</summary>
    public string[] SegmentIds { get; init; } = [];
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

    /// <summary>
    /// What the engineer reported at the last <c>complete</c> of a pole_gps_update order, in <c>pole_ids</c> order (PG-11). Not yet in
    /// the GIS: <c>verify</c> writes it there (PG-12).
    /// </summary>
    public WorkOrderPoleLocation[] PoleLocations { get; init; } = [];

    /// <summary>The PDFs handed to the engineer, oldest first (PG-9).</summary>
    public WorkOrderAttachmentItem[] Attachments { get; init; } = [];
}

public sealed record WorkOrderPoleLocation(string PoleId, double Lat, double Lng, double AccuracyM, DateTime CapturedAt);

/// <param name="DownloadUrl">Relative path through the API, never presigned (BE-11 rule 1).</param>
public sealed record WorkOrderAttachmentItem(string AttachmentId, string FileName, long ByteCount, DateTime UploadedAt, string DownloadUrl);

public sealed record WorkOrderLocation(double Lat, double Lng);
public sealed record WorkOrderFaultDetail(string FaultId, string? PoleId, string? SegmentId,
    WorkOrderLocation Location, FaultType FaultType, FaultStatus FaultStatus, Severity Severity,
    InspectionOutcome? InspectionOutcome);
public sealed record WorkOrderAssignee(string UserId, string FullName);
