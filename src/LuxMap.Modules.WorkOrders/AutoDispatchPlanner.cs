using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.WorkOrders;

public sealed class AutoDispatchOptions
{
    public const string SectionName = "WorkOrders:AutoDispatch";
    public int LeadDays { get; init; } = 2;
    public int QuotaPerDay { get; init; } = 2;
    public int MaxPolesPerOrder { get; init; } = 8;
    public void Validate()
    {
        if (LeadDays <= 0 || QuotaPerDay <= 0 || MaxPolesPerOrder <= 0)
            throw new InvalidOperationException($"{SectionName}: all values must be positive.");
    }
}

public sealed record DispatchEngineer(string UserId, string FullName, string CommuneId);
public sealed record EngineerLoad(string UserId, DateOnly Date);
/// <param name="CabinetId">IoT faults have no road: they group by the cabinet the device sits in instead.</param>
public sealed record DispatchFault(string FaultId, string CommuneId, string? SegmentId, string SegmentName,
    string? PoleId, double Along, Severity Severity, double? PriorityScore, DateTime DetectedAt,
    string? CabinetId = null, string? CabinetName = null);
public sealed record DispatchSkipped(string FaultId, string Reason);
public sealed record AutoDispatchPreview(DateOnly StartDate, int FaultCount, int OrderCount,
    DispatchProposal[] Proposals, DispatchSkipped[] Skipped);
public sealed record DispatchProposal(TaskKind TaskKind, string Title, DateOnly ScheduledDate, DateOnly DueDate,
    string? AssignedTo, string[] FaultIds, string Note, string CommuneId, string? SegmentId, string? SegmentName,
    string? AssignedToName, string[] PoleIds, int PoleCount, Severity MaxSeverity, string? UnassignedReason,
    string? CabinetId);

/// <summary>Reusable per-run load board; selecting reserves the slot for later selections in this run.</summary>
public sealed class EngineerLoadBoard(IEnumerable<EngineerLoad> existing, int quotaPerDay)
{
    public const int SearchDays = 366;
    private readonly Dictionary<(string UserId, DateOnly Date), int> daily = existing
        .GroupBy(x => (x.UserId, x.Date)).ToDictionary(g => g.Key, g => g.Count());

    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    private int Day(string id, DateOnly date) => daily.GetValueOrDefault((id, date));
    private int Week(string id, DateOnly date)
    {
        var monday = WeekStart(date);
        return Enumerable.Range(0, 7).Sum(i => Day(id, monday.AddDays(i)));
    }

    /// <summary>Fixed recurring night: ignore quota and reserve only that date.</summary>
    public DispatchEngineer? SelectOn(IEnumerable<DispatchEngineer> eligible, DateOnly date)
    {
        var engineer = eligible.DistinctBy(e => e.UserId).OrderBy(e => Week(e.UserId, date))
            .ThenBy(e => Day(e.UserId, date)).ThenBy(e => e.UserId.Length)
            .ThenBy(e => e.UserId, StringComparer.Ordinal).FirstOrDefault();
        if (engineer is not null) daily[(engineer.UserId, date)] = Day(engineer.UserId, date) + 1;
        return engineer;
    }

    public (DispatchEngineer? Engineer, DateOnly Date, string? Reason) Select(
        IEnumerable<DispatchEngineer> eligible, DateOnly start)
    {
        var engineers = eligible.DistinctBy(e => e.UserId).ToArray();
        if (engineers.Length == 0) return (null, start, "NO_ELIGIBLE_ENGINEER");
        for (var offset = 0; offset < SearchDays; offset++)
        {
            var date = start.AddDays(offset);
            var engineer = engineers.Where(e => Day(e.UserId, date) < quotaPerDay)
                .OrderBy(e => Week(e.UserId, date)).ThenBy(e => Day(e.UserId, date))
                .ThenBy(e => e.UserId.Length).ThenBy(e => e.UserId, StringComparer.Ordinal).FirstOrDefault();
            if (engineer is null) continue;
            daily[(engineer.UserId, date)] = Day(engineer.UserId, date) + 1;
            return (engineer, date, null);
        }
        return (null, start, "SEARCH_LIMIT_EXCEEDED");
    }
}

public static class AutoDispatchPlanner
{
    public const int CandidateLimit = 2000;
    public static int SeverityRank(Severity severity) => severity switch
    {
        Severity.Critical => 4, Severity.High => 3, Severity.Medium => 2, Severity.Low => 1, _ => 0,
    };

    public static AutoDispatchPreview Plan(DateOnly start, IEnumerable<DispatchFault> candidates,
        IEnumerable<DispatchEngineer> engineers, IEnumerable<EngineerLoad> loads, AutoDispatchOptions options)
    {
        options.Validate();
        var sorted = candidates.OrderByDescending(f => SeverityRank(f.Severity)).ThenBy(f => f.DetectedAt)
            .ThenBy(f => f.FaultId.Length).ThenBy(f => f.FaultId, StringComparer.Ordinal).ToArray();
        var skipped = sorted.Skip(CandidateLimit).Select(f => new DispatchSkipped(f.FaultId, "LIMIT_EXCEEDED")).ToList();
        var selected = sorted.Take(CandidateLimit).ToArray();
        // A road groups first; a fault with no road but a cabinet (IoT) groups by its cabinet; only a fault with neither is skipped.
        skipped.AddRange(selected.Where(f => f.SegmentId is null && f.CabinetId is null)
            .Select(f => new DispatchSkipped(f.FaultId, "NO_SEGMENT")));
        var batches = new List<(DispatchFault[] Faults, int Units, int Index, int Count, DateTime Detected, bool ByCabinet, string Key)>();
        foreach (var group in selected.Where(f => f.SegmentId is not null || f.CabinetId is not null)
                     .GroupBy(f => (f.CommuneId, ByCabinet: f.SegmentId is null, Key: (f.SegmentId ?? f.CabinetId)!)))
        {
            var units = group.GroupBy(f => (IsPole: f.PoleId is not null, Id: f.PoleId ?? f.FaultId))
                .OrderBy(g => !g.Key.IsPole).ThenBy(g => g.Key.IsPole ? g.Min(f => f.Along) : 0d)
                .ThenBy(g => g.Key.Id.Length).ThenBy(g => g.Key.Id, StringComparer.Ordinal)
                .Select(g => g.OrderBy(f => f.FaultId.Length).ThenBy(f => f.FaultId, StringComparer.Ordinal).ToArray()).ToArray();
            var chunks = units.Chunk(options.MaxPolesPerOrder).ToArray();
            for (var i = 0; i < chunks.Length; i++)
                // Detection time is the SEGMENT's earliest, so chunks of one road with equal severity / score keep road order (ca 1 before ca 2).
                batches.Add((chunks[i].SelectMany(x => x).ToArray(), chunks[i].Length, i + 1, chunks.Length, group.Min(f => f.DetectedAt),
                    group.Key.ByCabinet, group.Key.Key));
        }
        var board = new EngineerLoadBoard(loads, options.QuotaPerDay);
        var people = engineers.ToArray();
        var proposals = batches.OrderByDescending(b => b.Faults.Max(f => SeverityRank(f.Severity)))
            .ThenByDescending(b => b.Faults.Max(f => f.PriorityScore)).ThenBy(b => b.Detected)
            .ThenBy(b => b.ByCabinet).ThenBy(b => b.Key.Length).ThenBy(b => b.Key, StringComparer.Ordinal)
            .ThenBy(b => b.Index).ThenBy(b => b.Faults[0].CommuneId.Length).ThenBy(b => b.Faults[0].CommuneId, StringComparer.Ordinal)
            .Select(b =>
            {
                var first = b.Faults[0];
                var slot = board.Select(people.Where(e => e.CommuneId == first.CommuneId), start);
                var title = b.ByCabinet ? $"Kiểm tra tủ {first.CabinetName ?? first.CabinetId} - {b.Units} sự cố IoT"
                    : $"Kiểm tra {b.Units} cột sự cố - {first.SegmentName}";
                return new DispatchProposal(TaskKind.Inspection,
                    title + (b.Count > 1 ? $" (ca {b.Index}/{b.Count})" : ""),
                    slot.Date, slot.Date, slot.Engineer?.UserId, b.Faults.Select(f => f.FaultId).ToArray(), "Phân lịch tự động",
                    first.CommuneId, b.ByCabinet ? null : first.SegmentId, b.ByCabinet ? null : first.SegmentName, slot.Engineer?.FullName,
                    b.Faults.Select(f => f.PoleId).OfType<string>().Distinct().ToArray(), b.Units,
                    b.Faults.MaxBy(f => SeverityRank(f.Severity))!.Severity, slot.Reason, b.ByCabinet ? b.Key : null);
            }).OrderBy(p => p.ScheduledDate).ToArray();
        return new(start, proposals.Sum(p => p.FaultIds.Length), proposals.Length, proposals, skipped.ToArray());
    }
}
