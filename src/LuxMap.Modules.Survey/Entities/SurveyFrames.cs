using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Survey.Entities;

public sealed class SurveyFrame : IImmutableRecord
{
    public string FrameId { get; set; } = null!;
    public required string SweepId { get; set; }
    public long ClipId { get; set; }
    public long PtsNs { get; set; }
    public long PhoneElapsedNs { get; set; }
    public long ExtractorVersionId { get; set; }
    public required string ObjectKey { get; set; }
    public required string ThumbnailKey { get; set; }
    public required string Sha256 { get; set; }
    public long ByteCount { get; set; }
    public long ThumbnailBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DataSource DataSource { get; set; }
}
public sealed class Detection : IImmutableRecord
{
    public string DetectionId { get; set; } = null!;
    public SurveyProcessingRun Run { get; set; } = null!;
    public long RunId { get; set; }
    public required string SweepId { get; set; }
    public required string FrameId { get; set; }
    public int ItemNo { get; set; }
    public required string CvState { get; set; }
    public double Confidence { get; set; }
    public double BboxX { get; set; }
    public double BboxY { get; set; }
    public double BboxWidth { get; set; }
    public double BboxHeight { get; set; }
    public long ModelVersionId { get; set; }
    public required string RawPrediction { get; set; }
}
