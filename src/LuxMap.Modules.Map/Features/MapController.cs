using Asp.Versioning;
using LuxMap.Modules.Map.Bbox;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.GeoJson;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Map.Features;

/// <summary>
/// The map layers — <c>GET /poles</c> and <c>GET /segments</c> (BE-14, Contract sections 5.1–5.2).
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Routed at <c>/poles</c> and <c>/segments</c>, NOT under <c>/assets</c>.</b> The inventory
/// endpoints of BE-12a already own <c>/assets/poles</c>: paginated JSON, administrator writes. These
/// are read-only GeoJSON layers keyed by <c>bbox</c>. Two surfaces for two consumers, and the
/// Contract gives each its own path.
/// </para>
/// <para>
/// No role policy, the same reasoning as every other GET in the system: <c>SetFallbackPolicy</c>
/// already requires a login, and a policy is one EXACT role, so naming one would lock out the other
/// three rather than set a floor (BE-12a, rule 4).
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
public sealed class MapController(
    MapQueryService service,
    ICommuneScopeAccessor scopeAccessor) : ControllerBase
{
    /// <summary>
    /// Poles inside a bounding box, as a GeoJSON <c>FeatureCollection</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>bbox</c> is REQUIRED — <c>minLng,minLat,maxLng,maxLat</c> in EPSG:4326. There is no
    /// "fetch everything" variant and there must not be one: CLAUDE.md lists it as an anti-pattern,
    /// because an unbounded query cannot meet the 500 ms budget and degrades without a signal.
    /// </para>
    /// <para>
    /// Past <see cref="MapQueryService.MaxPoles"/> poles the answer is <b>413
    /// <c>BBOX_TOO_LARGE</c></b> carrying the real count, so the front end can say "zoom in to see
    /// detail" rather than guess why a map came back empty.
    /// </para>
    /// <para>
    /// <c>data_source</c> defaults to everything EXCEPT <c>calibration_rig</c> (section 1.6). Pass it
    /// by name to see the calibration rig.
    /// </para>
    /// </remarks>
    [HttpGet("poles")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<FeatureCollection<PoleProperties>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<FeatureCollection<PoleProperties>>> PolesAsync(
        [FromQuery] string? bbox,
        [FromQuery] string? status,
        [FromQuery(Name = "power_source")] PowerSource? powerSource,
        [FromQuery(Name = "segment_id")] string? segmentId,
        [FromQuery(Name = "commune_id")] string[]? communeId,
        [FromQuery(Name = "has_open_fault")] bool? hasOpenFault,
        [FromQuery(Name = "data_source")] string? dataSource,
        CancellationToken ct)
        => Ok(await service.PolesAsync(
            new PoleMapQuery
            {
                Bbox = BoundingBox.Parse(bbox),
                Statuses = CsvEnum<FixtureStatus>(status, "status"),
                PowerSource = powerSource,
                SegmentId = segmentId,
                CommuneIds = CommuneFilter.Narrow(scopeAccessor.Scope, communeId),
                HasOpenFault = hasOpenFault,
                DataSource = CsvEnum<DataSource>(dataSource, "data_source"),
            },
            ct));

    /// <summary>Road segments inside a bounding box, as a <c>FeatureCollection</c> of LineStrings.</summary>
    /// <remarks>
    /// No size limit here, unlike poles: a commune has tens of roads where it has thousands of
    /// lamps, so the cap that protects the pole layer would only ever add a failure mode to this one.
    /// </remarks>
    [HttpGet("segments")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<FeatureCollection<SegmentProperties>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<FeatureCollection<SegmentProperties>>> SegmentsAsync(
        [FromQuery] string? bbox,
        [FromQuery(Name = "commune_id")] string[]? communeId,
        [FromQuery(Name = "data_source")] string? dataSource,
        CancellationToken ct)
        => Ok(await service.SegmentsAsync(
            new SegmentMapQuery
            {
                Bbox = BoundingBox.Parse(bbox),
                CommuneIds = CommuneFilter.Narrow(scopeAccessor.Scope, communeId),
                DataSource = CsvEnum<DataSource>(dataSource, "data_source"),
            },
            ct));

    /// <summary>
    /// Reads a comma-separated enum list in the WIRE spelling, refusing an unknown member by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bound as a string and parsed here rather than as <c>TEnum[]</c>, because the framework's
    /// binder answers an unparseable value with a generic 400 that does not say WHICH value it
    /// rejected — and for an enum the front end hardcodes, that is the one thing worth saying.
    /// </para>
    /// <para>
    /// 🔴 <b>Matched against the SNAKE_CASE name, not with <c>Enum.TryParse</c>.</b> The wire values
    /// of Contract section 3.1 are lowercase snake_case — <c>calibration_rig</c>,
    /// <c>field_report</c>, <c>node_offline</c> — and <c>Enum.TryParse</c> does not know about the
    /// underscore, so it rejects every multi-word member while happily accepting single-word ones
    /// like <c>normal</c>. That shape of bug hides: the common filters work and the map simply
    /// refuses a few values.
    /// </para>
    /// <para>
    /// The comparison uses the SAME policy that serialises these enums on the way out, so the values
    /// a client reads back are exactly the values it may send.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<TEnum>? CsvEnum<TEnum>(string? raw, string parameter)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var parsed = new List<TEnum>();

        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Enum.GetValues<TEnum>()
                .Where(candidate => WireName(candidate).Equals(part, StringComparison.OrdinalIgnoreCase))
                .Select(candidate => (TEnum?)candidate)
                .FirstOrDefault();

            if (match is not { } value)
            {
                throw new LuxMapException(
                    ErrorCodes.ValidationFailed,
                    System.Net.HttpStatusCode.BadRequest,
                    $"'{part}' is not a valid {parameter}.",
                    new Dictionary<string, object?>
                    {
                        [parameter] = part,
                        // The same policy that serialises them, so the list a caller is shown is the
                        // list they can actually send back.
                        ["allowed"] = Enum.GetValues<TEnum>().Select(WireName).ToArray(),
                    });
            }

            parsed.Add(value);
        }

        return parsed;
    }

    /// <summary>The value as it appears on the wire — the same policy <c>LuxMapJsonOptions</c> uses.</summary>
    private static string WireName<TEnum>(TEnum value)
        where TEnum : struct, Enum
        => System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()!);
}
