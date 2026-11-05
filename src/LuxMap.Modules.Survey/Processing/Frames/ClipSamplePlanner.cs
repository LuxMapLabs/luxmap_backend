using System.Net;
using LuxMap.Shared.Http;
using LuxMap.Shared.Media;

namespace LuxMap.Modules.Survey.Processing.Frames;

/// <summary>Which frames of a one-lamp clip to cut (POLE-VIDEO D-4). Pure: works on the clip's real display times, never on an average frame rate.</summary>
public static class ClipSamplePlanner
{
    /// <summary>Length of the clip: first displayed frame to the end of the last one (one last frame interval past its time).</summary>
    public static long DurationNs(long[] ptsNs)
        => ptsNs.Length < 2 ? 0 : ptsNs[^1] - ptsNs[0] + (ptsNs[^1] - ptsNs[^2]);

    /// <summary>
    /// Indices into <paramref name="ptsNs"/> (ascending), spread evenly over the usable part of the clip, nearest displayed frame to each
    /// target. Refuses a clip outside the allowed length with 400 <c>VIDEO_TOO_SHORT</c> / <c>VIDEO_TOO_LONG</c>.
    /// </summary>
    public static int[] Select(long[] ptsNs, PoleVideoOptions options)
    {
        var duration = DurationNs(ptsNs) / 1e9;
        var details = new Dictionary<string, object?> { ["duration_ms"] = (int)Math.Round(duration * 1000),
            ["minimum_seconds"] = options.MinimumSeconds, ["maximum_seconds"] = options.MaximumSeconds };
        if (duration < options.MinimumSeconds)
            throw new LuxMapException("VIDEO_TOO_SHORT", HttpStatusCode.BadRequest, "The clip is shorter than the minimum length.", details);
        if (duration > options.MaximumSeconds + options.DurationGraceSeconds)
            throw new LuxMapException("VIDEO_TOO_LONG", HttpStatusCode.BadRequest, "The clip is longer than the maximum length.", details);

        var start = duration > options.SkipLeadingOnlyAboveSeconds ? options.SkipLeadingSeconds : 0;
        var usable = duration - start;
        var count = (int)Math.Clamp(Math.Ceiling(usable * options.FramesPerSecond - 1e-9), 1, options.MaximumFrames);
        var selected = new SortedSet<int>();
        for (var n = 0; n < count; n++)
        {
            var target = (long)((start + (n + .5) * usable / count) * 1e9) + ptsNs[0];
            var found = Array.BinarySearch(ptsNs, target);
            var index = found >= 0 ? found : ~found;
            if (index >= ptsNs.Length) index = ptsNs.Length - 1;
            else if (index > 0 && target - ptsNs[index - 1] <= ptsNs[index] - target) index--;
            selected.Add(index);
        }
        return [.. selected];
    }
}
