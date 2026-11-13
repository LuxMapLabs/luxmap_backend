using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Modules.Assets.Crud;

/// <summary>
/// The one rule for moving a pole to a measured position (POLE-LOCATION PL-3/PL-4): <c>PUT /assets/poles/{id}/location</c>, and a manager
/// verifying a <c>pole_gps_update</c> work order (WO-POLE-GPS PG-12). Measure first, then stage; the caller holds the transaction and the
/// row lock, and saves.
/// </summary>
/// <remarks>
/// No cap on how far a pole moves: a position first taken from a photo's GPS can be tens of metres out, and correcting that is the point.
/// The one limit is the survey corridor of the pole's OWN segment (<see cref="ISurveyCorridor"/>): past it, the survey's pole query no
/// longer sees the pole and it silently drops out of every later sweep.
/// </remarks>
public sealed class PoleRelocation(LuxMapDbContext dbContext, ICurrentActorAccessor actor, ISurveyCorridor corridor, IAuditTrail audit)
{
    public double CorridorM => corridor.RouteCorridorM;

    /// <summary>Where the pole would go and how that measures up. Changes nothing.</summary>
    /// <param name="pole">Tracked, and read AFTER the caller locked its row, so the "before" is the position this move replaces.</param>
    public async Task<Measured> MeasureAsync(Pole pole, double lat, double lng, CancellationToken ct)
    {
        var target = new Point(lng, lat) { SRID = 4326 };

        // First position of a pole known only from the books (POLE-GEOM-NULL G-2, GN-9): nothing to measure a move from.
        double? moved = pole.Geom is null ? null : await dbContext.Set<Pole>().Where(candidate => candidate.PoleId == pole.PoleId)
            .Select(candidate => SpatialFunctions.DistanceMeters(candidate.Geom!, target)).SingleAsync(ct);
        // Unfiltered on purpose: an inter_commune segment may belong to another commune than its pole (BE-REVIEW-02
        // constraint 1). Only a distance leaves the query, never the segment.
        // A segment not drawn yet has no route to measure against (ROAD-SEGMENT-GEOM-NULL RG-11): filtered out BEFORE the call
        // (a NULL distance materialised into a double throws), so no row comes back and the distance stays null — the move is
        // accepted. The safety net is the corridor check when the segment is first drawn (UpdateSegmentAsync).
        double? offSegment = pole.SegmentId is null ? null : await dbContext.Set<RoadSegment>().IgnoreQueryFilters()
            .Where(segment => segment.SegmentId == pole.SegmentId && segment.Geom != null)
            .Select(segment => (double?)SpatialFunctions.DistanceMeters(segment.Geom!, target)).SingleOrDefaultAsync(ct);

        return new Measured(pole, target, moved, offSegment);
    }

    /// <summary>A pole that already stands there is never refused, wherever its road runs.</summary>
    public bool IsOffSegment(Measured measured)
        => !measured.Unchanged && measured.SegmentDistanceM is { } distance && distance > corridor.RouteCorridorM;

    /// <summary>
    /// Moves the pole and stages its one <c>pole / relocated</c> audit event (PL-4). The caller saves. <c>false</c> when the pole already
    /// stood there: nothing is stamped and nothing is audited.
    /// </summary>
    /// <param name="workOrderId">The order whose report the position came from (PG-12); recorded in the audit, null for a direct move.</param>
    public bool Stage(Measured measured, string? workOrderId)
    {
        if (IsOffSegment(measured)) throw new InvalidOperationException("Check IsOffSegment before staging a relocation.");
        var pole = measured.Pole;
        var before = pole.Geom is null ? null : new { lat = pole.Geom.Y, lng = pole.Geom.X };
        pole.Geom = measured.Target;
        if (!AssetStamp.Touch(dbContext, pole, actor)) return false;

        var lat = measured.Target.Y;
        var lng = measured.Target.X;
        var movedM = measured.MovedM is { } metres ? (double?)Math.Round(metres, 2) : null;
        var segmentDistanceM = measured.SegmentDistanceM is { } distance ? (double?)Math.Round(distance, 2) : null;
        object after = workOrderId is null
            ? new { lat, lng, moved_m = movedM, pole.SegmentId, segment_distance_m = segmentDistanceM }
            : new { lat, lng, moved_m = movedM, pole.SegmentId, segment_distance_m = segmentDistanceM, work_order_id = workOrderId };
        audit.Record(new AuditChange(pole.UpdatedAt, AuditActorKind.User, actor.UserId, actor.Role, pole.CommuneId,
            AuditEntityType.Pole, pole.PoleId, AuditAction.Relocated, before, after));
        return true;
    }

    /// <param name="MovedM">Metres from the stored position; null when the pole had none.</param>
    /// <param name="SegmentDistanceM">Metres from the pole's own segment; null with no segment, or one not drawn yet.</param>
    public sealed record Measured(Pole Pole, Point Target, double? MovedM, double? SegmentDistanceM)
    {
        public bool Unchanged => Pole.Geom is { } current && current.X == Target.X && current.Y == Target.Y;
    }
}
