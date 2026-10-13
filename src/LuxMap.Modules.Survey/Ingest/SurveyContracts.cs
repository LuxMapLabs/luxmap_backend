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
public record SweepResponse(string SweepId, string WorkOrderId, DateTime? StartedAt, DateTime? EndedAt,
    string[] SegmentIds, int FrameCount, double? CoveragePct, SweepProcessingStatus ProcessingStatus,
    SweepStatus Status, DataSource DataSource, DateTime? SubmittedAt, SurveyClipResponse[] Clips, SurveyRawResponse[] RawFiles, uint Version);

/// <summary>
/// <c>GET /sweeps/{id}</c> only (BE-17, D-1 / D-5): the sweep plus its latest processing attempt, so a failed sweep says WHY — the
/// same code the Manager's notice already names. The list stays light and carries no run.
/// </summary>
public sealed record SweepDetailResponse(string SweepId, string WorkOrderId, DateTime? StartedAt, DateTime? EndedAt,
    string[] SegmentIds, int FrameCount, double? CoveragePct, SweepProcessingStatus ProcessingStatus,
    SweepStatus Status, DataSource DataSource, DateTime? SubmittedAt, SurveyClipResponse[] Clips, SurveyRawResponse[] RawFiles, uint Version,
    SweepRunSummary? LatestRun)
    : SweepResponse(SweepId, WorkOrderId, StartedAt, EndedAt, SegmentIds, FrameCount, CoveragePct, ProcessingStatus, Status, DataSource,
        SubmittedAt, Clips, RawFiles, Version);

/// <summary>
/// The latest FINISHED processing attempt; null until one finishes. While a sweep is processing this is still the previous attempt —
/// runs are written when they end (D-09), and live progress is <c>processing_status</c> (D-1: no stage while running).
/// </summary>
/// <param name="ResultState"><c>succeeded</c> or <c>failed</c>.</param>
/// <param name="Stage">Where it stopped: <c>complete</c> on success, otherwise the failing step (<c>clock</c>, <c>extractor</c>, …). Open list.</param>
/// <param name="ErrorCode">Null on success. Open list — the pipeline adds codes (drift BE-17).</param>
public sealed record SweepRunSummary(long RunId, int Attempt, string ResultState, string Stage, string? ErrorCode,
    DateTime StartedAt, DateTime FinishedAt, double? CoveragePct, double? DetectionCoveragePct, double? DimCoveragePct, string? CoverageReason);
