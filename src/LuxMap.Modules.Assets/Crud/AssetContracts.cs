using System.ComponentModel.DataAnnotations;
using LuxMap.Shared.Contracts.Enums;

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
}
