using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using LuxMap.Infrastructure.Storage;
using LuxMap.Modules.AI.DTOs;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LuxMap.Modules.AI.Detection;

/// <summary>What the model saw in one image: its size after EXIF orientation, and the boxes in its pixels.</summary>
public sealed record YoloResult(int Width, int Height, IReadOnlyList<DetectionResult> Detections);

/// <summary>
/// The YOLO ON / OFF model (classes <c>normal</c>, <c>out</c>), loaded once. Used by the survey pipeline through
/// <see cref="YoloOnOffDetector"/> and by <c>POST /ai/detect</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Decoding is JPEG-only</b> (magic bytes, then <see cref="ThumbnailFactory.JpegOnly"/>) — the premise under which the
/// ImageSharp 3.1.12 advisories are suppressed. The PR #118 version decoded with the default configuration and trusted the
/// client's Content-Type, so a BigTIFF labelled <c>image/jpeg</c> reached the vulnerable TIFF decoder.
/// </para>
/// <para>
/// The model is CHECKED at load: one input <c>[1,3,640,640]</c>, one output <c>[1, 4 + classes, N]</c>, and the class names
/// stored by Ultralytics in the metadata must be exactly <c>normal, out</c> in that order — a retrained model with swapped
/// classes would otherwise report every lit lamp as out.
/// </para>
/// <para>
/// Preprocessing follows Ultralytics: EXIF-oriented RGB, letterbox to 640 with grey 114 padding, BILINEAR resize
/// (<c>cv2.INTER_LINEAR</c> — ImageSharp's default is bicubic), values / 255.
/// </para>
/// </remarks>
public sealed partial class YoloModel : IDisposable
{
    public const int InputSize = 640;

    /// <summary>Training order. Index = class id.</summary>
    public static readonly IReadOnlyList<string> Classes = ["normal", "out"];

    private readonly InferenceSession session;
    private readonly string inputName;
    private readonly SemaphoreSlim gate;
    private readonly AiOptions options;

    public YoloModel(AiOptions options)
    {
        this.options = options;
        var path = Path.IsPathRooted(options.ModelPath) ? options.ModelPath : Path.Combine(AppContext.BaseDirectory, options.ModelPath);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"{AiOptions.SectionName}:ModelPath — no model at {path}.");
        }

        var bytes = File.ReadAllBytes(path);
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (options.ModelSha256 is { Length: > 0 } pinned && !string.Equals(pinned, Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{AiOptions.SectionName}:ModelSha256 pins {pinned}, but {path} is {Sha256}. A model change must change the pin too.");
        }

        session = new InferenceSession(bytes);
        try
        {
            inputName = CheckShape(session);
        }
        catch
        {
            session.Dispose();
            throw;
        }

        gate = new SemaphoreSlim(options.MaxConcurrency);
    }

    /// <summary>SHA-256 of the model file actually loaded.</summary>
    public string Sha256 { get; }

    public AiOptions Options => options;

    /// <summary>Decodes a JPEG and runs the model on it. A file that is not a decodable JPEG is a 415; too many pixels a 400.</summary>
    public async Task<YoloResult> DetectAsync(Stream jpeg, CancellationToken ct)
    {
        jpeg.Position = 0;
        await JpegMagicBytes.EnsureJpegAsync(jpeg, ct);
        jpeg.Position = 0;

        // The gate covers decoding and resizing too, not only inference: a 40 MP decode is the expensive part of a large upload
        // (Codex review). The signature check above is cheap and stays outside.
        await gate.WaitAsync(ct);
        try
        {
            return await DecodeAndRunAsync(jpeg, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<YoloResult> DecodeAndRunAsync(Stream jpeg, CancellationToken ct)
    {
        var decoder = new DecoderOptions { Configuration = ThumbnailFactory.JpegOnly };
        Image<Rgb24> image;
        try
        {
            var info = await Image.IdentifyAsync(decoder, jpeg, ct);
            if ((long)info.Width * info.Height > options.MaxPixels)
            {
                throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                    $"The image has more than {options.MaxPixels} pixels.",
                    new Dictionary<string, object?> { ["width"] = info.Width, ["height"] = info.Height, ["max_pixels"] = options.MaxPixels });
            }

            jpeg.Position = 0;
            image = await Image.LoadAsync<Rgb24>(decoder, jpeg, ct);
        }
        catch (Exception error) when (error is ImageFormatException or UnknownImageFormatException)
        {
            throw new LuxMapException(ErrorCodes.UnsupportedImageFormat, HttpStatusCode.UnsupportedMediaType,
                "The upload starts like a JPEG but cannot be decoded as one: " + error.Message);
        }

        using (image)
        {
            image.Mutate(context => context.AutoOrient());
            var box = Letterbox.For(image.Width, image.Height, InputSize);
            var tensor = ToTensor(image, box);

            try
            {
                using var run = new RunOptions();
                using var cancel = ct.Register(() => run.Terminate = true);
                using var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, tensor)], session.OutputNames, run);
                var output = results[0].AsTensor<float>();
                var detections = YoloPostprocess.Nms(
                    YoloPostprocess.Parse(output, Classes, options.ConfidenceThreshold, box), options.IouThreshold);
                return new YoloResult(box.Width, box.Height, detections);
            }
            catch (OnnxRuntimeException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
        }
    }

    public void Dispose()
    {
        session.Dispose();
        gate.Dispose();
    }

    private static DenseTensor<float> ToTensor(Image<Rgb24> image, Letterbox box)
    {
        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new Size(box.ResizedWidth, box.ResizedHeight),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Triangle,
        }));

        const int plane = InputSize * InputSize;
        var data = new float[3 * plane];
        Array.Fill(data, 114f / 255f);
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                var offset = (y + box.PadY) * InputSize + box.PadX;
                for (var x = 0; x < row.Length; x++)
                {
                    data[offset + x] = row[x].R / 255f;
                    data[plane + offset + x] = row[x].G / 255f;
                    data[2 * plane + offset + x] = row[x].B / 255f;
                }
            }
        });

        return new DenseTensor<float>(data, [1, 3, InputSize, InputSize]);
    }

    private static string CheckShape(InferenceSession session)
    {
        var input = session.InputMetadata.Single();
        if (!input.Value.Dimensions.SequenceEqual([1, 3, InputSize, InputSize]))
        {
            throw new InvalidOperationException($"The model input must be [1,3,{InputSize},{InputSize}]; got [{string.Join(',', input.Value.Dimensions)}].");
        }

        var output = session.OutputMetadata.Single().Value.Dimensions;
        if (output.Length != 3 || output[0] != 1 || output[1] != 4 + Classes.Count || output[2] <= 0)
        {
            throw new InvalidOperationException($"The model output must be [1,{4 + Classes.Count},N]; got [{string.Join(',', output)}].");
        }

        // Ultralytics writes names as "{0: 'normal', 1: 'out'}".
        var names = session.ModelMetadata.CustomMetadataMap.TryGetValue("names", out var raw)
            ? NamePattern().Matches(raw).OrderBy(match => int.Parse(match.Groups[1].Value)).Select(match => match.Groups[2].Value).ToArray()
            : [];
        if (!names.SequenceEqual(Classes))
        {
            throw new InvalidOperationException($"The model classes must be [{string.Join(", ", Classes)}] in that order; the metadata says [{string.Join(", ", names)}].");
        }

        return input.Key;
    }

    [GeneratedRegex(@"(\d+)\s*:\s*'([^']*)'")]
    private static partial Regex NamePattern();
}
