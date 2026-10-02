using System.ComponentModel.DataAnnotations;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Survey.Ingest;

/// <summary>SELF-SIGNED BE-15 P2a; temporary API until FW confirmation. Nanoseconds are decimal strings.</summary>
public sealed record CreateSweepRequest(
    [Required] string WorkOrderId, Guid ClientOpId, Guid BootSessionId,
    [Required] string ElapsedAnchorNs, DateTime UtcAnchor, double UtcUncertaintyMs, [Required] DataSource? DataSource,
    [Required] string StartedElapsedNs);
public sealed record ClipManifest(int ClipNo, string Sha256);
public sealed record SweepManifest([Required] ClipManifest[] Clips, [Required] string GpsHash,
    [Required] string LuxHash, [Required] string ConfigHash);
public sealed record SubmitSweepRequest(Guid ClientOpId, [Required] string EndedElapsedNs, [Required] SweepManifest Manifest);
public sealed record SurveyClipResponse(int ClipNo, string Sha256, long ByteCount, string ContentType);
public sealed record SurveyRawResponse(SurveyRawKind Kind, string Sha256, long ByteCount, int SchemaVersion);
public sealed record SweepResponse(string SweepId, string WorkOrderId, DateTime? StartedAt, DateTime? EndedAt,
    string[] SegmentIds, int FrameCount, double? CoveragePct, SweepProcessingStatus ProcessingStatus,
    SweepStatus Status, DataSource DataSource, DateTime? SubmittedAt, SurveyClipResponse[] Clips, SurveyRawResponse[] RawFiles);
