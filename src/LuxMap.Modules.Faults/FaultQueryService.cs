using System.Linq.Expressions;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Paging;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Faults;

/// <summary>Reads for <c>GET /faults</c> (BE-40, Contract section 5.4).</summary>
public sealed class FaultQueryService(LuxMapDbContext db, IActiveWorkOrderLookup workOrders, ICurrentActorAccessor actor)
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
            // The type the API reports: a Manager's reclassification wins (BE-19 D-4). Two branches,
            // each list typed like its column — EF cannot bind one non-nullable list to the nullable
            // override column, nor translate Contains over the coalesce.
            var overrides = types.Select(type => (FaultType?)type).ToArray();
            faults = faults.Where(fault => fault.OverrideFaultType != null
                ? overrides.Contains(fault.OverrideFaultType)
                : types.Contains(fault.FaultType));
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

        var rows = Locate(faults);

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

        var items = await ToItemsAsync(paged, ct);

        return PagedResult<FaultItem>.From(page, total, items);
    }

    /// <summary>One fault as <c>GET /faults</c> would show it; NULL when absent or outside scope.</summary>
    public async Task<FaultItem?> ItemAsync(string faultId, CancellationToken ct)
    {
        var rows = await Locate(db.Set<Fault>().AsNoTracking().Where(fault => fault.FaultId == faultId)).ToListAsync(ct);
        return rows.Count == 0 ? null : (await ToItemsAsync(rows, ct))[0];
    }

    // Location is the fault's own lat/lng, falling back to its pole's point (BE-40 D-4) — the same
    // rule as the work order detail. The table CHECK allows a pole-bound fault without coordinates,
    // and section 5.4 makes location non-null.
    private IQueryable<LocatedFault> Locate(IQueryable<Fault> faults)
        =>
            from fault in faults
            join pole in db.Set<Pole>() on fault.PoleId equals pole.PoleId into poles
            from pole in poles.DefaultIfEmpty()
            select new LocatedFault
            {
                Fault = fault,
                Lat = fault.Lat ?? (pole == null ? 0 : pole.Geom.Y),
                Lng = fault.Lng ?? (pole == null ? 0 : pole.Geom.X),
            };

    private async Task<FaultItem[]> ToItemsAsync(IReadOnlyList<LocatedFault> rows, CancellationToken ct)
    {
        var ids = rows.Select(row => row.Fault.FaultId).ToArray();
        var held = await workOrders.ActiveWorkOrdersAsync(ids, ct);
        var reviewer = actor.Role == UserRole.Manager;
        var repairs = reviewer
            ? await workOrders.ActiveRepairsAsync(ids, ct)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        return rows.Select(row => new FaultItem
        {
            FaultId = row.Fault.FaultId,
            PoleId = row.Fault.PoleId,
            FixtureId = row.Fault.FixtureId,
            SegmentId = row.Fault.SegmentId,
            Location = new FaultLocation(row.Lat, row.Lng),
            FaultType = row.Fault.EffectiveType,
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
            ReviewNote = row.Fault.ReviewNote,
            AllowedActions = reviewer
                ? FaultReviewRules.AllowedActions(row.Fault, repairs.ContainsKey(row.Fault.FaultId))
                : [],
        }).ToArray();
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
            (FaultSortKey.Severity, true) => rows
                .OrderByDescending(SeverityRank)
                .ThenBy(row => row.Fault.DetectedAt),
            (FaultSortKey.Severity, false) => rows
                .OrderBy(SeverityRank)
                .ThenBy(row => row.Fault.DetectedAt),
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

    /// <summary>
    /// 🔴 Severity is stored as TEXT, so ordering by the column is alphabetical —
    /// <c>critical, high, low, medium</c>. Rank it explicitly. An expression, not a method: EF turns
    /// it into a CASE, whereas a C# method call inside OrderBy cannot be translated at all.
    /// </summary>
    private static readonly Expression<Func<LocatedFault, int>> SeverityRank = row =>
        row.Fault.Severity == Severity.Critical ? 3
        : row.Fault.Severity == Severity.High ? 2
        : row.Fault.Severity == Severity.Medium ? 1
        : 0;

    private sealed class LocatedFault
    {
        public required Fault Fault { get; init; }
        public double Lat { get; init; }
        public double Lng { get; init; }
    }
}
