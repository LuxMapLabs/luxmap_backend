namespace LuxMap.Api.Tests;

public partial class WorkOrderTests
{
    /// <summary>
    /// TEST-WO-PARALLEL: <c>PoleClient</c> builds its host and client lazily, so the first calls of a test can
    /// overlap. Unguarded, every overlapping caller built its own client and wrote the shared dictionary at once
    /// (the flaky <c>InvalidOperationException</c> in the parallel-upload test). Guarded, they all share one.
    /// </summary>
    [Fact]
    public async Task Overlapping_first_calls_of_PoleClient_share_one_client_per_user()
    {
        var clients = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => PoleClient("a"))));

        Assert.Single(clients.Distinct());
        Assert.Same(clients[0], await PoleClient("a"));
    }
}
