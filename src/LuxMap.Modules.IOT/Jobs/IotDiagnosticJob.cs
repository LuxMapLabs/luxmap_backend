using System.Net;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.IOT.Entities;
using LuxMap.Modules.Notifications;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Persistence.Jobs;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LuxMap.Modules.IOT.Jobs;

/// <summary>Persist diagnostic evidence independently of dashboard requests.</summary>
public sealed class IotDiagnosticJob(
    JobContextFactory contexts, IServiceScopeFactory scopes, IOptions<IotOptions> options,
    ILogger<IotDiagnosticJob> log) : IScheduledJob
{
    public string Name => "iot_diagnostics";
    public TimeSpan Interval => TimeSpan.FromSeconds(30);

    public async Task RunAsync(DateTime now, string correlationId, CancellationToken ct)
    {
        await using var scan = contexts.Unscoped();
        var nodes = await scan.Set<IotNode>().IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.CabinetId != null)
            .OrderBy(n => n.CreatedAt).ThenBy(n => n.NodeId.Length).ThenBy(n => n.NodeId)
            .Select(n => new { n.NodeId, n.CommuneId }).ToListAsync(ct);
        using var scope = scopes.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IotDiagnosticsService>();
        foreach (var candidate in nodes)
        {
            ct.ThrowIfCancellationRequested();
            await using var db = contexts.Open(candidate.CommuneId);
            try
            {
                var settings = await IotRegistrationStore.ReadAsync(db, options.Value, [candidate.NodeId], ct);
                settings.Nodes.TryGetValue(candidate.NodeId, out var profile);
                IotDiagnosticResult? result = null;
                var status = "unconfigured";
                if (profile is not null)
                {
                    try
                    {
                        result = await reader.ForContext(db).GetAsync(candidate.NodeId, ct);
                        status = result.Status;
                    }
                    catch (LuxMapException e) when (e.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        status = "source_unavailable";
                    }
                }
                await SaveAsync(db, scope.ServiceProvider.GetRequiredService<IActiveWorkOrderLookup>().InContext(db),
                    candidate.NodeId, profile, result, status, now, correlationId, ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                log.LogError(e, "IoT diagnostics failed for {NodeId}", candidate.NodeId);
            }
        }
    }

    internal static async Task SaveAsync(LuxMapDbContext db, IActiveWorkOrderLookup assignments, string nodeId, NodeProfile? profile,
        IotDiagnosticResult? result, string status, DateTime now, string correlationId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Source changes lock the node too. Never hold this lock during HTTP calls.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM iot_node WHERE node_id = {nodeId} FOR UPDATE", ct);
        var node = await db.Set<IotNode>().AsNoTracking().SingleOrDefaultAsync(n => n.NodeId == nodeId, ct);
        if (node?.CabinetId is null) return;
        var source = await IotRegistrationStore.ReadAsync(db, new IotOptions(), [nodeId], ct);
        if (source.Nodes.TryGetValue(nodeId, out var actual) &&
            actual != profile)
            return;
        if (await db.Set<IotSourceRegistration>().AnyAsync(s => s.NodeId == nodeId && s.DetachedAt != null, ct))
            return;
        var state = await db.Set<IotDiagnosticState>().SingleOrDefaultAsync(s => s.NodeId == nodeId, ct);
        if (state is null)
        {
            state = new() { NodeId = nodeId, CommuneId = node.CommuneId };
            db.Add(state);
        }
        var identity = JsonSerializer.Serialize(profile, LuxMapJsonOptions.Default);
        var evidence = JsonSerializer.Deserialize<DiagnosticEvidence>(state.Evidence, LuxMapJsonOptions.Default) ?? new();
        if (evidence.Source != identity) evidence = new() { Source = identity };
        state.FeederId = profile?.FeederId;
        state.DiagnosticStatus = status;
        state.EvaluatedAt = now;
        state.Result = JsonSerializer.Serialize(result, LuxMapJsonOptions.Default);
        if (result is not null && profile is not null)
        {
            var faults = await db.Set<Fault>().Where(f => f.OriginNodeId == nodeId &&
                f.FeederId == profile.FeederId && f.IotAnomalyCode != null &&
                (f.FaultStatus == FaultStatus.Detected || f.FaultStatus == FaultStatus.Confirmed ||
                 f.FaultStatus == FaultStatus.InProgress || f.FaultStatus == FaultStatus.Resolved))
                .OrderBy(f => f.FaultId.Length).ThenBy(f => f.FaultId).ToListAsync(ct);
            foreach (var fault in faults)
            {
                await FaultLocks.LockAsync(db, [fault.FaultId], ct);
                await db.Entry(fault).ReloadAsync(ct);
            }
            foreach (var code in IotDiagnosticRules.Codes)
            {
                if (!IotDiagnosticRules.TryObservation(result, code, profile, now, out var key)) continue;
                var active = result.Anomalies.Any(a => a.Code == code);
                if (!evidence.Codes.TryGetValue(code, out var tracked))
                    evidence.Codes[code] = tracked = new();
                if (!tracked.Observe(key, active, result.MeasurementSequence, now, code, result.LampCount?.EstimatedLit)) continue;
                var fault = faults.FirstOrDefault(f => f.IotAnomalyCode == code);
                if (active && tracked.Ready(code, now) && fault is null && !tracked.Raised)
                {
                    var cabinet = await db.Set<ElectricalCabinet>().AsNoTracking()
                        .SingleAsync(c => c.CabinetId == node.CabinetId, ct);
                    var idSql = $"SELECT {PrefixedIds.Fault.DefaultValueSql} AS \"Value\"";
                    var id = await db.Database.SqlQueryRaw<string>(idSql).SingleAsync(ct);
                    fault = new Fault
                    {
                        FaultId = id, CommuneId = node.CommuneId, OriginNodeId = nodeId,
                        FeederId = profile.FeederId, CabinetId = node.CabinetId, IotAnomalyCode = code,
                        Lat = cabinet.Geom.Y, Lng = cabinet.Geom.X,
                        FaultType = IotDiagnosticRules.TypeOf(code), FaultStatus = FaultStatus.Detected,
                        Severity = code == "voltage_loss" ? Severity.High : Severity.Medium,
                        SourceChannel = SourceChannel.Iot, DataSource = node.DataSource,
                        DetectedAt = tracked.FirstSeen!.Value, CreatedAt = now, UpdatedAt = now,
                        Note = $"IoT {nodeId}, feeder {profile.FeederId}: {code}.",
                        IotEvidence = JsonSerializer.Serialize(new { result, algorithm_version = IotDiagnosticRules.Version,
                            evidence_key = key, missing_lamps = result.LampCount?.EstimatedMissing }, LuxMapJsonOptions.Default),
                    };
                    db.Add(fault);
                    StageAudit(db, fault, null, now, correlationId, AuditAction.Created);
                    await NotifyAsync(db, fault, "Phát hiện sự cố IoT", now, ct);
                    await db.SaveChangesAsync(ct);
                    faults.Add(fault);
                    tracked.Raised = true;
                }
                else if (!active && tracked.HealthySince is { } healthy && now - healthy >= TimeSpan.FromSeconds(90) &&
                         fault is not null && fault.RecoveredAt is null)
                {
                    var before = new { fault.FaultStatus, fault.RecoveredAt, fault.ReviewNote };
                    fault.RecoveredAt = now;
                    fault.UpdatedAt = now;
                    var note = $"Thiết bị đã tự phục hồi lúc {now:yyyy-MM-ddTHH:mm:ss}Z.";
                    // Keep human decisions and work orders intact.
                    var assigned = (await assignments.ActiveWorkOrdersAsync([fault.FaultId], ct)).ContainsKey(fault.FaultId);
                    if (fault.FaultStatus == FaultStatus.Detected && !assigned)
                    {
                        fault.FaultStatus = FaultStatus.Rejected;
                        fault.ConfirmedAt = now;
                        fault.ReviewNote = note;
                    }
                    else fault.Note = string.IsNullOrEmpty(fault.Note) ? note : fault.Note + "\n" + note;
                    StageAudit(db, fault, before, now, correlationId,
                        fault.FaultStatus == FaultStatus.Rejected ? AuditAction.Rejected : AuditAction.Reported);
                    await NotifyAsync(db, fault, "Thiết bị IoT đã phục hồi", now, ct);
                    await db.SaveChangesAsync(ct);
                }
            }
        }
        state.Evidence = JsonSerializer.Serialize(evidence, LuxMapJsonOptions.Default);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static void StageAudit(LuxMapDbContext db, Fault fault, object? before,
        DateTime now, string correlationId, AuditAction action) =>
        JobContextFactory.AuditFor(db, correlationId).Record(new(now, AuditActorKind.Iot, null, null,
            fault.CommuneId, AuditEntityType.Fault, fault.FaultId, action, before,
            new { fault.FaultStatus, fault.RecoveredAt, fault.IotAnomalyCode, fault.OriginNodeId,
                fault.FeederId, fault.CabinetId, fault.IotEvidence, fault.Note, fault.ReviewNote, fault.ConfirmedAt, algorithm_version = IotDiagnosticRules.Version }));

    private static async Task NotifyAsync(LuxMapDbContext db, Fault fault, string title, DateTime now, CancellationToken ct) =>
        Notifier.Stage(db, new(NotificationType.FaultReported, fault.CommuneId, NotificationEntityType.Fault,
            fault.FaultId, title, $"{fault.FaultId}: {fault.IotAnomalyCode}, {fault.OriginNodeId}, {fault.FeederId}."),
            await Notifier.ManagersCoveringAsync(db, [fault.CommuneId], ct), null, now);
}

public sealed class DiagnosticEvidence
{
    public string Source { get; set; } = "";
    public Dictionary<string, DiagnosticCodeEvidence> Codes { get; set; } = [];
}

public sealed class DiagnosticCodeEvidence
{
    public string? Key { get; set; }
    public DateTime? FirstSeen { get; set; }
    public DateTime? LastSeen { get; set; }
    public DateTime? HealthySince { get; set; }
    public int Windows { get; set; }
    public long? WindowSequence { get; set; }
    public string? WindowSession { get; set; }
    public int? WindowLit { get; set; }
    public bool Raised { get; set; }

    public bool Observe(string key, bool active, long? sequence, DateTime now, string code, int? estimatedLit = null)
    {
        if (key == Key) return false;
        if (LastSeen is { } previous && now - previous > TimeSpan.FromSeconds(90))
        {
            FirstSeen = HealthySince = null;
            Windows = 0;
            WindowSequence = null;
        }
        Key = key;
        LastSeen = now;
        if (!active)
        {
            HealthySince ??= now;
            FirstSeen = null;
            Windows = 0;
            WindowSequence = null;
            Raised = false;
            return true;
        }
        HealthySince = null;
        FirstSeen ??= now;
        if (code == "lamp_count_shortfall")
        {
            var session = key.Split('/')[0];
            if (session != WindowSession || estimatedLit != WindowLit)
            {
                Windows = 0;
                WindowSequence = null;
                WindowSession = session;
                WindowLit = estimatedLit;
            }
            if (sequence is { } seq && (WindowSequence is null || seq - WindowSequence >= 25))
            {
                Windows++;
                WindowSequence = seq;
            }
        }
        return true;
    }

    public bool Ready(string code, DateTime now) => code == "lamp_count_shortfall"
        ? Windows >= 2
        : FirstSeen is { } first && now - first >= TimeSpan.FromMinutes(
            code == "telemetry_stale" ? 0 : code.StartsWith("voltage_") ? 3 : 5);
}

public static class IotDiagnosticRules
{
    public const string Version = "iot-faults-v1";
    public static readonly string[] Codes = [
        "telemetry_stale", "voltage_loss", "voltage_deviation", "lamp_count_shortfall",
        "ds3231_unavailable", "ds3231_read_failed", "ds3231_time_invalid",
        "ina226_unavailable", "ina226_read_failed", "ina226_shunt_out_of_range",
        "bh1750_unavailable", "bh1750_read_failed",
        "microsd_unavailable", "microsd_read_failed", "microsd_write_failed",
        "lora_tx_blocked", "lora_rx_frame_error",
    ];

    public static FaultType TypeOf(string code) => code switch
    {
        "telemetry_stale" => FaultType.NodeOffline,
        "voltage_loss" => FaultType.SegmentOutage,
        "voltage_deviation" => FaultType.VoltageAnomaly,
        "lamp_count_shortfall" => FaultType.LampOut,
        _ => FaultType.IotHardwareFault,
    };

    public static bool TryObservation(IotDiagnosticResult result, string code, NodeProfile profile,
        DateTime now, out string key)
    {
        key = "";
        if (code == "telemetry_stale")
        {
            if (result.Measurement is not { } sample) return false;
            // Keep one key per job pass for silence while recovery needs a fresh report.
            key = result.Status == "stale" || result.Anomalies.Any(a => a.Code == code)
                ? $"{result.MeasurementKey}/{now.Ticks}" :
                result.MeasuredAt is { } recentMeasured && recentMeasured <= now &&
                now - recentMeasured <= TimeSpan.FromSeconds(profile.MaxSilenceSeconds) ? result.MeasurementKey ?? "" : "";
            return key.Length > 0;
        }
        if (code.Contains('_') && !code.StartsWith("voltage_") && code != "lamp_count_shortfall")
        {
            key = result.SensorHealthKey ?? "";
            var component = code.Split('_')[0] + "_";
            var changedFailure = !result.Anomalies.Any(a => a.Code == code) &&
                result.Anomalies.Any(a => a.Code.StartsWith(component, StringComparison.Ordinal));
            return result.SensorHealth.Status == "fresh" && key.Length > 0 && !changedFailure;
        }
        key = result.MeasurementKey ?? "";
        if (key.Length == 0 || result.Measurement is not { LampCommand: "ON" } ||
            result.MeasuredAt is not { } measured || measured > now || now - measured > TimeSpan.FromSeconds(profile.MaxSilenceSeconds) ||
            result.Measurement.ReceivedAt > now || now - result.Measurement.ReceivedAt > TimeSpan.FromSeconds(profile.MaxSilenceSeconds) ||
            result.Anomalies.Any(a => a.Code.StartsWith("ina226_"))) return false;
        return code != "lamp_count_shortfall" || result.LampCount?.Status == "estimated";
    }
}
