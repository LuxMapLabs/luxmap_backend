using System.Net;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;

namespace LuxMap.Modules.WorkOrders;

/// <summary>
/// One pole an engineer will meet on a work order, with the status the LAST published survey left on it.
/// SELF-SIGNED (drift WO-12), provisional until FW.
/// </summary>
public sealed record WorkOrderPole
{
    public required string PoleId { get; init; }
    public required string SegmentId { get; init; }

    /// <summary>1-based place along its segment, in the direction the segment's line is drawn.</summary>
    public int Position { get; init; }

    public required WorkOrderLocation Location { get; init; }

    /// <summary><c>unknown</c> when no published sweep ever covered the pole — that is what unknown MEANS (Contract 3.1).</summary>
    public FixtureStatus FixtureStatus { get; init; }

    public double? StatusConfidence { get; init; }

    /// <summary>When the status was last seen by a published sweep; <c>null</c> for <c>unknown</c>.</summary>
    public DateTime? LastSeenAt { get; init; }

    public int OpenFaultCount { get; init; }
    public bool NearSensitivePoi { get; init; }

    /// <summary>The lamp in use; both <c>null</c> when the pole has no active fixture.</summary>
    public FixtureType? FixtureType { get; init; }
    public int? LampWatt { get; init; }

    /// <summary>Faults of THIS work order standing on the pole — the poles to repair or inspect.</summary>
    public required string[] WorkOrderFaultIds { get; init; }
}

/// <summary>
/// <c>GET /work-orders/{id}/poles</c> — what is on the road the engineer was sent to, and what we last
/// knew about each lamp, BEFORE the visit. The status is whatever the latest accepted survey published;
/// this work order's own survey changes it only once a manager accepts the sweep.
/// </summary>
/// <remarks>
/// <para>
/// Poles are those on the work order's segments (<c>work_order_segment</c>, plus the single
/// <c>segment_id</c> of inspection and repair orders) UNION the poles carrying this order's faults, so a
/// repair created from faults lists its targets even without a segment.
/// </para>
/// <para>
/// Visibility is the work order's own: it is read through the same query filter as
/// <c>GET /work-orders/{id}</c>, so an order the caller cannot see is 404. Poles and segments then pass
/// the commune filter, so on an <c>inter_commune</c> road a caller sees only the poles of communes in
/// their scope — never another commune's.
/// </para>
/// <para>
/// Order: segments in work-order position, then along each segment's line. The ordering projects the pole
/// onto the line in the stored coordinates; it is an ORDER only, so no distance is emitted.
/// </para>
/// </remarks>
public sealed class WorkOrderPoleService(LuxMapDbContext db)
{
    private static readonly FaultStatus[] OpenStatuses = [.. FaultStatusSets.Open];

    public async Task<PagedResult<WorkOrderPole>> PolesAsync(string id, PageRequest page, CancellationToken ct)
    {
        var order = await db.Set<WorkOrder>().AsNoTracking().Where(x => x.WorkOrderId == id)
            .Select(x => new { x.SegmentId }).FirstOrDefaultAsync(ct)
            ?? throw new LuxMapException("WORK_ORDER_NOT_FOUND", HttpStatusCode.NotFound, "WORK ORDER NOT FOUND");

        var segmentIds = await db.Set<WorkOrderSegment>().AsNoTracking().Where(x => x.WorkOrderId == id)
            .OrderBy(x => x.Position).Select(x => x.SegmentId).ToListAsync(ct);
        if (order.SegmentId is { } own && !segmentIds.Contains(own)) segmentIds.Add(own);

        var faultsByPole = (await (
            from link in db.Set<WorkOrderFault>().AsNoTracking()
            join fault in db.Set<Fault>().AsNoTracking() on link.FaultId equals fault.FaultId
            where link.WorkOrderId == id && fault.PoleId != null
            select new { PoleId = fault.PoleId!, fault.FaultId, fault.CreatedAt })
            .ToListAsync(ct))
            .GroupBy(x => x.PoleId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.CreatedAt).ThenBy(x => x.FaultId.Length)
                .ThenBy(x => x.FaultId, StringComparer.Ordinal).Select(x => x.FaultId).ToArray());
        var faultPoleIds = faultsByPole.Keys.ToArray();

        var rows = await db.Set<Pole>().AsNoTracking()
            .Where(p => segmentIds.Contains(p.SegmentId) || faultPoleIds.Contains(p.PoleId))
            .Select(p => new
            {
                p.PoleId, p.SegmentId, p.Geom, p.NearSensitivePoi,
                Status = db.Set<PoleCurrentStatus>().Where(s => s.PoleId == p.PoleId)
                    .Select(s => new { s.FixtureStatus, s.StatusConfidence, s.LastSeenAt }).FirstOrDefault(),
                Lamp = db.Set<Fixture>().Where(f => f.PoleId == p.PoleId && f.RemovedDate == null)
                    .Select(f => new { f.FixtureType, f.LampWatt }).FirstOrDefault(),
                // FaultStatusSets.Open is the one definition of an open fault (BE-18); never a copied list.
                OpenFaults = db.Set<Fault>().Count(f => f.PoleId == p.PoleId && OpenStatuses.Contains(f.FaultStatus)),
            })
            .ToListAsync(ct);

        var lines = (await db.Set<RoadSegment>().AsNoTracking().Where(s => segmentIds.Contains(s.SegmentId))
            .Select(s => new { s.SegmentId, s.Geom }).ToListAsync(ct))
            .ToDictionary(s => s.SegmentId, s => new LengthIndexedLine(s.Geom));

        var ordered = rows
            .Select(r => new
            {
                Row = r,
                SegmentOrder = segmentIds.IndexOf(r.SegmentId) is var at and >= 0 ? at : int.MaxValue,
                Along = lines.TryGetValue(r.SegmentId, out var line) ? line.Project(r.Geom.Coordinate) : 0d,
            })
            .OrderBy(x => x.SegmentOrder).ThenBy(x => x.Row.SegmentId.Length).ThenBy(x => x.Row.SegmentId, StringComparer.Ordinal)
            .ThenBy(x => x.Along).ThenBy(x => x.Row.PoleId.Length).ThenBy(x => x.Row.PoleId, StringComparer.Ordinal)
            .ToList();

        var positions = new Dictionary<string, int>();
        var all = ordered.Select(x =>
        {
            var position = positions[x.Row.SegmentId] = positions.GetValueOrDefault(x.Row.SegmentId) + 1;
            var r = x.Row;
            return new WorkOrderPole
            {
                PoleId = r.PoleId,
                SegmentId = r.SegmentId,
                Position = position,
                Location = new WorkOrderLocation(r.Geom.Y, r.Geom.X),
                FixtureStatus = r.Status?.FixtureStatus ?? FixtureStatus.Unknown,
                StatusConfidence = r.Status?.StatusConfidence,
                LastSeenAt = r.Status?.LastSeenAt,
                OpenFaultCount = r.OpenFaults,
                NearSensitivePoi = r.NearSensitivePoi,
                FixtureType = r.Lamp?.FixtureType,
                LampWatt = r.Lamp?.LampWatt,
                WorkOrderFaultIds = faultsByPole.GetValueOrDefault(r.PoleId, []),
            };
        }).ToList();

        return PagedResult<WorkOrderPole>.From(page, all.Count, all.Skip(page.Skip).Take(page.PageSize).ToList());
    }
}
