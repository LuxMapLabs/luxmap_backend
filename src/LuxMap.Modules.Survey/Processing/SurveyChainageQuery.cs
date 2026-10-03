using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Survey.Processing;

public sealed class ProjectedGps
{
    public required string SegmentId { get; set; }
    public long TimeNs { get; set; }
    public double ChainageM { get; set; }
    public double LengthM { get; set; }
    public double AccuracyM { get; set; }
    public double? SpeedMps { get; set; }
}
public sealed class ProjectedPole
{
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
    public static Task<ProjectedGps[]> Gps(LuxMapDbContext db, string sweepId, string[] segments,
        string[] communes, SurveyProcessingOptions o, CancellationToken ct) => db.Database.SqlQuery<ProjectedGps>($"""
        SELECT r.segment_id AS segment_id, g.phone_elapsed_ns AS time_ns,
            ST_LineLocatePoint(ST_Transform(r.geom,3405), ST_Transform(g.geom,3405))
                * ST_Length(ST_Transform(r.geom,3405)) AS chainage_m,
            ST_Length(ST_Transform(r.geom,3405)) AS length_m,
            g.accuracy_m AS accuracy_m, g.speed_mps AS speed_mps
        FROM road_segment r JOIN survey_gps_sample g
          ON ST_Intersects(g.geom, ST_Expand(ST_Envelope(r.geom), {o.BboxPaddingDegrees}))
        WHERE g.sweep_id = {sweepId} AND r.segment_id = ANY({segments}) AND r.commune_id = ANY({communes})
          AND ST_Distance(ST_Transform(r.geom,3405), ST_Transform(g.geom,3405)) <= {o.RouteCorridorM}
        ORDER BY r.segment_id, g.phone_elapsed_ns
        """).ToArrayAsync(ct);

    public static Task<ProjectedPole[]> Poles(LuxMapDbContext db, string[] segments, string[] communes,
        string source, SurveyProcessingOptions o, CancellationToken ct) => db.Database.SqlQuery<ProjectedPole>($"""
        SELECT r.segment_id AS segment_id, p.pole_id AS pole_id, p.commune_id AS commune_id,
            ST_AsGeoJSON(p.geom) AS geometry_json, ST_AsGeoJSON(r.geom) AS route_geometry_json,
            ST_LineLocatePoint(ST_Transform(r.geom,3405), ST_Transform(p.geom,3405))
                * ST_Length(ST_Transform(r.geom,3405)) AS chainage_m,
            ST_Length(ST_Transform(r.geom,3405)) AS length_m
        FROM road_segment r JOIN pole p ON p.segment_id = r.segment_id
          AND ST_Intersects(p.geom, ST_Expand(ST_Envelope(r.geom), {o.BboxPaddingDegrees}))
        WHERE r.segment_id = ANY({segments}) AND r.commune_id = ANY({communes})
          AND p.commune_id = ANY({communes}) AND p.data_source = {source}
          AND ST_Distance(ST_Transform(r.geom,3405), ST_Transform(p.geom,3405)) <= {o.RouteCorridorM}
        ORDER BY r.segment_id, chainage_m, length(p.pole_id), p.pole_id
        """).ToArrayAsync(ct);
}
