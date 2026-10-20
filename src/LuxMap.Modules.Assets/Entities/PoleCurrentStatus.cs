using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Assets.Entities;

/// <summary>
/// The latest published observation (accepted sweep or Manager-confirmed pole photo) — one row per pole, keyed by <c>pole_id</c>.
/// Backs four of the fifteen properties of Contract section 2.1: <c>fixture_status</c>,
/// <c>status_confidence</c>, <c>last_seen_at</c>, <c>last_sweep_id</c>.
/// <para>
/// Those four are ONE event, not four fields. The shared publication writer (BE-15 → BE-17 / PP-9)
/// writes them together at a single moment. PHOTO-REVIEW also permits a human classification without
/// an AI score; an ON photo preserving a prior classification keeps that classification's score.
/// </para>
/// </summary>
/// <remarks>
/// <b>Schema here, writes elsewhere.</b> BE-09 creates the table because BE-14 (<c>GET /poles</c>)
/// runs BEFORE BE-15 and needs these four properties. But the publication writer OWNS the writes: asset
/// CRUD and CSV import (BE-12) must never touch this table, which is why it is separate from
/// <c>pole</c> rather than four more columns on it.
/// <para>
/// A missing row means no published observation yet; a row saying <c>unknown</c> means the latest
/// published sweep could not see this pole. Confirmed photos can establish a status without a sweep.
/// </para>
/// </remarks>
public class PoleCurrentStatus : ICommuneScoped
{
    /// <summary>Primary key AND foreign key — one row per pole, no surrogate id, no display id.</summary>
    public required string PoleId { get; set; }

    public FixtureStatus FixtureStatus { get; set; }

    /// <summary>
    /// Finite 0..1 AI score, or <c>NULL</c> for unknown or a human classification without an AI score.
    /// Unknown must carry null; known statuses may have no score (PHOTO-REVIEW PP-14).
    /// </summary>
    public double? StatusConfidence { get; set; }

    public DateTime? LastSeenAt { get; set; }

    /// <summary>
    /// The sweep that produced this classification; null after a confirmed pole photo (PP-10). Plain <c>text</c> with NO foreign key: the
    /// <c>survey_sweep</c> table does not exist until BE-15, which adds the constraint in its own
    /// migration. This is the ONLY deferred foreign key in BE-09.
    /// </summary>
    public string? LastSweepId { get; set; }

    /// <summary>
    /// Denormalized from <see cref="Pole"/>, for the same reason as on <see cref="Fixture"/>: a
    /// dashboard aggregating statuses would otherwise leak across communes the moment someone forgot
    /// a join, and this table is a very natural root for exactly that kind of query.
    /// </summary>
    public required string CommuneId { get; set; }

    public DateTime? LastEvaluatedAt { get; set; }
    public long? LastRunId { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Pole Pole { get; set; } = null!;
}
