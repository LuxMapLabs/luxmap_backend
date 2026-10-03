namespace LuxMap.Modules.Survey.Processing.Frames;

public sealed record DetectedFrame(string FrameId, long PhoneElapsedNs, Prediction[] Predictions);
public sealed record Association(string? State, double? Confidence, string? RepresentativeFrameId, int TrackCount, string Reason)
{
    public string[] EvidenceKeys { get; init; } = [];
}

/// <summary>Conservative short-window tracking. Multiple possible tracks remain ambiguous.</summary>
public static class DetectionAssociation
{
    public static Association Match(IEnumerable<DetectedFrame> frames, long startNs, long endNs,
        string cameraSide, int poleSideInTravelDirection, SurveyFrameOptions options)
    {
        if (poleSideInTravelDirection == 0 || cameraSide is not ("front" or "left" or "right"))
            return new(null, null, null, 0, "camera_side_ambiguous");
        if (cameraSide != "front" && (cameraSide == "left" ? 1 : -1) != poleSideInTravelDirection)
            return new(null, null, null, 0, "outside_camera_side");
        var track = new List<(DetectedFrame Frame, Prediction Prediction)>();
        bool poor = false;
        foreach (var frame in frames.Where(x => x.PhoneElapsedNs >= startNs && x.PhoneElapsedNs <= endNs)
                     .OrderBy(x => x.PhoneElapsedNs).ThenBy(x => x.FrameId))
        {
            var candidates = new List<Prediction>();
            foreach (var p in frame.Predictions)
            {
                var center = p.X + p.Width / 2;
                if (cameraSide == "front" && (poleSideInTravelDirection > 0 ? center >= .5 : center <= .5)) continue;
                if (p.Confidence < options.MinimumConfidence || p.Width * p.Height < options.MinimumBoxArea)
                { poor = true; continue; }
                candidates.Add(p);
            }
            // A small lamp can move farther than its bbox between samples. Within this short
            // pole window, one candidate per frame is one track; simultaneous lamps are ambiguous.
            if (candidates.Count > 1) return new(null, null, null, candidates.Count, "ambiguous_tracks");
            if (candidates.Count == 1) track.Add((frame, candidates[0]));
        }
        if (track.Count == 0) return new(null, null, null, 0, poor ? "frame_poor" : "no_detection");
        if (track.Select(x => x.Prediction.Label).Distinct().Count() != 1)
            return new(null, null, null, 1, "on_off_conflict");
        var best = track.OrderByDescending(x => x.Prediction.Width * x.Prediction.Height * x.Prediction.Confidence)
            .ThenBy(x => x.Frame.PhoneElapsedNs).First();
        return new(best.Prediction.Label, best.Prediction.Confidence, best.Frame.FrameId, 1, "associated")
        { EvidenceKeys = track.Select(x => $"{x.Frame.FrameId}:{x.Prediction.ItemNo}").ToArray() };
    }

    // Adjacent pole windows may overlap. The same detected track cannot prove two different poles.
    public static Association[] ResolveSharedEvidence(IReadOnlyList<(string PoleId, Association Association)> associations)
        => associations.Select(association => associations.Where(other => other.PoleId != association.PoleId)
            .Any(other => association.Association.EvidenceKeys.Intersect(other.Association.EvidenceKeys).Any())
                ? new Association(null, null, null, association.Association.TrackCount, "shared_cv_evidence") : association.Association).ToArray();
}
