using System.Net;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Telemetry;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.GeoJson;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

// Both namespaces define Geometry: ours is the wire shape, NTS's is the stored one. Aliased rather
// than resolved by using-order, so a reader can tell which side of the boundary a line is on.
using GeoJsonGeometry = LuxMap.Shared.Contracts.GeoJson.Geometry;

namespace LuxMap.Modules.Map.Features;

/// <summary>
/// The bbox map queries behind <c>GET /poles</c> and <c>GET /segments</c> (BE-14).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>The bbox predicate MUST stay <c>ST_Intersects(geom, envelope)</c> on the 4326 column.</b>
/// That is the only form PostGIS can answer from <c>ix_pole_geom</c>. Wrapping the column in
/// <c>ST_Transform</c>, or comparing X and Y with <c>&gt;=</c> and <c>&lt;=</c>, makes the predicate
/// a function of the column and the index stops applying — measured at BE-10 as cost 62608 and a
/// sequential scan against cost 86 and a bitmap index scan, on 2500 poles. Contract section 6 puts
/// the budget at 500 ms for 2000 poles, so losing the index loses the requirement.
/// </para>
/// <para>
/// There is no distance refinement here, so the two-tier rule reduces to its first tier. When
/// BE-29 adds one, the coarse <c>ST_Intersects</c> comes FIRST and the metric filter refines what
/// survives — never the other way round.
/// </para>
/// </remarks>
public sealed class MapQueryService(LuxMapDbContext dbContext, IotOptions iot, TimeProvider clock)
{
    /// <summary>Past this many poles the client is told to zoom in rather than served (section 5.1).</summary>
    public const int MaxPoles = 2000;

    /// <summary>
    /// <c>FaultStatusSets.Open</c> as an array, because EF Core cannot translate
    /// <c>IReadOnlySet&lt;T&gt;.Contains</c> into SQL.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>DERIVED from the one definition, never a second list.</b> BE-18 is emphatic that the
    /// open-fault set is written down once: a hand-copied <c>[Detected, Confirmed, InProgress]</c>
    /// here would be correct the day it was typed and wrong the day the definition moved, with
    /// nothing to detect the difference. Spreading the set keeps a single source and only changes
    /// its container.
    /// </remarks>
    private static readonly FaultStatus[] OpenFaultStatuses = [.. FaultStatusSets.Open];

    public async Task<FeatureCollection<PoleProperties>> PolesAsync(
        PoleMapQuery query, CancellationToken ct)
    {
        var poles = PoleQuery(query);

        // COUNT before SELECT, deliberately. The alternative — fetch and count the list — would
        // materialise the very payload the limit exists to avoid, so the protection would cost
        // exactly what it is meant to save.
        var total = await poles.CountAsync(ct);

        if (total > MaxPoles)
        {
            throw new LuxMapException(
                ErrorCodes.BboxTooLarge,
                HttpStatusCode.RequestEntityTooLarge,
                $"That area holds {total} poles, more than the {MaxPoles} this endpoint serves. Zoom in.",
                new Dictionary<string, object?> { ["count"] = total, ["max"] = MaxPoles });
        }

        return await ProjectPolesAsync(poles, ct);
    }

    /// <summary>
    /// Every pole on the given segments, in id order — the offline bundle's pole layer (BE-43). Same
    /// properties as the bbox layer, from the same projection; no data_source default and no size limit,
    /// because the caller named the roads (at most twenty).
    /// </summary>
    public Task<FeatureCollection<PoleProperties>> PolesOnSegmentsAsync(IReadOnlyCollection<string> segmentIds, CancellationToken ct)
        => ProjectPolesAsync(dbContext.Set<Pole>().AsNoTracking()
            .Where(pole => segmentIds.Contains(pole.SegmentId))
            .OrderBy(pole => pole.CreatedAt).ThenBy(pole => pole.PoleId.Length).ThenBy(pole => pole.PoleId), ct);

    /// <summary>The given segments, in the order asked — the offline bundle's segment layer (BE-43).</summary>
    public async Task<FeatureCollection<SegmentProperties>> SegmentsByIdAsync(IReadOnlyList<string> segmentIds, CancellationToken ct)
    {
        var layer = await ProjectSegmentsAsync(dbContext.Set<RoadSegment>().AsNoTracking()
            .Where(segment => segmentIds.Contains(segment.SegmentId)), ct);
        var order = segmentIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index, StringComparer.Ordinal);
        return new FeatureCollection<SegmentProperties> { Features = [.. layer.Features.OrderBy(f => order[f.Properties.SegmentId])] };
    }

    private async Task<FeatureCollection<PoleProperties>> ProjectPolesAsync(IQueryable<Pole> poles, CancellationToken ct)
    {
        var rows = await poles
            .Select(pole => new
            {
                pole.PoleId,
                pole.SegmentId,
                pole.CommuneId,
                pole.NearSensitivePoi,
                pole.Geom,

                // Left joins, all of them. A pole with no status row, no lamp and no fault is a
                // perfectly ordinary pole — most of the mock set is exactly that.
                Status = dbContext.Set<PoleCurrentStatus>()
                    .Where(status => status.PoleId == pole.PoleId)
                    .Select(status => new
                    {
                        status.FixtureStatus,
                        status.StatusConfidence,
                        status.LastSeenAt,
                        status.LastSweepId,
                    })
                    .FirstOrDefault(),

                // The lamp IN SERVICE — unique per pole since BE-REVIEW-02 constraint 3.
                Lamp = pole.Fixtures
                    .Where(lamp => lamp.RemovedDate == null)
                    .Select(lamp => new
                    {
                        lamp.PowerSource,
                        lamp.FixtureType,
                        lamp.LampWatt,
                        lamp.InstallDate,
                        lamp.WarrantyExpiry,
                    })
                    .FirstOrDefault(),

                // FaultStatusSets.Open is the ONE definition of an open fault (BE-18). Never a
                // hand-written status list here: a second copy would be right the day it was
                // written and wrong the day the definition moved, with nothing to detect it.
                OpenFaultCount = dbContext.Set<Fault>()
                    .Count(fault => fault.PoleId == pole.PoleId
                        && OpenFaultStatuses.Contains(fault.FaultStatus)),
            })
            .ToListAsync(ct);

        return new FeatureCollection<PoleProperties>
        {
            Features = [.. rows.Select(row => new Feature<PoleProperties>
            {
                Geometry = GeoJsonGeometry.Point(row.Geom.X, row.Geom.Y),
                Properties = new PoleProperties
                {
                    PoleId = row.PoleId,
                    SegmentId = row.SegmentId,

                    // No status row means the latest sweep never covered this pole, which is what
                    // `unknown` MEANS (section 3.1) — not a stand-in for missing data.
                    FixtureStatus = row.Status?.FixtureStatus ?? FixtureStatus.Unknown,

                    // Section 5.1: null if and only if the status is unknown. Reading it off the
                    // same row that decided the status keeps the two from disagreeing.
                    StatusConfidence = row.Status?.StatusConfidence,

                    PowerSource = row.Lamp?.PowerSource,
                    FixtureType = row.Lamp?.FixtureType,
                    LampWatt = row.Lamp?.LampWatt,
                    InstallDate = row.Lamp?.InstallDate,
                    WarrantyExpiry = row.Lamp?.WarrantyExpiry,
                    CommuneId = row.CommuneId,
                    LastSeenAt = row.Status?.LastSeenAt,
                    LastSweepId = row.Status?.LastSweepId,
                    OpenFaultCount = row.OpenFaultCount,

                    // No iot_node table yet. The key is emitted so consumers bind the final shape
                    // now — the precedent BE-42 set with nearest_luminance.
                    HasIotNode = false,

                    NearSensitivePoi = row.NearSensitivePoi,
                },
            })],
        };
    }

    public async Task<FeatureCollection<SegmentProperties>> SegmentsAsync(
        SegmentMapQuery query, CancellationToken ct)
    {
        var envelope = Envelope(query.Bbox);

        var segments = dbContext.Set<RoadSegment>().AsNoTracking()
            .Where(segment => segment.Geom.Intersects(envelope));

        segments = WithDataSource(segments, query.DataSource, segment => segment.DataSource);

        if (query.CommuneIds is { Count: > 0 } communes)
        {
            segments = segments.Where(segment => communes.Contains(segment.CommuneId));
        }

        return await ProjectSegmentsAsync(segments, ct);
    }

    private async Task<FeatureCollection<SegmentProperties>> ProjectSegmentsAsync(IQueryable<RoadSegment> segments, CancellationToken ct)
    {
        var rows = await segments
            .Select(segment => new
            {
                segment.SegmentId,
                segment.SegmentName,
                segment.RoadClass,
                segment.LengthM,
                segment.Geom,

                PoleCount = dbContext.Set<Pole>().Count(pole => pole.SegmentId == segment.SegmentId),

                // ONE cause for the whole road, not N lamp faults — the output of CV-15's spatial
                // clustering. Nothing writes segment_outage yet, so this is false everywhere today.
                HasActiveSegmentFault = dbContext.Set<Fault>()
                    .Any(fault => fault.SegmentId == segment.SegmentId
                        && fault.FaultType == FaultType.SegmentOutage
                        && OpenFaultStatuses.Contains(fault.FaultStatus)),

                // I-7b: derived on read through segment → pole → feeder → feeder_control → device.
                // The commune filter applies to every set here, so a device of another commune
                // never appears even on an inter_commune road.
                ControllerNodeIds = dbContext.Set<IotNode>()
                    .Where(node => dbContext.Set<FeederControl>().Any(control =>
                        control.NodeId == node.NodeId
                        && dbContext.Set<Pole>().Any(pole =>
                            pole.SegmentId == segment.SegmentId && pole.FeederId == control.FeederId)))
                    .OrderBy(node => node.CreatedAt)
                    .ThenBy(node => node.NodeId.Length)
                    .ThenBy(node => node.NodeId)
                    .Select(node => node.NodeId)
                    .ToList(),
            })
            .ToListAsync(ct);

        return new FeatureCollection<SegmentProperties>
        {
            Features = [.. rows.Select(row => new Feature<SegmentProperties>
            {
                Geometry = GeoJsonGeometry.LineString(
                    row.Geom.Coordinates.Select(point => (point.X, point.Y))),
                Properties = new SegmentProperties
                {
                    SegmentId = row.SegmentId,
                    SegmentName = row.SegmentName,
                    RoadClass = row.RoadClass,
                    LengthM = row.LengthM,
                    PoleCount = row.PoleCount,
                    ControllerNodeIds = row.ControllerNodeIds,
                    HasActiveSegmentFault = row.HasActiveSegmentFault,
                },
            })],
        };
    }

    /// <summary>
    /// IoT devices inside a bounding box (BE-14b), as a <c>FeatureCollection</c> of points.
    /// </summary>
    /// <remarks>
    /// No size limit, like segments: a commune has a handful of cabinets. Features come in id order
    /// (<c>created_at, length(id), id</c> — never the bare id, see CLAUDE.md section 0).
    /// </remarks>
    public async Task<FeatureCollection<IotNodeProperties>> IotNodesAsync(
        IotNodeMapQuery query, CancellationToken ct)
    {
        var rows = await IotNodeQuery(query)
            .Select(placed => new
            {
                placed.Node.NodeId,
                placed.Node.NodeRole,
                placed.Geom,
                placed.Node.SupportsRemoteControl,
                placed.Node.LastReportAt,

                FeederIds = dbContext.Set<FeederControl>()
                    .Where(control => control.NodeId == placed.Node.NodeId)
                    .OrderBy(control => control.RelayNo)
                    .Select(control => control.FeederId)
                    .ToList(),

                SegmentIds = dbContext.Set<RoadSegment>()
                    .Where(segment => dbContext.Set<Pole>().Any(pole =>
                        pole.SegmentId == segment.SegmentId
                        && dbContext.Set<FeederControl>().Any(control =>
                            control.NodeId == placed.Node.NodeId && control.FeederId == pole.FeederId)))
                    .OrderBy(segment => segment.CreatedAt)
                    .ThenBy(segment => segment.SegmentId.Length)
                    .ThenBy(segment => segment.SegmentId)
                    .Select(segment => segment.SegmentId)
                    .ToList(),
            })
            .ToListAsync(ct);

        // One clock reading for the whole response, so two devices with the same last report can
        // never straddle the threshold within one answer.
        var now = clock.GetUtcNow().UtcDateTime;

        return new FeatureCollection<IotNodeProperties>
        {
            Features = [.. rows.Select(row => new Feature<IotNodeProperties>
            {
                Geometry = GeoJsonGeometry.Point(row.Geom.X, row.Geom.Y),
                Properties = new IotNodeProperties
                {
                    NodeId = row.NodeId,
                    NodeRole = row.NodeRole,
                    NodeStatus = iot.StatusAt(row.LastReportAt, now),
                    PoleId = null,
                    SegmentIds = row.SegmentIds,
                    FeederIds = row.FeederIds,
                    SupportsRemoteControl = row.SupportsRemoteControl,
                    LastReportAt = row.LastReportAt,
                },
            })],
        };
    }

    /// <summary>
    /// The devices of <c>GET /map/iot-nodes</c>, each with its cabinet's point, in id order.
    /// </summary>
    /// <remarks>
    /// CAB-3: a device has no point of its own — it is where its cabinet is. ⚠️ The bbox is
    /// <c>Intersects(envelope)</c> on the cabinet's RAW 4326 column, the only form that reaches
    /// <c>ix_electrical_cabinet_geom</c> (BE-14 trap 2). Exposed for <c>MapQueryPlanTests</c>, which
    /// explains this SQL rather than a hand-written prediction of it.
    /// </remarks>
    public IQueryable<PlacedNode> IotNodeQuery(IotNodeMapQuery query)
    {
        var envelope = Envelope(query.Bbox);

        var nodes = WithDataSource(dbContext.Set<IotNode>().AsNoTracking(), query.DataSource, node => node.DataSource);

        if (query.CommuneIds is { Count: > 0 } communes)
        {
            nodes = nodes.Where(node => communes.Contains(node.CommuneId));
        }

        return nodes
            .Join(
                dbContext.Set<ElectricalCabinet>().AsNoTracking().Where(cabinet => cabinet.Geom.Intersects(envelope)),
                node => node.CabinetId,
                cabinet => cabinet.CabinetId,
                (node, cabinet) => new PlacedNode { Node = node, Geom = cabinet.Geom })
            .OrderBy(placed => placed.Node.CreatedAt)
            .ThenBy(placed => placed.Node.NodeId.Length)
            .ThenBy(placed => placed.Node.NodeId);
    }

    /// <summary>A device and the point of the cabinet it is mounted in.</summary>
    /// <remarks>Init properties, not a positional record: EF composes later operators over a member-init, not a constructor.</remarks>
    public sealed class PlacedNode
    {
        public required IotNode Node { get; init; }

        public required Point Geom { get; init; }
    }

    /// <summary>
    /// Cabinets inside a bounding box (CAB-7, CAB-8), as a <c>FeatureCollection</c> of points — with or without a
    /// device.
    /// </summary>
    /// <remarks>
    /// No size limit, like segments and devices: a commune has a handful of cabinets. <c>feeder_ids</c> through the
    /// query filter and in id order; <c>iot_node_id</c> is the device's id only — its state stays on
    /// <c>GET /map/iot-nodes</c>, one answer per question.
    /// </remarks>
    public async Task<FeatureCollection<CabinetProperties>> CabinetsAsync(CabinetMapQuery query, CancellationToken ct)
    {
        var rows = await CabinetQuery(query)
            .Select(cabinet => new
            {
                cabinet.CabinetId,
                cabinet.CabinetName,
                cabinet.CommuneId,
                cabinet.Geom,
                FeederIds = dbContext.Set<Feeder>()
                    .Where(feeder => feeder.CabinetId == cabinet.CabinetId)
                    .OrderBy(feeder => feeder.CreatedAt)
                    .ThenBy(feeder => feeder.FeederId.Length)
                    .ThenBy(feeder => feeder.FeederId)
                    .Select(feeder => feeder.FeederId)
                    .ToList(),
                IotNodeId = dbContext.Set<IotNode>()
                    .Where(node => node.CabinetId == cabinet.CabinetId)
                    .Select(node => node.NodeId)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return new FeatureCollection<CabinetProperties>
        {
            Features = [.. rows.Select(row => new Feature<CabinetProperties>
            {
                Geometry = GeoJsonGeometry.Point(row.Geom.X, row.Geom.Y),
                Properties = new CabinetProperties
                {
                    CabinetId = row.CabinetId,
                    CabinetName = row.CabinetName,
                    CommuneId = row.CommuneId,
                    FeederIds = row.FeederIds,
                    IotNodeId = row.IotNodeId,
                },
            })],
        };
    }

    /// <summary>The cabinets of <c>GET /map/cabinets</c>, in id order. Exposed for <c>MapQueryPlanTests</c>.</summary>
    /// <remarks>⚠️ <c>Intersects(envelope)</c> on the raw 4326 column — the form that reaches <c>ix_electrical_cabinet_geom</c>.</remarks>
    public IQueryable<ElectricalCabinet> CabinetQuery(CabinetMapQuery query)
    {
        var envelope = Envelope(query.Bbox);

        var cabinets = dbContext.Set<ElectricalCabinet>().AsNoTracking()
            .Where(cabinet => cabinet.Geom.Intersects(envelope));

        cabinets = WithDataSource(cabinets, query.DataSource, cabinet => cabinet.DataSource);

        if (query.CommuneIds is { Count: > 0 } communes)
        {
            cabinets = cabinets.Where(cabinet => communes.Contains(cabinet.CommuneId));
        }

        return cabinets
            .OrderBy(cabinet => cabinet.CreatedAt)
            .ThenBy(cabinet => cabinet.CabinetId.Length)
            .ThenBy(cabinet => cabinet.CabinetId);
    }

    /// <summary>Everything in the box that the caller asked for and is allowed to see.</summary>
    /// <remarks>
    /// <para>
    /// The commune filter of BE-08 is already in the <c>WHERE</c> of every query through
    /// <c>HasQueryFilter</c>; <c>commune_id</c> here narrows FURTHER within what the caller holds,
    /// and <c>CommuneFilter.Narrow</c> at the controller answers 403 if they ask outside it.
    /// </para>
    /// <para>
    /// <b>Public so a test can assert the query PLAN, not only the rows.</b> Contract section 6 puts
    /// a 500 ms budget on 2000 poles and requires the bbox to go through the GIST index; both are
    /// properties of the plan, and a query that quietly stopped using the index would return exactly
    /// the same rows. <c>ToQueryString()</c> on what this returns is what a plan test runs
    /// <c>EXPLAIN</c> against.
    /// </para>
    /// </remarks>
    public IQueryable<Pole> PoleQuery(PoleMapQuery query)
    {
        var envelope = Envelope(query.Bbox);

        // ⚠️ Intersects(envelope), not X/Y comparisons: this is the form that reaches ix_pole_geom.
        var poles = dbContext.Set<Pole>().AsNoTracking()
            .Where(pole => pole.Geom.Intersects(envelope));

        poles = WithDataSource(poles, query.DataSource, pole => pole.DataSource);

        if (query.CommuneIds is { Count: > 0 } communes)
        {
            poles = poles.Where(pole => communes.Contains(pole.CommuneId));
        }

        if (query.SegmentId is { Length: > 0 } segmentId)
        {
            poles = poles.Where(pole => pole.SegmentId == segmentId);
        }

        if (query.Statuses is { Count: > 0 } statuses)
        {
            // A pole with no status row reads as `unknown`, so asking for `unknown` must return
            // those too — otherwise the filter and the rendered value disagree on the same pole.
            var wantsUnknown = statuses.Contains(FixtureStatus.Unknown);

            poles = poles.Where(pole =>
                (wantsUnknown && !dbContext.Set<PoleCurrentStatus>().Any(status => status.PoleId == pole.PoleId))
                || dbContext.Set<PoleCurrentStatus>().Any(status =>
                    status.PoleId == pole.PoleId && statuses.Contains(status.FixtureStatus)));
        }

        if (query.PowerSource is { } power)
        {
            poles = poles.Where(pole =>
                pole.Fixtures.Any(lamp => lamp.RemovedDate == null && lamp.PowerSource == power));
        }

        if (query.HasOpenFault is { } wantsFault)
        {
            var withFault = dbContext.Set<Fault>()
                .Where(fault => OpenFaultStatuses.Contains(fault.FaultStatus))
                .Select(fault => fault.PoleId);

            poles = wantsFault
                ? poles.Where(pole => withFault.Contains(pole.PoleId))
                : poles.Where(pole => !withFault.Contains(pole.PoleId));
        }

        return poles;
    }

    /// <summary>
    /// Contract section 1.6: <c>calibration_rig</c> is EXCLUDED unless asked for by name.
    /// </summary>
    /// <remarks>
    /// The default is the guard rail, not a convenience. The FO-07 calibration rig is registered as
    /// a real <c>RoadSegment</c> so the pipeline has one path, which means its poles sit in the same
    /// table as the study area's; a map that silently mixed them would mix the only photometric
    /// ground truth into the sensory-labelled figures, and CLAUDE.md calls that a serious error
    /// rather than a presentation detail.
    /// </remarks>
    private static IQueryable<T> WithDataSource<T>(
        IQueryable<T> source,
        IReadOnlyList<DataSource>? requested,
        System.Linq.Expressions.Expression<Func<T, DataSource>> selector)
    {
        var parameter = selector.Parameters[0];

        var predicate = requested is { Count: > 0 }
            ? System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(
                System.Linq.Expressions.Expression.Call(
                    System.Linq.Expressions.Expression.Constant(requested),
                    typeof(ICollection<DataSource>).GetMethod(nameof(ICollection<DataSource>.Contains))!,
                    selector.Body),
                parameter)
            : System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(
                System.Linq.Expressions.Expression.NotEqual(
                    selector.Body,
                    System.Linq.Expressions.Expression.Constant(DataSource.CalibrationRig)),
                parameter);

        return source.Where(predicate);
    }

    /// <summary>
    /// The bbox as a geometry PostGIS can compare against an indexed column.
    /// </summary>
    /// <remarks>
    /// SRID 4326 set EXPLICITLY: a geometry built in code carries SRID 0, and PostGIS refuses to
    /// compare mixed SRIDs — the same trap WKTReader has, recorded in CLAUDE.md for BE-12a.
    /// </remarks>
    private static Polygon Envelope(BoundingBox box)
        => new(new LinearRing([
            new Coordinate(box.MinLng, box.MinLat),
            new Coordinate(box.MaxLng, box.MinLat),
            new Coordinate(box.MaxLng, box.MaxLat),
            new Coordinate(box.MinLng, box.MaxLat),
            new Coordinate(box.MinLng, box.MinLat),
        ]))
        { SRID = 4326 };
}
