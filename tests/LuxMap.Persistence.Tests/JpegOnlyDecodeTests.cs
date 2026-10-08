using System.Text.RegularExpressions;

namespace LuxMap.Persistence.Tests;

/// <summary>
/// Every image the API decodes goes through the JPEG-only ImageSharp configuration (BE-11 rule 5) — and since 08/10/2026 that
/// is also what makes suppressing five ImageSharp 3.1.12 advisories acceptable (<c>Directory.Build.props</c>).
/// </summary>
/// <remarks>
/// <c>Image.Load(stream)</c> with no <c>DecoderOptions</c> uses <c>Configuration.Default</c>, which registers TIFF, PNG, WebP
/// and the rest — a file that merely CLAIMS to be a JPEG would then reach the BigTIFF decoder (GHSA-wmxv-xphr-5c9g). Nothing
/// at compile time says so, so this reads the source: every decode call must pass, as its first argument, a
/// <c>DecoderOptions</c> built in the same file from <c>JpegOnly</c>. Blunt on purpose, like <see cref="BannedDistanceApiTests"/>.
/// Lives here because this assembly needs no database.
/// </remarks>
public class JpegOnlyDecodeTests
{
    private static readonly Regex DecodeCall = new(@"\bImage\.(Load|Identify|DetectFormat)\w*\(\s*(?<arg>[A-Za-z_][\w.]*)?", RegexOptions.Compiled);

    [Fact]
    public void Every_image_decode_in_src_uses_the_jpeg_only_configuration()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();
        var calls = 0;

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match call in DecodeCall.Matches(lines[i]))
                {
                    calls++;
                    var arg = call.Groups["arg"].Value;
                    var safe = arg.Length > 0 && Regex.IsMatch(text,
                        $@"\b{Regex.Escape(arg)}\s*=\s*new\s+DecoderOptions\s*\{{[^}}]*\bConfiguration\s*=\s*[\w.]*JpegOnly\b");
                    if (!safe)
                    {
                        offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1} decodes without the JPEG-only configuration");
                    }
                }
            }
        }

        Assert.True(calls > 0, "No decode call found at all — has the scan lost its way?");
        Assert.Empty(offenders);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LuxMap.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("LuxMap.slnx not found above the test output.");
    }
}
