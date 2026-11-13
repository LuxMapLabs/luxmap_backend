using LuxMap.Shared.Authorization;

namespace LuxMap.Modules.WorkOrders.Entities;

/// <summary>
/// A PDF the manager hands the engineer with a <c>pole_gps_update</c> order — the QR labels to print and stick (drift WO-POLE-GPS PG-6).
/// </summary>
/// <remarks>
/// Object first, row second (BE-11 rule 3). Bytes only through the API. Removal is soft (PG-8): the row stays with
/// <see cref="RemovedAt"/> set, the object stays in the store — there is no object delete yet (BE-35). Every read goes through the
/// parent order, which carries the assignee scope.
/// </remarks>
public class WorkOrderAttachment : ICommuneScoped
{
    public string AttachmentId { get; set; } = null!;
    public required string WorkOrderId { get; set; }
    public required string CommuneId { get; set; }

    /// <summary>The name the manager's file had, for display and the download name; never part of the object key.</summary>
    public required string FileName { get; set; }

    public required string ObjectKey { get; set; }

    /// <summary>Bytes actually written (BE-35 capacity report), never a client-declared length.</summary>
    public long ByteCount { get; set; }

    public required string Sha256 { get; set; }
    public required string UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; }
    public DateTime? RemovedAt { get; set; }
    public string? RemovedBy { get; set; }
}
