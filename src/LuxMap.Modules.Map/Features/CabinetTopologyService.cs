using System.Net;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.GeoJson;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;

using GeoJsonGeometry = LuxMap.Shared.Contracts.GeoJson.Geometry;

namespace LuxMap.Modules.Map.Features;

/// <summary>
/// One edge of a cabinet's branch diagram (TOPO-INFER TI-4) — the flat <c>properties</c> of a <c>LineString</c> feature.
/// </summary>
/// <remarks>
/// ⚠️ A LOGICAL diagram, not a cable route: an edge joins two consecutive points of one branch in the order they stand
/// along the road. Every edge is drawn alike; the front end shows <see cref="FeederSource"/> in the edge's TOOLTIP
/// ("inferred from the survey, not verified" / "verified") — the label must stay visible, not only the line (TI-7).
/// </remarks>
public sealed record CabinetTopologyEdgeProperties
{
    public required string FeederId { get; init; }

    public required string SegmentId { get; init; }

    /// <summary>1-based, numbered across the whole collection in its order.</summary>
    public required int Branch { get; init; }

    /// <summary>1-based place of the edge within its branch; edge 1 starts at the cabinet.</summary>
    public required int Order { get; init; }

    /// <summary>The cabinet id for edge 1, otherwise the previous pole's id.</summary>
    public required string FromId { get; init; }

    public required string ToPoleId { get; init; }

    /// <summary>
    /// The arriving pole's label; on edge 1 the LOWER of it and the feeder → cabinet label — <c>inferred</c> when either is.
    /// <c>verified</c> means "this pole is on this feeder", not that this edge is a surveyed wire.
    /// </summary>
    public required TopologySource FeederSource { get; init; }
}

/// <summary>
/// <c>GET /map/cabinets/{cabinetId}/topology</c> — the branches a cabinet feeds, built from stored relations only.
/// </summary>
/// <remarks>
/// <para>
/// Feeders of the cabinet → poles of each feeder, grouped by <c>(feeder, segment)</c>. On each segment the cabinet and the
/// poles are projected onto the road's line (NTS <c>LengthIndexedLine</c> on the stored 4326 coordinates — an ORDER, never a
/// distance, exactly like WO-12's <c>WorkOrderPoleService</c>). Poles before the cabinet's point form one branch walked
/// toward the start, the rest one branch walked toward the end. Each branch chains cabinet → pole → pole.
/// </para>
/// <para>
/// Deterministic: feeders and segments by <c>created_at, length(id), id</c>; the start-side branch before the end-side one;
/// equal projections by <c>length(pole_id), pole_id</c>; ordinal comparisons throughout.
/// </para>
/// <para>
/// Scope: cabinet, feeders and poles all pass the commune filter, and composite keys keep the three in ONE commune, so a
/// caller who can see the cabinet sees the whole diagram. 🔴 Road geometry is read UNFILTERED, only for ordering — an
/// <c>inter_commune</c> road may belong to another commune (the WO-12 reasoning); nothing of it leaves this method.
/// </para>
/// </remarks>
public sealed class CabinetTopologyService(LuxMapDbContext db)
{
    public async Task<FeatureCollection<CabinetTopologyEdgeProperties>> TopologyAsync(string cabinetId, CancellationToken ct)
    {
        var cabinet = await db.Set<ElectricalCabinet>().AsNoTracking()
            .Where(candidate => candidate.CabinetId == cabinetId)
            .Select(candidate => new { candidate.CabinetId, candidate.Geom })
            .FirstOrDefaultAsync(ct)
            ?? throw new LuxMapException(
                ErrorCodes.AssetNotFound,
                HttpStatusCode.NotFound,
                "That cabinet does not exist, or it is outside your permitted commune scope.");

        var feeders = await db.Set<Feeder>().AsNoTracking()
            .Where(feeder => feeder.CabinetId == cabinetId)
            .OrderBy(feeder => feeder.CreatedAt).ThenBy(feeder => feeder.FeederId.Length).ThenBy(feeder => feeder.FeederId)
            .Select(feeder => new { feeder.FeederId, feeder.CabinetSource })
            .ToListAsync(ct);

        var feederIds = feeders.Select(feeder => feeder.FeederId).ToArray();
        var poles = await db.Set<Pole>().AsNoTracking()
            .Where(pole => pole.FeederId != null && feederIds.Contains(pole.FeederId))
            .Select(pole => new { pole.PoleId, FeederId = pole.FeederId!, pole.SegmentId, pole.Geom, pole.FeederSource })
            .ToListAsync(ct);

        var segmentIds = poles.Select(pole => pole.SegmentId).Distinct().ToArray();
        var roads = (await db.Set<RoadSegment>().AsNoTracking().IgnoreQueryFilters()
                .Where(segment => segmentIds.Contains(segment.SegmentId))
                .Select(segment => new { segment.SegmentId, segment.CreatedAt, segment.Geom })
                .ToListAsync(ct))
            .ToDictionary(segment => segment.SegmentId, StringComparer.Ordinal);

        var features = new List<Feature<CabinetTopologyEdgeProperties>>();
        var branch = 0;

        foreach (var feeder in feeders)
        {
            // A road missing here was deleted after its poles were read (a pole cannot outlive its segment — RESTRICT), so
            // its poles are gone too: skip them rather than answer 500 on a concurrent delete (Codex review P2).
            var bySegment = poles
                .Where(pole => pole.FeederId == feeder.FeederId && roads.ContainsKey(pole.SegmentId))
                .GroupBy(pole => pole.SegmentId, StringComparer.Ordinal)
                .OrderBy(group => roads[group.Key].CreatedAt)
                .ThenBy(group => group.Key.Length)
                .ThenBy(group => group.Key, StringComparer.Ordinal);

            foreach (var group in bySegment)
            {
                var line = new LengthIndexedLine(roads[group.Key].Geom);
                var cabinetAt = line.Project(cabinet.Geom.Coordinate);
                var placed = group
                    .Select(pole => new { Pole = pole, At = line.Project(pole.Geom.Coordinate) })
                    .ToList();

                var towardStart = placed.Where(item => item.At < cabinetAt)
                    .OrderByDescending(item => item.At)
                    .ThenBy(item => item.Pole.PoleId.Length).ThenBy(item => item.Pole.PoleId, StringComparer.Ordinal);
                var towardEnd = placed.Where(item => item.At >= cabinetAt)
                    .OrderBy(item => item.At)
                    .ThenBy(item => item.Pole.PoleId.Length).ThenBy(item => item.Pole.PoleId, StringComparer.Ordinal);

                foreach (var side in new[] { towardStart.ToList(), towardEnd.ToList() })
                {
                    if (side.Count == 0)
                    {
                        continue;
                    }

                    branch++;
                    var from = (Id: cabinet.CabinetId, Point: cabinet.Geom);
                    for (var index = 0; index < side.Count; index++)
                    {
                        var pole = side[index].Pole;
                        var poleSource = pole.FeederSource ?? TopologySource.Inferred;
                        var source = index == 0 ? Lower(feeder.CabinetSource, poleSource) : poleSource;

                        features.Add(new Feature<CabinetTopologyEdgeProperties>
                        {
                            Geometry = GeoJsonGeometry.LineString([(from.Point.X, from.Point.Y), (pole.Geom.X, pole.Geom.Y)]),
                            Properties = new CabinetTopologyEdgeProperties
                            {
                                FeederId = feeder.FeederId,
                                SegmentId = group.Key,
                                Branch = branch,
                                Order = index + 1,
                                FromId = from.Id,
                                ToPoleId = pole.PoleId,
                                FeederSource = source,
                            },
                        });

                        from = (pole.PoleId, pole.Geom);
                    }
                }
            }
        }

        return new FeatureCollection<CabinetTopologyEdgeProperties> { Features = features };
    }

    /// <summary><c>inferred</c> when either side is — a chain is only as confirmed as its weakest link.</summary>
    private static TopologySource Lower(TopologySource? cabinetSource, TopologySource poleSource)
        => cabinetSource == TopologySource.Verified && poleSource == TopologySource.Verified
            ? TopologySource.Verified
            : TopologySource.Inferred;
}
