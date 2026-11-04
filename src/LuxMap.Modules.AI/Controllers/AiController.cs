using Asp.Versioning;
using LuxMap.Modules.AI.Detection;
using LuxMap.Modules.AI.DTOs;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.AI.Controllers;

/// <summary>
/// Runs the YOLO ON / OFF model on one uploaded JPEG (AI-1, SELF-SIGNED) — for trying the model by hand. Survey frames do not
/// go through here: the pipeline calls the model directly (<see cref="YoloOnOffDetector"/>).
/// </summary>
/// <remarks>
/// Manager only (<c>ReviewSurveys</c> — the role that judges CV results). Never <c>[AllowAnonymous]</c>: PR #118 had it on the
/// class, which let anyone on the internet make the server decode and infer.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/ai")]
public sealed class AiController(IDayNightClassifier dayNight) : ControllerBase
{
    /// <summary>A phone photo is a few MB; the same ceiling as evidence uploads.</summary>
    public const long MaxUploadBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Detects lamps in a JPEG (form field <c>image</c>): <c>normal</c> = ON, <c>out</c> = OFF, boxes in pixels. JPEG by magic
    /// bytes only — anything else is 415, whatever its Content-Type says. Day or low-confidence images return the JSON
    /// day/unknown object with confidence_percent without YOLO; night includes classification confidence and lamp detections.
    /// </summary>
    [HttpPost("detect")]
    [ClientSurface(ClientSurface.Web)]
    [Authorize(Policy = LuxMapPolicies.ReviewSurveys)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    [ProducesResponseType<AiDetectResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> DetectAsync(IFormFile? image, CancellationToken ct)
    {
        if (image is null || image.Length == 0)
        {
            throw new LuxMapException(ErrorCodes.ValidationFailed, System.Net.HttpStatusCode.BadRequest,
                "Send a JPEG in the multipart form field `image`.", new Dictionary<string, object?> { ["field"] = "image" });
        }

        await using var buffer = new MemoryStream();
        await image.CopyToAsync(buffer, ct);
        var classification = await dayNight.ClassifyAsync(buffer, ct);
        if (classification.Classification != DayNightClassification.Night)
            return Ok(new AiClassificationResponse
            {
                Classification = classification.Classification,
                DaynightModelVersion = dayNight.Artifact.Version,
                ConfidencePercent = Math.Round(classification.Confidence * 100d, 2),
            });

        // Resolve only for night: day/unknown must not load or run YOLO.
        var model = HttpContext.RequestServices.GetRequiredService<YoloModel>();
        var detector = HttpContext.RequestServices.GetRequiredService<YoloOnOffDetector>();
        var result = await model.DetectAsync(buffer, ct);

        return Ok(new AiDetectResponse
        {
            Classification = classification.Classification,
            DaynightModelVersion = dayNight.Artifact.Version,
            ConfidencePercent = Math.Round(classification.Confidence * 100d, 2),
            ModelVersion = detector.Artifact.Version,
            Width = result.Width,
            Height = result.Height,
            Count = result.Detections.Count,
            Detections = result.Detections,
        });
    }
}
