using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using LuxMap.Infrastructure.Storage;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LuxMap.Modules.AI.Detection;

public sealed record DayNightResult(string Classification, float Confidence);

public interface IDayNightClassifier
{
    Task<DayNightResult> ClassifyAsync(Stream jpeg, CancellationToken ct);
}

/// <summary>Two-class classifier; unknown is a backend confidence rejection, never a model class.</summary>
public sealed partial class DayNightOnnxService : IDayNightClassifier, IDisposable
{
    public const int InputSize = 224;

    /// <summary>Training order. Index = class id.</summary>
    public static readonly IReadOnlyList<string> Classes = ["day", "night"];

    private readonly InferenceSession session;
    private readonly string inputName;
    private readonly SemaphoreSlim gate;
    private readonly DayNightOptions options;

    public DayNightOnnxService(DayNightOptions options)
    {
        options.Validate();
        this.options = options;
        var path = Path.IsPathRooted(options.ModelPath) ? options.ModelPath : Path.Combine(AppContext.BaseDirectory, options.ModelPath);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"{DayNightOptions.SectionName}:ModelPath â€” no model at {path}.");
        }

        var bytes = File.ReadAllBytes(path);
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (options.ModelSha256 is { Length: > 0 } pinned && !string.Equals(pinned, Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{DayNightOptions.SectionName}:ModelSha256 pins {pinned}, but {path} is {Sha256}. A model change must change the pin too.");
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

    public DayNightOptions Options => options;

    /// <summary>Decodes a JPEG and runs the model on it. A file that is not a decodable JPEG is a 415; too many pixels a 400.</summary>
    public async Task<DayNightResult> ClassifyAsync(Stream jpeg, CancellationToken ct)
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

    private async Task<DayNightResult> DecodeAndRunAsync(Stream jpeg, CancellationToken ct)
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
            // A very narrow JPEG can become enormous when its shortest side is enlarged to 224.
            var shortEdge = Math.Min(image.Width, image.Height);
            var resizedPixels = ((long)image.Width * InputSize / shortEdge)
                * ((long)image.Height * InputSize / shortEdge);
            if (resizedPixels > options.MaxPixels)
                throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                    "The image aspect ratio exceeds the classification resize budget.");
            var tensor = ToTensor(image);

            try
            {
                using var run = new RunOptions();
                using var cancel = ct.Register(() => run.Terminate = true);
                using var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, tensor)], session.OutputNames, run);
                var output = results[0].AsTensor<float>();
                return new DayNightResult(Classify(output[0, 0], output[0, 1], options.ConfidenceThreshold),
                    Math.Max(output[0, 0], output[0, 1]));
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

    public static string Classify(float day, float night, float threshold)
    {
        if (!float.IsFinite(day) || !float.IsFinite(night) || day < 0 || night < 0
            || day > 1 || night > 1 || Math.Abs(day + night - 1) > 0.001f)
            throw new InvalidOperationException("Day/night output must contain two softmax probabilities.");
        if (day == night || Math.Max(day, night) < threshold) return "unknown";
        return day > night ? "day" : "night";
    }

    // Ultralytics classify_transforms: shortest-edge resize, center crop, RGB / 255;
    // mean=(0,0,0), std=(1,1,1). Unlike detection, there is no letterbox.
    public static DenseTensor<float> ToTensor(Image<Rgb24> image)
    {
        var shortEdge = Math.Min(image.Width, image.Height);
        var width = (int)((long)image.Width * InputSize / shortEdge);
        var height = (int)((long)image.Height * InputSize / shortEdge);
        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new Size(width, height), Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Triangle, Compand = false,
        }).Crop(new Rectangle((int)Math.Round((width - InputSize) / 2.0),
            (int)Math.Round((height - InputSize) / 2.0), InputSize, InputSize)));
        const int plane = InputSize * InputSize;
        var data = new float[3 * plane];
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var offset = y * InputSize + x;
                    data[offset] = row[x].R / 255f;
                    data[plane + offset] = row[x].G / 255f;
                    data[2 * plane + offset] = row[x].B / 255f;
                }
            }
        });
        return new DenseTensor<float>(data, [1, 3, InputSize, InputSize]);
    }

    private static string CheckShape(InferenceSession session)
    {
        var input = session.InputMetadata.Single();
        if (input.Value.ElementType != typeof(float) || !input.Value.Dimensions.SequenceEqual([1, 3, InputSize, InputSize]))
        {
            throw new InvalidOperationException($"The model input must be [1,3,{InputSize},{InputSize}]; got [{string.Join(',', input.Value.Dimensions)}].");
        }

        var output = session.OutputMetadata.Single().Value;
        if (output.ElementType != typeof(float) || !output.Dimensions.SequenceEqual([1, 2]))
            throw new InvalidOperationException("The day/night output must be float32 [1,2].");
        if (!session.ModelMetadata.CustomMetadataMap.TryGetValue("task", out var task) || task != "classify")
            throw new InvalidOperationException("The day/night model must declare task=classify.");

        // Ultralytics writes names as "{0: 'day', 1: 'night'}".
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
