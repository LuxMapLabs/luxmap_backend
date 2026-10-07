using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;

namespace LuxMap.Modules.WorkOrders;

/// <summary>
/// When "tonight" starts. <c>WorkOrders:Agenda:TimeZone</c> / <c>WorkOrders:Agenda:NightStartsAt</c> in appsettings;
/// a restart applies it. The 12:00 boundary is PROVISIONAL (BE-25 D-2) until field operations confirm the shift.
/// </summary>
public sealed record WorkOrderAgendaOptions
{
    public const string SectionName = "WorkOrders:Agenda";

    /// <summary>IANA zone of the communes. Every pilot commune is in Vietnam.</summary>
    public string TimeZone { get; init; } = "Asia/Ho_Chi_Minh";

    /// <summary>
    /// Local time at which "tonight" becomes today's date. Before it, the engineer is still on LAST night's shift —
    /// a night shift crosses midnight, and <c>scheduled_date</c> is the evening's date (like <c>night_of</c>).
    /// </summary>
    public TimeOnly NightStartsAt { get; init; } = new(12, 0);

    /// <summary>An unknown zone would make every agenda wrong by hours, so startup STOPS.</summary>
    public void Validate() => Zone();

    public TimeZoneInfo Zone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(TimeZone); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException($"{SectionName}:TimeZone '{TimeZone}' is not a known time zone.", e);
        }
    }

    /// <summary>The night an instant belongs to: the local date, or the day before when earlier than <see cref="NightStartsAt"/>.</summary>
    public DateOnly NightOf(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, Zone()).DateTime;
        var date = DateOnly.FromDateTime(local);
        return TimeOnly.FromDateTime(local) < NightStartsAt ? date.AddDays(-1) : date;
    }

    /// <summary>The instant <paramref name="night"/> begins, in UTC: <c>NightOf(t) == night</c> ⇔ <c>NightStartUtc(night) ≤ t &lt; NightStartUtc(night + 1)</c>.</summary>
    public DateTime NightStartUtc(DateOnly night)
        => TimeZoneInfo.ConvertTimeToUtc(night.ToDateTime(NightStartsAt, DateTimeKind.Unspecified), Zone());
}

/// <summary>Why a work order is on tonight's agenda. Several can hold at once, listed in this order; at most one of the three schedule flags.</summary>
public enum AgendaFlag
{
    /// <summary>Started and not finished — the engineer is mid-way through it.</summary>
    InProgress,
    /// <summary><c>scheduled_date</c> is tonight.</summary>
    ScheduledTonight,
    /// <summary>Scheduled for an earlier night and still open.</summary>
    CarriedOver,
    /// <summary>No <c>scheduled_date</c> — doable any night.</summary>
    Unscheduled,
    /// <summary><c>due_date</c> is before tonight.</summary>
    Overdue,
}

/// <summary>One work order on the agenda: the list shape (<see cref="WorkOrderItem"/>) plus where it is and why it is here.</summary>
public sealed record AgendaWorkOrder : WorkOrderItem
{
    [SetsRequiredMembers]
    public AgendaWorkOrder(WorkOrderItem item) : base(item) { }

    /// <summary>Every road the order touches; the first is the road it is grouped under.</summary>
    public string[] SegmentIds { get; init; } = [];

    public AgendaFlag[] Flags { get; init; } = [];

    /// <summary>
    /// Where to go: the first fault's spot (its own coordinates, else its pole's — the same rule as the detail),
    /// else the start of its first road. <c>null</c> only when neither is visible to the caller.
    /// </summary>
    public WorkOrderLocation? Location { get; init; }
}

/// <summary>The orders under one road — or, for orders with no road at all, under their commune (<c>segment_id</c> null).</summary>
public sealed record WorkOrderAgendaGroup
{
    public string? SegmentId { get; init; }

    /// <summary><c>null</c> when there is no road, or the road belongs to a commune outside the caller's scope.</summary>
    public string? SegmentName { get; init; }

    public required string CommuneId { get; init; }

    /// <summary>
    /// The point of the road nearest to <c>near</c>, else the road's start; for a group without a visible road,
    /// its first order's location. A navigation hint, not a measurement.
    /// </summary>
    public WorkOrderLocation? Location { get; init; }

    /// <summary>Metres from <c>near</c> (EPSG:3405) to the road, or to <see cref="Location"/> without a road; <c>null</c> without <c>near</c>.</summary>
    public double? DistanceM { get; init; }

    public required AgendaWorkOrder[] WorkOrders { get; init; }
}

/// <summary>SELF-SIGNED BE-25, provisional until FW: what one field engineer can work on tonight, grouped by road.</summary>
public sealed record WorkOrderAgenda
{
    public DateOnly NightOf { get; init; }
    public required string AssignedTo { get; init; }

    /// <summary>Open orders scheduled for a LATER night: not listed, only counted.</summary>
    public int UpcomingCount { get; init; }

    public required WorkOrderAgendaGroup[] Groups { get; init; }
}

/// <summary>
/// <c>GET /work-orders/agenda</c> (BE-25): the engineer's open orders for tonight, grouped by road and, given
/// where they stand, nearest road first.
/// </summary>
/// <remarks>
/// <para>
/// Listed: <c>assigned</c> and <c>in_progress</c> orders of one engineer — every order in progress, and the
/// rest unless scheduled for a later night. Nothing is scheduled yet in practice, so "only tonight's
/// <c>scheduled_date</c>" would be an empty screen (D-3). <c>done</c> waits for review: nothing left to do.
/// </para>
/// <para>
/// Each order appears ONCE, under its first road: the first survey segment, else its own <c>segment_id</c>, else
/// the road of its first fault in road-id order — the order <c>GET /work-orders/{id}/poles</c> walks (D-5).
/// </para>
/// <para>
/// Visibility is the listing's: the query filter already narrows a field engineer to their own orders and
/// everyone to their communes. Distances go through <see cref="SpatialFunctions.DistanceMeters"/> over the
/// handful of roads of a few orders, so no bounding-box pre-filter is needed (that rule guards a LEADING
/// predicate on a large table, CLAUDE.md BE-10).
/// </para>
/// </remarks>
public sealed class WorkOrderAgendaService(LuxMapDbContext db, ICurrentActorAccessor actor,
    WorkOrderAgendaOptions options, TimeProvider clock)
{
    public async Task<WorkOrderAgenda> AgendaAsync(DateOnly? nightOf, string? near, string? assignedTo, CancellationToken ct)
    {
        var origin = near is null ? null : ParseNear(near);
        var target = await TargetAsync(assignedTo, ct);
        var night = nightOf ?? options.NightOf(clock.GetUtcNow());

        var open = await db.Set<WorkOrder>().AsNoTracking()
            .Where(x => x.AssignedTo == target && (x.WoStatus == WorkOrderStatus.Assigned || x.WoStatus == WorkOrderStatus.InProgress))
            .ToListAsync(ct);
        var tonight = open.Where(x => x.WoStatus == WorkOrderStatus.InProgress || x.ScheduledDate is null || x.ScheduledDate <= night).ToList();
        var ids = tonight.Select(x => x.WorkOrderId).ToArray();

        var surveyRoads = (await db.Set<WorkOrderSegment>().AsNoTracking().Where(x => ids.Contains(x.WorkOrderId))
            .OrderBy(x => x.Position).Select(x => new { x.WorkOrderId, x.SegmentId }).ToListAsync(ct))
            .ToLookup(x => x.WorkOrderId, x => x.SegmentId);
        var faults = (await (from link in db.Set<WorkOrderFault>().AsNoTracking()
                             join fault in db.Set<Fault>().AsNoTracking() on link.FaultId equals fault.FaultId
                             join pole in db.Set<Pole>().AsNoTracking() on fault.PoleId equals pole.PoleId into poles
                             from pole in poles.DefaultIfEmpty()
                             where ids.Contains(link.WorkOrderId)
                             orderby fault.CreatedAt, fault.FaultId.Length, fault.FaultId
                             // The pole's road is where the lamp physically stands (WO-12 orders by it too).
                             select new { link.WorkOrderId, SegmentId = pole != null ? pole.SegmentId : fault.SegmentId,
                                 Lat = fault.Lat ?? (pole == null ? (double?)null : pole.Geom.Y),
                                 Lng = fault.Lng ?? (pole == null ? (double?)null : pole.Geom.X) }).ToListAsync(ct))
            .ToLookup(x => x.WorkOrderId);

        string[] RoadsOf(WorkOrder wo) => surveyRoads.Contains(wo.WorkOrderId)
            ? surveyRoads[wo.WorkOrderId].ToArray()
            : new[] { wo.SegmentId }.Concat(faults[wo.WorkOrderId].Select(x => x.SegmentId)
                    .OfType<string>().OrderBy(x => x.Length).ThenBy(x => x, StringComparer.Ordinal))
                .OfType<string>().Distinct().ToArray();
        var roadsOf = tonight.ToDictionary(x => x.WorkOrderId, RoadsOf);

        var firstRoads = roadsOf.Values.Where(x => x.Length > 0).Select(x => x[0]).Distinct().ToArray();
        // Filtered: a road of a commune outside the caller's scope stays nameless and placeless here.
        var roads = await db.Set<RoadSegment>().AsNoTracking().Where(s => firstRoads.Contains(s.SegmentId))
            .Select(s => new { s.SegmentId, s.SegmentName, s.CommuneId, s.CreatedAt, s.Geom }).ToDictionaryAsync(s => s.SegmentId, ct);
        Dictionary<string, double> roadDistance = origin is null ? [] : await db.Set<RoadSegment>().AsNoTracking()
            .Where(s => firstRoads.Contains(s.SegmentId))
            .Select(s => new { s.SegmentId, Metres = SpatialFunctions.DistanceMeters(s.Geom, origin) })
            .ToDictionaryAsync(s => s.SegmentId, s => s.Metres, ct);

        WorkOrderLocation? LocationOf(WorkOrder wo)
        {
            if (faults[wo.WorkOrderId].FirstOrDefault(x => x.Lat is not null && x.Lng is not null) is { } first)
                return new(first.Lat!.Value, first.Lng!.Value);
            return roadsOf[wo.WorkOrderId] is [var road, ..] && roads.TryGetValue(road, out var line)
                ? new(line.Geom.StartPoint.Y, line.Geom.StartPoint.X) : null;
        }

        var items = (await WorkOrderService.ItemsAsync(db, tonight, ct)).ToDictionary(x => x.WorkOrderId);
        var entries = tonight.Select(wo => new AgendaWorkOrder(items[wo.WorkOrderId])
        {
            SegmentIds = roadsOf[wo.WorkOrderId],
            Flags = FlagsOf(wo, night),
            Location = LocationOf(wo),
        }).ToList();

        var groups = new List<(WorkOrderAgendaGroup Group, IComparable[] Order)>();
        foreach (var bucket in entries.GroupBy(x => x.SegmentIds is [var road, ..] ? road : "\0" + x.CommuneId))
        {
            var orders = bucket.OrderByDescending(x => x.WoStatus == WorkOrderStatus.InProgress)
                .ThenByDescending(x => x.DueDate < night).ThenBy(x => x.DueDate is null).ThenBy(x => x.DueDate)
                .ThenBy(x => x.CreatedAt).ThenBy(x => x.WorkOrderId.Length).ThenBy(x => x.WorkOrderId, StringComparer.Ordinal)
                .ToArray();
            var segmentId = orders[0].SegmentIds is [var key, ..] ? key : null;
            if (segmentId is not null && roads.TryGetValue(segmentId, out var road))
            {
                groups.Add((new WorkOrderAgendaGroup
                {
                    SegmentId = segmentId, SegmentName = road.SegmentName, CommuneId = road.CommuneId,
                    Location = origin is null ? new(road.Geom.StartPoint.Y, road.Geom.StartPoint.X) : Nearest(road.Geom, origin),
                    DistanceM = origin is null ? null : roadDistance[segmentId], WorkOrders = orders,
                }, [0, road.CreatedAt, segmentId.Length, segmentId]));
                continue;
            }
            var location = orders.Select(x => x.Location).FirstOrDefault(x => x is not null);
            groups.Add((new WorkOrderAgendaGroup
            {
                SegmentId = segmentId, CommuneId = orders[0].CommuneId, Location = location,
                DistanceM = origin is null || location is null ? null : await PointDistanceAsync(location, origin, orders[0].WorkOrderId, ct),
                WorkOrders = orders,
            }, segmentId is null ? [2, orders[0].CommuneId.Length, orders[0].CommuneId] : [1, segmentId.Length, segmentId]));
        }

        var ordered = (origin is null ? groups.OrderBy(_ => 0) : groups.OrderBy(x => x.Group.DistanceM is null).ThenBy(x => x.Group.DistanceM))
            .ThenBy(x => x.Order, KeyComparer.Instance).Select(x => x.Group).ToArray();
        return new WorkOrderAgenda { NightOf = night, AssignedTo = target, UpcomingCount = open.Count - tonight.Count, Groups = ordered };
    }

    private static AgendaFlag[] FlagsOf(WorkOrder wo, DateOnly night)
    {
        var flags = new List<AgendaFlag>();
        if (wo.WoStatus == WorkOrderStatus.InProgress) flags.Add(AgendaFlag.InProgress);
        if (wo.ScheduledDate is null) flags.Add(AgendaFlag.Unscheduled);
        else if (wo.ScheduledDate == night) flags.Add(AgendaFlag.ScheduledTonight);
        else if (wo.ScheduledDate < night) flags.Add(AgendaFlag.CarriedOver);
        if (wo.DueDate < night) flags.Add(AgendaFlag.Overdue);
        return [.. flags];
    }

    /// <summary>
    /// A field engineer always gets their own agenda; naming anyone else is 404, as an order of someone else is.
    /// Everyone else MUST name a field engineer who works in one of their communes (D-6).
    /// </summary>
    private async Task<string> TargetAsync(string? assignedTo, CancellationToken ct)
    {
        var self = actor.UserId ?? throw new LuxMapException("UNAUTHENTICATED", HttpStatusCode.Unauthorized, "Authentication required.");
        var named = string.IsNullOrWhiteSpace(assignedTo) ? null : assignedTo.Trim() == "me" ? self : assignedTo.Trim();
        if (actor.Role == UserRole.FieldEngineer)
            return named is null || named == self ? self : throw NotFound();
        if (named is null) throw OptionalJson.Invalid("assigned_to");

        var scope = db.CurrentCommuneScope;
        var communes = scope.CommuneIds.ToArray();
        var known = await db.Set<AppUser>().AsNoTracking().AnyAsync(u => u.UserId == named && u.Role == UserRole.FieldEngineer
            && (scope.IsSystemWide || db.Set<AppUserCommune>().Any(l => l.UserId == u.UserId && communes.Contains(l.CommuneId))), ct);
        return known ? named : throw NotFound();
    }

    private static LuxMapException NotFound() => new("USER_NOT_FOUND", HttpStatusCode.NotFound, "USER NOT FOUND");

    /// <summary><c>near=lat,lng</c> in EPSG:4326, invariant culture, finite and on the globe — else 400.</summary>
    private static Point ParseNear(string raw)
    {
        var parts = raw.Split(',');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) && double.IsFinite(lat) && Math.Abs(lat) <= 90
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng) && double.IsFinite(lng) && Math.Abs(lng) <= 180)
            return new Point(lng, lat) { SRID = 4326 };
        throw OptionalJson.Invalid("near");
    }

    /// <summary>
    /// The point of the line nearest to <paramref name="origin"/>, projected in stored degrees: a place to head for,
    /// never a measurement, so the slight lat/lng anisotropy does not matter. No 3405 coordinate leaves the database.
    /// </summary>
    private static WorkOrderLocation Nearest(LineString line, Point origin)
    {
        var indexed = new LengthIndexedLine(line);
        var at = indexed.ExtractPoint(indexed.Project(origin.Coordinate));
        return new(at.Y, at.X);
    }

    private Task<double> PointDistanceAsync(WorkOrderLocation location, Point origin, string anyVisibleOrder, CancellationToken ct)
    {
        var point = new Point(location.Lng, location.Lat) { SRID = 4326 };
        // Evaluated by PostGIS: DistanceMeters is query-only and never runs in .NET.
        return db.Set<WorkOrder>().AsNoTracking().Where(x => x.WorkOrderId == anyVisibleOrder)
            .Select(_ => SpatialFunctions.DistanceMeters(point, origin)).SingleAsync(ct);
    }

    /// <summary>Lexicographic order over mixed keys of the same shape.</summary>
    private sealed class KeyComparer : IComparer<IComparable[]>
    {
        public static readonly KeyComparer Instance = new();
        public int Compare(IComparable[]? x, IComparable[]? y)
        {
            for (int i = 0; i < Math.Min(x!.Length, y!.Length); i++)
            {
                // Strings compare ordinally; IDs were already length-ordered by the key before them.
                int c = x[i] is string a && y[i] is string b ? string.CompareOrdinal(a, b)
                    : x[i].GetType() == y[i].GetType() ? x[i].CompareTo(y[i]) : 0;
                if (c != 0) return c;
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
