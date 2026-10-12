using Microsoft.AspNetCore.Mvc.Testing;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-36 — a factory that derives from <c>WebApplicationFactory&lt;Program&gt;</c> directly never waits for
/// the test container and never checks where its data source points, so it resolves its database from
/// <c>.env</c> — <c>luxmap_dev</c> on a developer machine. Nothing else would notice: the tests still pass.
/// A host either waits for the container (<see cref="LuxMapTestHost"/>) or has no database at all
/// (<see cref="NoDatabaseTestHost"/>).
/// </summary>
public sealed class TestHostCoverageTests
{
    [Fact]
    public void Every_test_host_waits_for_the_test_container()
    {
        var hosts = typeof(TestHostCoverageTests).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(WebApplicationFactory<Program>).IsAssignableFrom(type))
            .ToArray();

        Assert.NotEmpty(hosts);
        var bypassing = hosts
            .Where(type => !type.IsSubclassOf(typeof(LuxMapTestHost)) && !type.IsSubclassOf(typeof(NoDatabaseTestHost))
                && type.DeclaringType != typeof(TestDatabase))
            .Select(type => type.FullName)
            .ToArray();

        Assert.True(bypassing.Length == 0,
            "These hosts skip the test container and would use the database in .env: " + string.Join(", ", bypassing));
    }
}
