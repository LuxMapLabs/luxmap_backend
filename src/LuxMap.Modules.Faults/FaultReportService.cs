using System.Net;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Notifications;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LuxMap.Modules.Faults;

/// <summary>Body of <c>POST /faults</c> (Contract 5.4, BE-41).</summary>
public sealed class ReportFaultRequest
{
    /// <summary>UUID the phone generated for this report; a resend with the same key returns the same fault.</summary>
    public Guid? ClientOpId { get; init; }

    /// <summary>The pole, when it is in the records. Null for a pole not mapped yet: then <c>location</c> is required.</summary>
    public string? PoleId { get; init; }

    /// <summary>Only when the reporter knows the exact lamp; must be the lamp in use on <c>pole_id</c>.</summary>
    public string? FixtureId { get; init; }

    /// <summary>Both coordinates, or none: a missing one must not be read as 0 and put the fault in the sea.</summary>
    public ReportLocation? Location { get; init; }

    /// <summary>Only without a pole AND with several communes in scope. With a pole the server looks it up.</summary>
    public string? CommuneId { get; init; }

    /// <summary><c>lamp_out</c> or <c>lamp_dim</c>. The cluster and IoT types are produced by engines, never reported.</summary>
    public FaultType? FaultType { get; init; }

    /// <summary>Defaults to <c>medium</c>.</summary>
    public Severity? Severity { get; init; }

    /// <summary>What was seen, at least 10 characters.</summary>
    public string? Note { get; init; }

    /// <summary>
    /// ADDITIVE (drift BE-41): when the engineer saw it. A report queued offline reaches the server later than it
    /// was made; without this the receipt time would pass for the observation time. Defaults to now.
    /// </summary>
    public DateTime? DetectedAt { get; init; }

    /// <summary>
    /// REMOVED (drift EV-2, decided 04/10/2026): photos are attached after the report, with
    /// <c>POST /faults/{id}/photos</c>. Sending it is a 400 rather than a silently ignored field.
    /// </summary>
    public string? PhotoFrameId { get; init; }
}

/// <summary><c>location</c> of a report. Nullable members so an omitted coordinate is seen as missing, not as 0.</summary>
public sealed record ReportLocation(double? Lat, double? Lng);

/// <summary>
/// A field engineer reports a fault seen on site (BE-41). Every report starts <c>detected</c> with
/// <c>source_channel = field_report</c> and waits for a Manager's review (BE-19) — no role can create a fault
/// the system then trusts outright.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b><c>commune_id</c> comes from the pole when there is one</b> (BE-18 rule 2): a client-sent commune
/// next to a pole is a 400, because the write guard checks a commune is IN scope, not that it MATCHES the
/// pole. The pole is read first, so one outside the caller's scope is 404 like one that does not exist.
/// </para>
/// <para>
/// <b><c>data_source</c> is the pole's when there is one, else <c>field</c></b> (drift BE-41). The Contract
/// hard-codes <c>field</c>; a report on a testbed pole would then be counted as field data, exactly what the
/// data_source split exists to prevent.
/// </para>
/// </remarks>
public sealed class FaultReportService(
    LuxMapDbContext db, ICurrentActorAccessor actor, IAuditTrail audit, FaultQueryService query, TimeProvider clock)
{
    /// <summary>A phone clock a little ahead of the server is normal; more than this is a wrong clock.</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    private static readonly FaultType[] Reportable = [Shared.Contracts.Enums.FaultType.LampOut, Shared.Contracts.Enums.FaultType.LampDim];

    private string ActorId => actor.UserId
        ?? throw new LuxMapException("UNAUTHENTICATED", HttpStatusCode.Unauthorized, "Authentication required.");

    private static LuxMapException Invalid(string field, string message)
        => new(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, message,
            new Dictionary<string, object?> { ["field"] = field });

    public async Task<(ReportedFault Fault, bool Created)> ReportAsync(ReportFaultRequest request, CancellationToken ct)
    {
        if (request.ClientOpId is not { } operation || operation == Guid.Empty)
            throw Invalid("client_op_id", "client_op_id is required: a UUID generated on the phone for this report.");
        if (request.PhotoFrameId is not null)
            throw Invalid("photo_frame_id", "photo_frame_id is not accepted: attach photos with POST /faults/{fault_id}/photos.");
        if (request.FaultType is not { } type)
            throw Invalid("fault_type", "fault_type is required.");
        if (!Reportable.Contains(type))
            throw new LuxMapException(ErrorCodes.FaultTypeNotReportable, HttpStatusCode.BadRequest,
                "Only lamp_out and lamp_dim can be reported; cluster and IoT faults come from the engines.");
        var note = request.Note?.Trim();
        if (note is null || note.Length < 10) throw Invalid("note", "note must say what was seen, in at least 10 characters.");
        if (request.Location is { } at && (at.Lat is not { } lat || at.Lng is not { } lng || !Finite(lat, 90) || !Finite(lng, 180)))
            throw Invalid("location", "location must hold both lat (within ±90) and lng (within ±180), finite.");
        // The shared enum converter also reads integers; an undefined number must not reach the CHECK as text.
        if (request.Severity is { } severity && !Enum.IsDefined(severity))
            throw Invalid("severity", "severity must be low, medium, high or critical.");

        var now = UtcMicrosecondClock.UtcNow(clock);
        var detected = now;
        if (request.DetectedAt is { } claimed)
        {
            var utc = UtcNormalization.ToUtc(claimed);
            detected = new DateTime(utc.Ticks / 10 * 10, DateTimeKind.Utc); // timestamptz keeps microseconds
            if (detected > now + FutureTolerance) throw Invalid("detected_at", "detected_at is in the future.");
        }

        var key = operation.ToString("D");
        if (await Replay(key, ct) is { } earlier) return (earlier, false);

        string communeId; string? segmentId = null; DataSource source = DataSource.Field;
        if (request.PoleId is { } poleId)
        {
            if (request.CommuneId is not null)
                throw Invalid("commune_id", "commune_id is looked up from the pole; do not send it with pole_id.");
            // Read the pole FIRST: the commune filter is in this WHERE, so out of scope is simply not found.
            var pole = await db.Set<Pole>().AsNoTracking().SingleOrDefaultAsync(p => p.PoleId == poleId, ct)
                ?? throw new LuxMapException(ErrorCodes.PoleNotFound, HttpStatusCode.NotFound,
                    "That pole does not exist, or it is outside your permitted commune scope.");
            communeId = pole.CommuneId; segmentId = pole.SegmentId; source = pole.DataSource;
            if (request.FixtureId is { } fixtureId && !await db.Set<Fixture>().AnyAsync(
                    f => f.FixtureId == fixtureId && f.PoleId == poleId && f.RemovedDate == null, ct))
                throw Invalid("fixture_id", "fixture_id must be the lamp currently in use on that pole.");
        }
        else
        {
            if (request.Location is null)
                throw new LuxMapException(ErrorCodes.LocationRequired, HttpStatusCode.BadRequest,
                    "A report without pole_id needs location: the fault has to be somewhere on the map.");
            if (request.FixtureId is not null) throw Invalid("fixture_id", "fixture_id needs the pole it is mounted on.");
            communeId = await ReportCommune(request.CommuneId, ct);
        }

        var faultId = await NextFaultId(ct);
        var fault = new Fault
        {
            FaultId = faultId, ClientOpId = key, PoleId = request.PoleId, FixtureId = request.FixtureId,
            SegmentId = segmentId, CommuneId = communeId, Lat = request.Location?.Lat, Lng = request.Location?.Lng,
            FaultType = type, FaultStatus = FaultStatus.Detected, Severity = request.Severity ?? Severity.Medium,
            SourceChannel = SourceChannel.FieldReport, DataSource = source, DetectedAt = detected,
            Note = note, ReportedBy = ActorId, CreatedAt = now, UpdatedAt = now,
        };
        db.Add(fault);
        audit.Record(new(now, AuditActorKind.User, ActorId, actor.Role, communeId, AuditEntityType.Fault, faultId,
            AuditAction.Created, null, new { fault.FaultId, fault.PoleId, fault.FixtureId, fault.FaultType, fault.FaultStatus,
                fault.Severity, fault.SourceChannel, fault.DataSource, fault.DetectedAt, fault.Note, client_op_id = key }));
        Notifier.Stage(db, FaultNotices.Reported(fault), await Notifier.ManagersCoveringAsync(db, [communeId], ct), ActorId, now);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_fault_client_op_id" })
        {
            // A resend raced the first copy past the replay check: answer what the first one created.
            db.ChangeTracker.Clear();
            return (await Replay(key, ct) ?? throw new LuxMapException("IDEMPOTENCY_CONFLICT", HttpStatusCode.Conflict,
                "This client_op_id was already used for a report you cannot see."), false);
        }
        return (await Item(faultId, key, ct), true);
    }

    /// <summary>The fault this reporter already sent with this key; someone else's key is a conflict, not a replay.</summary>
    private async Task<ReportedFault?> Replay(string key, CancellationToken ct)
    {
        var earlier = await db.Set<Fault>().AsNoTracking().IgnoreQueryFilters()
            .Where(f => f.ClientOpId == key).Select(f => new { f.FaultId, f.ReportedBy }).SingleOrDefaultAsync(ct);
        if (earlier is null) return null;
        if (earlier.ReportedBy != ActorId)
            throw new LuxMapException("IDEMPOTENCY_CONFLICT", HttpStatusCode.Conflict, "This client_op_id belongs to another report.");
        return await Item(earlier.FaultId, key, ct);
    }

    /// <summary>Without a pole: the only commune in scope, or the one the reporter picked from several.</summary>
    private async Task<string> ReportCommune(string? requested, CancellationToken ct)
    {
        var scope = db.CurrentCommuneScope;
        if (requested is null)
        {
            if (!scope.IsSystemWide && scope.CommuneIds.Count == 1) return scope.CommuneIds[0];
            throw Invalid("commune_id", "commune_id is required without pole_id when you cover several communes.");
        }
        CommuneFilter.Narrow(scope, [requested]);
        if (!await db.Set<AdministrativeUnit>().AnyAsync(u => u.CommuneId == requested, ct))
            throw Invalid("commune_id", $"'{requested}' is not a known commune.");
        return requested;
    }

    private async Task<string> NextFaultId(CancellationToken ct)
    {
        // The same draw the column DEFAULT makes; the id is needed before saving for the audit event.
        var sql = $"SELECT {PrefixedIds.Fault.DefaultValueSql} AS \"Value\"";
        return await db.Database.SqlQueryRaw<string>(sql).SingleAsync(ct);
    }

    private async Task<ReportedFault> Item(string faultId, string key, CancellationToken ct)
    {
        var item = await query.ItemAsync(faultId, ct)
            ?? throw new LuxMapException("FAULT_NOT_FOUND", HttpStatusCode.NotFound, "FAULT NOT FOUND");
        return new ReportedFault
        {
            FaultId = item.FaultId, PoleId = item.PoleId, FixtureId = item.FixtureId, SegmentId = item.SegmentId,
            Location = item.Location, FaultType = item.FaultType, FaultStatus = item.FaultStatus, Severity = item.Severity,
            SourceChannel = item.SourceChannel, DataSource = item.DataSource, PriorityScore = item.PriorityScore,
            StatusConfidence = item.StatusConfidence, ClusterId = item.ClusterId, DetectedAt = item.DetectedAt,
            UpdatedAt = item.UpdatedAt, WorkOrderId = item.WorkOrderId, Note = item.Note, ReportedBy = item.ReportedBy,
            ReviewNote = item.ReviewNote, AllowedActions = item.AllowedActions, ClientOpId = key,
        };
    }

    private static bool Finite(double value, double limit) => double.IsFinite(value) && Math.Abs(value) <= limit;
}
