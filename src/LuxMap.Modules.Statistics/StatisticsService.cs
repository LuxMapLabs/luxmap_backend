using System.Net;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Assets.Warranty;
using LuxMap.Modules.WorkOrders;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Contracts.Warranty;
using LuxMap.Shared.Csv;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Statistics;

public sealed record FixtureStatusQuery(
    IReadOnlyList<StatisticsDimension> GroupBy,
    IReadOnlyList<string>? CommuneIds,
    string? SegmentId,
    IReadOnlyList<DataSource>? DataSources);

public sealed record WarrantyQuery(
    IReadOnlyList<WarrantyStatus>? Statuses,
    IReadOnlyList<string>? CommuneIds,
    string? SegmentId,
    IReadOnlyList<DataSource>? DataSources,
    int? ExpiringWithinDays);

public sealed record RepairTimelinessQuery(
    IReadOnlyList<StatisticsDimension> GroupBy,
    IReadOnlyList<string>? CommuneIds,
    DateOnly? From,
    DateOnly? To);

/// <summary>
/// BE-28. Every query starts from a commune-scoped root (<c>pole</c>, <c>work_order</c>), so the BE-08 query
/// filter limits the figures to the caller's communes without anything here remembering to.
/// </summary>
public sealed class StatisticsService(LuxMapDbContext db, WorkOrderAgendaOptions nights, TimeProvider clock, WarrantyCalendar warranty)
{
    /// <summary>Default window of <c>repair-timeliness</c>: the last 30 nights, tonight included.</summary>
    public const int DefaultNights = 30;

    /// <summary>
    /// Bounds a <c>from</c>/<c>to</c> must sit in. Not a business rule: <c>DateOnly</c> arithmetic on the edges of its
    /// range throws, and an ISO date such as 9999-12-31 binds fine — without this it is a 500, not a 400.
    /// </summary>
    public static readonly DateOnly EarliestNight = new(2000, 1, 1), LatestNight = new(2099, 12, 31);

    public async Task<FixtureStatusStatistics> FixtureStatusAsync(FixtureStatusQuery query, CancellationToken ct)
    {
        var byCommune = query.GroupBy.Contains(StatisticsDimension.Commune);
        var bySegment = query.GroupBy.Contains(StatisticsDimension.Segment);

        var poles = Poles(query.CommuneIds, query.SegmentId, query.DataSources);

        // No status row means no sweep ever covered the pole; the map shows it as unknown (drift ST-2).
        var groups = await (
                from p in poles
                join s in db.Set<PoleCurrentStatus>() on p.PoleId equals s.PoleId into statuses
                from s in statuses.DefaultIfEmpty()
                select new
                {
                    p.DataSource,
                    CommuneId = byCommune ? p.CommuneId : null,
                    SegmentId = bySegment ? p.SegmentId : null,
                    Status = (FixtureStatus?)s.FixtureStatus,
                })
            .GroupBy(x => new { x.DataSource, x.CommuneId, x.SegmentId })
            .Select(g => new FixtureStatusRow
            {
                DataSource = g.Key.DataSource,
                CommuneId = g.Key.CommuneId,
                SegmentId = g.Key.SegmentId,
                PoleCount = g.Count(),
                Normal = g.Count(x => x.Status == FixtureStatus.Normal),
                Dim = g.Count(x => x.Status == FixtureStatus.Dim),
                Out = g.Count(x => x.Status == FixtureStatus.Out),
                Unknown = g.Count(x => x.Status == null || x.Status == FixtureStatus.Unknown),
                NeverSurveyed = g.Count(x => x.Status == null),
            })
            .ToListAsync(ct);

        return new FixtureStatusStatistics
        {
            AsOf = clock.GetUtcNow().UtcDateTime,
            GroupBy = Dimensions(query.GroupBy, withDataSource: true),
            // Few rows, sorted here: data_source is stored as text, so ORDER BY in SQL would be alphabetical.
            Rows = groups
                .OrderBy(r => r.DataSource)
                .ThenBy(r => r.CommuneId?.Length).ThenBy(r => r.CommuneId, StringComparer.Ordinal)
                .ThenBy(r => r.SegmentId?.Length).ThenBy(r => r.SegmentId, StringComparer.Ordinal)
                .ToArray(),
        };
    }

    public async Task<RepairTimelinessStatistics> RepairTimelinessAsync(RepairTimelinessQuery query, CancellationToken ct)
    {
        if (query.GroupBy.Contains(StatisticsDimension.Segment))
            throw Invalid("group_by", "repair-timeliness groups by commune only.");

        var now = clock.GetUtcNow();
        var tonight = nights.NightOf(now);
        foreach (var (field, night) in new[] { ("from", query.From), ("to", query.To) })
            if (night is { } given && (given < EarliestNight || given > LatestNight))
                throw Invalid(field, $"{field} must be between {EarliestNight:yyyy-MM-dd} and {LatestNight:yyyy-MM-dd}.");
        var to = query.To ?? tonight;
        var from = query.From ?? to.AddDays(-(DefaultNights - 1));
        if (from > to) throw Invalid("from", "from must not be after to.");

        var byCommune = query.GroupBy.Contains(StatisticsDimension.Commune);
        var repairs = db.Set<WorkOrder>().Where(w => w.TaskKind == TaskKind.Repair);
        if (query.CommuneIds is { } communes) repairs = repairs.Where(w => communes.Contains(w.CommuneId));

        // Nights are a local-time notion, so the window is turned into UTC bounds here and the per-order
        // night is worked out in memory — through the SAME NightOf the agenda uses (drift ST-5).
        var start = nights.NightStartUtc(from);
        var end = nights.NightStartUtc(to.AddDays(1));
        var finished = await repairs
            .Where(w => (w.WoStatus == WorkOrderStatus.Done || w.WoStatus == WorkOrderStatus.Verified)
                && w.CompletedAt >= start && w.CompletedAt < end)
            .Select(w => new { w.CommuneId, w.DueDate, CompletedAt = w.CompletedAt!.Value })
            .ToListAsync(ct);

        var overdue = await repairs
            .Where(w => (w.WoStatus == WorkOrderStatus.Open || w.WoStatus == WorkOrderStatus.Assigned
                    || w.WoStatus == WorkOrderStatus.InProgress)
                && w.DueDate < tonight)
            .GroupBy(w => byCommune ? w.CommuneId : null)
            .Select(g => new { CommuneId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var completed = finished
            .Select(w => new
            {
                CommuneId = byCommune ? w.CommuneId : null,
                Verdict = w.DueDate is not { } due ? (bool?)null
                    : nights.NightOf(new DateTimeOffset(DateTime.SpecifyKind(w.CompletedAt, DateTimeKind.Utc))) <= due,
            })
            .ToList();

        var keys = completed.Select(x => x.CommuneId).Concat(overdue.Select(x => x.CommuneId)).Distinct();
        var rows = keys.Select(key =>
        {
            var mine = completed.Where(x => x.CommuneId == key).ToArray();
            int onTime = mine.Count(x => x.Verdict == true), late = mine.Count(x => x.Verdict == false);
            return new RepairTimelinessRow
            {
                CommuneId = key,
                Completed = mine.Length,
                OnTime = onTime,
                Late = late,
                NoDueDate = mine.Count(x => x.Verdict is null),
                OnTimeRate = onTime + late == 0 ? null : Math.Round((double)onTime / (onTime + late), 4, MidpointRounding.AwayFromZero),
                OpenOverdue = overdue.Where(x => x.CommuneId == key).Sum(x => x.Count),
            };
        });

        return new RepairTimelinessStatistics
        {
            From = from,
            To = to,
            AsOf = now.UtcDateTime,
            GroupBy = Dimensions(query.GroupBy, withDataSource: false),
            Rows = rows.OrderBy(r => r.CommuneId?.Length).ThenBy(r => r.CommuneId, StringComparer.Ordinal).ToArray(),
        };
    }

    /// <summary>
    /// BE-31 — poles and the warranty of their lamp in service. A pole with no lamp in service is listed too, as
    /// <c>unknown</c>: "nothing on record" must show up, not drop out of the list.
    /// </summary>
    /// <param name="page"><c>null</c> = every matching row, for the CSV export (capped by <see cref="CsvExport.MaxRows"/>).</param>
    public async Task<WarrantyList> WarrantyAsync(WarrantyQuery query, PageRequest? page, CancellationToken ct)
    {
        if (query.ExpiringWithinDays is { } days && days is < 0 or > WarrantyWindow.MaxExpiringWithinDays)
            throw Invalid("within_days", $"within_days must be between 0 and {WarrantyWindow.MaxExpiringWithinDays}.");
        var window = warranty.Window(query.ExpiringWithinDays);

        var rows =
            from p in Poles(query.CommuneIds, query.SegmentId, query.DataSources)
            join f in db.Set<Fixture>().Where(f => f.RemovedDate == null) on p.PoleId equals f.PoleId into lamps
            from f in lamps.DefaultIfEmpty()
            select new
            {
                p.PoleId, p.SegmentId, p.CommuneId, p.DataSource, p.CreatedAt,
                FixtureId = (string?)f.FixtureId,
                FixtureType = (FixtureType?)f.FixtureType,
                LampWatt = (int?)f.LampWatt,
                LampCount = (int?)f.LampCount,
                InstallDate = (DateOnly?)f.InstallDate,
                Expiry = (DateOnly?)f.WarrantyExpiry,
            };

        // The status filter in SQL, on the window's OWN bounds — never on dates worked out here. Its tests check every
        // returned row against WarrantyWindow.Classify, the one definition (drift W-2).
        if (query.Statuses is { Count: > 0 } statuses)
        {
            DateOnly today = window.Today, last = window.LastExpiringDay;
            bool unknown = statuses.Contains(WarrantyStatus.Unknown), expired = statuses.Contains(WarrantyStatus.Expired),
                expiring = statuses.Contains(WarrantyStatus.Expiring), active = statuses.Contains(WarrantyStatus.Active);
            rows = rows.Where(r => (unknown && r.Expiry == null) || (expired && r.Expiry < today)
                || (expiring && r.Expiry >= today && r.Expiry <= last) || (active && r.Expiry > last));
        }

        var total = await rows.CountAsync(ct);
        if (page is null) CsvExport.EnsureWithinLimit(total);

        // Soonest expiry first, unknown last; ties by the ID rule of section 0 (created_at, length(id), id).
        var ordered = rows.OrderBy(r => r.Expiry == null).ThenBy(r => r.Expiry)
            .ThenBy(r => r.CreatedAt).ThenBy(r => r.PoleId.Length).ThenBy(r => r.PoleId);
        var found = await (page is null ? ordered : ordered.Skip(page.Skip).Take(page.PageSize)).ToListAsync(ct);

        return new WarrantyList
        {
            Today = window.Today,
            ExpiringWithinDays = window.ExpiringWithinDays,
            Page = page?.Page ?? PageRequest.FirstPage,
            PageSize = page?.PageSize ?? found.Count,
            Total = total,
            Items = [.. found.Select(r => new WarrantyItem
            {
                PoleId = r.PoleId, SegmentId = r.SegmentId, CommuneId = r.CommuneId, DataSource = r.DataSource,
                FixtureId = r.FixtureId, FixtureType = r.FixtureType, LampWatt = r.LampWatt, LampCount = r.LampCount, InstallDate = r.InstallDate,
                WarrantyExpiry = r.Expiry,
                WarrantyStatus = window.Classify(r.Expiry),
                DaysRemaining = window.DaysRemaining(r.Expiry),
            })],
        };
    }

    /// <summary>The poles a pole statistic counts. Contract 1.6, as on the map: the calibration rig only when asked for by name.</summary>
    private IQueryable<Pole> Poles(IReadOnlyList<string>? communes, string? segmentId, IReadOnlyList<DataSource>? dataSources)
    {
        var poles = db.Set<Pole>().AsQueryable();
        poles = dataSources is { Count: > 0 } sources
            ? poles.Where(p => sources.Contains(p.DataSource))
            : poles.Where(p => p.DataSource != DataSource.CalibrationRig);
        if (communes is not null) poles = poles.Where(p => communes.Contains(p.CommuneId));
        if (segmentId is not null) poles = poles.Where(p => p.SegmentId == segmentId);
        return poles;
    }

    private static string[] Dimensions(IReadOnlyList<StatisticsDimension> groupBy, bool withDataSource)
        => (withDataSource ? ["data_source"] : Array.Empty<string>())
            .Concat(Enum.GetValues<StatisticsDimension>().Where(groupBy.Contains)
                .Select(d => d == StatisticsDimension.Commune ? "commune_id" : "segment_id"))
            .ToArray();

    private static LuxMapException Invalid(string field, string message)
        => new(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, message,
            new Dictionary<string, object?> { ["field"] = field });
}
