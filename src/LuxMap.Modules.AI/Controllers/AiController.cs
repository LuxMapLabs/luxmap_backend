using Asp.Versioning;
using LuxMap.Modules.AI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.AI.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/ai")]
[Route("api/ai")]
[AllowAnonymous]
public class AiController : ControllerBase
{
    private readonly YoloOnnxService _yoloService;

    public AiController(
        YoloOnnxService yoloService)
    {
        _yoloService = yoloService;
    }

    [HttpPost("detect")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Detect(
        IFormFile image)
    {
        if (image == null ||
            image.Length == 0)
        {
            return BadRequest(new
            {
                message =
                    "Vui lòng upload ảnh."
            });
        }

        var allowed =
            new[]
            {
                "image/jpeg",
                "image/png",
                "image/webp"
            };

        if (!allowed.Contains(
                image.ContentType.ToLower()))
        {
            return BadRequest(new
            {
                message =
                    "Chỉ hỗ trợ JPG, PNG, WEBP."
            });
        }

        var result =
            await _yoloService.DetectAsync(
                image,
                confidenceThreshold: 0.25f,
                iouThreshold: 0.45f
            );

        return Ok(new
        {
            count = result.Count,
            detections = result
        });
    }
}
