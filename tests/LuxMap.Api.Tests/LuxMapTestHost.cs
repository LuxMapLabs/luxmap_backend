using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LuxMap.Api.Tests;

/// <summary>
/// The base of every test host in this assembly (BE-36). Building a host first waits for the run's
/// PostGIS container, then proves the host's data source points at it.
/// </summary>
/// <remarks>
/// <c>CreateHost</c> is the one hook every path goes through — <c>Services</c>, <c>CreateClient</c>,
/// and the derived factories <c>WithWebHostBuilder</c> returns — and it runs before <c>Program</c>'s
/// top-level code, which is where the connection string is read. Deriving from
/// <c>WebApplicationFactory&lt;Program&gt;</c> directly skips both; <c>TestHostCoverageTests</c> fails on it.
/// </remarks>
public abstract class LuxMapTestHost : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        TestDatabase.EnsureReady();
        var host = base.CreateHost(builder);
        TestDatabase.AssertTargets(host.Services);
        return host;
    }
}
