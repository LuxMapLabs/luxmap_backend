using LuxMap.Shared.Authorization;

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

    /// <summary>
    /// A bulk write exempted in PRODUCTION code must target an entity the guard never covered anyway.
    /// </summary>
    /// <remarks>
    /// This is the condition the pragma rests on, and until now it was only asserted in a comment.
    /// <c>AuthService</c> may bypass the guard because <c>RefreshToken</c> is not
    /// <see cref="ICommuneScoped"/> — the guard was never going to see it. The day somebody makes
    /// <c>RefreshToken</c> scoped, or slips a scoped entity into an existing exempt region, that
    /// justification silently stops being true and commune scoping disappears from a write path with
    /// nothing to say so. Checking it turns a hand-written exception into a self-guarding one.
    /// <para>
    /// ⚠️ <b><c>src/</c> only.</b> Test teardown deliberately bulk-deletes scoped entities — poles,
    /// faults, lux readings — acting as the system to clean up under an empty scope, which is the one
    /// case where targeting a scoped entity is the point. Those are covered by
    /// <see cref="Every_bulk_write_sits_inside_an_explicit_RS0030_exemption"/> instead, and BE-36
    /// removes them altogether.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_exempted_bulk_write_in_production_targets_a_commune_scoped_entity()
    {
        var root = RepositoryRoot();
        var scoped = ScopedEntityNames();
        var offenders = new List<string>();

        foreach (var file in SourceFiles(root, "src"))
        {
            var exempt = false;
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(Disable, StringComparison.Ordinal))
                {
                    exempt = true;
                }
                else if (lines[i].Contains(Restore, StringComparison.Ordinal))
                {
                    exempt = false;
                }

                if (!exempt || !BulkWriteCalls.Any(call => lines[i].Contains(call, StringComparison.Ordinal)))
                {
                    continue;
                }

                var entity = TargetEntity(lines, i);
                if (entity is null)
                {
                    offenders.Add(
                        $"{Path.GetRelativePath(root, file)}:{i + 1} — could not tell which entity this "
                        + "targets. Write it as dbContext.Set<Entity>() so the exemption can be checked.");
                }
                else if (scoped.Contains(entity))
                {
                    offenders.Add(
                        $"{Path.GetRelativePath(root, file)}:{i + 1} targets {entity}, which IS "
                        + "ICommuneScoped — the exemption's justification does not hold.");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "An RS0030 exemption in production is only defensible when the guard had nothing to say "
            + "about that entity in the first place:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The scope lookup itself resolves both ways — otherwise the check above could pass by mistake.
    /// </summary>
    /// <remarks>
    /// The negative half is the one that matters: <c>RefreshToken</c> being outside
    /// <see cref="ICommuneScoped"/> is the entire justification for the <c>AuthService</c> exemption.
    /// If somebody makes it scoped, this fails here with a message saying so, next to the check that
    /// then starts rejecting the exemption.
    /// </remarks>
    [Fact]
    public void The_scoped_entity_lookup_resolves_real_types_in_both_directions()
    {
        var scoped = ScopedEntityNames();

        Assert.Contains("Pole", scoped);
        Assert.Contains("Fixture", scoped);
        Assert.Contains("Fault", scoped);
        Assert.Contains("LuxReading", scoped);

        Assert.DoesNotContain("RefreshToken", scoped);
        Assert.DoesNotContain("AppUser", scoped);

        // The anchor table is deliberately NOT scoped — filtering it would hide the row that defines a
        // commune behind the scope derived from it.
        Assert.DoesNotContain("AdministrativeUnit", scoped);
    }

    /// <summary>Every entity type in the modules that implements <see cref="ICommuneScoped"/>.</summary>
    /// <remarks>
    /// Built from EXPLICIT assembly references rather than by scanning <c>AppDomain</c>. A referenced
    /// assembly is loaded lazily, and a <c>_ = typeof(X)</c> that only exists to force the load has no
    /// side effect the compiler is obliged to keep — the first version of this method scanned the app
    /// domain, found nothing, and would have passed vacuously if the <c>Assert.NotEmpty</c> below had
    /// not been there.
    /// <para>
    /// A new module with scoped entities has to be added here. That is not a gap the test can close
    /// for itself, but it is a two-line change next to a project reference, both visible in a diff.
    /// </para>
    /// </remarks>
    private static HashSet<string> ScopedEntityNames()
    {
        System.Reflection.Assembly[] modules =
        [
            typeof(Modules.Assets.Entities.Pole).Assembly,
            typeof(Modules.Faults.Entities.Fault).Assembly,
            typeof(Modules.Survey.Entities.LuxReading).Assembly,
            typeof(Modules.Identity.Entities.AppUser).Assembly,
            typeof(AdministrativeUnit).Assembly,
        ];

        var names = modules
            .Distinct()
            .SelectMany(SafeTypes)
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ICommuneScoped).IsAssignableFrom(type))
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        // An empty set would make the whole test pass by finding nothing to object to, which is worse
        // than failing. It has already happened once — see the remarks.
        Assert.NotEmpty(names);
        return names;
    }

    private static IEnumerable<Type> SafeTypes(System.Reflection.Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null)!;
        }
    }

    /// <summary>
    /// The entity a bulk write targets, read from the nearest <c>Set&lt;X&gt;()</c> at or above the
    /// call — the statement may be split over several lines.
    /// </summary>
    private static string? TargetEntity(string[] lines, int callLine)
    {
        for (var i = callLine; i >= Math.Max(0, callLine - 10); i--)
        {
            var match = System.Text.RegularExpressions.Regex.Match(lines[i], @"Set<(\w+)>\s*\(");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    private static IEnumerable<string> SourceFiles(string root, params string[] directories)
    {
        foreach (var directory in directories.Length > 0 ? directories : ["src", "tests"])
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
