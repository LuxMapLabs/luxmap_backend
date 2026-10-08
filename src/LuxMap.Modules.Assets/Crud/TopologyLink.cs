using System.Net;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Assets.Crud;

/// <summary>
/// The ONE place the write rule for an electrical relation and its provenance lives (TOPO-INFER TI-2): pole → feeder
/// (<c>feeder_id</c> + <c>feeder_source</c>) and feeder → cabinet (<c>cabinet_id</c> + <c>cabinet_source</c>).
/// </summary>
/// <remarks>
/// <para>
/// Label absent + relation unchanged → <b>kept</b>; relation changed or new → <b><c>inferred</c></b>; relation removed → label
/// removed; a label with no relation, or <c>null</c> while the relation stays, → 400. Nothing becomes <c>verified</c> unless a
/// caller says so. Every write path — form, narrow endpoint, import — goes through <see cref="Resolve"/>.
/// </para>
/// <para>
/// 🔴 <b>Written as a PAIR.</b> EF writes only the columns that differ from its snapshot, so one request verifying feeder F1
/// (label only) and another moving the pole to F2 (id only) would leave <c>(F2, verified)</c> — a pair nobody asserted.
/// <see cref="ApplyPoleFeeder"/> / <see cref="ApplyFeederCabinet"/> mark BOTH columns modified whenever either changed, so
/// the last writer wins the whole pair. Nothing is marked when neither changed, so re-importing an identical file still
/// stamps nothing (drift N-4).
/// </para>
/// </remarks>
public static class TopologyLink
{
    /// <summary>A label as a request carried it: whether the key was sent, and its value (<c>null</c> = sent as null).</summary>
    public readonly record struct Label(bool Sent, TopologySource? Value)
    {
        public static Label Absent => new(false, null);
    }

    /// <summary>Reads an optional JSON label. Wrong type or unknown string → 400 naming <paramref name="field"/>.</summary>
    public static Label Read(JsonElement element, string field) => element.ValueKind switch
    {
        JsonValueKind.Undefined => Label.Absent,
        JsonValueKind.Null => new Label(true, null),
        JsonValueKind.String when Parse(element.GetString()) is { } value => new Label(true, value),
        _ => throw Invalid(field, $"{field} must be one of: {Allowed}."),
    };

    /// <summary>The label to store, or the reason the combination is refused (<see cref="ResolveOrThrow"/> / import row error).</summary>
    public static (TopologySource? Source, string? Error) Resolve(
        string? currentId, TopologySource? currentSource, string? newId, Label label, string field, string relation)
    {
        if (newId is null)
        {
            return label is { Sent: true, Value: not null }
                ? (null, $"{field} needs a {relation}: a label without a relation records nothing.")
                : (null, null);
        }

        if (label.Sent)
        {
            return label.Value is { } value
                ? (value, null)
                : (null, $"{field} cannot be null while {relation} is set; to drop the label, detach the relation.");
        }

        // Absent: keep the label only when the relation itself did not move — a moved relation is a new claim.
        return string.Equals(newId, currentId, StringComparison.Ordinal)
            ? (currentSource ?? TopologySource.Inferred, null)
            : (TopologySource.Inferred, null);
    }

    public static TopologySource? ResolveOrThrow(
        string? currentId, TopologySource? currentSource, string? newId, Label label, string field, string relation)
    {
        var (source, error) = Resolve(currentId, currentSource, newId, label, field, relation);
        return error is null ? source : throw Invalid(field, error);
    }

    /// <summary>Sets a pole's feeder and its label together, as a pair (see remarks).</summary>
    public static void ApplyPoleFeeder(LuxMapDbContext db, Pole pole, string? feederId, TopologySource? source)
    {
        pole.FeederId = feederId;
        pole.FeederSource = source;
        MarkPair(db, pole, nameof(Pole.FeederId), nameof(Pole.FeederSource));
    }

    /// <summary>Sets a feeder's cabinet and its label together, as a pair (see remarks).</summary>
    public static void ApplyFeederCabinet(LuxMapDbContext db, Feeder feeder, string? cabinetId, TopologySource? source)
    {
        feeder.CabinetId = cabinetId;
        feeder.CabinetSource = source;
        MarkPair(db, feeder, nameof(Feeder.CabinetId), nameof(Feeder.CabinetSource));
    }

    /// <summary>Parses the wire spelling (<c>verified</c> / <c>inferred</c>) — the same strings the CHECK was built from.</summary>
    public static TopologySource? Parse(string? text)
    {
        foreach (var candidate in Enum.GetValues<TopologySource>())
        {
            if (string.Equals(ContractEnum.ToDbValue(candidate), text, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string Allowed => string.Join(", ", ContractEnum.AllDbValues<TopologySource>());

    private static void MarkPair(LuxMapDbContext db, object entity, string idProperty, string sourceProperty)
    {
        var entry = db.Entry(entity);
        if (entry.State is EntityState.Added or EntityState.Detached)
        {
            return;
        }

        var id = entry.Property(idProperty);
        var source = entry.Property(sourceProperty);
        if (!Equals(id.OriginalValue, id.CurrentValue) || !Equals(source.OriginalValue, source.CurrentValue))
        {
            id.IsModified = true;
            source.IsModified = true;
        }
    }

    private static LuxMapException Invalid(string field, string message)
        => new(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, message,
            new Dictionary<string, object?> { ["field"] = field });
}
