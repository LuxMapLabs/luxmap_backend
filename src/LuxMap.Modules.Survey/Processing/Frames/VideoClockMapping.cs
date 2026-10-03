using System.Text.Json;
using System.Globalization;

namespace LuxMap.Modules.Survey.Processing.Frames;

public sealed record ClipClock(int ClipNo, long FirstPtsNs, long LastPtsNs, long FirstSensorTimestampNs,
    long LastSensorTimestampNs, int TimeBaseNum, int TimeBaseDen)
{
    public long ToPhone(long ptsNs) => checked(FirstSensorTimestampNs + checked(ptsNs - FirstPtsNs));
    public long ToPts(long phoneNs) => checked(FirstPtsNs + checked(phoneNs - FirstSensorTimestampNs));
    public bool Covers(long phoneNs) => phoneNs >= FirstSensorTimestampNs && phoneNs <= LastSensorTimestampNs;
}
public sealed record VideoClockMapping(string CameraSide, ClipClock[] Clips)
{
    private static long Nanoseconds(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out number)) return number;
        throw new JsonException();
    }
    // Provisional, affine, unit-slope mapping for simulated inputs only (D-06).
    public static VideoClockMapping Parse(string json, IEnumerable<int> expectedClipNumbers)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.GetProperty("sensor_timestamp_source").GetString() != "REALTIME") throw new JsonException();
            var side = root.GetProperty("mount").GetProperty("camera_side").GetString();
            if (side is not ("front" or "left" or "right")) throw new JsonException();
            var clips = root.GetProperty("clips").EnumerateArray().Select(c => new ClipClock(
                c.GetProperty("clip_no").GetInt32(), Nanoseconds(c.GetProperty("first_pts_ns")),
                Nanoseconds(c.GetProperty("last_pts_ns")), Nanoseconds(c.GetProperty("first_sensor_timestamp_ns")),
                Nanoseconds(c.GetProperty("last_sensor_timestamp_ns")), c.GetProperty("time_base_num").GetInt32(),
                c.GetProperty("time_base_den").GetInt32())).OrderBy(c => c.FirstSensorTimestampNs).ToArray();
            if (clips.Length == 0 || clips.Select(c => c.ClipNo).Distinct().Count() != clips.Length
                || !clips.Select(c => c.ClipNo).Order().SequenceEqual(expectedClipNumbers.Order())) throw new JsonException();
            foreach (var c in clips)
                if (c.ClipNo < 0 || c.FirstSensorTimestampNs < 0 || c.LastSensorTimestampNs <= c.FirstSensorTimestampNs
                    || c.LastPtsNs <= c.FirstPtsNs || c.TimeBaseNum <= 0 || c.TimeBaseDen <= 0
                    || checked(c.LastPtsNs - c.FirstPtsNs) != checked(c.LastSensorTimestampNs - c.FirstSensorTimestampNs))
                    throw new JsonException();
            for (int i = 1; i < clips.Length; i++)
                if (clips[i].FirstSensorTimestampNs <= clips[i - 1].LastSensorTimestampNs) throw new JsonException();
            return new(side, clips);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new ProcessingFailure("CLOCK_VIDEO_MAPPING", "video_clock"); }
    }
}
