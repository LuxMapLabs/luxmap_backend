namespace LuxMap.Persistence.Tests;

/// <summary>
/// Closes a measured hole in the BE-10 compile-time ban.
/// </summary>
/// <remarks>
/// <b>Microsoft.CodeAnalysis.BannedApiAnalyzers 5.6.0 only matches a call whose arguments are ALL
/// supplied explicitly.</b> Verified during BE-10 by probing every spelling:
/// <list type="bullet">
/// <item><c>EF.Functions.Distance(a, b, false)</c> → RS0030 raised.</item>
/// <item><c>EF.Functions.Distance(a, b)</c> — <c>useSpheroid</c> left at its default → <b>no
/// diagnostic at all</b>, not even with the whole type banned via a <c>T:</c> entry.</item>
/// </list>
/// The same holds for <c>IsWithinDistance</c>. Since omitting the optional flag is the natural way to
/// write the call, the analyzer misses precisely the form somebody would actually type, and there is
/// no analyzer configuration that closes it.
/// <para>
/// So this test reads the source instead. A text scan is a blunt instrument and would normally be the
/// wrong tool; here it is the only one that covers the gap, and the thing it guards — a distance
/// silently returned in degrees — is worth a blunt instrument. <c>Geometry.Distance</c> and
/// <c>DistanceKnn</c> take no optional arguments and are already fully covered by RS0030; they are
/// included anyway so the list reads as one rule rather than as two half-rules.
/// </para>
/// <para>
/// ⚠️ <b>It lives in this assembly because this assembly needs no database.</b> The rule it enforces
/// is a compile-time one, so the test that watches over it has to run wherever the compiler runs —
/// <c>dotnet test</c> and nothing else. In <c>LuxMap.Api.Tests</c> it would go down with the whole
/// assembly whenever Docker is not up, which is exactly when someone is running tests locally. A
/// guard that fails at the same moment as the infrastructure it does not depend on is not a guard.
/// </para>
/// </remarks>
public class BannedDistanceApiTests
{
    private static readonly string[] BannedFragments =
    [
        "EF.Functions.Distance(",
        "EF.Functions.IsWithinDistance(",
        "EF.Functions.DistanceKnn(",
        ".Distance(",
    ];

    /// <summary>This file names every banned fragment, so scanning it would always fail.</summary>
    private const string ThisFile = "BannedDistanceApiTests.cs";

    [Fact]
    public void No_source_file_calls_a_distance_api_that_answers_in_degrees()
    {
        var root = RepositoryRoot();

        var offenders = new List<string>();

        foreach (var directory in new[] { "src", "tests" })
        {
            var files = Directory.EnumerateFiles(
                Path.Combine(root, directory), "*.cs", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || Path.GetFileName(file) == ThisFile)
                {
                    continue;
                }

                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    foreach (var fragment in BannedFragments)
                    {
                        if (lines[i].Contains(fragment, StringComparison.Ordinal))
                        {
                            offenders.Add(
                                $"{Path.GetRelativePath(root, file)}:{i + 1} contains '{fragment}'");
                        }
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These call a distance API that returns DEGREES on a 4326 column. Use "
            + "LuxMap.Persistence.SpatialFunctions.DistanceMeters instead:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Walks up from the test binary until it finds the solution file. The test binary sits several
    /// levels below the repository root and the depth differs by configuration, so the root is
    /// located rather than assumed.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LuxMap.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
