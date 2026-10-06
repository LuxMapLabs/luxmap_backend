using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using LuxMap.Modules.Assets.Crud;
using LuxMap.Modules.Faults;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.Survey.LuxReadings;
using LuxMap.Modules.Sync.Entities;
using LuxMap.Modules.WorkOrders;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LuxMap.Modules.Sync;

/// <summary>
/// <c>POST /sync/push</c> — applies a phone's offline queue (BE-43 D-4, D-5, D-7).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Every operation goes through the service of its own endpoint</b> — same validation, same scope, same
/// audit, same notifications. This class only decides ORDER, PERMISSION, REPLAY and how a failure is reported;
/// it never writes a business row itself. A second write path would drift from the first the day either changed.
/// </para>
/// <para>
/// 🔴 <b>Each operation is authorized on its own capability</b>, on top of <c>SyncOffline</c> at the door. One
/// endpoint carrying five kinds of write is exactly where a single door check would quietly open
/// <c>ReportFaults</c> to every role that may sync.
/// </para>
/// <para>
/// Operations run in the order sent, each in its own unit of work: one failing does not undo the ones before it,
/// nor stop the ones after — except a later operation on the SAME work order or pole, which is rejected
/// <c>BLOCKED_BY_EARLIER_OP</c>: a <c>complete</c> after a failed <c>start</c> means nothing.
/// </para>
/// </remarks>
public sealed class SyncPushService(
    LuxMapDbContext db,
    ICurrentActorAccessor actor,
    IAuthorizationService authorization,
    FaultReportService faults,
    LuxReadingService luxReadings,
    AssetCrudService assets,
    WorkOrderService workOrders)
{
    /// <summary>A queue longer than this is split by the phone; one request must stay short enough to finish on 3G.</summary>
    public const int MaxOperations = 100;

    private string ActorId => actor.UserId
        ?? throw new LuxMapException(ErrorCodes.Unauthenticated, HttpStatusCode.Unauthorized, "Authentication required.");

    public async Task<SyncPushResult> PushAsync(SyncPushRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var operations = CheckEnvelope(request);
        var applied = new List<SyncApplied>();
        var conflicts = new List<SyncConflict>();
        var rejected = new List<SyncRejected>();

        // work_order:{id} / pole:{id} → the operation that failed on it earlier in this batch.
        var blocked = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var operation in operations)
        {
            var key = operation.ClientOpId!.Value;
            var wireType = operation.OpType;
            await ResetAsync(ct);

            if (!TryParseType(wireType, out var type))
            {
                rejected.Add(new(key, wireType, Error(OptionalJson.Invalid("op_type"))));
                continue;
            }

            if (!(await authorization.AuthorizeAsync(user, PolicyFor(type))).Succeeded)
            {
                rejected.Add(new(key, wireType, new(ErrorCodes.RoleForbidden,
                    "Your role may not perform this operation.", new Dictionary<string, object?> { ["op_type"] = wireType })));
                continue;
            }

            Parsed parsed;
            try
            {
                parsed = Parse(type, operation);
            }
            catch (LuxMapException error)
            {
                rejected.Add(new(key, wireType, Error(error)));
                continue;
            }

            if (parsed.Target is { } target && blocked.TryGetValue(target, out var blocker))
            {
                rejected.Add(new(key, wireType, new(ErrorCodes.BlockedByEarlierOp,
                    "An earlier operation of this batch on the same item failed, so this one was not attempted.",
                    new Dictionary<string, object?> { ["blocked_by"] = blocker })));
                continue;
            }

            try
            {
                var (id, replayed) = await ApplyAsync(type, key, parsed, ct);
                applied.Add(new(key, wireType!, id, replayed));
                continue;
            }
            catch (LuxMapException error)
            {
                await ResetAsync(ct);

                // A twin push may have applied this very operation in the meantime; then this one is its replay.
                if (await RecordedAsync(key, ct) is { } earlier && earlier.OpType == type && earlier.EntityId == parsed.EntityId)
                {
                    applied.Add(new(key, wireType!, earlier.EntityId, Replayed: true));
                    continue;
                }

                if (IsConflict(error))
                {
                    conflicts.Add(new(key, wireType!, error.Code, error.Message, await ServerStateAsync(type, parsed, ct)));
                }
                else
                {
                    rejected.Add(new(key, wireType, Error(error)));
                }
            }
            catch (DbUpdateException error) when (error.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "pk_sync_operation" })
            {
                // Lost the race on the replay key: the twin is committed and this step rolled back whole. A replay
                // only if the twin was the SAME operation — a different one reusing the key wrote nothing here.
                await ResetAsync(ct);
                if (await RecordedAsync(key, ct) is { } twin && twin.OpType == type && twin.EntityId == parsed.EntityId)
                {
                    applied.Add(new(key, wireType!, twin.EntityId, Replayed: true));
                    continue;
                }

                rejected.Add(new(key, wireType, Error(KeyReused(key))));
            }

            if (parsed.Target is { } failed)
            {
                blocked.TryAdd(failed, key);
            }
        }

        return new SyncPushResult { Applied = applied, Conflicts = conflicts, Rejected = rejected };
    }

    private static IReadOnlyList<SyncOperationRequest> CheckEnvelope(SyncPushRequest request)
    {
        if (request.Operations is not { Count: > 0 and <= MaxOperations } operations || operations.Any(op => op is null))
        {
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                $"operations must hold between 1 and {MaxOperations} operations.",
                new Dictionary<string, object?> { ["field"] = "operations", ["max"] = MaxOperations });
        }

        if (operations.FirstOrDefault(op => op.ClientOpId is not { } id || id == Guid.Empty) is not null)
        {
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "Every operation needs a client_op_id: a UUID generated on the phone.",
                new Dictionary<string, object?> { ["field"] = "client_op_id" });
        }

        if (operations.GroupBy(op => op.ClientOpId).FirstOrDefault(group => group.Count() > 1) is { } twice)
        {
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "A client_op_id appears twice in one batch; each operation needs its own.",
                new Dictionary<string, object?> { ["field"] = "client_op_id", ["client_op_id"] = twice.Key });
        }

        return operations;
    }

    private static bool TryParseType(string? wire, out SyncOpType type)
    {
        foreach (var candidate in Enum.GetValues<SyncOpType>())
        {
            if (WireEnum.Name(candidate) == wire)
            {
                type = candidate;
                return true;
            }
        }

        type = default;
        return false;
    }

    /// <summary>The capability of the operation's own endpoint — the same policy, not a copy of its role list.</summary>
    private static string PolicyFor(SyncOpType type) => type switch
    {
        SyncOpType.FaultReport => LuxMapPolicies.ReportFaults,
        SyncOpType.LuxReading => LuxMapPolicies.RecordLuxReading,
        SyncOpType.PoleNote => LuxMapPolicies.EditPoleNotes,
        SyncOpType.WorkOrderStart or SyncOpType.WorkOrderComplete => LuxMapPolicies.ExecuteWorkOrders,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    // ── Parsing: the payload is the body of the operation's own endpoint ──────────────────────────────────

    /// <param name="EntityId">The pole or work order acted on; <c>null</c> for an operation that creates a row.</param>
    /// <param name="Target">The key a failure blocks later operations on; <c>null</c> when nothing depends on it.</param>
    private sealed record Parsed(object Body, string? EntityId, string? Target);

    private sealed record StartPayload
    {
        public string? WorkOrderId { get; init; }

        public DateTime? PerformedAt { get; init; }
    }

    private sealed record CompletePayload
    {
        public string? WorkOrderId { get; init; }

        public string? ReportNote { get; init; }

        public string? MaterialsUsed { get; init; }

        public JsonElement FaultOutcomes { get; init; }

        public DateTime? PerformedAt { get; init; }
    }

    private sealed record NotePayload(string PoleId, string? Note, ExpectedNote? Expected);

    private static Parsed Parse(SyncOpType type, SyncOperationRequest operation)
    {
        var body = PayloadObject(operation);
        switch (type)
        {
            case SyncOpType.FaultReport:
                return new(Read<ReportFaultRequest>(WithKey(body, operation)), null, null);

            case SyncOpType.LuxReading:
            {
                var reading = Read<CreateLuxReadingRequest>(WithKey(body, operation));
                Validate(reading);
                LuxReadingService.RejectServerOwnedFields(reading);
                return new(reading, null, null);
            }

            case SyncOpType.PoleNote:
            {
                var element = JsonSerializer.SerializeToElement(body);
                var poleId = OptionalJson.Text(element.TryGetProperty("pole_id", out var p) ? p : default, "pole_id");
                if (string.IsNullOrWhiteSpace(poleId)) throw OptionalJson.Invalid("pole_id");
                var (sent, note) = PoleNoteInput.Read(element.TryGetProperty("note", out var n) ? n : default);
                if (!sent) throw OptionalJson.Invalid("note");
                var (seen, text) = PoleNoteInput.Read(element.TryGetProperty("base_note", out var b) ? b : default);
                return new(new NotePayload(poleId, note, seen ? new ExpectedNote(text) : null), poleId, $"pole:{poleId}");
            }

            case SyncOpType.WorkOrderStart:
            {
                var start = Read<StartPayload>(body);
                var id = RequiredId(start.WorkOrderId);
                return new(start, id, $"work_order:{id}");
            }

            case SyncOpType.WorkOrderComplete:
            {
                var complete = Read<CompletePayload>(body);
                var id = RequiredId(complete.WorkOrderId);
                return new(complete, id, $"work_order:{id}");
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }

    private static JsonObject PayloadObject(SyncOperationRequest operation)
        => operation.Payload.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(operation.Payload.GetRawText())!.AsObject()
            : throw OptionalJson.Invalid("payload");

    /// <summary>
    /// The operation's <c>client_op_id</c> IS the report's idempotency key (D-5): one fault whether it arrives by
    /// <c>POST /faults</c>, by the queue, or by both. A payload naming a different key is refused, not overridden.
    /// </summary>
    private static JsonObject WithKey(JsonObject body, SyncOperationRequest operation)
    {
        var key = operation.ClientOpId!.Value.ToString("D");
        if (body.TryGetPropertyValue("client_op_id", out var sent) && sent is not null
            && !(sent.GetValueKind() == JsonValueKind.String && Guid.TryParse(sent.GetValue<string>(), out var given) && given == operation.ClientOpId))
        {
            throw OptionalJson.Invalid("payload.client_op_id");
        }

        body["client_op_id"] = key;
        return body;
    }

    private static T Read<T>(JsonObject body) where T : class
    {
        try
        {
            return body.Deserialize<T>(LuxMapJsonOptions.Default) ?? throw OptionalJson.Invalid("payload");
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "payload does not match the body of this operation's endpoint.",
                new Dictionary<string, object?> { ["field"] = "payload" });
        }
    }

    /// <summary>The DataAnnotations the endpoint's model binding enforces, in the same error shape.</summary>
    private static void Validate(object body)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(body, new ValidationContext(body), results, validateAllProperties: true)) return;

        var fields = results
            .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty), (result, member) => (member, result.ErrorMessage))
            .GroupBy(x => JsonNamingPolicy.SnakeCaseLower.ConvertName(x.member))
            .ToDictionary(group => group.Key, group => (object?)group.Select(x => x.ErrorMessage ?? "Invalid value.").ToArray());
        throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, "The submitted payload is invalid.", fields);
    }

    private static string RequiredId(string? id)
        => string.IsNullOrWhiteSpace(id) ? throw OptionalJson.Invalid("work_order_id") : id;

    // ── Applying ───────────────────────────────────────────────────────────────────────────────────────

    private async Task<(string Id, bool Replayed)> ApplyAsync(SyncOpType type, Guid key, Parsed parsed, CancellationToken ct)
    {
        // Three stores hold keys (fault, lux_reading, sync_operation); a key already spent in ANOTHER store is a
        // reuse, not a new operation. Best effort — the check is not atomic with the write — because each store
        // still applies its own operation at most once: a missed reuse is a client bug unreported, never a double write.
        if (await SpentElsewhereAsync(type, key, ct))
        {
            throw KeyReused(key);
        }

        switch (parsed.Body)
        {
            case ReportFaultRequest report:
            {
                var (fault, created) = await faults.ReportAsync(report, ct);
                return (fault.FaultId, !created);
            }

            case CreateLuxReadingRequest reading:
            {
                var (created, stored) = await luxReadings.CreateAsync(reading, ActorId, ct);
                return (stored.LuxId, !created);
            }
        }

        // The three operations with no key of their own: answer a resend from the record of the first one.
        if (await RecordedAsync(key, ct) is { } earlier)
        {
            return earlier.OpType == type && earlier.EntityId == parsed.EntityId
                ? (earlier.EntityId, true)
                : throw KeyReused(key);
        }

        // Staged BEFORE the service runs, so the service's own SaveChanges writes it with the step — or not at all.
        db.Set<SyncOperation>().Add(new SyncOperation
        {
            UserId = ActorId, ClientOpId = key, OpType = type, EntityId = parsed.EntityId!,
            AppliedAt = UtcMicrosecondClock.UtcNow(),
        });

        switch (parsed.Body)
        {
            case NotePayload note:
                await assets.SetPoleNoteAsync(note.PoleId, note.Note, ct, note.Expected);
                break;
            case StartPayload start:
                await workOrders.Act(parsed.EntityId!, "start", null, default, ct, performedAt: start.PerformedAt);
                break;
            case CompletePayload complete:
                await workOrders.Act(parsed.EntityId!, "complete", complete.ReportNote, complete.FaultOutcomes, ct,
                    complete.MaterialsUsed, complete.PerformedAt);
                break;
        }

        return (parsed.EntityId!, false);
    }

    private static LuxMapException KeyReused(Guid key) => new("IDEMPOTENCY_CONFLICT", HttpStatusCode.Conflict,
        "This client_op_id was already used for a different operation.", new Dictionary<string, object?> { ["client_op_id"] = key });

    private async Task<bool> SpentElsewhereAsync(SyncOpType type, Guid key, CancellationToken ct)
    {
        var text = key.ToString("D");
        var asFault = type != SyncOpType.FaultReport
            && await db.Set<Fault>().IgnoreQueryFilters().AnyAsync(fault => fault.ClientOpId == text, ct);
        var asReading = type != SyncOpType.LuxReading
            && await db.Set<LuxReading>().IgnoreQueryFilters().AnyAsync(reading => reading.ClientOpId == text, ct);
        var asStep = type is SyncOpType.FaultReport or SyncOpType.LuxReading && await RecordedAsync(key, ct) is not null;
        return asFault || asReading || asStep;
    }

    private Task<SyncOperation?> RecordedAsync(Guid key, CancellationToken ct)
        => db.Set<SyncOperation>().AsNoTracking().SingleOrDefaultAsync(op => op.UserId == ActorId && op.ClientOpId == key, ct);

    /// <summary>
    /// A conflict is the server's state disagreeing with the step (409), or the thing no longer being there for this
    /// caller (404). A reused <c>client_op_id</c> is a 409 too, but it is the phone's mistake, not the server's state.
    /// </summary>
    private static bool IsConflict(LuxMapException error)
        => error.StatusCode == HttpStatusCode.NotFound
           || (error.StatusCode == HttpStatusCode.Conflict && error.Code != "IDEMPOTENCY_CONFLICT");

    /// <summary>The thing as the caller may see it NOW — or <c>null</c> when it is gone or out of reach.</summary>
    private async Task<object?> ServerStateAsync(SyncOpType type, Parsed parsed, CancellationToken ct)
    {
        if (parsed.EntityId is not { } id) return null;
        try
        {
            return type switch
            {
                SyncOpType.PoleNote => (await assets.PoleAsync(id, ct)).Pole,
                SyncOpType.WorkOrderStart or SyncOpType.WorkOrderComplete => await workOrders.Detail(id, ct),
                _ => null,
            };
        }
        catch (LuxMapException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Nothing from one operation may leak into the next: an entity a failed step left tracked would be written by
    /// the next step's SaveChanges, and a transaction a step left open would swallow the next one.
    /// </summary>
    private async Task ResetAsync(CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is { } open) await open.RollbackAsync(ct);
        db.ChangeTracker.Clear();
    }

    private static SyncError Error(LuxMapException error) => new(error.Code, error.Message, error.Details);
}
