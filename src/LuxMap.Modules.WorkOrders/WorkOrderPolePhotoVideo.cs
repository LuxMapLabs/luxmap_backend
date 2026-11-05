using System.Net;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Serialization;
using LuxMap.Shared.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace LuxMap.Modules.WorkOrders;

/// <summary>
/// What to send for <c>GET …/pole-photos/{photo_id}/video</c>. <see cref="Unsatisfiable"/> means a 416 with no body from the store;
/// otherwise <see cref="Body"/> holds exactly bytes <see cref="From"/>..<see cref="To"/> of a clip of <see cref="Total"/> bytes
/// (<see cref="Partial"/> says whether that is a 206 or the whole clip as a 200). The caller disposes <see cref="Body"/>.
/// </summary>
public sealed record PoleVideoRead(Stream? Body, long Total, long From, long To, bool Partial)
{
    public bool Unsatisfiable => Body is null;
}

// POLE-VIDEO PV-c1 (D-9, D-13): the clip of a pole photo, served through the API with HTTP ranges, and the record that a Manager opened it.
public sealed partial class WorkOrderPolePhotoService
{
    /// <summary>
    /// Opens the clip for a read the caller may make. <paramref name="recordView"/> is true for a user who may decide on it (a Manager):
    /// the first time they open a clip that is not decided yet, that is recorded with an audit event. A clip that does not exist is a 404;
    /// a range the clip cannot satisfy is <see cref="PoleVideoRead.Unsatisfiable"/>.
    /// </summary>
    public async Task<PoleVideoRead> OpenVideoAsync(string id, string photoId, HttpContext http, bool recordView, CancellationToken ct)
    {
        var row = await Photo(id, photoId, ct);
        if (row.VideoObjectKey is null || row.VideoBytes is not { } total)
            throw Error("POLE_VIDEO_NOT_FOUND", HttpStatusCode.NotFound);

        var range = PoleVideoRange.Resolve(http.Request.GetTypedHeaders().Range, total);
        if (range.Unsatisfiable) return new(null, total, 0, 0, true);
        var isRangeRequest = range.Partial;
        var from = range.From;
        var to = range.To;
        var body = isRangeRequest
            ? await store.OpenRangeAsync(StorageBucket.Video, row.VideoObjectKey, from, to, ct)
            : await store.OpenAsync(StorageBucket.Video, row.VideoObjectKey, ct);
        try
        {
            if (recordView) await RecordViewAsync(id, row, ct);
        }
        catch { await body.DisposeAsync(); throw; }
        return new(body, total, from, to, isRangeRequest);
    }

    // Short transaction on the photo row alone: the row is the last lock in the order work order → pole → photo, so taking only it
    // cannot invert that order. Two ranges of one player arriving together find the row already marked and write nothing.
    private async Task RecordViewAsync(string id, WorkOrderPolePhoto seen, CancellationToken ct)
    {
        var actorId = ActorId;
        if (seen.Decision is not null || seen.VideoViewedBy == actorId) return;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order_pole_photo WHERE photo_id = {seen.PhotoId} AND commune_id = {seen.CommuneId} FOR UPDATE", ct);
        var row = await db.Set<WorkOrderPolePhoto>().SingleAsync(p => p.WorkOrderId == id && p.PhotoId == seen.PhotoId, ct);
        await db.Entry(row).ReloadAsync(ct);
        if (row.Decision is not null || row.VideoViewedBy == actorId) return;
        var now = UtcMicrosecondClock.UtcNow();
        row.VideoViewedBy = actorId; row.VideoViewedAt = now;
        audit.Record(new(now, AuditActorKind.User, actorId, actor.Role, row.CommuneId, AuditEntityType.WorkOrder, id,
            AuditAction.Viewed, null, new { row.PhotoId, row.PoleId, row.VideoViewedBy, row.VideoViewedAt }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
