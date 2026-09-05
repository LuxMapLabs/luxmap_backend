using System.Net;
using System.Text;
using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Assets.Import;

/// <summary>
/// Bulk asset import (BE-12a). NOT in Contract v1.1 — registered as drift.
/// </summary>
/// <remarks>
/// Load the four kinds IN ORDER: <c>segments</c>, <c>feeders</c>, <c>poles</c>, <c>fixtures</c>.
/// The order is foreign keys, not preference, and it enforces itself — poles uploaded before their
/// segments fail every row with a message naming the <c>segment_external_ref</c> that matched nothing.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/assets/import")]
[Authorize(Policy = LuxMapPolicies.Administrator)]
public sealed class AssetImportController(AssetImportService service) : ControllerBase
{
    /// <summary>
    /// 10 MB. The whole FO-26 mock set is about 24 KB, so this is four orders of magnitude of room.
    /// </summary>
    /// <remarks>
    /// Set explicitly because the framework default is not obvious and not what people assume: Kestrel
    /// caps a request body at 30,000,000 bytes (~28.6 MB), well BELOW the 128 MB
    /// <c>FormOptions.MultipartBodyLengthLimit</c> everyone quotes. Nothing in this repo had ever
    /// configured either. An inventory spreadsheet that reaches 10 MB is a mistake worth stopping at
    /// the door rather than buffering and parsing first.
    /// </remarks>
    public const int MaxUploadBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Uploads one file. <paramref name="kind"/> says which of the four it is.
    /// </summary>
    /// <remarks>
    /// The file arrives as <c>IFormFile</c>, never as a form VALUE: <c>FormOptions.ValueLengthLimit</c>
    /// caps an individual form value at 4 MB, so a large GeoJSON posted as a field would fail on a
    /// limit that has nothing to do with the one declared here.
    /// <para>
    /// Answers <b>200</b> even when rows failed — see <see cref="ImportResult"/>. The valid rows were
    /// really written, so the request succeeded; the body says what happened to each one.
    /// </para>
    /// </remarks>
    [HttpPost("{kind}")]
    [RequestSizeLimit(MaxUploadBytes)]
    [ProducesResponseType<ImportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<ImportResult>> ImportAsync(
        string kind,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ImportKind>(kind, ignoreCase: true, out var importKind))
        {
            throw new LuxMapException(
                ErrorCodes.ValidationFailed,
                HttpStatusCode.BadRequest,
                "Unknown import kind.",
                new Dictionary<string, object?>
                {
                    ["kind"] = string.Join(", ", ImportKindExtensions.LoadOrder.Select(k => k.ToString().ToLowerInvariant())),
                });
        }

        if (file is null || file.Length == 0)
        {
            throw new LuxMapException(
                ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, "No file was uploaded.");
        }

        var isGeoJson = file.FileName.EndsWith(".geojson", StringComparison.OrdinalIgnoreCase)
            || file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        var isCsv = file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

        if (!isCsv && !isGeoJson)
        {
            throw new LuxMapException(
                ErrorCodes.UnsupportedMediaType,
                HttpStatusCode.UnsupportedMediaType,
                "Upload a .csv or a .geojson file.");
        }

        // UTF8Encoding rather than Encoding.UTF8: the latter EMITS a BOM when writing, which is
        // irrelevant here, but being explicit about not throwing on malformed bytes is not — a file
        // saved as CP1258 must reach the parser and be reported, never crash the request.
        using var reader = new StreamReader(file.OpenReadStream(), new UTF8Encoding(false));
        var text = await reader.ReadToEndAsync(cancellationToken);

        var result = isGeoJson
            ? await service.ImportGeoJsonAsync(importKind, text, cancellationToken)
            : await service.ImportCsvAsync(importKind, text, cancellationToken);

        return Ok(result);
    }
}
