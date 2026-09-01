using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Assets.Entities;

/// <summary>
/// The result of the latest sweep that covered a pole — one row per pole, keyed by <c>pole_id</c>.
/// Backs four of the fifteen properties of Contract section 2.1: <c>fixture_status</c>,
/// <c>status_confidence</c>, <c>last_seen_at</c>, <c>last_sweep_id</c>.
/// <para>
/// Those four are ONE event, not four fields. A single process (sweep processing, BE-15 → BE-17)
/// writes them together at a single moment. The mock set proves it: <c>status_confidence</c> is null
/// on exactly the seven <c>unknown</c> poles, not one row off.
/// </para>
/// </summary>
/// <remarks>
/// <b>Schema here, writes elsewhere.</b> BE-09 creates the table because BE-14 (<c>GET /poles</c>)
/// runs BEFORE BE-15 and needs these four properties. But the sweep pipeline OWNS the writes: asset
/// CRUD and CSV import (BE-12) must never touch this table, which is why it is separate from
/// <c>pole</c> rather than four more columns on it.
/// <para>
/// A missing row means "never covered by a sweep", which is a distinct state from a row saying
/// <c>unknown</c> ("the latest sweep ran but could not see this pole").
/// </para>
/// </remarks>
public class PoleCurrentStatus : ICommuneScoped
{
    /// <summary>Primary key AND foreign key — one row per pole, no surrogate id, no display id.</summary>
    public required string PoleId { get; set; }

    public FixtureStatus FixtureStatus { get; set; }

    /// <summary>
    /// 0..1, or <c>NULL</c> when the status is <c>unknown</c>. A CHECK constraint pins the two
    /// together in both directions: no confidence without an observation, no observation without a
    /// confidence.
    /// </summary>
    public double? StatusConfidence { get; set; }

    public DateTime? LastSeenAt { get; set; }

    /// <summary>
    /// The sweep that produced this classification. Plain <c>text</c> with NO foreign key: the
    /// <c>survey_sweep</c> table does not exist until BE-15, which adds the constraint in its own
    /// migration. This is the ONLY deferred foreign key in BE-09.
    /// </summary>
    public string? LastSweepId { get; set; }

    /// <summary>
    /// Denormalised from <see cref="Pole"/>, for the same reason as on <see cref="Fixture"/>: a
    /// dashboard aggregating statuses would otherwise leak across communes the moment someone forgot
    /// a join, and this table is a very natural root for exactly that kind of query.
    /// </summary>
    public required string CommuneId { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Pole Pole { get; set; } = null!;
}
