using System.Net;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.IOT.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.IOT;

public sealed record IotDiagnosticHistoryItem(
    long HistoryId, string FeederId, string NodeId, string Code, string Status,
    DateTime StartedAt, DateTime EndedAt, DateTime? ClosedAt, IotAnomaly Anomaly);

public sealed partial class IotDiagnosticsService
{
    private async Task RequireDiagnosticFeederAsync(string feederId, CancellationToken ct)
    {
        if (!await db.Set<Feeder>().AnyAsync(f => f.FeederId == feederId, ct))
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Không tìm thấy mạch điện.");
    }

    public async Task<IotDiagnosticResult> FeederDiagnosticsAsync(string feederId, CancellationToken ct)
    {
        await RequireDiagnosticFeederAsync(feederId, ct);
        var (_, links) = await sources.LinksAsync(db, [feederId], ct);
        if (!links.TryGetValue(feederId, out var link))
            throw Unavailable("Mạch điện chưa được gán nguồn chẩn đoán IoT.");
        var result = await GetAsync(link.NodeId, ct);
        if (result.FeederId != feederId)
            throw Unavailable("Nguồn chẩn đoán của mạch điện vừa thay đổi; vui lòng thử lại.");
        return result;
    }

    public async Task<PagedResult<IotDiagnosticHistoryItem>> DiagnosticHistoryAsync(
        string feederId, PageRequest page, DateTime? from, DateTime? to, CancellationToken ct)
    {
        await RequireDiagnosticFeederAsync(feederId, ct);
        var start = from.HasValue ? UtcNormalization.ToUtc(from.Value) : (DateTime?)null;
        var end = to.HasValue ? UtcNormalization.ToUtc(to.Value) : (DateTime?)null;
        if (start.HasValue && end.HasValue && start >= end)
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "from phải nhỏ hơn to.", new Dictionary<string, object?> { ["field"] = "to" });
        // A single snapshot keeps total/page consistent while the worker extends episodes.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var query = db.Set<IotDiagnosticEpisode>().AsNoTracking().Where(e => e.FeederId == feederId);
        if (start.HasValue) query = query.Where(e => e.EndedAt >= start.Value);
        if (end.HasValue) query = query.Where(e => e.StartedAt < end.Value);
        var total = await query.CountAsync(ct);
        var offset = ((long)page.Page - 1) * page.PageSize;
        var rows = offset >= total ? [] : await query.OrderByDescending(e => e.StartedAt).ThenByDescending(e => e.Id)
            .Skip((int)offset).Take(page.PageSize).ToListAsync(ct);
        var items = rows.Select(e => new IotDiagnosticHistoryItem(e.Id, e.FeederId, e.NodeId, e.Code,
            e.Status, e.StartedAt, e.EndedAt, e.ClosedAt,
            JsonSerializer.Deserialize<IotAnomaly>(e.Anomaly, LuxMapJsonOptions.Default)!)).ToArray();
        await transaction.CommitAsync(ct);
        return PagedResult<IotDiagnosticHistoryItem>.From(page, total, items);
    }
}

internal static class IotDiagnosticHistory
{
    private static DateTime Bucket(DateTime now) => new(
        now.Ticks - now.Ticks % (5 * TimeSpan.TicksPerSecond), DateTimeKind.Utc);
    internal static async Task InterruptNodeAsync(LuxMapDbContext db, string nodeId, DateTime now, CancellationToken ct)
    {
        var bucket = Bucket(now);
        foreach (var row in await db.Set<IotDiagnosticEpisode>().Where(e => e.NodeId == nodeId && e.ClosedAt == null).ToListAsync(ct))
            Close(row, bucket < row.EndedAt ? row.EndedAt : bucket, "interrupted");
    }

    internal static async Task ObserveAsync(LuxMapDbContext db, IotDiagnosticState state,
        NodeProfile? profile, IotDiagnosticResult? result, DateTime now, CancellationToken ct)
    {
        if (result is null || profile is null) return; // unavailable is not recovery
        var bucket = Bucket(now);
        if (state.HistorySampledAt >= bucket) return;
        state.HistorySampledAt = bucket;
        var rows = await db.Set<IotDiagnosticEpisode>()
            .Where(e => (e.NodeId == state.NodeId || e.FeederId == profile.FeederId) && e.ClosedAt == null).ToListAsync(ct);
        var active = result.Anomalies.ToDictionary(a => a.Code, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var changedSource = row.FeederId != profile.FeederId || row.NodeId != state.NodeId;
            var gap = bucket - row.EndedAt > TimeSpan.FromMinutes(1);
            active.TryGetValue(row.Code, out var anomaly);
            var signature = anomaly is null ? null : Signature(anomaly, profile);
            if (changedSource || gap || (signature is not null && signature != row.Signature))
                Close(row, bucket, "interrupted");
            else if (anomaly is not null)
            {
                row.EndedAt = bucket;
                row.Anomaly = JsonSerializer.Serialize(anomaly, LuxMapJsonOptions.Default);
                active.Remove(row.Code);
            }
            else if (CanRecover(result, row.Code))
                Close(row, bucket, "resolved");
        }
        // Save closures before inserts to satisfy the unique active node/code index.
        await db.SaveChangesAsync(ct);
        foreach (var anomaly in active.Values)
        {
            // An unobservable existing episode must not get a duplicate.
            if (rows.Any(e => e.Code == anomaly.Code && e.ClosedAt is null)) continue;
            db.Add(new IotDiagnosticEpisode {
                CommuneId = state.CommuneId, FeederId = profile.FeederId, NodeId = state.NodeId,
                Code = anomaly.Code, Signature = Signature(anomaly, profile),
                Anomaly = JsonSerializer.Serialize(anomaly, LuxMapJsonOptions.Default),
                StartedAt = bucket, EndedAt = bucket,
            });
        }
    }

    private static string Signature(IotAnomaly a, NodeProfile profile) =>
        a.Code + "/" + JsonSerializer.Serialize(profile, LuxMapJsonOptions.Default);

    private static void Close(IotDiagnosticEpisode row, DateTime bucket, string status)
    {
        row.ClosedAt = bucket;
        row.Status = status;
    }

    private static bool CanRecover(IotDiagnosticResult result, string code)
    {
        if (code == "telemetry_stale")
            return result.ElectricalStatus == "fresh" && result.ConnectionStatus == "online";
        if (code.StartsWith("voltage_", StringComparison.Ordinal) || code == "current_drop")
            return result.Measurement?.LampCommand == "ON" && result.ElectricalStatus == "fresh" &&
                !result.Anomalies.Any(a => a.Code.StartsWith("ina226_", StringComparison.Ordinal)) &&
                result.ElectricalWindow?.Status == "evaluated" &&
                (code != "current_drop" || result.ElectricalWindow.CurrentEvaluationAvailable);
        return result.SensorHealth.Status == "fresh";
    }
}
