namespace LuxMap.Persistence.Tests;

/// <summary>
/// Every use of EF Core's bulk-write APIs must be an EXPLICIT, visible exception (BE-12a).
/// </summary>
/// <remarks>
/// <b>Why these are banned at all.</b> <c>CommuneWriteGuard</c> is a <c>SaveChanges</c> override that
/// walks the change tracker. <c>ExecuteUpdate</c> and <c>ExecuteDelete</c> translate straight to SQL
/// and never touch the tracker, so a bulk write is invisible to the guard — Contract section 7 simply
/// does not apply to it. That is an architectural hole, not a style preference: the guard was written
/// precisely because the BE-08 query filter covered reads only, and a bulk write re-opens the same
/// gap on the write side.
/// <para>
/// <b>Why a text scan on top of RS0030.</b> Two reasons. BannedApiAnalyzers matches a call only when
/// every argument is supplied explicitly — the hole measured in BE-10, where leaving the optional
/// <c>useSpheroid</c> flag off a banned distance call raised nothing while passing it raised RS0030
/// (see <see cref="BannedDistanceApiTests"/>) — and both bulk APIs take an optional
/// <c>CancellationToken</c>. And unlike the distance ban, this one has
/// legitimate exceptions, so a plain "zero occurrences" scan cannot express it. What CAN be checked
/// is that every occurrence sits inside a <c>#pragma warning disable RS0030</c> region: the exception
/// then has to be written down, and it shows up in a diff.
/// </para>
/// <para>
/// The two categories of exception that exist today, both fine:
/// <list type="bullet">
/// <item><c>AuthService</c> — <c>RefreshToken</c> is not <c>ICommuneScoped</c>, and BE-07's rotation
/// depends on the row count a conditional UPDATE returns to settle concurrent refreshes.</item>
/// <item>Test teardown — bulk delete is the only way to clean up under an empty scope. BE-36 removes
/// the need altogether by giving each run its own database.</item>
/// </list>
/// </para>
/// <para>
/// ⚠️ Lives in this assembly because this assembly needs no database, for the same reason as
/// <see cref="BannedDistanceApiTests"/>: a compile-time rule must be watched wherever the compiler
/// runs, not only when Docker happens to be up.
/// </para>
/// </remarks>
public class BannedBulkWriteApiTests
{
    /// <summary>Written with the leading dot so prose mentioning the API name does not match.</summary>
    private static readonly string[] BulkWriteCalls =
    [
        ".ExecuteUpdate(",
        ".ExecuteUpdateAsync(",
        ".ExecuteDelete(",
        ".ExecuteDeleteAsync(",
    ];

    private const string Disable = "#pragma warning disable RS0030";
    private const string Restore = "#pragma warning restore RS0030";

    private const string ThisFile = "BannedBulkWriteApiTests.cs";

    [Fact]
    public void Every_bulk_write_sits_inside_an_explicit_RS0030_exemption()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();

        foreach (var file in SourceFiles(root))
        {
            var exempt = false;
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (line.Contains(Disable, StringComparison.Ordinal))
                {
                    exempt = true;
                }
                else if (line.Contains(Restore, StringComparison.Ordinal))
                {
                    exempt = false;
                }

                if (exempt)
                {
                    continue;
                }

                foreach (var call in BulkWriteCalls)
                {
                    if (line.Contains(call, StringComparison.Ordinal))
                    {
                        offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1} calls '{call.Trim('.', '(')}'");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These bypass the change tracker, so CommuneWriteGuard never sees them and Contract "
            + "section 7 goes unenforced on the write. Load the rows and Remove/mutate them, or — if "
            + "the write really is outside any commune scope — say so with "
            + "EnterUnscopedSystemWriteBackdoor and a #pragma warning disable RS0030 stating why:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// An exemption must not be left open to the end of the file: a later edit would land inside it
    /// without anyone noticing.
    /// </summary>
    [Fact]
    public void No_file_leaves_an_RS0030_exemption_unclosed()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();

        foreach (var file in SourceFiles(root))
        {
            var depth = 0;
            foreach (var line in File.ReadAllLines(file))
            {
                if (line.Contains(Disable, StringComparison.Ordinal))
                {
                    depth++;
                }
                else if (line.Contains(Restore, StringComparison.Ordinal))
                {
                    depth--;
                }
            }

            if (depth != 0)
            {
                offenders.Add($"{Path.GetRelativePath(root, file)} ends with {depth} unclosed exemption(s)");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<string> SourceFiles(string root)
    {
        foreach (var directory in new[] { "src", "tests" })
        {
            foreach (var file in Directory.EnumerateFiles(
                Path.Combine(root, directory), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || Path.GetFileName(file) == ThisFile)
                {
                    continue;
                }

                yield return file;
            }
        }
    }

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
