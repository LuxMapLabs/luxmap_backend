using System.Data;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Modules.WorkOrders.Jobs;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.WorkOrders;

public enum SchedulingSuggestionKind { FaultInspection, FaultRepair, MissingGps, SurveyNeeded, Overdue }
public enum SchedulingSuggestionState { Ready, InWorkOrder, Blocked }

public sealed record SchedulingSuggestionGroup(SchedulingSuggestionKind Kind, int Total, int ReadyCount,
    int InWorkOrderCount, int BlockedCount, int PoleCount, int SegmentCount);
public sealed record SchedulingSuggestionSummary(DateTime GeneratedAt, DateOnly NightOf, SchedulingSuggestionGroup[] Groups);

public sealed record SchedulingSuggestionItem
{
    public required string Id { get; init; }
    public SchedulingSuggestionKind Kind { get; init; }
    public SchedulingSuggestionState State { get; init; }
    public required string CommuneId { get; init; }
    public string? SegmentId { get; init; }
    public string? SegmentName { get; init; }
    public bool? SegmentHasGeometry { get; init; }
    public string? PoleId { get; init; }
    public string? ExternalRef { get; init; }
    public string? Note { get; init; }
    public string? FaultId { get; init; }
    public FaultType? FaultType { get; init; }
    public FaultStatus? FaultStatus { get; init; }
    public Severity? Severity { get; init; }
    public DataSource? DataSource { get; init; }
    public string? WorkOrderId { get; init; }
    public string? WorkOrderTitle { get; init; }
    public string? AssignedTo { get; init; }
    public WorkOrderStatus? WoStatus { get; init; }
    public TaskKind? RecommendedTaskKind { get; init; }
    public string? BlockingReason { get; init; }
    public DateTime? ObservedAt { get; init; }
    public DateOnly? ScheduledDate { get; init; }
    public DateOnly? DueDate { get; init; }
}

/// <summary>
/// Manager's work backlog, independent of the month on the calendar. All candidates and aggregates stay in SQL;
/// only the requested page is materialized. Existing commands own assignment and revalidate these advisory reads.
/// </summary>
public sealed class SchedulingSuggestionService(LuxMapDbContext db, WorkOrderAgendaOptions agenda, TimeProvider clock)
{
    public async Task<SchedulingSuggestionSummary> Summary(string[]? communes, string? segment, string? sources, CancellationToken ct)
    {
        var filter = Filter(communes, segment, sources);
        var now = UtcMicrosecondClock.UtcNow(clock);
        var night = agenda.NightOf(new DateTimeOffset(now));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var groups = new List<SchedulingSuggestionGroup>();
        foreach (var kind in Enum.GetValues<SchedulingSuggestionKind>())
        {
            var counts = await Candidates(kind, filter, night).GroupBy(x => 1).Select(g => new
            {
                Total = g.Count(),
                Ready = g.Count(x => x.WorkOrderId == null && x.BlockingReason == null),
                Held = g.Count(x => x.WorkOrderId != null),
                Blocked = g.Count(x => x.WorkOrderId == null && x.BlockingReason != null),
                Poles = g.Where(x => x.PoleId != null).Select(x => x.PoleId).Distinct().Count(),
                Segments = g.Where(x => x.SegmentId != null).Select(x => x.SegmentId).Distinct().Count(),
            }).SingleOrDefaultAsync(ct);
            groups.Add(new(kind, counts?.Total ?? 0, counts?.Ready ?? 0, counts?.Held ?? 0,
                counts?.Blocked ?? 0, counts?.Poles ?? 0, counts?.Segments ?? 0));
        }
        await transaction.CommitAsync(ct);
        return new(now, night, groups.ToArray());
    }

    public async Task<PagedResult<SchedulingSuggestionItem>> List(string kindValue, string[]? communes, string? segment,
        string? sources, string? stateValue, PageRequest page, CancellationToken ct)
    {
        var kind = WireEnum.Parse<SchedulingSuggestionKind>(kindValue, "kind");
        var state = stateValue is null ? (SchedulingSuggestionState?)null
            : WireEnum.Parse<SchedulingSuggestionState>(stateValue, "state");
        var filter = Filter(communes, segment, sources);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var query = Candidates(kind, filter, agenda.NightOf(clock.GetUtcNow()));
        query = state switch
        {
            SchedulingSuggestionState.Ready => query.Where(x => x.WorkOrderId == null && x.BlockingReason == null),
            SchedulingSuggestionState.InWorkOrder => query.Where(x => x.WorkOrderId != null),
            SchedulingSuggestionState.Blocked => query.Where(x => x.WorkOrderId == null && x.BlockingReason != null),
            _ => query,
        };
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.SeverityRank).ThenBy(x => x.DueDate)
            .ThenBy(x => x.ObservedAt).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id.Length).ThenBy(x => x.Id)
            .Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        var roadIds = rows.Select(x => x.SegmentId).OfType<string>().Distinct().ToArray();
        // Names stay scoped, including an inter-commune road referenced by a visible pole/fault.
        var roads = await db.Set<RoadSegment>().AsNoTracking().Where(x => roadIds.Contains(x.SegmentId))
            .Select(x => new { x.SegmentId, x.SegmentName, HasGeometry = x.Geom != null }).ToDictionaryAsync(x => x.SegmentId, ct);
        var poleIds = rows.Select(x => x.PoleId).OfType<string>().Distinct().ToArray();
        var poles = await db.Set<Pole>().AsNoTracking().Where(x => poleIds.Contains(x.PoleId))
            .Select(x => new { x.PoleId, x.ExternalRef, x.Note }).ToDictionaryAsync(x => x.PoleId, ct);
        var orderIds = rows.Select(x => x.WorkOrderId).OfType<string>().Distinct().ToArray();
        var orders = await db.Set<WorkOrder>().AsNoTracking().Where(x => orderIds.Contains(x.WorkOrderId))
            .Select(x => new { x.WorkOrderId, x.Title, x.AssignedTo, x.WoStatus, x.ScheduledDate, x.DueDate }).ToDictionaryAsync(x => x.WorkOrderId, ct);
        var items = rows.Select(x =>
        {
            var itemState = x.WorkOrderId is not null ? SchedulingSuggestionState.InWorkOrder
                : x.BlockingReason is not null ? SchedulingSuggestionState.Blocked : SchedulingSuggestionState.Ready;
            var pole = x.PoleId is null ? null : poles.GetValueOrDefault(x.PoleId);
            var order = x.WorkOrderId is null ? null : orders.GetValueOrDefault(x.WorkOrderId);
            var road = x.SegmentId is null ? null : roads.GetValueOrDefault(x.SegmentId);
            return new SchedulingSuggestionItem
            {
                Id = x.Id, Kind = kind, State = itemState, CommuneId = x.CommuneId, SegmentId = x.SegmentId,
                SegmentName = road?.SegmentName, SegmentHasGeometry = road?.HasGeometry, PoleId = x.PoleId,
                ExternalRef = pole?.ExternalRef, Note = pole?.Note, FaultId = x.FaultId, FaultType = x.FaultType,
                FaultStatus = x.FaultStatus, Severity = x.Severity, DataSource = x.DataSource, WorkOrderId = x.WorkOrderId,
                WorkOrderTitle = order?.Title, AssignedTo = order?.AssignedTo, WoStatus = order?.WoStatus,
                RecommendedTaskKind = itemState != SchedulingSuggestionState.Ready ? null : kind switch
                {
                    SchedulingSuggestionKind.FaultInspection => TaskKind.Inspection,
                    SchedulingSuggestionKind.FaultRepair => TaskKind.Repair,
                    SchedulingSuggestionKind.MissingGps => TaskKind.PoleGpsUpdate,
                    SchedulingSuggestionKind.SurveyNeeded => TaskKind.Survey,
                    _ => null,
                },
                BlockingReason = x.BlockingReason, ObservedAt = x.ObservedAt,
                ScheduledDate = order?.ScheduledDate, DueDate = order?.DueDate,
            };
        }).ToArray();
        await transaction.CommitAsync(ct);
        return PagedResult<SchedulingSuggestionItem>.From(page, total, items);
    }

    private sealed record SuggestionFilter(IReadOnlyList<string>? Communes, string? Segment, DataSource[] Sources);

    private SuggestionFilter Filter(string[]? communes, string? segment, string? sources)
    {
        var scope = CommuneFilter.Narrow(db.CurrentCommuneScope, communes);
        var parsed = WireEnum.ParseCsv<DataSource>(sources, "data_source");
        if (sources is not null && parsed is not { Count: > 0 }) throw OptionalJson.Invalid("data_source");
        return new(scope, string.IsNullOrWhiteSpace(segment) ? null : segment.Trim(), parsed?.Distinct().ToArray() ?? [DataSource.Field]);
    }

    private IQueryable<Candidate> Candidates(SchedulingSuggestionKind kind, SuggestionFilter filter, DateOnly night)
    {
        IQueryable<Candidate> query = kind switch
        {
            SchedulingSuggestionKind.FaultInspection => Faults(TaskKind.Inspection, filter.Sources),
            SchedulingSuggestionKind.FaultRepair => Faults(TaskKind.Repair, filter.Sources),
            SchedulingSuggestionKind.MissingGps => Poles(false, filter.Sources),
            SchedulingSuggestionKind.SurveyNeeded => Poles(true, filter.Sources),
            SchedulingSuggestionKind.Overdue => Overdue(night, filter.Sources, filter.Segment),
            _ => throw OptionalJson.Invalid("kind"),
        };
        if (filter.Communes is { } communes) query = query.Where(x => communes.Contains(x.CommuneId));
        if (kind != SchedulingSuggestionKind.Overdue && filter.Segment is { } segment) query = query.Where(x => x.SegmentId == segment);
        return query;
    }

    private IQueryable<Candidate> Faults(TaskKind kind, DataSource[] sources)
    {
        var statuses = FaultStatusSets.Open.Where(x => WorkOrderRules.Eligible(kind, x)
            && (kind != TaskKind.Inspection || x == FaultStatus.Detected)).ToArray();
        return db.Set<Fault>().AsNoTracking().Where(x => sources.Contains(x.DataSource) && statuses.Contains(x.FaultStatus))
            .Select(x => new Candidate
            {
                Id = x.FaultId, CommuneId = x.CommuneId, SegmentId = x.SegmentId, PoleId = x.PoleId,
                FaultId = x.FaultId, FaultType = x.OverrideFaultType ?? x.FaultType, FaultStatus = x.FaultStatus,
                Severity = x.Severity, SeverityRank = x.Severity == Severity.Critical ? 4
                    : x.Severity == Severity.High ? 3 : x.Severity == Severity.Medium ? 2 : 1,
                DataSource = x.DataSource, CreatedAt = x.CreatedAt, ObservedAt = x.DetectedAt,
                WorkOrderId = db.Set<WorkOrderFault>().Where(l => l.FaultId == x.FaultId && l.ReleasedAt == null)
                    .Select(l => l.WorkOrderId).FirstOrDefault(),
                BlockingReason = null, DueDate = null,
            });
    }

    private IQueryable<Candidate> Poles(bool survey, DataSource[] sources)
    {
        var query = db.Set<Pole>().AsNoTracking().Where(p => sources.Contains(p.DataSource));
        query = survey ? query.Where(p => p.Geom != null && !db.Set<PoleCurrentStatus>()
                .Any(s => s.PoleId == p.PoleId && s.FixtureStatus != FixtureStatus.Unknown))
            : query.Where(p => p.Geom == null);
        return query.Select(p => new Candidate
        {
            Id = p.PoleId, CommuneId = p.CommuneId, SegmentId = p.SegmentId, PoleId = p.PoleId,
            DataSource = p.DataSource, CreatedAt = p.CreatedAt,
            FaultId = null, FaultType = null, FaultStatus = null, Severity = null, SeverityRank = 0,
            DueDate = null,
            ObservedAt = db.Set<PoleCurrentStatus>().Where(s => s.PoleId == p.PoleId).Select(s => s.LastSeenAt).FirstOrDefault(),
            WorkOrderId = survey ? db.Set<WorkOrder>().Where(w => w.TaskKind == TaskKind.Survey && w.CommuneId == p.CommuneId
                    && w.WoStatus != WorkOrderStatus.Verified && w.WoStatus != WorkOrderStatus.Cancelled
                    && db.Set<WorkOrderSegment>().Any(s => s.WorkOrderId == w.WorkOrderId && s.SegmentId == p.SegmentId))
                .OrderBy(w => w.CreatedAt).ThenBy(w => w.WorkOrderId.Length).ThenBy(w => w.WorkOrderId).Select(w => w.WorkOrderId).FirstOrDefault()
                : db.Set<WorkOrderPoleTarget>().Where(t => t.PoleId == p.PoleId && t.ReleasedAt == null).Select(t => t.WorkOrderId).FirstOrDefault(),
            BlockingReason = p.SegmentId == null ? "POLE_SEGMENT_REQUIRED"
                : !db.Set<RoadSegment>().Any(s => s.SegmentId == p.SegmentId) ? "SEGMENT_NOT_ACCESSIBLE"
                : !survey && !db.Set<RoadSegment>().Any(s => s.SegmentId == p.SegmentId && s.CommuneId == p.CommuneId) ? "CROSS_COMMUNE_REFERENCE" : null,
        });
    }

    private IQueryable<Candidate> Overdue(DateOnly night, DataSource[] sources, string? segment)
    {
        var faults = db.Set<WorkOrderFault>();
        var roads = db.Set<WorkOrderSegment>();
        var poles = db.Set<Pole>();
        return db.Set<WorkOrder>().AsNoTracking().Where(WorkOrderOverdueJob.OverdueOn(night))
            .Where(w => segment == null || w.SegmentId == segment || roads.Any(s => s.WorkOrderId == w.WorkOrderId && s.SegmentId == segment)
                || faults.Any(l => l.WorkOrderId == w.WorkOrderId && db.Set<Fault>().Any(f => f.FaultId == l.FaultId && f.SegmentId == segment))
                || db.Set<WorkOrderPoleTarget>().Any(t => t.WorkOrderId == w.WorkOrderId
                    && poles.Any(p => p.PoleId == t.PoleId && p.SegmentId == segment)))
            .Where(w => faults.Any(l => l.WorkOrderId == w.WorkOrderId
                    && db.Set<Fault>().Any(f => f.FaultId == l.FaultId && sources.Contains(f.DataSource)))
                || w.TaskKind == TaskKind.PoleGpsUpdate && db.Set<WorkOrderPoleTarget>().Any(t => t.WorkOrderId == w.WorkOrderId
                    && poles.Any(p => p.PoleId == t.PoleId && sources.Contains(p.DataSource)))
                || (w.TaskKind == TaskKind.Survey || w.TaskKind == TaskKind.Inspection && !faults.Any(l => l.WorkOrderId == w.WorkOrderId))
                    && (poles.Any(p => sources.Contains(p.DataSource) && (w.SegmentId != null && p.SegmentId == w.SegmentId
                            || roads.Any(s => s.WorkOrderId == w.WorkOrderId && s.SegmentId == p.SegmentId)))
                        || db.Set<RoadSegment>().Any(r => sources.Contains(r.DataSource)
                            && !poles.Any(p => p.SegmentId == r.SegmentId)
                            && (r.SegmentId == w.SegmentId || roads.Any(s => s.WorkOrderId == w.WorkOrderId && s.SegmentId == r.SegmentId)))))
            .Select(w => new Candidate
            {
                Id = w.WorkOrderId, CommuneId = w.CommuneId,
                SegmentId = segment ?? w.SegmentId ?? roads.Where(s => s.WorkOrderId == w.WorkOrderId).OrderBy(s => s.Position)
                    .Select(s => s.SegmentId).FirstOrDefault(),
                WorkOrderId = w.WorkOrderId, PoleId = null, FaultId = null, FaultType = null, FaultStatus = null,
                Severity = null, SeverityRank = 0, DataSource = null, BlockingReason = null, ObservedAt = null,
                CreatedAt = w.CreatedAt, DueDate = w.DueDate,
            });
    }

    private sealed class Candidate
    {
        public required string Id { get; init; }
        public required string CommuneId { get; init; }
        public string? SegmentId { get; init; }
        public string? PoleId { get; init; }
        public string? FaultId { get; init; }
        public FaultType? FaultType { get; init; }
        public FaultStatus? FaultStatus { get; init; }
        public Severity? Severity { get; init; }
        public int SeverityRank { get; init; }
        public DataSource? DataSource { get; init; }
        public string? WorkOrderId { get; init; }
        public string? BlockingReason { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? ObservedAt { get; init; }
        public DateOnly? DueDate { get; init; }
    }
}
