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
