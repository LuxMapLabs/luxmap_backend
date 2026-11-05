using LuxMap.Modules.Survey.Processing.Frames;
using LuxMap.Modules.AI;
using LuxMap.Modules.AI.Controllers;
using LuxMap.Modules.AI.Detection;
using LuxMap.Modules.AI.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LuxMap.Api.Tests;

public sealed class DayNightTests
{
    [Theory]
    [InlineData(0.9f, 0.1f, "day")]
    [InlineData(0.1f, 0.9f, "night")]
    [InlineData(0.69f, 0.31f, "unknown")]
    [InlineData(0.3f, 0.7f, "night")]
    [InlineData(0.5f, 0.5f, "unknown")]
    public void Confidence_gate_uses_probabilities_without_another_softmax(float day, float night, string expected)
        => Assert.Equal(Enum.Parse<DayNightClassification>(expected, true), DayNightOnnxService.Classify(day, night, 0.7f));

    [Theory]
    [InlineData("day")]
    [InlineData("unknown")]
    public async Task Rejection_never_resolves_yolo(string label)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var controller = Controller(label, services);
        var response = await controller.DetectAsync(Upload(), CancellationToken.None);
        var body = Assert.IsType<AiClassificationResponse>(Assert.IsType<OkObjectResult>(response).Value);
        Assert.Equal(Enum.Parse<DayNightClassification>(label, true), body.Classification);
        Assert.Equal(label == "day" ? 99.56 : 62.35, body.ConfidencePercent);
    }

    [Fact]
    public async Task Night_keeps_the_detection_response()
    {
        using var services = new ServiceCollection().AddSingleton(new AiOptions())
            .AddSingleton<YoloModel>().AddSingleton<YoloOnOffDetector>().BuildServiceProvider();
        var response = Assert.IsType<OkObjectResult>(await Controller("night", services).DetectAsync(Upload(), CancellationToken.None));
        var result = Assert.IsType<AiDetectResponse>(response.Value);
        Assert.Equal(DayNightClassification.Night, result.Classification);
        Assert.Equal(99.56, result.ConfidencePercent);
        Assert.Equal((320, 240), (result.Width, result.Height));
        Assert.Equal(result.Detections.Count, result.Count);
    }

    [Fact]
    public async Task Real_model_loads_and_runs_and_rejects_invalid_uploads()
    {
        using var model = new DayNightOnnxService(new DayNightOptions());
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AI", "day.jpg"));
        Assert.Equal(DayNightClassification.Day, (await model.ClassifyAsync(stream, CancellationToken.None)).Classification);
        await Assert.ThrowsAsync<LuxMap.Shared.Http.LuxMapException>(() => model.ClassifyAsync(new MemoryStream([1, 2, 3]), CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => model.ClassifyAsync(stream, new CancellationToken(true)));
        using var small = new DayNightOnnxService(new DayNightOptions { MaxPixels = 10 });
        await Assert.ThrowsAsync<LuxMap.Shared.Http.LuxMapException>(() => small.ClassifyAsync(stream, CancellationToken.None));
    }

    [Fact]
    public void Preprocessing_center_crops_and_keeps_rgb_planes_in_zero_one_range()
    {
        using var image = new Image<Rgb24>(448, 224, new Rgb24(255, 0, 0));
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
                rows.GetRowSpan(y).Slice(112, 224).Fill(new Rgb24(0, 128, 255));
        });
        var tensor = DayNightOnnxService.ToTensor(image);
        Assert.Equal(new[] { 1, 3, 224, 224 }, tensor.Dimensions.ToArray());
        Assert.Equal(0, tensor[0, 0, 0, 0]);
        Assert.Equal(128f / 255, tensor[0, 1, 100, 100]);
        Assert.Equal(1, tensor[0, 2, 223, 223]);
    }

    internal sealed class Stub(string label) : IDayNightClassifier
    {
        public MediaArtifact Artifact { get; } = new("daynight-stub", "stub-hash", "{}");
        public Task<DayNightResult> ClassifyAsync(Stream jpeg, CancellationToken ct) => Task.FromResult(new DayNightResult(Enum.Parse<DayNightClassification>(label, true), label == "unknown" ? 0.623456f : 0.9955745f));
    }

    [Theory]
    [InlineData("day.jpg", DayNightClassification.Day)]
    [InlineData("night.jpg", DayNightClassification.Night)]
    public async Task Real_fixtures_verify_class_order(string filename, DayNightClassification expected)
    {
        using var model = new DayNightOnnxService(new DayNightOptions());
        using var image = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AI", filename));
        Assert.InRange(image.Length, 1, 100_000);
        var result = await model.ClassifyAsync(image, CancellationToken.None);
        Assert.Equal(expected, result.Classification);
        Assert.InRange(result.Confidence, 0.7f, 1f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(float.NaN)]
    public void Invalid_threshold_is_rejected(float threshold)
        => Assert.Throws<InvalidOperationException>(() => new DayNightOptions { ConfidenceThreshold = threshold }.Validate());

    [Theory]
    [InlineData("")]
    [InlineData("abcd")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Invalid_pin_is_rejected(string pin)
        => Assert.Throws<InvalidOperationException>(() => new DayNightOptions { ModelSha256 = pin }.Validate());

    [Fact]
    public void Artifact_tracks_model_and_settings()
    {
        using var model = new DayNightOnnxService(new DayNightOptions());
        using var changed = new DayNightOnnxService(new DayNightOptions { ConfidenceThreshold = 0.8f });
        Assert.NotEqual(model.Artifact.Version, changed.Artifact.Version);
        // Independently recompute identity from the exact metadata payload exposed by the artifact.
        var raw = model.Artifact.Metadata;
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
        Assert.Equal("daynight-" + hash[..16], model.Artifact.Version);
        using var json = System.Text.Json.JsonDocument.Parse(raw);
        var body = json.RootElement;
        Assert.Equal(new[] { "classes", "confidence_threshold", "crop", "exif_orientation", "input_shape", "mean", "model_sha256", "output_shape", "provider", "resize", "std", "unknown_rule" },
            body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("day", body.GetProperty("classes").GetProperty("0").GetString());
        Assert.Equal("night", body.GetProperty("classes").GetProperty("1").GetString());
        Assert.Equal(new DayNightOptions().ModelSha256, body.GetProperty("model_sha256").GetString());
    }

    private static AiController Controller(string label, IServiceProvider services) => new(new Stub(label))
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } },
    };

    private static IFormFile Upload()
    {
        using var image = new Image<Rgb24>(320, 240, new Rgb24(10, 10, 10));
        var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "image", "test.jpg");
    }
}
