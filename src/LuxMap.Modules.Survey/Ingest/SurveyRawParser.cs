using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Shared.Http;
using NetTopologySuite.Geometries;

namespace LuxMap.Modules.Survey.Ingest;

public sealed record ParsedSurveyRaw(List<SurveyGpsSample> Gps, List<SurveyLuxSample> Lux);

/// <summary>SELF-SIGNED schema v1; validates the ENTIRE file before any database/object write.</summary>
public static class SurveyRawParser
{
    public static ParsedSurveyRaw Parse(byte[] bytes, SurveyRawKind kind, SurveySweep sweep)
    {
        var gps = new List<SurveyGpsSample>();
        var lux = new List<SurveyLuxSample>();
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { throw Invalid(1, "utf8"); }
        if (text.StartsWith('\uFEFF')) text = text[1..];
        if (kind == SurveyRawKind.CaptureConfig)
        {
            using var config = Document(text, 1);
            var root = config.RootElement;
            Header(root, sweep, 1);
            CheckFinite(root, 1, "capture_config");
            if (Digits(root, "elapsed_anchor_ns", 1) != sweep.ElapsedAnchorNs
                || !DateTimeOffset.TryParse(Text(root, "utc_anchor", 1), CultureInfo.InvariantCulture, DateTimeStyles.None, out var utc)
                || utc.UtcDateTime.Ticks / 10 != sweep.UtcAnchor.Ticks / 10 || Number(root, "utc_uncertainty_ms", 1, 0) != sweep.UtcUncertaintyMs)
                throw Invalid(1, "clock_anchor");
            foreach (var field in new[] { "phone_model", "camera_id", "app_version", "sensor_timestamp_source" }) Text(root, field, 1);
            Integer(root, "profile_id", 1, 1); Integer(root, "module_firmware_version_id", 1, 1);
            var camera = Property(root, "camera", 1);
            Number(camera, "iso", 1, double.Epsilon); Digits(camera, "exposure_time_ns", 1);
            Number(camera, "aperture", 1, double.Epsilon); Number(camera, "fps", 1, double.Epsilon);
            Number(camera, "focus_distance", 1, 0);
            Text(camera, "focus_mode", 1); Text(camera, "white_balance_mode", 1);
            Property(camera, "white_balance_value", 1);
            var resolution = Property(camera, "resolution", 1);
            Integer(resolution, "width", 1, 1); Integer(resolution, "height", 1, 1);
            foreach (var flag in new[] { "ae_enabled", "eis_enabled", "hdr_enabled", "night_mode_enabled" })
                if (Property(camera, flag, 1).ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid(1, flag);
            var mount = Property(root, "mount", 1);
            Text(mount, "camera_side", 1); Text(mount, "sensor_position", 1);
            Number(mount, "mount_height_m", 1, 0); Number(mount, "angle_deg", 1, -360, 360);
            // Frame timestamp mapping is retained byte-for-byte here; device quality gates belong to P2b.
            return new(gps, lux);
        }
        using var reader = new StringReader(text);
        using (var header = Document(reader.ReadLine() ?? "", 1))
        {
            Header(header.RootElement, sweep, 1);
            var expected = kind == SurveyRawKind.GpsTrack ? "gps_track" : "lux_log";
            if (Text(header.RootElement, "kind", 1) != expected) throw Invalid(1, "kind");
            if (kind == SurveyRawKind.GpsTrack && Text(header.RootElement, "time_unit", 1) != "ns") throw Invalid(1, "time_unit");
            if (kind == SurveyRawKind.LuxLog) Integer(header.RootElement, "module_firmware_version_id", 1, 1);
        }
        var seen = new Dictionary<int, string>();
        var seqs = new HashSet<(int, long)>();
        string? line;
        var lineNo = 1;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNo++;
            using var doc = Document(line, lineNo);
            var root = doc.RootElement;
            CheckFinite(root, lineNo, "sample");
            var sample = checked((int)Integer(root, "sample_no", lineNo, 0, int.MaxValue));
            // Exact repeat is harmless; a conflicting sample key is never silently overwritten.
            var canonical = JsonSerializer.Serialize(root);
            if (seen.TryGetValue(sample, out var previous))
            {
                if (previous != canonical) throw Invalid(lineNo, "sample_no");
                continue;
            }
            seen.Add(sample, canonical);
            var elapsed = Digits(root, "phone_elapsed_ns", lineNo);
            if (kind == SurveyRawKind.GpsTrack)
                gps.Add(new SurveyGpsSample { SweepId = sweep.SweepId, SampleNo = sample, PhoneElapsedNs = elapsed,
                    Geom = new Point(Number(root, "lng", lineNo, -180, 180), Number(root, "lat", lineNo, -90, 90)) { SRID = 4326 },
                    AccuracyM = Number(root, "accuracy_m", lineNo, 0), Provider = Text(root, "provider", lineNo),
                    HeadingDeg = OptionalNumber(root, "heading_deg", lineNo, 0, 360, true),
                    SpeedMps = OptionalNumber(root, "speed_mps", lineNo, 0) });
            else
            {
                var epoch = (int)Integer(root, "module_epoch", lineNo, 0, int.MaxValue);
                var seq = Integer(root, "seq", lineNo);
                if (!seqs.Add((epoch, seq))) throw Invalid(lineNo, "seq");
                lux.Add(new SurveyLuxSample { SweepId = sweep.SweepId, SampleNo = sample, PhoneElapsedNs = elapsed,
                    ModuleEpoch = epoch, Seq = seq, ModuleMs = Digits(root, "module_ms", lineNo), Lux = Number(root, "lux", lineNo, 0) });
            }
        }
        if (seen.Count == 0) throw Invalid(2, "sample");
        return new(gps, lux);
    }

    private static JsonDocument Document(string text, int line)
    {
        try
        {
            var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { doc.Dispose(); throw Invalid(line, "object"); }
            return doc;
        }
        catch (JsonException) { throw Invalid(line, "json"); }
    }
    private static void Header(JsonElement root, SurveySweep sweep, int line)
    {
        CheckFinite(root, line, "header");
        if (Integer(root, "schema_version", line) != 1) throw Invalid(line, "schema_version");
        if (!Guid.TryParse(Text(root, "boot_session_id", line), out var boot) || boot != sweep.BootSessionId)
            throw Invalid(line, "boot_session_id");
    }
    private static JsonElement Property(JsonElement root, string field, int line)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(field, out var value) ? value : throw Invalid(line, field);
    private static string Text(JsonElement root, string field, int line)
        => Property(root, field, line) is var v && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()! : throw Invalid(line, field);
    public static long ParseDigits(string? text, string field, int line = 0)
        => text is { Length: > 0 } && text.All(c => c is >= '0' and <= '9')
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : throw Invalid(line, field);
    private static long Digits(JsonElement root, string field, int line) => ParseDigits(Text(root, field, line), field, line);
    private static long Integer(JsonElement root, string field, int line, long min = 0, long max = long.MaxValue)
        => Property(root, field, line) is var v && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) && n >= min && n <= max
            ? n : throw Invalid(line, field);
    private static double Number(JsonElement root, string field, int line, double min, double max = double.MaxValue, bool exclusive = false)
    {
        var v = Property(root, field, line);
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var n) || !double.IsFinite(n) || n < min || n > max || (exclusive && n == max))
            throw Invalid(line, field);
        return n;
    }
    private static double? OptionalNumber(JsonElement root, string field, int line, double min, double max = double.MaxValue, bool exclusive = false)
        => !root.TryGetProperty(field, out var v) || v.ValueKind == JsonValueKind.Null ? null : Number(root, field, line, min, max, exclusive);
    private static void CheckFinite(JsonElement value, int line, string path)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>();
            foreach (var p in value.EnumerateObject())
            {
                if (!names.Add(p.Name)) throw Invalid(line, path + "." + p.Name);
                CheckFinite(p.Value, line, path + "." + p.Name);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) CheckFinite(item, line, path);
        else if ((value.ValueKind == JsonValueKind.Number && (!value.TryGetDouble(out var d) || !double.IsFinite(d)))
            || (value.ValueKind == JsonValueKind.String && value.GetString() is "NaN" or "Infinity" or "-Infinity")) throw Invalid(line, path);
    }
    public static LuxMapException Invalid(int line, string field) => new("VALIDATION_FAILED", HttpStatusCode.BadRequest,
        "Invalid survey data.", new Dictionary<string, object?> { ["line"] = line, ["field"] = field });
}
