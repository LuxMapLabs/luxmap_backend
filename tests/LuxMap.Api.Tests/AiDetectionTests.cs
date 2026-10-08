using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using LuxMap.Modules.AI;
using LuxMap.Modules.AI.Detection;
using LuxMap.Modules.AI.DTOs;
using LuxMap.Modules.Survey.Processing.Frames;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LuxMap.Api.Tests;

/// <summary>
/// AI-1 — the YOLO ON / OFF model as the survey detector and as <c>POST /ai/detect</c> (PR #118 reworked after review).
/// </summary>
[Collection(nameof(ScopeCollection))]
public sealed class AiDetectionTests(ScopeTestFixture factory)
{
    private const string Detect = "/api/v1/ai/detect";

    // ── Post-processing on hand-built tensors (no model) ───────────────────────────────────────

    /// <summary>
    /// A 1280×720 image letterboxes to scale 0.5 with 140 px of padding top and bottom. A box centred at (320, 320), 100 × 50 in
    /// model input pixels is (540, 310)–(740, 410) in the original. Below-threshold and non-finite candidates are dropped.
    /// </summary>
    [Fact]
    public void Output_boxes_map_back_through_the_letterbox_and_junk_is_dropped()
    {
        var box = Letterbox.For(1280, 720, YoloModel.InputSize);
        Assert.Equal((0.5f, 0, 140), (box.Scale, box.PadX, box.PadY));

        var output = new DenseTensor<float>([1, 6, 3]);
        Candidate(output, 0, 320, 320, 100, 50, normal: 0.9f, @out: 0.1f);
        Candidate(output, 1, 100, 100, 20, 20, normal: 0.1f, @out: 0.2f);       // below 0.25
        Candidate(output, 2, float.NaN, 100, 20, 20, normal: 0.95f, @out: 0f);   // not finite

        var found = Assert.Single(YoloPostprocess.Parse(output, YoloModel.Classes, 0.25f, box));

        Assert.Equal("normal", found.ClassName);
        Assert.Equal(0.9f, found.Confidence);
        Assert.Equal((540f, 310f, 740f, 410f), (found.BoundingBox.X1, found.BoundingBox.Y1, found.BoundingBox.X2, found.BoundingBox.Y2));
    }

    [Fact]
    public void Nms_keeps_the_strongest_of_overlapping_boxes_per_class_only()
    {
        DetectionResult Box(int classId, float confidence, float x) => new()
        {
            ClassId = classId, ClassName = YoloModel.Classes[classId], Confidence = confidence,
            BoundingBox = new BoundingBox { X1 = x, Y1 = 0, X2 = x + 100, Y2 = 100 },
        };

        var kept = YoloPostprocess.Nms([Box(0, 0.6f, 5), Box(0, 0.9f, 0), Box(1, 0.7f, 0), Box(0, 0.5f, 500)], 0.45f);

        Assert.Equal([(0, 0.9f), (1, 0.7f), (0, 0.5f)], kept.Select(d => (d.ClassId, d.Confidence)).ToArray());
    }

    // ── The real model ─────────────────────────────────────────────────────────────────────────

    /// <summary>The model that loads is the pinned file, with Ultralytics class names exactly <c>normal, out</c>.</summary>
    [Fact]
    public void The_loaded_model_is_the_pinned_file_with_the_expected_classes()
    {
        var model = factory.Services.GetRequiredService<YoloModel>();
        var file = Path.Combine(AppContext.BaseDirectory, "Models", "best.onnx");

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))), model.Sha256);
        Assert.Equal(model.Sha256, factory.Services.GetRequiredService<AiOptions>().ModelSha256);
    }

    [Fact]
    public void A_model_file_that_does_not_match_the_pin_stops_the_load()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new YoloModel(new AiOptions { ModelSha256 = new string('0', 64) }));
        Assert.Contains("ModelSha256", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// As the survey detector: labels are on / off only, boxes normalised, and the output passes the pipeline's own
    /// <see cref="DetectorValidation"/>. A threshold is part of the artifact identity (BE-34).
    /// </summary>
    [Fact]
    public async Task As_the_survey_detector_its_output_passes_the_pipeline_validation()
    {
        var detector = factory.Services.GetRequiredService<YoloOnOffDetector>();
        var jpeg = NightImage(640, 480);
        var input = new FrameInput("req-1", "FRM-000001", jpeg, 640, 480, detector.Artifact.Version);

        var output = await detector.DetectAsync(input, CancellationToken.None);

        Assert.Equal(detector.Artifact.Version, output.ModelVersion);
        Assert.All(output.Predictions, p => Assert.Contains(p.Label, new[] { "on", "off" }));
        Assert.StartsWith("yolo-", detector.Artifact.Version, StringComparison.Ordinal);

        var model = factory.Services.GetRequiredService<YoloModel>();
        var stricter = new YoloOnOffDetector(new YoloModel(model.Options with { ConfidenceThreshold = 0.5f }));
        Assert.NotEqual(detector.Artifact.Version, stricter.Artifact.Version);
    }

    /// <summary><c>SurveyProcessing:Frames:Detector = yolo</c> makes the pipeline use this model (BE-15 §6).</summary>
    [Fact]
    public void Choosing_yolo_makes_the_survey_pipeline_use_the_model()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("SurveyProcessing:Frames:Detector", "yolo"));

        var detector = host.Services.GetRequiredService<IOnOffDetector>();

        Assert.IsType<YoloOnOffDetector>(detector);
        Assert.False(detector.IsFake);
    }

    // ── The endpoint ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_manager_gets_the_detections_and_the_model_version()
    {
        var response = await (await ClientAsync("engineer", "SEED_ENGINEER_PASSWORD")).PostAsync(Detect, Upload(NightImage(800, 600)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(["count", "detections", "height", "model_version", "width"],
            body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal((800, 600), (body.GetProperty("width").GetInt32(), body.GetProperty("height").GetInt32()));
        Assert.Equal(body.GetProperty("count").GetInt32(), body.GetProperty("detections").GetArrayLength());
        Assert.Equal(factory.Services.GetRequiredService<YoloOnOffDetector>().Artifact.Version, body.GetProperty("model_version").GetString());
    }

    [Fact]
    public async Task Nobody_unauthenticated_and_no_one_but_a_manager_may_run_the_model()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsync(Detect, Upload(NightImage(64, 64)))).StatusCode);
        var crew = await ClientAsync("crew", "SEED_CREW_PASSWORD");
        Assert.Equal(HttpStatusCode.Forbidden, (await crew.PostAsync(Detect, Upload(NightImage(64, 64)))).StatusCode);

        // PR #118's unversioned alias is gone.
        var manager = await ClientAsync("engineer", "SEED_ENGINEER_PASSWORD");
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsync("/api/ai/detect", Upload(NightImage(64, 64)))).StatusCode);
    }

    /// <summary>
    /// JPEG by magic bytes, whatever the Content-Type says (BE-11 rule 5) — a PNG sent as <c>image/jpeg</c> never reaches a
    /// decoder. That is what keeps the suppressed ImageSharp advisories unreachable.
    /// </summary>
    [Fact]
    public async Task A_non_jpeg_is_a_415_even_when_it_claims_to_be_one_and_no_file_is_a_400()
    {
        var manager = await ClientAsync("engineer", "SEED_ENGINEER_PASSWORD");
        using var png = new MemoryStream();
        using (var image = new Image<Rgb24>(32, 32))
        {
            await image.SaveAsPngAsync(png);
        }

        var disguised = await manager.PostAsync(Detect, Upload(png.ToArray(), "image/jpeg"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, disguised.StatusCode);
        Assert.Equal("UNSUPPORTED_IMAGE_FORMAT", Code(await disguised.Content.ReadAsStringAsync()));

        var empty = await manager.PostAsync(Detect, new MultipartFormDataContent());
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("VALIDATION_FAILED", Code(await empty.Content.ReadAsStringAsync()));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static void Candidate(DenseTensor<float> output, int i, float x, float y, float w, float h, float normal, float @out)
    {
        output[0, 0, i] = x;
        output[0, 1, i] = y;
        output[0, 2, i] = w;
        output[0, 3, i] = h;
        output[0, 4, i] = normal;
        output[0, 5, i] = @out;
    }

    /// <summary>A dark frame with one bright glow — not a real lamp, so the test asserts the contract, never a detection.</summary>
    private static byte[] NightImage(int width, int height)
    {
        using var image = new Image<Rgb24>(width, height, new Rgb24(8, 8, 16));
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var d = Math.Sqrt(Math.Pow(x - width / 2.0, 2) + Math.Pow(y - height / 3.0, 2));
                    var v = (byte)Math.Clamp(255 - d * 6, 8, 255);
                    row[x] = new Rgb24(v, v, (byte)Math.Max(v * 0.8, 16));
                }
            }
        });
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string contentType = "image/jpeg")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "image", "lamp.jpg" } };
    }

    private static string? Code(string body) => JsonDocument.Parse(body).RootElement.GetProperty("error").GetProperty("code").GetString();

    private async Task<HttpClient> ClientAsync(string username, string passwordVariable)
    {
        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(username, passwordVariable);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }
}
