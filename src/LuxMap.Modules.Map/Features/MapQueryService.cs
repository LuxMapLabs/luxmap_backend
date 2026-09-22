using System.Net;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Map.Bbox;
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
public sealed class MapQueryService(LuxMapDbContext dbContext)
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
                    ControllerNodeId = null,
                    HasActiveSegmentFault = row.HasActiveSegmentFault,
                },
            })],
        };
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
