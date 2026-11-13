using System.Net;
using System.Security.Cryptography;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using LuxMap.Shared.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.WorkOrders;

/// <summary>
/// PDFs a manager hands the engineer with a <c>pole_gps_update</c> order — the QR labels to print (drift WO-POLE-GPS PG-6…PG-9).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Every read goes through the parent work order</b>, as for evidence: its query filter carries the assignee scope that
/// <c>work_order_attachment</c> does not, so an engineer cannot open another engineer's file by guessing an id.
/// </para>
/// <para>
/// A PDF is decided by its magic bytes <c>%PDF-</c>, never by the declared type or the file name. The file is buffered (≤ 20 MiB) so
/// its hash and length are the server's own, then written object first, row second (BE-11 rule 3) — no lock is held across the
/// write, so the order is locked and checked again before the row goes in.
/// </para>
/// </remarks>
public sealed class WorkOrderAttachmentService(LuxMapDbContext db, ICurrentActorAccessor actor, IObjectStore store)
{
    public const long MaxBytes = 20 * 1024 * 1024;

    /// <summary>Room for the multipart envelope around one file of <see cref="MaxBytes"/>.</summary>
    public const long MaxRequestBytes = MaxBytes + 1024 * 1024;

    public const int MaxActive = 10;
    public const string PdfContentType = "application/pdf";
    public const int MaxFileNameLength = 200;
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static LuxMapException Error(string code, HttpStatusCode status, params (string Key, object? Value)[] details)
        => new(code, status, code.Replace('_', ' '), details.ToDictionary(x => x.Key, x => x.Value));

    private string ActorId => actor.UserId ?? throw Error("UNAUTHENTICATED", HttpStatusCode.Unauthorized);

    /// <summary>The active files of one order, oldest first — the shape of <c>WorkOrderDetail.attachments</c>.</summary>
    internal static async Task<WorkOrderAttachmentItem[]> ItemsAsync(LuxMapDbContext db, string workOrderId, CancellationToken ct)
        => (await db.Set<WorkOrderAttachment>().AsNoTracking()
                .Where(x => x.WorkOrderId == workOrderId && x.RemovedAt == null)
                .OrderBy(x => x.UploadedAt).ThenBy(x => x.AttachmentId.Length).ThenBy(x => x.AttachmentId)
                .ToListAsync(ct))
            .Select(Item).ToArray();

    public async Task<WorkOrderAttachmentItem> UploadAsync(string id, IFormFile? file, CancellationToken ct)
    {
        // Shape first, so a malformed request costs no storage write.
        if (file is null || file.Length == 0) throw OptionalJson.Invalid("file");
        if (file.Length > MaxBytes) throw Error("UPLOAD_TOO_LARGE", HttpStatusCode.RequestEntityTooLarge, ("max_bytes", MaxBytes));
        await WritableAsync(id, ct);
        await RequireRoomAsync(id, ct);

        using var buffer = new MemoryStream();
        await using (var content = file.OpenReadStream()) await content.CopyToAsync(buffer, ct);
        var (length, sha256) = Inspect(buffer);

        // A fixed expression from the id registry, no user input: the same draw the column DEFAULT makes.
        var nextId = $"SELECT {PrefixedIds.WorkOrderAttachment.DefaultValueSql} AS \"Value\"";
        var attachmentId = await db.Database.SqlQueryRaw<string>(nextId).SingleAsync(ct);
        var key = $"attachment/{attachmentId}.pdf";
        buffer.Position = 0;
        var stored = await store.StoreStreamAsync(StorageBucket.Evidence, key, buffer,
            new StreamUpload(length, sha256, MaxBytes, PdfContentType), ct);

        // The write above took a while with no lock held: the order may have closed, or filled up, meanwhile.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order WHERE work_order_id = {id} FOR UPDATE", ct);
        var order = await WritableAsync(id, ct);
        await RequireRoomAsync(id, ct);
        var row = new WorkOrderAttachment
        {
            AttachmentId = attachmentId, WorkOrderId = id, CommuneId = order.CommuneId, FileName = FileName(file.FileName),
            ObjectKey = stored.Key, ByteCount = stored.ByteCount, Sha256 = stored.Sha256, UploadedBy = ActorId,
            UploadedAt = UtcMicrosecondClock.UtcNow(),
        };
        db.Add(row);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Item(row);
    }

    /// <summary>The bytes, after the same visibility check as the order itself; a removed file is gone (404).</summary>
    public async Task<(Stream Content, string FileName)> OpenAsync(string id, string attachmentId, CancellationToken ct)
    {
        await VisibleOrderAsync(id, ct);
        var row = await db.Set<WorkOrderAttachment>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.AttachmentId == attachmentId && x.WorkOrderId == id && x.RemovedAt == null, ct)
            ?? throw Error("ATTACHMENT_NOT_FOUND", HttpStatusCode.NotFound);
        return (await store.OpenAsync(StorageBucket.Evidence, row.ObjectKey, ct), row.FileName);
    }

    /// <summary>PG-8: soft removal while the order is still open for edits; the row and the object stay.</summary>
    public async Task RemoveAsync(string id, string attachmentId, CancellationToken ct)
    {
        await VisibleOrderAsync(id, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order WHERE work_order_id = {id} FOR UPDATE", ct);
        await WritableAsync(id, ct);
        var row = await db.Set<WorkOrderAttachment>()
            .FirstOrDefaultAsync(x => x.AttachmentId == attachmentId && x.WorkOrderId == id && x.RemovedAt == null, ct)
            ?? throw Error("ATTACHMENT_NOT_FOUND", HttpStatusCode.NotFound);
        row.RemovedAt = UtcMicrosecondClock.UtcNow();
        row.RemovedBy = ActorId;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>The server's own length and SHA-256 of what was read — after deciding it IS a PDF, by its first bytes.</summary>
    private static (long Length, string Sha256) Inspect(MemoryStream buffer)
    {
        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        if (bytes.Length > MaxBytes) throw Error("UPLOAD_TOO_LARGE", HttpStatusCode.RequestEntityTooLarge, ("max_bytes", MaxBytes));
        if (!bytes.StartsWith(PdfMagic))
            throw Error(ErrorCodes.UnsupportedMediaType, HttpStatusCode.UnsupportedMediaType, ("accepted", PdfContentType));
        return (bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private async Task<WorkOrder> VisibleOrderAsync(string id, CancellationToken ct)
        => await db.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.WorkOrderId == id, ct)
            ?? throw Error("WORK_ORDER_NOT_FOUND", HttpStatusCode.NotFound);

    /// <summary>Only a pole_gps_update order takes files, only while it can still be edited, and at most <see cref="MaxActive"/>.</summary>
    private async Task<WorkOrder> WritableAsync(string id, CancellationToken ct)
    {
        var order = await VisibleOrderAsync(id, ct);
        if (order.TaskKind != TaskKind.PoleGpsUpdate)
            throw Error("ATTACHMENT_NOT_ALLOWED", HttpStatusCode.Conflict, ("task_kind", WireEnum.Name(order.TaskKind)));
        if (!WorkOrderRules.Allows(order.WoStatus, "edit"))
            throw Error("INVALID_STATE_TRANSITION", HttpStatusCode.Conflict, ("work_order_id", id), ("wo_status", WireEnum.Name(order.WoStatus)));
        return order;
    }

    /// <summary>
    /// Counted inside the upload's lock; a removal frees a place. Checked on its own so a delete is never refused for a full order.
    /// </summary>
    private async Task RequireRoomAsync(string id, CancellationToken ct)
    {
        if (await db.Set<WorkOrderAttachment>().CountAsync(x => x.WorkOrderId == id && x.RemovedAt == null, ct) >= MaxActive)
            throw Error("ATTACHMENT_LIMIT_REACHED", HttpStatusCode.Conflict, ("max_attachments", MaxActive));
    }

    /// <summary>
    /// The name the manager's file had, for display only: path and control characters dropped, at most 200 UTF-16 units without
    /// splitting a surrogate pair. Never part of the object key.
    /// </summary>
    internal static string FileName(string? raw)
    {
        var name = raw ?? "";
        var slash = name.LastIndexOfAny(['/', '\\']);
        if (slash >= 0) name = name[(slash + 1)..];
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length > MaxFileNameLength)
            name = name[..(char.IsHighSurrogate(name[MaxFileNameLength - 1]) ? MaxFileNameLength - 1 : MaxFileNameLength)].TrimEnd();
        return name.Length == 0 ? "attachment.pdf" : name;
    }

    private static WorkOrderAttachmentItem Item(WorkOrderAttachment x) => new(x.AttachmentId, x.FileName, x.ByteCount, x.UploadedAt,
        $"/api/v1/work-orders/{x.WorkOrderId}/attachments/{x.AttachmentId}");
}
