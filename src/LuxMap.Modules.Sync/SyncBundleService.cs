using System.Data;
using System.Net;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Map.Features;
using LuxMap.Modules.WorkOrders;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.GeoJson;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Sync;

/// <summary><c>GET /sync/bundle</c> — the offline package for some roads (BE-43 D-1, D-2, D-3).</summary>
/// <remarks>
/// Every part is read by the service that owns it — the map layers, the fault list, the work order detail —
/// so the bundle can never describe a pole, a fault or an order differently from its own endpoint. All reads
/// go through the commune filter; the work orders also through the assignee filter.
/// </remarks>
public sealed class SyncBundleService(
    LuxMapDbContext db,
    ICurrentActorAccessor actor,
    MapQueryService map,
    FaultQueryService faults,
    WorkOrderService workOrders)
{
    /// <summary>The most segments one request may name; a night's work is a handful of roads.</summary>
    public const int MaxSegments = 20;

    /// <summary>The work order states an engineer still has work in. <c>done</c> waits on the Manager.</summary>
    private static readonly WorkOrderStatus[] OpenForEngineer = [WorkOrderStatus.Assigned, WorkOrderStatus.InProgress];

    private string ActorId => actor.UserId
        ?? throw new LuxMapException(ErrorCodes.Unauthenticated, HttpStatusCode.Unauthorized, "Authentication required.");

    public async Task<SyncBundle> BundleAsync(IReadOnlyList<string>? requested, CancellationToken ct)
    {
        // One snapshot: the layers, the faults and the orders must describe the same moment.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var generatedAt = UtcMicrosecondClock.UtcNow();

        var segmentIds = requested is { Count: > 0 } ? await NamedSegmentsAsync(requested, ct) : await MySegmentsAsync(ct);
        var poles = await map.PolesOnSegmentsAsync(segmentIds, ct);
        var notes = await db.Set<Pole>().AsNoTracking()
            .Where(pole => segmentIds.Contains(pole.SegmentId))
            .Select(pole => new { pole.PoleId, pole.Note })
            .ToDictionaryAsync(pole => pole.PoleId, pole => pole.Note, StringComparer.Ordinal, ct);

        var bundle = new SyncBundle
        {
            GeneratedAt = generatedAt,
            SegmentIds = segmentIds,
            Segments = await map.SegmentsByIdAsync(segmentIds, ct),
            Poles = new FeatureCollection<SyncPoleProperties>
            {
                Features = [.. poles.Features.Select(feature => new Feature<SyncPoleProperties>
                {
                    Geometry = feature.Geometry,
                    Properties = new SyncPoleProperties(feature.Properties, notes.GetValueOrDefault(feature.Properties.PoleId)),
                })],
            },
            OpenFaults = await faults.OpenOnSegmentsAsync(segmentIds, ct),
            WorkOrders = await WorkOrdersAsync(segmentIds, ct),
        };

        await transaction.CommitAsync(ct);
        return bundle;
    }

    /// <summary>The segments asked for, deduplicated in the order given. One the caller cannot see is a 404, like any read of it.</summary>
    private async Task<IReadOnlyList<string>> NamedSegmentsAsync(IReadOnlyList<string> requested, CancellationToken ct)
    {
        var ids = requested.Select(id => id.Trim()).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length is 0 or > MaxSegments)
        {
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                $"segment_id names between 1 and {MaxSegments} segments.",
                new Dictionary<string, object?> { ["field"] = "segment_id", ["max"] = MaxSegments });
        }

        var visible = await db.Set<RoadSegment>().Where(segment => ids.Contains(segment.SegmentId)).Select(segment => segment.SegmentId).ToListAsync(ct);
        if (ids.FirstOrDefault(id => !visible.Contains(id)) is { } missing)
        {
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound,
                "That road segment does not exist, or it is outside your permitted commune scope.",
                new Dictionary<string, object?> { ["segment_id"] = missing });
        }

        return ids;
    }

    /// <summary>
    /// D-2 — no <c>segment_id</c>: "tonight's work". Every segment of the caller's open work orders — the order's own
    /// segment, its survey segments, and the segments of the faults it holds and of their poles — in id order.
    /// </summary>
    private async Task<IReadOnlyList<string>> MySegmentsAsync(CancellationToken ct)
    {
        var mine = MyOpenOrders();
        var links = db.Set<WorkOrderFault>().Where(link => link.ReleasedAt == null && mine.Any(wo => wo.WorkOrderId == link.WorkOrderId));
        var held = db.Set<Fault>().Where(fault => links.Any(link => link.FaultId == fault.FaultId));

        return await db.Set<RoadSegment>()
            .Where(segment => mine.Any(wo => wo.SegmentId == segment.SegmentId)
                || db.Set<WorkOrderSegment>().Any(s => s.SegmentId == segment.SegmentId && mine.Any(wo => wo.WorkOrderId == s.WorkOrderId))
                || held.Any(fault => fault.SegmentId == segment.SegmentId)
                || db.Set<Pole>().Any(pole => pole.SegmentId == segment.SegmentId && held.Any(fault => fault.PoleId == pole.PoleId)))
            .OrderBy(segment => segment.CreatedAt).ThenBy(segment => segment.SegmentId.Length).ThenBy(segment => segment.SegmentId)
            .Select(segment => segment.SegmentId)
            .ToListAsync(ct);
    }

    /// <summary>The caller's open orders touching these segments, oldest first, each read as its own detail.</summary>
    private async Task<WorkOrderDetail[]> WorkOrdersAsync(IReadOnlyList<string> segmentIds, CancellationToken ct)
    {
        var onSegments = db.Set<Fault>().Where(fault => (fault.SegmentId != null && segmentIds.Contains(fault.SegmentId))
            || db.Set<Pole>().Any(pole => pole.PoleId == fault.PoleId && segmentIds.Contains(pole.SegmentId)));

        var ids = await MyOpenOrders()
            .Where(wo => (wo.SegmentId != null && segmentIds.Contains(wo.SegmentId))
                || db.Set<WorkOrderSegment>().Any(s => s.WorkOrderId == wo.WorkOrderId && segmentIds.Contains(s.SegmentId))
                || db.Set<WorkOrderFault>().Any(link => link.WorkOrderId == wo.WorkOrderId && link.ReleasedAt == null
                    && onSegments.Any(fault => fault.FaultId == link.FaultId)))
            .OrderBy(wo => wo.CreatedAt).ThenBy(wo => wo.WorkOrderId.Length).ThenBy(wo => wo.WorkOrderId)
            .Select(wo => wo.WorkOrderId)
            .ToListAsync(ct);

        var details = new WorkOrderDetail[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            details[i] = await workOrders.Detail(ids[i], ct);
        }

        return details;
    }

    /// <summary>Assigned to the caller by name, on top of the assignee filter a field engineer already reads through.</summary>
    private IQueryable<WorkOrder> MyOpenOrders()
    {
        var me = ActorId;
        return db.Set<WorkOrder>().Where(wo => wo.AssignedTo == me && OpenForEngineer.Contains(wo.WoStatus));
    }
}
