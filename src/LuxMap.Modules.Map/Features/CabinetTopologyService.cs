using System.Net;
using System.Text.Json.Serialization;
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
/// The answer of <c>GET /map/cabinets/{cabinetId}/topology</c>: a GeoJSON <c>FeatureCollection</c> of edges plus one count.
/// </summary>
/// <remarks>
/// Its own type rather than <see cref="FeatureCollection{TProperties}"/> so the count does not appear on every map layer.
/// On the wire it is that collection with one extra member, which GeoJSON allows.
/// </remarks>
public sealed record CabinetTopology
{
    [JsonPropertyOrder(-4)]
    public string Type => "FeatureCollection";

    [JsonPropertyOrder(-3)]
    public string CrsNote => "EPSG:4326 (lng, lat)";

    /// <summary>
    /// Poles on this cabinet's feeders that have no position yet and are therefore NOT in the diagram (POLE-GEOM-NULL
    /// GN-5). An edge needs two points; a pole from the books has none. 0 means the diagram is complete.
    /// </summary>
    [JsonPropertyOrder(-2)]
    public required int UnlocatedPoleCount { get; init; }

    /// <summary>
    /// Poles WITH a position that stand on a segment not drawn yet (ROAD-SEGMENT-GEOM-NULL RG-10), so they cannot be
    /// ordered along a road and are NOT in the diagram. Never overlaps <see cref="UnlocatedPoleCount"/>: a pole with no
    /// position is counted there only. 0 for both means the diagram is complete.
    /// </summary>
    [JsonPropertyOrder(-1)]
    public required int UnroutedPoleCount { get; init; }

    [JsonPropertyOrder(0)]
    public required IReadOnlyList<Feature<CabinetTopologyEdgeProperties>> Features { get; init; }
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
    public async Task<CabinetTopology> TopologyAsync(string cabinetId, CancellationToken ct)
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
            .Select(pole => new { pole.PoleId, FeederId = pole.FeederId!, pole.SegmentId, pole.Geom, pole.FeederSource, pole.CreatedAt })
            .ToListAsync(ct);

        var unassigned = poles.Where(pole => pole.SegmentId == null).ToArray();
        if (unassigned.Length > 0)
        {
            throw new LuxMapException(ErrorCodes.PoleSegmentRequired, HttpStatusCode.Conflict,
                "Assign a segment to every pole before building the cabinet topology.",
                new Dictionary<string, object?>
                {
                    ["operation"] = "cabinet_topology",
                    ["pole_ids"] = unassigned.OrderBy(pole => pole.CreatedAt)
                        .ThenBy(pole => pole.PoleId.Length).ThenBy(pole => pole.PoleId, StringComparer.Ordinal)
                        .Take(100).Select(pole => pole.PoleId).ToArray(),
                    ["pole_count"] = unassigned.Length,
                });
        }

        // The guard above establishes this invariant before any dictionary lookup or branch is built.
        // A pole without a position is LEFT OUT and counted (POLE-GEOM-NULL GN-5) — unlike a pole without a segment above,
        // which refuses: book-only poles are an ordinary, long-lived state, and a 409 would lock the whole cabinet's diagram
        // until the last one is visited.
        var assigned = poles.Where(pole => pole.Geom is not null).Select(pole => new
        {
            pole.PoleId, pole.FeederId, Geom = pole.Geom!, pole.FeederSource,
            SegmentId = pole.SegmentId ?? throw new InvalidOperationException("Unassigned pole passed the topology guard."),
        }).ToArray();
        var unlocated = poles.Count - assigned.Length;
        var segmentIds = assigned.Select(pole => pole.SegmentId).Distinct().ToArray();
        var roads = (await db.Set<RoadSegment>().AsNoTracking().IgnoreQueryFilters()
                .Where(segment => segmentIds.Contains(segment.SegmentId))
                .Select(segment => new { segment.SegmentId, segment.CreatedAt, segment.Geom })
                .ToListAsync(ct))
            .ToDictionary(segment => segment.SegmentId, StringComparer.Ordinal);

        // A pole on a segment that has no geometry yet cannot be ordered along it (RG-10): left out and counted, apart from
        // the unlocated ones above. Same reasoning as GN-5 for not answering 409 — an undrawn road is a long-lived state.
        // A road missing from `roads` was deleted meanwhile (below), which is not "undrawn".
        var unrouted = assigned.Count(pole => roads.TryGetValue(pole.SegmentId, out var road) && road.Geom is null);

        var features = new List<Feature<CabinetTopologyEdgeProperties>>();
        var branch = 0;

        foreach (var feeder in feeders)
        {
            // A road missing here was deleted after its poles were read (a pole cannot outlive its segment — RESTRICT), so
            // its poles are gone too: skip them rather than answer 500 on a concurrent delete (Codex review P2).
            var bySegment = assigned
                .Where(pole => pole.FeederId == feeder.FeederId && roads.TryGetValue(pole.SegmentId, out var road) && road.Geom is not null)
                .GroupBy(pole => pole.SegmentId, StringComparer.Ordinal)
                .OrderBy(group => roads[group.Key].CreatedAt)
                .ThenBy(group => group.Key.Length)
                .ThenBy(group => group.Key, StringComparer.Ordinal);

            foreach (var group in bySegment)
            {
                var line = new LengthIndexedLine(roads[group.Key].Geom!);
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

        return new CabinetTopology { UnlocatedPoleCount = unlocated, UnroutedPoleCount = unrouted, Features = features };
    }

    /// <summary><c>inferred</c> when either side is — a chain is only as confirmed as its weakest link.</summary>
    private static TopologySource Lower(TopologySource? cabinetSource, TopologySource poleSource)
        => cabinetSource == TopologySource.Verified && poleSource == TopologySource.Verified
            ? TopologySource.Verified
            : TopologySource.Inferred;
}
