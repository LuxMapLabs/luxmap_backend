using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;

namespace LuxMap.Modules.Assets.Crud;

/// <summary>
/// Request bodies for asset CRUD (BE-12a).
/// </summary>
/// <remarks>
/// ⚠️ REQUEST shapes only. What a GET returns for one asset is <b>BE-12b</b>, still waiting on
/// review, which is why every write here answers <c>201</c> with a <c>Location</c> header or
/// <c>204</c> rather than echoing an entity — publishing a response shape now would pre-empt that
/// decision, and unpublishing one is far harder than publishing it late.
/// <para>
/// Display ids are absent from every body on purpose. Contract section 0.4: the client never invents
/// one, the database sequence does.
/// </para>
/// </remarks>
public sealed record CreateSegmentRequest
{
    /// <summary>The authority's own inventory code. Optional — assets traced from imagery have none.</summary>
    [MaxLength(64)]
    public string? ExternalRef { get; init; }

    [Required]
    [MaxLength(256)]
    public string? SegmentName { get; init; }

    [Required]
    public RoadClass? RoadClass { get; init; }

    /// <summary>Metres, DECLARED. Never recomputed from the geometry — see <c>RoadSegment.LengthM</c>.</summary>
    [Required]
    [Range(1, int.MaxValue)]
    public int? LengthM { get; init; }

    /// <summary>WKT <c>LINESTRING</c> in EPSG:4326, longitude first.</summary>
    [Required]
    public string? GeomWkt { get; init; }

    [Required]
    [MaxLength(32)]
    public string? CommuneId { get; init; }

    [Required]
    public DataSource? DataSource { get; init; }
}

public sealed record CreateFeederRequest
{
    [MaxLength(64)]
    public string? ExternalRef { get; init; }

    [Required]
    [MaxLength(256)]
    public string? FeederName { get; init; }

    [Required]
    [MaxLength(32)]
    public string? CommuneId { get; init; }

    /// <summary>Optional WKT <c>LINESTRING</c>. Branch C surveyed no cable routes; blank beats invented.</summary>
    public string? GeomWkt { get; init; }

    /// <summary>The cabinet this circuit leaves from (CAB-2), in the feeder's own commune. Optional.</summary>
    [MaxLength(32)]
    public string? CabinetId { get; init; }

    /// <summary>
    /// Provenance of the feeder → cabinet relation (TOPO-INFER D-10): <c>verified</c> | <c>inferred</c>. Same rule as a pole's
    /// <c>feeder_source</c>: absent = <c>inferred</c> for a new or moved cabinet, KEEP when it stays.
    /// </summary>
    public JsonElement CabinetSource { get; init; }
}

/// <summary>A main electrical cabinet (CAB-1). <c>commune_id</c> from the body, checked against scope.</summary>
public sealed record CreateCabinetRequest
{
    [MaxLength(64)]
    public string? ExternalRef { get; init; }

    [Required]
    [MaxLength(256)]
    public string? CabinetName { get; init; }

    [Required]
    [MaxLength(32)]
    public string? CommuneId { get; init; }

    /// <summary>WKT <c>POINT</c> in EPSG:4326, longitude first. Required — a cabinet is somewhere.</summary>
    [Required]
    public string? GeomWkt { get; init; }

    [Required]
    public DataSource? DataSource { get; init; }
}

public sealed record CreatePoleRequest
{
    [MaxLength(64)]
    public string? ExternalRef { get; init; }

    [Required]
    [MaxLength(32)]
    public string? SegmentId { get; init; }

    /// <summary>Null for a <c>solar_all_in_one</c> pole — it is on no circuit at all.</summary>
    [MaxLength(32)]
    public string? FeederId { get; init; }

    /// <summary>
    /// Taken from the BODY, unlike <c>LuxReading</c> where it is read from the pole.
    /// </summary>
    /// <remarks>
    /// There is nothing here to derive it from. A pole's segment is not a reliable source: a road
    /// with <c>road_class = inter_commune</c> runs BETWEEN communes, so its poles legitimately sit in
    /// a different commune from the segment's owner. The value is checked against the caller's scope
    /// on the way in, and <c>CommuneWriteGuard</c> stands behind that.
    /// </remarks>
    [Required]
    [MaxLength(32)]
    public string? CommuneId { get; init; }

    /// <summary>WKT <c>POINT</c> in EPSG:4326, longitude first.</summary>
    [Required]
    public string? GeomWkt { get; init; }

    public bool NearSensitivePoi { get; init; }

    [Required]
    public DataSource? DataSource { get; init; }

    /// <summary>Optional engineer's note on the spot (POLE-NOTE); blank means none, at most 1000 characters.</summary>
    public string? Note { get; init; }

    /// <summary>
    /// Provenance of the pole → feeder relation (TOPO-INFER TI-2): <c>verified</c> | <c>inferred</c>. Optional: absent means
    /// <c>inferred</c> for a new or moved relation and KEEP when the feeder stays; <c>null</c> while a feeder is set, or a label without a feeder, is 400.
    /// </summary>
    public JsonElement FeederSource { get; init; }
}

/// <summary>
/// A lamp installation. <c>commune_id</c> is absent by design — it is copied from the pole.
/// </summary>
public sealed record CreateFixtureRequest
{
    [Required]
    [MaxLength(32)]
    public string? PoleId { get; init; }

    [Required]
    public FixtureType? FixtureType { get; init; }

    [Required]
    public PowerSource? PowerSource { get; init; }

    [Required]
    [Range(1, 10_000)]
    public int? LampWatt { get; init; }

    [Required]
    public DateOnly? InstallDate { get; init; }

    /// <summary>Set this to retire a lamp; a new row records its replacement.</summary>
    public DateOnly? RemovedDate { get; init; }

    public DateOnly? WarrantyExpiry { get; init; }

    [Required]
    public DataSource? DataSource { get; init; }
}

/// <summary>
/// Bodies of the three asset update endpoints (BE-12). Each is a FULL REPLACEMENT.
/// </summary>
/// <remarks>
/// <b>PUT, not PATCH, and the difference bites.</b> Every writable field is sent every time, and a
/// field left out is not "unchanged" — it is the absent value. On <see cref="UpdatePoleRequest"/> that
/// means omitting <c>feeder_id</c> CLEARS the pole's circuit. A caller wanting to touch only the
/// circuit has <c>PUT /assets/poles/{id}/feeder</c>, which exists for exactly that and distinguishes
/// an absent key from an explicit <c>null</c>.
/// <para>
/// PATCH was the alternative and was rejected for this round: telling "not sent" from "set to null"
/// needs a wrapper type on every nullable field, and the one place that genuinely needs the
/// distinction already has it in <c>SetPoleFeederRequest</c>. Two half-expressive verbs would be
/// worse than one honest one.
/// </para>
/// <para>
/// <b><c>commune_id</c> is absent from all three on purpose.</b> Moving an asset between communes is
/// not an edit, it is a transfer: it changes who may see the row, and it would have to be checked
/// against the caller's scope on BOTH sides — the commune losing the asset and the one gaining it.
/// <c>CommuneWriteGuard</c> does check both (<c>OriginalValues</c> and <c>CurrentValues</c>), so the
/// guard would hold, but the request shape would still be inviting an operation nobody has specified.
/// An asset created in the wrong commune is corrected by deleting and recreating it.
/// </para>
/// <para>
/// No response body — <c>204</c>. What a read returns is still <b>BE-12b</b>.
/// </para>
/// </remarks>
public sealed record UpdateSegmentRequest
{
    /// <summary>The authority's own inventory code, or null. Unique per commune when present.</summary>
    [MaxLength(64)]
    public string? ExternalRef { get; init; }

    [Required]
    [MaxLength(256)]
    public string? SegmentName { get; init; }

    [Required]
    public RoadClass? RoadClass { get; init; }

    /// <summary>Metres, DECLARED. Never recomputed from the geometry — see <c>RoadSegment.LengthM</c>.</summary>
    [Required]
    [Range(1, int.MaxValue)]
    public int? LengthM { get; init; }

    /// <summary>WKT <c>LINESTRING</c> in EPSG:4326, longitude first.</summary>
    [Required]
    public string? GeomWkt { get; init; }

    /// <summary>
    /// ⚠️ Rewriting this rewrites PROVENANCE. Branch C keeps three data sources apart from ingest all
    /// the way to the statistics (CLAUDE.md), so relabelling calibration-rig data as field data would
    /// corrupt a research result rather than fix a typo. It stays writable because an import that set
    /// it wrong has no other remedy once faults reference the asset, and the endpoint is
    /// administrator-only.
    /// </summary>
    [Required]
    public DataSource? DataSource { get; init; }
}

/// <summary>Full replacement of a feeder. See <see cref="UpdateSegmentRequest"/> for the shared rules.</summary>
/// <remarks><c>data_source</c> is absent because <c>Feeder</c> does not carry one (BE-09).</remarks>
public sealed record UpdateFeederRequest
{
    [MaxLength(64)]
    public string? ExternalRef { get; init; }

    [Required]
    [MaxLength(256)]
    public string? FeederName { get; init; }

    /// <summary>Optional WKT <c>LINESTRING</c>. Branch C surveyed no cable routes; blank beats invented.</summary>
    public string? GeomWkt { get; init; }

    /// <summary>
    /// The cabinet (CAB-6). The second field of a full replacement that is KEPT when absent, like a pole's
    /// <c>note</c>: leave the key out to keep it, <c>null</c> to detach the feeder, an id to move it.
    /// </summary>
    /// <remarks>
    /// A feeder form written before cabinets existed must not detach every feeder it renames — and while a
    /// device switches the feeder, detaching it is refused (409), so "absent = clear" would turn every such
    /// rename into an error. Read through <see cref="ReadCabinetId"/>.
    /// </remarks>
    public JsonElement CabinetId { get; init; }

    /// <summary><c>Present = false</c> when the key was not sent; otherwise the id, or <c>null</c> to detach.</summary>
    public (bool Present, string? CabinetId) ReadCabinetId() => CabinetId.ValueKind switch
    {
        JsonValueKind.Undefined => (false, null),
        JsonValueKind.Null => (true, null),
        JsonValueKind.String when CabinetId.GetString() is { Length: > 0 and <= 32 } id => (true, id),
        _ => throw new LuxMapException(
            ErrorCodes.ValidationFailed,
            HttpStatusCode.BadRequest,
            "cabinet_id must be a cabinet id (at most 32 characters), or null to detach the feeder from its cabinet.",
            new Dictionary<string, object?> { ["field"] = "cabinet_id" }),
    };

    /// <summary>
    /// Provenance of the feeder → cabinet relation (TOPO-INFER D-10): <c>verified</c> | <c>inferred</c>. Same rule as a pole's
    /// <c>feeder_source</c>: absent = <c>inferred</c> for a new or moved cabinet, KEEP when it stays.
    /// </summary>
    public JsonElement CabinetSource { get; init; }
}

/// <summary>Full replacement of a cabinet. <c>commune_id</c> is not writable — see <see cref="UpdateSegmentRequest"/>.</summary>
public sealed record UpdateCabinetRequest
{
    [MaxLength(64)]
    public string? ExternalRef { get; init; }

    [Required]
    [MaxLength(256)]
    public string? CabinetName { get; init; }

    /// <summary>WKT <c>POINT</c> in EPSG:4326, longitude first.</summary>
    [Required]
    public string? GeomWkt { get; init; }

    /// <summary>⚠️ Provenance — see <see cref="UpdateSegmentRequest.DataSource"/>.</summary>
    [Required]
    public DataSource? DataSource { get; init; }
}

/// <summary>Full replacement of a pole. See <see cref="UpdateSegmentRequest"/> for the shared rules.</summary>
public sealed record UpdatePoleRequest
{
    [MaxLength(64)]
    public string? ExternalRef { get; init; }

    [Required]
    [MaxLength(32)]
    public string? SegmentId { get; init; }

    /// <summary>
    /// Null for a <c>solar_all_in_one</c> pole — it is on no circuit at all.
    /// <b>Omitting the key clears the circuit</b>, because this is a full replacement.
    /// </summary>
    [MaxLength(32)]
    public string? FeederId { get; init; }

    /// <summary>WKT <c>POINT</c> in EPSG:4326, longitude first.</summary>
    [Required]
    public string? GeomWkt { get; init; }

    public bool NearSensitivePoi { get; init; }

    /// <summary>⚠️ Provenance — see <see cref="UpdateSegmentRequest.DataSource"/>.</summary>
    [Required]
    public DataSource? DataSource { get; init; }

    /// <summary>
    /// The engineer's note (POLE-NOTE). The ONE field of this full replacement that is not replaced when
    /// absent: leaving the key out keeps the note, <c>null</c> or blank clears it, text overwrites it.
    /// </summary>
    /// <remarks>
    /// A form that predates the note must not wipe what an engineer wrote on site. Read through
    /// <see cref="PoleNoteInput.Read"/>.
    /// </remarks>
    public JsonElement Note { get; init; }

    /// <summary>
    /// Provenance of the pole → feeder relation (TOPO-INFER TI-2): <c>verified</c> | <c>inferred</c>. Optional: absent means
    /// <c>inferred</c> for a new or moved relation and KEEP when the feeder stays; <c>null</c> while a feeder is set, or a label without a feeder, is 400.
    /// </summary>
    public JsonElement FeederSource { get; init; }
}

/// <summary>
/// BE-13 — one pole as the topology endpoints report it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>PROVISIONAL SHAPE — the Contract specifies no topology endpoint at all.</b> Proposed in
/// <c>docs/review/BE-13-topology-shape.md</c>, registered as drift 46, and not stable until the next
/// FW confirms it. Anything built on top of this must say its foundation is temporary.
/// </para>
/// <para>
/// <b>This is NOT the BE-12b read shape and must not drift into it.</b> The consumer here is CV-15
/// and CV-05 — clustering engines, not a screen — so it carries what clustering needs and nothing
/// else. No <c>fixture_status</c>, no <c>open_fault_count</c>, no <c>install_date</c>. Two endpoints
/// answering the same question with two values is how drift starts, and nothing detects the day they
/// disagree.
/// </para>
/// <para>
/// <c>{lat, lng}</c> rather than GeoJSON, following the precedent <c>GET /faults</c> set in Contract
/// section 2.4: a paginated list for an engine is not a map layer. EPSG:4326 like every other
/// endpoint — 3405 exists only inside the SQL tree (BE-10, rule 3).
/// </para>
/// <para>
/// <c>FeederId</c> is repeated in every item although the route already names it, so that
/// <c>/segments/{id}/poles</c> and <c>/feeders/{id}/poles</c> share one item type and a result set
/// merged from several calls still describes itself.
/// </para>
/// <para>
/// ⚠️ <b>It used to carry <c>power_source</c>, and that field was REMOVED on 22/09/2026.</b> Its only
/// job was to separate "this pole is solar, so it has no circuit" from "nobody has assigned this pole
/// yet" — both being <c>feeder_id = NULL</c>. Solar lighting left the project scope, so there is no
/// longer a pole that legitimately has no circuit, and the distinction it existed to draw no longer
/// exists. Every unassigned pole is now simply unassigned.
/// </para>
/// </remarks>
public sealed record TopologyPole
{
    public required string PoleId { get; init; }

    public required string SegmentId { get; init; }

    /// <summary>Null for a pole on no circuit — a fact, not a missing value.</summary>
    public string? FeederId { get; init; }

    /// <summary>Provenance of <see cref="FeederId"/> (TOPO-INFER): <c>verified</c> | <c>inferred</c>; <c>null</c> with no feeder.</summary>
    public TopologySource? FeederSource { get; init; }

    public required double Lat { get; init; }

    public required double Lng { get; init; }
}

// ── BE-12b — the READ shape for asset inventory ───────────────────────────────────────────────
//
// Contract section 5.3, decided 22/09/2026 after review with an independent agent against the WP5
// repository. Replaces the PagedResult<string> placeholder that stood here since BE-12a.

/// <summary>
/// The lamp currently in service on a pole. <c>null</c> when the pole carries none.
/// </summary>
/// <remarks>
/// <para>
/// Exactly ONE, never a list: <c>ux_fixture_pole_id_active</c> makes the lamp with no
/// <c>removed_date</c> unique per pole (BE-REVIEW-02, constraint 3), so there is nothing to
/// aggregate and no rule to invent.
/// </para>
/// <para>
/// <b>Nested here, flat in Contract section 5.1 — and that is deliberate, not drift.</b> The map
/// endpoint flattens these onto <c>properties</c> because MapLibre cannot read a nested object in a
/// data expression. An inventory table has no such constraint, and nesting says "a pole may have no
/// lamp" once instead of making five sibling fields separately nullable.
/// </para>
/// <para>
/// Carries its OWN <c>data_source</c>. A lamp's provenance can differ from its pole's — the pole may
/// come from public imagery while the lamp record was typed in from an inventory sheet.
/// </para>
/// </remarks>
public sealed record ActiveFixture
{
    public required string FixtureId { get; init; }

    public required FixtureType FixtureType { get; init; }

    public required PowerSource PowerSource { get; init; }

    public required int LampWatt { get; init; }

    public required DateOnly InstallDate { get; init; }

    public DateOnly? WarrantyExpiry { get; init; }

    public required DataSource DataSource { get; init; }
}

/// <summary>Where an asset is, as a paginated list reports it.</summary>
/// <remarks>
/// <c>{lat, lng}</c> rather than GeoJSON: this is a paginated list, not a map layer, and Contract
/// section 5.4 already published this exact shape for <c>GET /faults</c>. Reusing it beats inventing
/// a second one. EPSG:4326 like everything that leaves the API.
/// </remarks>
public sealed record AssetLocation
{
    public required double Lat { get; init; }

    public required double Lng { get; init; }
}

/// <summary>
/// One pole in the inventory list.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Shares nothing with Contract section 5.1 beyond identity.</b> No <c>fixture_status</c>, no
/// <c>status_confidence</c>, no <c>open_fault_count</c>, no <c>last_seen_at</c>. Those belong to the
/// OPERATIONAL view and asking two endpoints the same question is how they start disagreeing, with
/// nothing to notice the day they do.
/// </para>
/// <para>
/// <c>external_ref</c>, <c>data_source</c> and <c>feeder_id</c> ARE emitted here although section
/// 5.1 forbids them on the map. That prohibition was written for the map; the three questions in
/// <c>docs/review/BE-12b-read-shape.md</c> asked whether it binds the inventory surface too, and the
/// answer was no — <c>external_ref</c> is the code the operator types during import and the only way
/// to match a row on screen to a row in their spreadsheet; <c>data_source</c> is what keeps
/// calibration data distinguishable from real; and <c>feeder_id</c> must be readable because
/// <c>PUT</c> is a full replacement and an editor has to know the value it is preserving.
/// </para>
/// </remarks>
public sealed record PoleListItem
{
    public required string PoleId { get; init; }

    public string? ExternalRef { get; init; }

    public required string SegmentId { get; init; }

    /// <summary><c>null</c> when the pole is not wired to a circuit yet.</summary>
    public string? FeederId { get; init; }

    /// <summary>
    /// Provenance of <see cref="FeederId"/> (TOPO-INFER): <c>verified</c> | <c>inferred</c>; <c>null</c> with no feeder. A screen
    /// must show an inferred relation as inferred — it is deduced from the field, not confirmed.
    /// </summary>
    public TopologySource? FeederSource { get; init; }

    /// <summary>
    /// The cabinet the pole's feeder leaves from (CAB-1), read through the feeder — <c>null</c> when the pole has no feeder or the
    /// feeder no cabinet. Derived at read time: the pole itself stores no cabinet.
    /// </summary>
    public string? CabinetId { get; init; }

    public string? CabinetName { get; init; }

    /// <summary>
    /// Provenance of the feeder → cabinet link (TOPO-INFER), separate from <see cref="FeederSource"/>: the pole → cabinet chain is
    /// only as confirmed as BOTH labels. <c>null</c> with no cabinet.
    /// </summary>
    public TopologySource? CabinetSource { get; init; }

    public required string CommuneId { get; init; }

    public required DataSource DataSource { get; init; }

    public required bool NearSensitivePoi { get; init; }

    public required AssetLocation Location { get; init; }

    /// <summary>
    /// Carried in the LIST, not only the detail, because the inventory table shows lamp type and
    /// wattage per row — a boolean would force one request per pole to fill a visible column.
    /// </summary>
    public ActiveFixture? ActiveFixture { get; init; }

    /// <summary>The free-text note on this spot (POLE-NOTE); <c>null</c> when there is none.</summary>
    public string? Note { get; init; }

    public required DateTime UpdatedAt { get; init; }

    /// <summary>Who last changed the row by form, note or import (e.g. <c>USR-004</c>); <c>null</c> for system-loaded rows.</summary>
    public string? UpdatedBy { get; init; }

    /// <summary>That account's display name now, so a screen can say who without another call.</summary>
    public string? UpdatedByName { get; init; }
}

/// <summary>
/// The one set of rules for a pole note, whichever request carries it: trimmed, blank means none, at most
/// <see cref="MaxLength"/> characters (POLE-NOTE).
/// </summary>
public static class PoleNoteInput
{
    public const int MaxLength = 1000;

    /// <summary>The trimmed note, or <c>null</c> when it is null or blank. 400 when it is too long.</summary>
    public static string? Normalize(string? note)
    {
        var text = note?.Trim();
        if (text is { Length: > MaxLength })
        {
            throw Invalid($"note is at most {MaxLength} characters; this one has {text.Length}.");
        }

        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>
    /// Reads a <c>note</c> key that may be absent: <c>Present = false</c> when the body did not send it,
    /// otherwise the normalized text (<c>null</c> = clear). 400 when the value is neither text nor null.
    /// </summary>
    public static (bool Present, string? Text) Read(JsonElement note) => note.ValueKind switch
    {
        JsonValueKind.Undefined => (false, null),
        JsonValueKind.Null => (true, null),
        JsonValueKind.String => (true, Normalize(note.GetString())),
        _ => throw Invalid("note must be text, or null to clear the note."),
    };

    internal static LuxMapException Invalid(string message)
        => new(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, message, new Dictionary<string, object?> { ["field"] = "note" });
}

/// <summary>
/// The note a caller last saw on a pole (BE-43 D-8), already normalized like any note: <c>null</c> = it saw none.
/// </summary>
public sealed record ExpectedNote(string? Text);

/// <summary><c>PUT /assets/poles/{id}/note</c>: the new note, or <c>null</c> / blank to clear it.</summary>
/// <remarks>
/// A JSON element, like <see cref="SetPoleFeederRequest"/>, so a body WITHOUT the key is a 400 rather than
/// a silent clear: wiping an engineer's note must be something the caller said.
/// </remarks>
public sealed record SetPoleNoteRequest
{
    public JsonElement Note { get; init; }

    /// <summary>The trimmed note, or <c>null</c> to clear. 400 when the key is missing, not text, or too long.</summary>
    public string? ReadNote()
    {
        var (present, text) = PoleNoteInput.Read(Note);
        return present ? text : throw PoleNoteInput.Invalid("note is required. Send null or \"\" to clear the note.");
    }
}

/// <summary>What <c>PUT /assets/poles/{id}/note</c> answers: the pole's note as stored and who last changed the pole.</summary>
public sealed record PoleNoteResponse(string PoleId, string? Note, DateTime UpdatedAt, string? UpdatedBy, string? UpdatedByName);

/// <summary>One pole read on its own — the list row plus what only a detail view needs.</summary>
public sealed record PoleDetail
{
    public required PoleListItem Pole { get; init; }

    /// <summary>Resolved for display. The list omits it: one join per row for a label.</summary>
    public required string SegmentName { get; init; }

    /// <summary>WKT <c>POINT</c> in EPSG:4326, longitude first — what a <c>PUT</c> body wants back.</summary>
    public required string GeomWkt { get; init; }

    public required DateTime CreatedAt { get; init; }
}

/// <summary>One road segment in the inventory list.</summary>
public sealed record SegmentListItem
{
    public required string SegmentId { get; init; }

    public string? ExternalRef { get; init; }

    public required string SegmentName { get; init; }

    public required RoadClass RoadClass { get; init; }

    /// <summary>The DECLARED length, never <c>ST_Length</c> of the geometry (BE-10, rule 4).</summary>
    public required double LengthM { get; init; }

    public required string CommuneId { get; init; }

    public required DataSource DataSource { get; init; }

    /// <summary>⚠️ Counted within the CALLER'S commune scope, like every other read.</summary>
    public required int PoleCount { get; init; }

    public required DateTime UpdatedAt { get; init; }

    /// <summary>Who last changed the row by form, note or import (e.g. <c>USR-004</c>); <c>null</c> for system-loaded rows.</summary>
    public string? UpdatedBy { get; init; }

    /// <summary>That account's display name now, so a screen can say who without another call.</summary>
    public string? UpdatedByName { get; init; }
}

/// <summary>One road segment read on its own.</summary>
public sealed record SegmentDetail
{
    public required SegmentListItem Segment { get; init; }

    /// <summary>WKT <c>LINESTRING</c> in EPSG:4326.</summary>
    public required string GeomWkt { get; init; }

    public required DateTime CreatedAt { get; init; }
}

/// <summary>One feeder in the inventory list.</summary>
/// <remarks>
/// 🔴 <b>No <c>data_source</c>, and that is not an oversight.</b> Contract section 1.6 puts the field
/// on eight entities and <c>Feeder</c> is not one of them. Inventing provenance for an entity that
/// has none would make the API answer a question the database cannot.
/// </remarks>
public sealed record FeederListItem
{
    public required string FeederId { get; init; }

    public string? ExternalRef { get; init; }

    public required string FeederName { get; init; }

    public required string CommuneId { get; init; }

    /// <summary>
    /// Whether a cable route was recorded, rather than the route itself.
    /// </summary>
    /// <remarks>
    /// Branch C surveyed no cable routes, so this is <c>false</c> for almost every feeder. Shipping a
    /// WKT column that is nearly always null in every list row costs bandwidth for nothing; the
    /// detail endpoint carries the geometry for the one feeder a caller actually opened.
    /// </remarks>
    public required bool HasGeometry { get; init; }

    public required int PoleCount { get; init; }

    /// <summary>
    /// The cabinet this circuit leaves from, with its point (CAB-6) — <c>null</c> when none is recorded. In the
    /// LIST, like a pole's <c>active_fixture</c>: an inventory row shows where the cabinet is, and a bare id would
    /// cost one request per row.
    /// </summary>
    public FeederCabinet? Cabinet { get; init; }

    public required DateTime UpdatedAt { get; init; }

    /// <summary>Who last changed the row by form, note or import (e.g. <c>USR-004</c>); <c>null</c> for system-loaded rows.</summary>
    public string? UpdatedBy { get; init; }

    /// <summary>That account's display name now, so a screen can say who without another call.</summary>
    public string? UpdatedByName { get; init; }
}

/// <summary>One feeder read on its own.</summary>
public sealed record FeederDetail
{
    public required FeederListItem Feeder { get; init; }

    /// <summary>WKT <c>LINESTRING</c>, or <c>null</c> — most feeders have no surveyed route.</summary>
    public string? GeomWkt { get; init; }

    public required DateTime CreatedAt { get; init; }
}

/// <summary>A feeder's cabinet as a feeder row shows it (CAB-6).</summary>
public sealed record FeederCabinet
{
    public required string CabinetId { get; init; }

    public required string CabinetName { get; init; }

    public required AssetLocation Location { get; init; }

    /// <summary>Provenance of the feeder → cabinet relation (TOPO-INFER D-10): <c>verified</c> | <c>inferred</c>.</summary>
    public required TopologySource CabinetSource { get; init; }
}

/// <summary>One cabinet in the inventory list (CAB-8).</summary>
/// <remarks>
/// <para>
/// 🔴 <b><c>iot_node_id</c> only — no device state.</b> <c>node_status</c>, <c>last_report_at</c> and
/// <c>supports_remote_control</c> belong to <c>GET /map/iot-nodes</c>; two endpoints answering the same
/// question is how they start disagreeing (Contract section 5.3.1).
/// </para>
/// <para>
/// <c>data_source</c> IS emitted, unlike on a feeder: a cabinet has real provenance, and the testbed cabinet
/// must stay apart from field cabinets all the way to the statistics.
/// </para>
/// </remarks>
public sealed record CabinetListItem
{
    public required string CabinetId { get; init; }

    public string? ExternalRef { get; init; }

    public required string CabinetName { get; init; }

    public required string CommuneId { get; init; }

    public required DataSource DataSource { get; init; }

    public required AssetLocation Location { get; init; }

    /// <summary>
    /// Feeders leaving from this cabinet, in id order; <c>[]</c> when none. ⚠️ Within the CALLER'S commune
    /// scope, like every count on this surface.
    /// </summary>
    public required IReadOnlyList<string> FeederIds { get; init; }

    /// <summary>The device mounted in the cabinet, or <c>null</c> — most cabinets carry none.</summary>
    public string? IotNodeId { get; init; }

    public required DateTime UpdatedAt { get; init; }

    /// <summary>Who last changed the row by form or import; <c>null</c> for system-loaded rows.</summary>
    public string? UpdatedBy { get; init; }

    /// <summary>That account's display name now, so a screen can say who without another call.</summary>
    public string? UpdatedByName { get; init; }
}

/// <summary>One cabinet read on its own.</summary>
public sealed record CabinetDetail
{
    public required CabinetListItem Cabinet { get; init; }

    /// <summary>WKT <c>POINT</c> in EPSG:4326 — what a <c>PUT</c> body wants back.</summary>
    public required string GeomWkt { get; init; }

    public required DateTime CreatedAt { get; init; }
}
