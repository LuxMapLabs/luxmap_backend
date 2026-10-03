using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Survey.Processing;

public sealed class ProjectedGps
{
    public string? SegmentId { get; set; }
    public string? RouteGeometryJson { get; set; }
    public double? RouteDistanceM { get; set; }
    public bool RouteAmbiguous { get; set; }
    public long TimeNs { get; set; }
    public double ChainageM { get; set; }
    public double LengthM { get; set; }
    public double AccuracyM { get; set; }
    public double? SpeedMps { get; set; }
}
public sealed class ProjectedPole
{
    public double SideOfRoute { get; set; }
    public required string SegmentId { get; set; }
    public required string PoleId { get; set; }
    public required string CommuneId { get; set; }
    public required string GeometryJson { get; set; }
    public required string RouteGeometryJson { get; set; }
    public double ChainageM { get; set; }
    public double LengthM { get; set; }
}

/// <summary>Internal job seam: caller has looked up the sweep through a finite scope.
/// Every spatial predicate starts with a 4326 bounding box; transformed geometry never leaves SQL.</summary>
public static class SurveyChainageQuery
{
    public static async Task<(ProjectedGps[] Gps, ProjectedPole[] Poles)> Snapshot(
        LuxMapDbContext db, string sweepId, string[] segments, string[] communes,
        string source, SurveyProcessingOptions o, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var gps = await Gps(db, sweepId, segments, communes, o, ct);
        var poles = await Poles(db, segments, communes, source, o, ct);
        await tx.CommitAsync(ct);
        return (gps, poles);
    }

    // Bbox narrows normal candidates. If every bbox misses, measure only the finite assigned
    // routes as a diagnostic fallback; the outer LEFT JOIN still preserves an unassigned sample.
    public static Task<ProjectedGps[]> Gps(LuxMapDbContext db, string sweepId, string[] segments,
        string[] communes, SurveyProcessingOptions o, CancellationToken ct) => db.Database.SqlQuery<ProjectedGps>($"""
        SELECT nearest.segment_id AS segment_id, g.phone_elapsed_ns AS time_ns,
            COALESCE(nearest.chainage_m, 0) AS chainage_m,
            COALESCE(nearest.length_m, 0) AS length_m,
            nearest.distance_m AS route_distance_m, nearest.route_geometry_json AS route_geometry_json,
            COALESCE(nearest.second_distance_m - nearest.distance_m < {o.RouteAmbiguityM}, false) AS route_ambiguous,
            g.accuracy_m AS accuracy_m, g.speed_mps AS speed_mps
        FROM survey_gps_sample g
        LEFT JOIN LATERAL (
            SELECT candidate.*, lead(distance_m) OVER (ORDER BY distance_m, segment_id) AS second_distance_m
            FROM (
                SELECT r.segment_id, ST_AsGeoJSON(r.geom) AS route_geometry_json,
                    ST_Distance(ST_Transform(r.geom,3405), ST_Transform(g.geom,3405)) AS distance_m,
                    ST_LineLocatePoint(ST_Transform(r.geom,3405), ST_Transform(g.geom,3405))
                        * ST_Length(ST_Transform(r.geom,3405)) AS chainage_m,
                    ST_Length(ST_Transform(r.geom,3405)) AS length_m
                FROM road_segment r
                WHERE r.segment_id = ANY({segments}) AND r.commune_id = ANY({communes})
                  AND (ST_Intersects(g.geom, ST_Expand(ST_Envelope(r.geom), {o.BboxPaddingDegrees}))
                    OR NOT EXISTS (
                        SELECT 1 FROM road_segment bounded
                        WHERE bounded.segment_id = ANY({segments}) AND bounded.commune_id = ANY({communes})
                          AND ST_Intersects(g.geom, ST_Expand(ST_Envelope(bounded.geom), {o.BboxPaddingDegrees}))))
            ) candidate
            ORDER BY distance_m, segment_id LIMIT 1
        ) nearest ON true
        WHERE g.sweep_id = {sweepId}
        ORDER BY g.phone_elapsed_ns, g.sample_no
        """).ToArrayAsync(ct);

    public static Task<ProjectedPole[]> Poles(LuxMapDbContext db, string[] segments, string[] communes,
        string source, SurveyProcessingOptions o, CancellationToken ct) => db.Database.SqlQuery<ProjectedPole>($"""
        SELECT r.segment_id AS segment_id, p.pole_id AS pole_id, p.commune_id AS commune_id,
            ST_AsGeoJSON(p.geom) AS geometry_json, ST_AsGeoJSON(r.geom) AS route_geometry_json,
            ((ST_X(tangent.b) - ST_X(tangent.a)) * (ST_Y(projected.pole) - ST_Y(tangent.a))
             - (ST_Y(tangent.b) - ST_Y(tangent.a)) * (ST_X(projected.pole) - ST_X(tangent.a))) AS side_of_route,
            ST_LineLocatePoint(ST_Transform(r.geom,3405), ST_Transform(p.geom,3405))
                * ST_Length(ST_Transform(r.geom,3405)) AS chainage_m,
            ST_Length(ST_Transform(r.geom,3405)) AS length_m
        FROM road_segment r JOIN pole p ON p.segment_id = r.segment_id
          AND ST_Intersects(p.geom, ST_Expand(ST_Envelope(r.geom), {o.BboxPaddingDegrees}))
        CROSS JOIN LATERAL (SELECT ST_Transform(r.geom,3405) AS route, ST_Transform(p.geom,3405) AS pole) projected
        CROSS JOIN LATERAL (SELECT ST_LineLocatePoint(projected.route, projected.pole) AS fraction) position
        CROSS JOIN LATERAL (SELECT ST_LineInterpolatePoint(projected.route, greatest(0, position.fraction - 0.00001)) AS a,
            ST_LineInterpolatePoint(projected.route, least(1, position.fraction + 0.00001)) AS b) tangent
        WHERE r.segment_id = ANY({segments}) AND r.commune_id = ANY({communes})
          AND p.commune_id = ANY({communes}) AND p.data_source = {source}
          AND ST_Distance(ST_Transform(r.geom,3405), ST_Transform(p.geom,3405)) <= {o.RouteCorridorM}
        ORDER BY r.segment_id, chainage_m, length(p.pole_id), p.pole_id
        """).ToArrayAsync(ct);
}
