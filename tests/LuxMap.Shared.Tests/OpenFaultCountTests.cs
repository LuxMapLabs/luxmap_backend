using System.Text.Json;
using System.Text.Json.Serialization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Serialization;

namespace LuxMap.Shared.Tests;

/// <summary>
/// <c>open_fault_count</c> — the count itself, and the definition it rests on.
/// </summary>
/// <remarks>
/// Two tests rather than one, because they fail for different reasons and one cannot cover the other.
/// <see cref="The_count_on_every_pole_matches_the_faults_that_are_actually_open"/> guards the DATA in
/// the mock set; <see cref="Only_detected_confirmed_and_in_progress_count_as_open"/> guards the
/// DEFINITION. Both go through <see cref="CountByPole"/>, so a change to the definition really does
/// change what the data test measures — two separate counting paths would let T2 pass while T1 was
/// quietly measuring something else.
/// <para>
/// ⚠️ No database, no host, no Docker. The mock set is a pair of JSON files and
/// <see cref="FaultStatusSets"/> is a plain constant, so this assembly is where a guard over them can
/// still run when the infrastructure is down — the same reasoning that put
/// <c>BannedDistanceApiTests</c> in a DB-free assembly.
/// </para>
/// </remarks>
public class OpenFaultCountTests
{
    /// <summary>
    /// The one counting path. Both tests call it; nothing else re-implements it.
    /// </summary>
    /// <remarks>
    /// Filtering through <see cref="FaultStatusSets.IsOpen"/> rather than an inline set is the whole
    /// point. Contract section 2.1 names <c>open_fault_count</c> and never says which statuses count,
    /// so BE-28, BE-40 and this test would each have picked a set, all three would have looked
    /// reasonable, and the numbers would have disagreed with nothing to explain why.
    /// </remarks>
    private static Dictionary<string, int> CountByPole(IEnumerable<FaultRow> faults)
        => faults
            .Where(fault => fault.PoleId is not null && FaultStatusSets.IsOpen(fault.FaultStatus))
            .GroupBy(fault => fault.PoleId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    /// <summary>
    /// T1 — every pole's <c>open_fault_count</c> equals the faults really open against it.
    /// </summary>
    /// <remarks>
    /// This is the test that would have caught the defect it was written for: <c>POLE-0075</c> carried
    /// two open faults while declaring one, and <c>POLE-0076</c> carried one while declaring none, so
    /// the file totalled 26 against 28 real faults. Nothing compared the two files before.
    /// <para>
    /// ⚠️ <b>Known limit — this test cannot tell one definition of "open" from another.</b> All 28
    /// faults in the current mock set are open (21 <c>detected</c>, 7 <c>confirmed</c>, and not a
    /// single <c>rejected</c>, <c>resolved</c> or <c>verified</c>), so widening
    /// <see cref="FaultStatusSets.Open"/> to include a closed status changes no count here and this
    /// test stays green. That gap is exactly what
    /// <see cref="Only_detected_confirmed_and_in_progress_count_as_open"/> exists to close, and it is
    /// why the pair cannot be collapsed into one test.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_count_on_every_pole_matches_the_faults_that_are_actually_open()
    {
        var counts = CountByPole(ReadMock<FaultPage>("mock-faults.json").Items);
        var poles = ReadMock<PoleCollection>("mock-poles.geojson").Features;

        Assert.Equal(103, poles.Count);

        var wrong = poles
            .Select(feature => feature.Properties)
            .Where(pole => pole.OpenFaultCount != counts.GetValueOrDefault(pole.PoleId))
            .Select(pole => $"{pole.PoleId}: declares {pole.OpenFaultCount}, "
                + $"really has {counts.GetValueOrDefault(pole.PoleId)}")
            .ToList();

        Assert.True(
            wrong.Count == 0,
            "open_fault_count in mock-poles.geojson disagrees with mock-faults.json. Recount rather "
            + "than patching the listed poles — the mock set is the front end's reference:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, wrong));

        // The totals are asserted separately: the per-pole check above would still pass if a fault
        // pointed at a pole_id that does not exist in the collection at all.
        Assert.Equal(
            counts.Values.Sum(),
            poles.Sum(feature => feature.Properties.OpenFaultCount));
    }

    /// <summary>
    /// T2 — the definition itself: exactly <c>detected</c>, <c>confirmed</c> and <c>in_progress</c>.
    /// </summary>
    /// <remarks>
    /// In memory on purpose, and it covers ALL SIX statuses rather than the two the mock set happens
    /// to contain. One fault per status against one pole, so the expected count is simply "how many
    /// statuses are open".
    /// <para>
    /// It goes through <see cref="CountByPole"/>, the same path T1 uses, which is what makes it a
    /// guard over T1 rather than a separate opinion: widening
    /// <see cref="FaultStatusSets.Open"/> turns this red immediately, while T1 would not notice
    /// until the mock set gains a closed fault.
    /// </para>
    /// </remarks>
    [Fact]
    public void Only_detected_confirmed_and_in_progress_count_as_open()
    {
        const string pole = "POLE-0001";

        // Every value of the enum, so a status added later cannot slip past unclassified.
        var all = Enum.GetValues<FaultStatus>();
        Assert.Equal(6, all.Length);

        var counts = CountByPole(all.Select(status => new FaultRow(pole, status)));

        Assert.Equal(3, counts[pole]);

        foreach (var status in new[] { FaultStatus.Detected, FaultStatus.Confirmed, FaultStatus.InProgress })
        {
            Assert.Equal(1, CountByPole([new FaultRow(pole, status)]).GetValueOrDefault(pole));
        }

        // Excluded for three DIFFERENT reasons, which is why the set is written out rather than
        // expressed as "not finished": rejected means an engineer decided it was never a fault,
        // resolved means the work is done, verified means the fix was checked.
        foreach (var status in new[] { FaultStatus.Rejected, FaultStatus.Resolved, FaultStatus.Verified })
        {
            Assert.Equal(0, CountByPole([new FaultRow(pole, status)]).GetValueOrDefault(pole));
        }
    }

    /// <summary>
    /// Reads a file from <c>mocks/</c> through the repository's OWN JSON conventions.
    /// </summary>
    /// <remarks>
    /// <see cref="LuxMapJsonOptions.Default"/> rather than a local options object: it carries the
    /// snake_case policy and the lowercase string enum converter the API itself uses, so
    /// <c>"in_progress"</c> maps to <see cref="FaultStatus.InProgress"/> by the same rule the wire
    /// format follows. A hand-written string-to-enum mapping here would be a second answer to a
    /// question that already has one.
    /// </remarks>
    private static T ReadMock<T>(string fileName)
    {
        var path = Path.Combine(MocksDirectory(), fileName);
        Assert.True(File.Exists(path), $"{fileName} not found at {path}.");

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), LuxMapJsonOptions.Default)
            ?? throw new InvalidOperationException($"{fileName} deserialised to null.");
    }

    /// <summary>
    /// Walks up from the test binary to the repository's <c>mocks/</c> directory.
    /// </summary>
    /// <remarks>
    /// Throws when it cannot find it. Returning an empty or best-guess path would turn a missing
    /// directory into a file-not-found further along, or worse into a silently empty mock set that
    /// makes every assertion here vacuously true — the failure mode this whole file exists to prevent.
    /// </remarks>
    private static string MocksDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "mocks")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException(
                $"No 'mocks' directory above {AppContext.BaseDirectory}. The test binary is expected "
                + "to sit inside the repository.");
        }

        return Path.Combine(directory.FullName, "mocks");
    }

    /// <summary>The two fields of a fault this file cares about. Everything else in section 2.4 is ignored.</summary>
    private sealed record FaultRow(
        [property: JsonPropertyName("pole_id")] string? PoleId,
        [property: JsonPropertyName("fault_status")] FaultStatus FaultStatus);

    private sealed record FaultPage(IReadOnlyList<FaultRow> Items);

    private sealed record PoleCollection(IReadOnlyList<PoleFeature> Features);

    private sealed record PoleFeature(PoleProperties Properties);

    private sealed record PoleProperties(
        [property: JsonPropertyName("pole_id")] string PoleId,
        [property: JsonPropertyName("open_fault_count")] int OpenFaultCount);
}
