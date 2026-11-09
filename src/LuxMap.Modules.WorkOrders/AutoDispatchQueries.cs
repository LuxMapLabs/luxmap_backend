using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;

namespace LuxMap.Modules.WorkOrders;

internal static class WorkOrderAssignees
{
    internal static IQueryable<AppUser> EligibleUsers(LuxMapDbContext db, string commune) => db.Set<AppUser>().Where(user =>
        user.Role == UserRole.FieldEngineer && !user.IsLocked
        && db.Set<AppUserCommune>().Any(link => link.UserId == user.UserId && link.CommuneId == commune));
}

internal static class WorkOrderPoleOrdering
{
    internal static double Along(string? segment, Point point, IReadOnlyDictionary<string, LengthIndexedLine> lines)
        => segment is not null && lines.TryGetValue(segment, out var line) ? line.Project(point.Coordinate) : 0d;
}

public sealed class AutoDispatchRequest
{
    public string? CommuneId { get; init; }
}

/// <summary>Read-only adapter. All business ordering and scheduling lives in AutoDispatchPlanner.</summary>
public sealed class AutoDispatchQueries(LuxMapDbContext db, TimeProvider clock, WorkOrderAgendaOptions agenda,
    AutoDispatchOptions options)
{
    public async Task<AutoDispatchPreview> Preview(AutoDispatchRequest? request, CancellationToken ct)
    {
        var scope = CommuneFilter.Narrow(db.CurrentCommuneScope, [request?.CommuneId ?? ""]);
        var start = agenda.NightOf(clock.GetUtcNow()).AddDays(options.LeadDays);
        var query = db.Set<Fault>().AsNoTracking().Where(f => f.FaultStatus == FaultStatus.Detected
            && !db.Set<WorkOrderFault>().Any(link => link.FaultId == f.FaultId && link.ReleasedAt == null));
        if (scope is not null) query = query.Where(f => scope.Contains(f.CommuneId));
        // Keep overflow IDs for skipped, but fetch geometry only for the bounded candidate set.
        var faults = await query.OrderByDescending(f => f.Severity == Severity.Critical ? 4 : f.Severity == Severity.High ? 3
                : f.Severity == Severity.Medium ? 2 : 1)
            .ThenBy(f => f.DetectedAt).ThenBy(f => f.FaultId.Length).ThenBy(f => f.FaultId)
            .Select(f => new { f.FaultId, f.CommuneId, f.SegmentId, f.PoleId, f.Severity, f.PriorityScore, f.DetectedAt }).ToListAsync(ct);
        var selected = faults.Take(AutoDispatchPlanner.CandidateLimit).ToArray();
        var roadIds = selected.Select(f => f.SegmentId).OfType<string>().Distinct().ToArray();
        // As in WorkOrderPoleService: unfiltered geometry ONLY, for scoped poles on inter-commune roads.
        var lines = (await db.Set<RoadSegment>().AsNoTracking().IgnoreQueryFilters().Where(s => roadIds.Contains(s.SegmentId))
            .Select(s => new { s.SegmentId, s.Geom }).ToListAsync(ct)).ToDictionary(s => s.SegmentId, s => new LengthIndexedLine(s.Geom));
        // Names remain scoped; another commune's road name must never leak.
        var names = await db.Set<RoadSegment>().AsNoTracking().Where(s => roadIds.Contains(s.SegmentId))
            .ToDictionaryAsync(s => s.SegmentId, s => s.SegmentName, ct);
        var poleIds = selected.Select(f => f.PoleId).OfType<string>().Distinct().ToArray();
        var poles = await db.Set<Pole>().AsNoTracking().Where(p => poleIds.Contains(p.PoleId))
            .Select(p => new { p.PoleId, p.SegmentId, p.Geom }).ToListAsync(ct);
        var positions = poles.ToDictionary(p => p.PoleId, p => WorkOrderPoleOrdering.Along(p.SegmentId, p.Geom, lines));
        var engineers = new List<DispatchEngineer>();
        foreach (var commune in selected.Select(f => f.CommuneId).Distinct().Order(StringComparer.Ordinal))
            engineers.AddRange(await WorkOrderAssignees.EligibleUsers(db, commune).AsNoTracking()
                .Select(u => new DispatchEngineer(u.UserId, u.FullName, commune)).ToListAsync(ct));
        var engineerIds = engineers.Select(e => e.UserId).Distinct().ToArray();
        var from = EngineerLoadBoard.WeekStart(start);
        var to = EngineerLoadBoard.WeekStart(start.AddDays(EngineerLoadBoard.SearchDays - 1)).AddDays(6);
        var loads = await db.Set<WorkOrder>().AsNoTracking().Where(w => w.AssignedTo != null && engineerIds.Contains(w.AssignedTo)
                && w.WoStatus != WorkOrderStatus.Cancelled && w.ScheduledDate >= from && w.ScheduledDate <= to)
            .Select(w => new EngineerLoad(w.AssignedTo!, w.ScheduledDate!.Value)).ToListAsync(ct);
        var candidates = selected.Select(f => new DispatchFault(f.FaultId, f.CommuneId, f.SegmentId,
            f.SegmentId is null ? "" : names.GetValueOrDefault(f.SegmentId, f.SegmentId), f.PoleId,
            positions.GetValueOrDefault(f.PoleId ?? ""), f.Severity, f.PriorityScore, f.DetectedAt)).ToArray();
        var preview = AutoDispatchPlanner.Plan(start, candidates, engineers, loads, options);
        return preview with { Skipped = [.. preview.Skipped, .. faults.Skip(AutoDispatchPlanner.CandidateLimit)
            .Select(f => new DispatchSkipped(f.FaultId, "LIMIT_EXCEEDED"))] };
    }
}
