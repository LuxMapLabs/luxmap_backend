using System.Security.Cryptography;
using System.Text;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Storage;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Survey.Processing.Frames;

public sealed record ObservationCv(Association Association, Classification Classification, Review.BaselineReference? Baseline = null);
public sealed class FramePipelineResult
{
    public List<SurveyFrame> NewFrames { get; } = [];
    public List<Detection> Detections { get; } = [];
    public Dictionary<(int PassNo, string Pole, long Time), ObservationCv> Observations { get; } = [];
    public int FrameCount { get; set; }
}

public sealed class SurveyFramePipeline(IObjectStore store, IFrameExtractor extractor, IOnOffDetector detector,
    SurveyFrameOptions options)
{
    public async Task<FramePipelineResult> ProcessAsync(LuxMapDbContext db, SurveySweep sweep, SurveyProcessingRun run,
        List<(string Segment, double Length, PassResult Pass)> passes, ProjectedPole[] poles,
        Func<CancellationToken, Task> heartbeat, double leaseSeconds, CancellationToken ct)
    {
        // D-06 and D-07 block real field processing; fake results never attach to field assets.
        if (sweep.DataSource != DataSource.Simulated) throw new ProcessingFailure("VIDEO_DEVICE_MAPPING_PENDING", "video_clock");
        var clips = await db.Set<SurveyVideoClip>().Where(x => x.SweepId == sweep.SweepId).OrderBy(x => x.ClipNo).ToArrayAsync(ct);
        var config = await db.Set<SurveyRawFile>().SingleAsync(x => x.SweepId == sweep.SweepId && x.Kind == SurveyRawKind.CaptureConfig, ct);
        var configJson = await WithHeartbeat(async token =>
        {
            await using var raw = await store.OpenAsync(StorageBucket.Video, config.ObjectKey, token);
            using var memory = new MemoryStream(); var buffer = new byte[81920]; int read;
            while ((read = await raw.ReadAsync(buffer, token)) > 0)
            {
                if (memory.Length + read > 10 * 1024 * 1024) throw new ProcessingFailure("CLOCK_VIDEO_MAPPING", "video_clock");
                memory.Write(buffer, 0, read);
            }
            var bytes = memory.ToArray();
            if (bytes.LongLength != config.ByteCount || Convert.ToHexStringLower(SHA256.HashData(bytes)) != config.Sha256)
                throw new ProcessingFailure("CLOCK_VIDEO_MAPPING", "video_clock");
            return Encoding.UTF8.GetString(bytes);
        }, heartbeat, leaseSeconds, options.TimeoutSeconds, ct);
        var mapping = VideoClockMapping.Parse(configJson, clips.Select(x => x.ClipNo));
        var model = detector.Artifact;
        run.ModelVersionId = await RegisterArtifact(db, "cv_model", model, ct);
        var extraction = await WithHeartbeat(extractor.DescribeAsync, heartbeat, leaseSeconds, options.TimeoutSeconds, ct);
        run.ExtractorVersionId = await RegisterArtifact(db, "frame_extractor", extraction, ct);
        var result = new FramePipelineResult();
        var outputs = new Dictionary<long, List<DetectedFrame>>();
        var requests = passes.SelectMany(x => x.Pass.Observations).Select(p => new FrameWindow(p.TimeNs,
            Math.Max(0, p.TimeNs - (long)(options.BeforeSeconds * 1e9)), checked(p.TimeNs + (long)(options.AfterSeconds * 1e9)))).Distinct().ToArray();
        foreach (var clip in clips)
        {
            var clock = mapping.Clips.Single(x => x.ClipNo == clip.ClipNo);
            var windows = requests.Where(w => clock.Covers(w.CenterNs)).ToArray();
            if (windows.Length == 0) continue;
            outputs[clip.ClipId] = [];
            var existing = await db.Set<SurveyFrame>().Where(x => x.ClipId == clip.ClipId && x.ExtractorVersionId == run.ExtractorVersionId).ToDictionaryAsync(x => x.PtsNs, ct);
            // Extraction callback communicates through a bounded channel; only this consumer touches DbContext.
            var channel = System.Threading.Channels.Channel.CreateBounded<ExtractedFrame>(1);
            using var extractionStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var produce = Produce();
            async Task Produce()
            {
                try
                {
                    await extractor.ExtractAsync(clip.ObjectKey, clip.ByteCount, clip.Sha256, clock, windows,
                        async (frame, token) => await channel.Writer.WriteAsync(frame, token), extractionStop.Token);
                    channel.Writer.TryComplete();
                }
                catch (Exception ex) { channel.Writer.TryComplete(ex); throw; }
            }
            try
            {
                while (await WithHeartbeat(async token => await channel.Reader.WaitToReadAsync(token), heartbeat,
                           leaseSeconds, options.TimeoutSeconds * 2, ct))
                {
                    while (channel.Reader.TryRead(out var extracted))
                    {
                        await heartbeat(ct);
                        var hash = Convert.ToHexStringLower(SHA256.HashData(extracted.Jpeg));
                        if (!existing.TryGetValue(extracted.PtsNs, out var frame))
                        {
                            var spec = PrefixedIds.SurveyFrame;
                            // Reserve DB-generated ID before object writes; no row exists until completion.
                            var id = await db.Database.SqlQuery<string>($"SELECT luxmap_format_id({spec.Prefix}, nextval({spec.SequenceName}::regclass), {spec.Digits}) AS \"Value\"").SingleAsync(ct);
                            var stored = await WithHeartbeat(async token =>
                            {
                                using var content = new MemoryStream(extracted.Jpeg, false);
                                return await store.StoreImageAsync(StorageBucket.Survey, id, content, token);
                            }, heartbeat, leaseSeconds, options.TimeoutSeconds, ct);
                            frame = new() { FrameId = id, SweepId = sweep.SweepId, ClipId = clip.ClipId,
                                PtsNs = extracted.PtsNs, PhoneElapsedNs = extracted.PhoneElapsedNs, ExtractorVersionId = run.ExtractorVersionId.Value,
                                ObjectKey = stored.OriginalKey, ThumbnailKey = stored.ThumbnailKey, ByteCount = stored.OriginalBytes,
                                ThumbnailBytes = stored.ThumbnailBytes, Sha256 = hash, Width = extracted.Width, Height = extracted.Height, DataSource = sweep.DataSource };
                            result.NewFrames.Add(frame); existing.Add(frame.PtsNs, frame);
                        }
                        else if (frame.Sha256 != hash) throw new ProcessingFailure("FRAME_REPLAY_MISMATCH", "extractor");
                        var requestId = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(run.InputHash + hash + model.Version)));
                        var input = new FrameInput(requestId, frame.FrameId, extracted.Jpeg, frame.Width, frame.Height, model.Version);
                        DetectorOutput output;
                        try { output = await WithHeartbeat(token => detector.DetectAsync(input, token), heartbeat, leaseSeconds, options.DetectorTimeoutSeconds, ct); }
                        catch (ProcessingFailure ex) when (ex.Code == "MEDIA_TIMEOUT") { throw new ProcessingFailure("DETECTOR_TIMEOUT", "detector"); }
                        DetectorValidation.Validate(input, output);
                        outputs[clip.ClipId].Add(new(frame.FrameId, frame.PhoneElapsedNs, output.Predictions));
                        foreach (var p in output.Predictions)
                            result.Detections.Add(new() { Run = run, SweepId = sweep.SweepId, FrameId = frame.FrameId, ItemNo = p.ItemNo,
                                CvState = p.Label, Confidence = p.Confidence, BboxX = p.X, BboxY = p.Y, BboxWidth = p.Width, BboxHeight = p.Height,
                                ModelVersionId = run.ModelVersionId.Value, RawPrediction = output.RawOutput });
                    }
                }
                await produce;
            }
            finally
            {
                await extractionStop.CancelAsync();
                try { await produce; } catch when (extractionStop.IsCancellationRequested) { /* Consumer reports the original failure. */ }
            }
        }
        await ClassifyObservationsAsync(result, run, sweep, passes, poles, mapping,
            clips.ToDictionary(c => c.ClipNo, c => outputs.GetValueOrDefault(c.ClipId) ?? []), new Review.SurveyBaselineLookup(db), options, ct);
        result.FrameCount = await db.Set<SurveyFrame>().CountAsync(x => x.SweepId == sweep.SweepId, ct) + result.NewFrames.Count;
        return result;
    }

    public static async Task ClassifyObservationsAsync(FramePipelineResult result, SurveyProcessingRun run, SurveySweep sweep,
        List<(string Segment, double Length, PassResult Pass)> passes, ProjectedPole[] poles,
        VideoClockMapping mapping, IReadOnlyDictionary<int, List<DetectedFrame>> outputs,
        ISurveyBaselineLookup baselines, SurveyFrameOptions options, CancellationToken ct)
    {
        foreach (var (pass, passNo) in passes.Select((pass, index) => (pass, index)))
        foreach (var p in pass.Pass.Observations)
        {
            var clock = mapping.Clips.SingleOrDefault(c => c.Covers(p.TimeNs));
            var flags = p.Flags.ToList();
            if (clock is null) flags.Add("video_gap");
            var frames = clock is null ? [] : outputs.GetValueOrDefault(clock.ClipNo) ?? [];
            var pole = poles.Single(x => x.PoleId == p.PoleId && x.SegmentId == pass.Segment);
            int side = Math.Sign(pole.SideOfRoute) * (pass.Pass.Track[^1].ChainageM > pass.Pass.Track[0].ChainageM ? 1 : -1);
            var association = DetectionAssociation.Match(frames, Math.Max(0, p.TimeNs - (long)(options.BeforeSeconds * 1e9)),
                checked(p.TimeNs + (long)(options.AfterSeconds * 1e9)), mapping.CameraSide, side, options);
            var before = sweep.AtElapsed(sweep.StartedElapsedNs)
                ?? throw new InvalidOperationException("A submitted sweep must have a valid start time.");
            var baseline = await baselines.FindAsync(new(p.PoleId, pass.Pass.Direction, sweep.SweepId, before, sweep.DataSource), ct);
            result.Observations.Add((passNo, p.PoleId, p.TimeNs), EvaluateObservation(association, p.Peak?.Lux, baseline?.Value, flags, options.DimThresholdRatio) with { Baseline = baseline });
        }
        // Resolve once across the whole run, before coverage and persistence. A pole revisited in
        // another pass may reuse evidence; distinct poles must never claim the same prediction.
        var observations = result.Observations.ToArray();
        var resolved = DetectionAssociation.ResolveSharedEvidence(observations
            .Select(x => (x.Key.Pole, x.Value.Association)).ToArray());
        for (int i = 0; i < observations.Length; i++)
            if (resolved[i].Reason == "shared_cv_evidence")
                result.Observations[observations[i].Key] = new(resolved[i],
                    FrameClassification.Classify(resolved[i], null, null, [], options.DimThresholdRatio));
        int denominator = poles.Select(p => p.PoleId).Distinct().Count();
        run.DetectionCoveragePct = denominator == 0 ? null : 100d * result.Observations.Where(x => x.Value.Association.State != null).Select(x => x.Key.Pole).Distinct().Count() / denominator;
        run.DimCoveragePct = denominator == 0 ? null : 100d * result.Observations.Where(x => x.Value.Classification.DimEvaluationEligible).Select(x => x.Key.Pole).Distinct().Count() / denominator;
    }

    public static ObservationCv EvaluateObservation(Association association, double? peakLux, double? baseline,
        IReadOnlyCollection<string> flags, double dimThresholdRatio)
    {
        // Only these timing/coverage flags invalidate CV: no usable route crossing, or no covering clip.
        // gps_degraded/gps_offset_unresolved still have a usable crossing; ambiguous_association concerns lux only.
        if (flags.Contains("route_ambiguous") || flags.Contains("video_gap"))
            association = new(null, null, null, association.TrackCount,
                flags.Contains("video_gap") ? "video_gap" : "route_ambiguous");
        if (flags.Contains("ambiguous_association")) peakLux = null;
        return new(association, FrameClassification.Classify(association, peakLux, baseline, flags, dimThresholdRatio));
    }

    public static async Task<long> RegisterArtifact(LuxMapDbContext db, string component, MediaArtifact artifact, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO artifact_version (component, version, artifact_hash, metadata, created_at)
            VALUES ({component}, {artifact.Version}, {artifact.Hash}, {artifact.Metadata}::jsonb, now())
            ON CONFLICT (component, version) DO NOTHING
            """, ct);
        var row = await db.Set<ArtifactVersion>().SingleAsync(x => x.Component == component && x.Version == artifact.Version, ct);
        if (row.ArtifactHash != artifact.Hash) throw new ProcessingFailure("ARTIFACT_VERSION_CONFLICT", "artifact");
        return row.VersionId;
    }

    public static async Task<T> WithHeartbeat<T>(Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> heartbeat, double leaseSeconds, double timeoutSeconds, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var task = operation(stop.Token);
        try
        {
            while (!task.IsCompleted)
            {
                var tick = Task.Delay(TimeSpan.FromSeconds(Math.Min(leaseSeconds / 3, 10)), stop.Token);
                if (await Task.WhenAny(task, tick) == task) break;
                await tick;
                await heartbeat(stop.Token);
            }
            return await task;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ProcessingFailure("MEDIA_TIMEOUT", "media"); }
        finally
        {
            await stop.CancelAsync();
            try { await task; } catch when (stop.IsCancellationRequested) { /* Preserve the failure from the operation or heartbeat. */ }
        }
    }
}
