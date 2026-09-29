using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Paging;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Faults;

/// <summary>Reads for <c>GET /faults</c> (BE-40, Contract section 5.4).</summary>
public sealed class FaultQueryService(LuxMapDbContext db, IActiveWorkOrderLookup workOrders)
{
    public async Task<PagedResult<FaultItem>> ListAsync(FaultListQuery query, PageRequest page, CancellationToken ct)
    {
        var faults = db.Set<Fault>().AsNoTracking();

        if (query.CommuneIds is { } communes)
        {
            faults = faults.Where(fault => communes.Contains(fault.CommuneId));
        }

        if (query.Statuses is { } statuses)
        {
            faults = faults.Where(fault => statuses.Contains(fault.FaultStatus));
        }

        if (query.Severities is { } severities)
        {
            faults = faults.Where(fault => severities.Contains(fault.Severity));
        }

        if (query.FaultTypes is { } types)
        {
            faults = faults.Where(fault => types.Contains(fault.FaultType));
        }

        if (query.SourceChannels is { } channels)
        {
            faults = faults.Where(fault => channels.Contains(fault.SourceChannel));
        }

        faults = query.DataSources is { } sources
            ? faults.Where(fault => sources.Contains(fault.DataSource))
            : faults.Where(fault => fault.DataSource != DataSource.CalibrationRig);

        // An unknown pole and a pole outside the caller's scope both come back as an empty page —
        // section 5.4 closes that probe, and the query filter makes the two cases the same query.
        if (query.PoleId is { } poleId)
        {
            faults = faults.Where(fault => fault.PoleId == poleId);
        }

        if (query.SegmentId is { } segment)
        {
            faults = faults.Where(fault => fault.SegmentId == segment);
        }

        if (query.ClusterId is { } cluster)
        {
            faults = faults.Where(fault => fault.ClusterId == cluster);
        }

        // Location is the fault's own lat/lng, falling back to its pole's point (BE-40 D-4) — the
        // same rule as the work order detail. The table CHECK allows a pole-bound fault without
        // coordinates, and section 5.4 makes location non-null.
        var rows =
            from fault in faults
            join pole in db.Set<Pole>() on fault.PoleId equals pole.PoleId into poles
            from pole in poles.DefaultIfEmpty()
            select new LocatedFault
            {
                Fault = fault,
                Lat = fault.Lat ?? (pole == null ? 0 : pole.Geom.Y),
                Lng = fault.Lng ?? (pole == null ? 0 : pole.Geom.X),
            };

        // Plain range on the numbers, inclusive like ST_Intersects on an envelope. No spatial index:
        // fault has no geometry column (BE-40 D-5), and the other filters have already narrowed it.
        if (query.Bbox is { } box)
        {
            rows = rows.Where(row => row.Lng >= box.MinLng && row.Lng <= box.MaxLng
                && row.Lat >= box.MinLat && row.Lat <= box.MaxLat);
        }

        var total = await rows.CountAsync(ct);

        var paged = await Order(rows, query.Sort)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(ct);

        var held = await workOrders.ActiveWorkOrdersAsync(paged.Select(row => row.Fault.FaultId).ToArray(), ct);

        var items = paged.Select(row => new FaultItem
        {
            FaultId = row.Fault.FaultId,
            PoleId = row.Fault.PoleId,
            FixtureId = row.Fault.FixtureId,
            SegmentId = row.Fault.SegmentId,
            Location = new FaultLocation(row.Lat, row.Lng),
            FaultType = row.Fault.FaultType,
            FaultStatus = row.Fault.FaultStatus,
            Severity = row.Fault.Severity,
            SourceChannel = row.Fault.SourceChannel,
            DataSource = row.Fault.DataSource,
            PriorityScore = row.Fault.PriorityScore,
            StatusConfidence = row.Fault.StatusConfidence,
            ClusterId = row.Fault.ClusterId,
            DetectedAt = row.Fault.DetectedAt,
            UpdatedAt = row.Fault.UpdatedAt,
            WorkOrderId = held.GetValueOrDefault(row.Fault.FaultId),
            Note = row.Fault.Note,
            ReportedBy = row.Fault.ReportedBy,
        }).ToArray();

        return PagedResult<FaultItem>.From(page, total, items);
    }

    /// <remarks>
    /// 🔴 The tiebreak is <c>created_at, length(fault_id), fault_id</c>, never <c>fault_id</c> alone:
    /// as text <c>FAULT-10000</c> sorts before <c>FAULT-9999</c>, and a whole seeding batch shares one
    /// <c>created_at</c>, so the tiebreak is what actually orders it (CLAUDE.md, section 0).
    /// <para>
    /// ⚠️ <c>ix_fault_priority_score</c> is a plain DESC index (NULLs first in PostgreSQL), so it does
    /// not serve this NULLS-LAST order. Harmless at hundreds of rows; recorded for BE-32.
    /// </para>
    /// </remarks>
    private static IQueryable<LocatedFault> Order(IQueryable<LocatedFault> rows, FaultSort sort)
    {
        var ordered = (sort.Key, sort.Descending) switch
        {
            (FaultSortKey.PriorityScore, true) => rows
                .OrderBy(row => row.Fault.PriorityScore == null)
                .ThenByDescending(row => row.Fault.PriorityScore),
            (FaultSortKey.PriorityScore, false) => rows
                .OrderBy(row => row.Fault.PriorityScore == null)
                .ThenBy(row => row.Fault.PriorityScore),
            (FaultSortKey.DetectedAt, true) => rows.OrderByDescending(row => row.Fault.DetectedAt),
            (FaultSortKey.DetectedAt, false) => rows.OrderBy(row => row.Fault.DetectedAt),
            (FaultSortKey.UpdatedAt, true) => rows.OrderByDescending(row => row.Fault.UpdatedAt),
            (FaultSortKey.UpdatedAt, false) => rows.OrderBy(row => row.Fault.UpdatedAt),
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, null),
        };

        return ordered
            .ThenBy(row => row.Fault.CreatedAt)
            .ThenBy(row => row.Fault.FaultId.Length)
            .ThenBy(row => row.Fault.FaultId);
    }

    private sealed class LocatedFault
    {
        public required Fault Fault { get; init; }
        public double Lat { get; init; }
        public double Lng { get; init; }
    }
}
