using System.Globalization;
using LuxMap.Modules.Survey.Processing.Frames;

namespace LuxMap.Api.Tests;

/// <summary>Short clips for the pole-video tests, cut by ffmpeg at run time (CI installs it); nothing binary is committed.</summary>
internal static class PoleVideoTestClips
{
    /// <summary>A dark 64×32 clip of <paramref name="seconds"/>; <paramref name="encode"/> picks the codec and colour tags (<c>setparams</c> for HDR).</summary>
    public static async Task<byte[]> MakeAsync(double seconds, int fps = 25, string encode = "-c:v libx264 -pix_fmt yuv420p")
    {
        var directory = Path.Combine(Path.GetTempPath(), "luxmap-pole-video-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "clip.mp4");
            string[] args = ["-v", "error", "-f", "lavfi", "-i",
                $"color=c=0x202020:size=64x32:rate={fps}:duration={seconds.ToString(CultureInfo.InvariantCulture)}", .. encode.Split(' '), "-y", file];
            await MediaProcess.RunAsync("ffmpeg", args, 120, default);
            return await File.ReadAllBytesAsync(file);
        }
        finally { Directory.Delete(directory, true); }
    }

    public const string Hlg = "-vf setparams=color_trc=arib-std-b67:colorspace=bt2020nc:color_primaries=bt2020 -c:v libx265 -pix_fmt yuv420p10le -x265-params log-level=error";
}
