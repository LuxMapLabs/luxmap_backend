using LuxMap.Modules.AI.DTOs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LuxMap.Modules.AI.Services;

public class YoloOnnxService : IDisposable
{
    private readonly InferenceSession _session;

    private const int InputWidth = 640;
    private const int InputHeight = 640;

    // PHẢI đúng thứ tự class lúc train
    private readonly string[] _classNames =
    {
        "normal",
        "out"
    };

    public YoloOnnxService(IWebHostEnvironment environment)
    {
        var candidates = new[]
        {
            Path.Combine(environment.ContentRootPath, "src", "LuxMap.Modules.AI", "Models", "best.onnx"),
            Path.Combine(environment.ContentRootPath, "Models", "best.onnx"),
            Path.Combine(AppContext.BaseDirectory, "Models", "best.onnx"),
            Path.Combine(AppContext.BaseDirectory, "src", "LuxMap.Modules.AI", "Models", "best.onnx")
        };

        var modelPath = candidates.FirstOrDefault(File.Exists)
            ?? Path.Combine(environment.ContentRootPath, "Models", "best.onnx");

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException(
                $"Không tìm thấy model: {modelPath}"
            );
        }

        _session = new InferenceSession(modelPath);
    }

    public async Task<List<DetectionResult>> DetectAsync(
        IFormFile imageFile,
        float confidenceThreshold = 0.25f,
        float iouThreshold = 0.45f)
    {
        using var stream = imageFile.OpenReadStream();

        using var original =
            await Image.LoadAsync<Rgb24>(stream);

        int originalWidth = original.Width;
        int originalHeight = original.Height;

        // =========================
        // 1. Letterbox Resize
        // =========================

        float scale = Math.Min(
            InputWidth / (float)originalWidth,
            InputHeight / (float)originalHeight
        );

        int resizedWidth =
            (int)Math.Round(originalWidth * scale);

        int resizedHeight =
            (int)Math.Round(originalHeight * scale);

        int padX =
            (InputWidth - resizedWidth) / 2;

        int padY =
            (InputHeight - resizedHeight) / 2;

        using var resized = original.Clone(ctx =>
        {
            ctx.Resize(resizedWidth, resizedHeight);
        });

        using var canvas =
            new Image<Rgb24>(
                InputWidth,
                InputHeight,
                new Rgb24(114, 114, 114)
            );

        canvas.Mutate(ctx =>
        {
            ctx.DrawImage(
                resized,
                new Point(padX, padY),
                1f
            );
        });

        // =========================
        // 2. Image -> Tensor
        // [1,3,640,640]
        // =========================

        var tensor =
            new DenseTensor<float>(
                new[]
                {
                    1,
                    3,
                    InputHeight,
                    InputWidth
                });

        canvas.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < InputHeight; y++)
            {
                var row = accessor.GetRowSpan(y);

                for (int x = 0; x < InputWidth; x++)
                {
                    var pixel = row[x];

                    tensor[0, 0, y, x] =
                        pixel.R / 255f;

                    tensor[0, 1, y, x] =
                        pixel.G / 255f;

                    tensor[0, 2, y, x] =
                        pixel.B / 255f;
                }
            }
        });

        // =========================
        // 3. Run ONNX
        // =========================

        string inputName =
            _session.InputMetadata.Keys.First();

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(
                inputName,
                tensor
            )
        };

        using var results =
            _session.Run(inputs);

        var output =
            results.First()
                   .AsTensor<float>();

        // =========================
        // 4. Parse YOLO output
        // =========================

        var detections = ParseOutput(
            output,
            confidenceThreshold,
            scale,
            padX,
            padY,
            originalWidth,
            originalHeight
        );

        // =========================
        // 5. NMS
        // =========================

        return ApplyNms(
            detections,
            iouThreshold
        );
    }

    private List<DetectionResult> ParseOutput(
        Tensor<float> output,
        float threshold,
        float scale,
        int padX,
        int padY,
        int originalWidth,
        int originalHeight)
    {
        var detections =
            new List<DetectionResult>();

        var dims = output.Dimensions.ToArray();

        // YOLO11 thông thường:
        // [1, 6, 8400]
        //
        // 6 =
        // x, y, w, h
        // normal_score
        // out_score

        bool channelFirst =
            dims.Length == 3 &&
            dims[1] == 4 + _classNames.Length;

        int predictionCount =
            channelFirst
                ? dims[2]
                : dims[1];

        for (int i = 0; i < predictionCount; i++)
        {
            float centerX;
            float centerY;
            float width;
            float height;

            if (channelFirst)
            {
                centerX = output[0, 0, i];
                centerY = output[0, 1, i];
                width = output[0, 2, i];
                height = output[0, 3, i];
            }
            else
            {
                // phòng trường hợp output:
                // [1,8400,6]

                centerX = output[0, i, 0];
                centerY = output[0, i, 1];
                width = output[0, i, 2];
                height = output[0, i, 3];
            }

            int bestClassId = -1;
            float bestConfidence = 0;

            for (int classId = 0;
                 classId < _classNames.Length;
                 classId++)
            {
                float score;

                if (channelFirst)
                {
                    score =
                        output[
                            0,
                            4 + classId,
                            i
                        ];
                }
                else
                {
                    score =
                        output[
                            0,
                            i,
                            4 + classId
                        ];
                }

                if (score > bestConfidence)
                {
                    bestConfidence = score;
                    bestClassId = classId;
                }
            }

            if (bestConfidence < threshold)
                continue;

            // YOLO xywh -> xyxy

            float x1 =
                centerX - width / 2f;

            float y1 =
                centerY - height / 2f;

            float x2 =
                centerX + width / 2f;

            float y2 =
                centerY + height / 2f;

            // =========================
            // Convert về ảnh gốc
            // =========================

            x1 = (x1 - padX) / scale;
            y1 = (y1 - padY) / scale;

            x2 = (x2 - padX) / scale;
            y2 = (y2 - padY) / scale;

            x1 = Math.Clamp(
                x1,
                0,
                originalWidth
            );

            y1 = Math.Clamp(
                y1,
                0,
                originalHeight
            );

            x2 = Math.Clamp(
                x2,
                0,
                originalWidth
            );

            y2 = Math.Clamp(
                y2,
                0,
                originalHeight
            );

            if (x2 <= x1 || y2 <= y1)
                continue;

            detections.Add(
                new DetectionResult
                {
                    ClassId = bestClassId,

                    ClassName =
                        _classNames[bestClassId],

                    Confidence =
                        bestConfidence,

                    BoundingBox =
                        new BoundingBox
                        {
                            X1 = x1,
                            Y1 = y1,
                            X2 = x2,
                            Y2 = y2
                        }
                }
            );
        }

        return detections;
    }

    private List<DetectionResult> ApplyNms(
        List<DetectionResult> detections,
        float iouThreshold)
    {
        var finalResults =
            new List<DetectionResult>();

        // NMS riêng từng class
        foreach (var group in detections
                     .GroupBy(x => x.ClassId))
        {
            var boxes = group
                .OrderByDescending(
                    x => x.Confidence)
                .ToList();

            while (boxes.Count > 0)
            {
                var best = boxes[0];

                finalResults.Add(best);

                boxes.RemoveAt(0);

                boxes = boxes
                    .Where(x =>
                        CalculateIoU(
                            best.BoundingBox,
                            x.BoundingBox
                        ) < iouThreshold)
                    .ToList();
            }
        }

        return finalResults;
    }

    private static float CalculateIoU(
        BoundingBox a,
        BoundingBox b)
    {
        float intersectionX1 =
            Math.Max(a.X1, b.X1);

        float intersectionY1 =
            Math.Max(a.Y1, b.Y1);

        float intersectionX2 =
            Math.Min(a.X2, b.X2);

        float intersectionY2 =
            Math.Min(a.Y2, b.Y2);

        float intersectionWidth =
            Math.Max(
                0,
                intersectionX2 -
                intersectionX1
            );

        float intersectionHeight =
            Math.Max(
                0,
                intersectionY2 -
                intersectionY1
            );

        float intersectionArea =
            intersectionWidth *
            intersectionHeight;

        float areaA =
            Math.Max(0, a.X2 - a.X1) *
            Math.Max(0, a.Y2 - a.Y1);

        float areaB =
            Math.Max(0, b.X2 - b.X1) *
            Math.Max(0, b.Y2 - b.Y1);

        float union =
            areaA +
            areaB -
            intersectionArea;

        if (union <= 0)
            return 0;

        return intersectionArea / union;
    }

    public void Dispose()
    {
        _session.Dispose();
    }
}
