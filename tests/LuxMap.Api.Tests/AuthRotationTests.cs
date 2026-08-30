using System.Net;
using System.Text.Json;
using LuxMap.Modules.Identity.Entities;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

/// <summary>
/// Rotation, replay and concurrency. Every assertion checks the REAL row state in the database rather
/// than trusting the service's return value.
/// </summary>
[Collection(nameof(AuthCollection))]
public class AuthRotationTests(AuthTestFactory factory, ITestOutputHelper output)
{
    private HttpClient Client => factory.CreateClient();

    private void Dump(string label, RefreshToken? t)
        => output.WriteLine(t is null
            ? $"  {label}: (not present in the database)"
            : $"  {label}: id={t.Id} chain={t.ChainId.ToString()[..8]} revoked_at={(t.RevokedAt?.ToString("HH:mm:ss") ?? "null")} "
              + $"reason={(t.RevokedReason?.ToString() ?? "null")} replaced_by={(t.ReplacedByTokenId?.ToString() ?? "null")}");

    [Fact]
    public async Task Rotation_revokes_the_old_token_and_links_it_to_the_new_one()
    {
        var first = await Client.LoginAsync("engineer", "SEED_ENGINEER_PASSWORD");
        var second = await (await Client.PostRefreshAsync(first.RefreshToken)).ReadTokensAsync();

        var oldToken = await factory.FindTokenAsync(first.RefreshToken);
        var newToken = await factory.FindTokenAsync(second.RefreshToken);

        output.WriteLine("── after one rotation ──");
        Dump("old token", oldToken);
        Dump("new token", newToken);

        Assert.NotNull(oldToken);
        Assert.NotNull(newToken);
        Assert.NotNull(oldToken.RevokedAt);
        Assert.Equal(RefreshTokenRevocationReason.Rotation, oldToken.RevokedReason);
        Assert.Equal(newToken.Id, oldToken.ReplacedByTokenId);
        Assert.Null(newToken.RevokedAt);
        Assert.Equal(oldToken.ChainId, newToken.ChainId);
    }

    [Fact]
    public async Task Two_concurrent_refreshes_leave_exactly_one_winner_whose_token_stays_alive()
    {
        var login = await Client.LoginAsync("engineer", "SEED_ENGINEER_PASSWORD");

        var clientA = factory.CreateClient();
        var clientB = factory.CreateClient();

        var responses = await Task.WhenAll(
            clientA.PostRefreshAsync(login.RefreshToken),
            clientB.PostRefreshAsync(login.RefreshToken));

        var winners = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToArray();
        var losers = responses.Where(r => r.StatusCode == HttpStatusCode.Unauthorized).ToArray();

        output.WriteLine($"── two concurrent requests: {winners.Length} won, {losers.Length} lost ──");

        Assert.Single(winners);
        Assert.Single(losers);

        var issued = await winners[0].ReadTokensAsync();
        var winnerToken = await factory.FindTokenAsync(issued.RefreshToken);
        var original = await factory.FindTokenAsync(login.RefreshToken);

        Dump("original token", original);
        Dump("token issued to the WINNER", winnerToken);

        // The crux: the loser must NOT touch the winner's token.
        Assert.NotNull(winnerToken);
        Assert.Null(winnerToken.RevokedAt);
        Assert.Null(winnerToken.RevokedReason);

        // And the chain still holds exactly one live token.
        var alive = (await factory.ChainAsync(original!.ChainId)).Count(t => t.RevokedAt is null);
        output.WriteLine($"  live tokens in the chain: {alive}");
        Assert.Equal(1, alive);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_inside_the_grace_window_does_not_kill_the_chain()
    {
        var login = await Client.LoginAsync("engineer", "SEED_ENGINEER_PASSWORD");
        var rotated = await (await Client.PostRefreshAsync(login.RefreshToken)).ReadTokensAsync();

        // Still inside 30 seconds of the revocation.
        factory.Clock.Advance(TimeSpan.FromSeconds(5));

        var reuse = await Client.PostRefreshAsync(login.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var live = await factory.FindTokenAsync(rotated.RefreshToken);
        output.WriteLine("── replay after 5 seconds (inside the grace window) ──");
        Dump("live token", live);

        Assert.NotNull(live);
        Assert.Null(live.RevokedAt);

        // The chain still works: the next refresh succeeds.
        var stillWorks = await Client.PostRefreshAsync(rotated.RefreshToken);
        output.WriteLine($"  next refresh on the chain: HTTP {(int)stillWorks.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_after_the_grace_window_revokes_that_chain_only()
    {
        var victim = await Client.LoginAsync("engineer", "SEED_ENGINEER_PASSWORD");
        var otherDevice = await Client.LoginAsync("engineer", "SEED_ENGINEER_PASSWORD");

        var rotated = await (await Client.PostRefreshAsync(victim.RefreshToken)).ReadTokensAsync();

        // Past the 30-second grace window.
        factory.Clock.Advance(TimeSpan.FromSeconds(31));

        var reuse = await Client.PostRefreshAsync(victim.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var killed = await factory.FindTokenAsync(rotated.RefreshToken);
        var survivor = await factory.FindTokenAsync(otherDevice.RefreshToken);

        output.WriteLine("── replay after 31 seconds (past the grace window) ──");
        Dump("token from the attacked chain", killed);
        Dump("token from the other device chain", survivor);

        Assert.NotNull(killed);
        Assert.NotNull(killed.RevokedAt);
        Assert.Equal(RefreshTokenRevocationReason.ReuseDetected, killed.RevokedReason);

        // The SAME user's other chain is untouched.
        Assert.NotNull(survivor);
        Assert.Null(survivor.RevokedAt);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostRefreshAsync(otherDevice.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Reusing_a_token_revoked_by_logout_never_kills_the_chain()
    {
        var login = await Client.LoginAsync("engineer", "SEED_ENGINEER_PASSWORD");
        var second = await (await Client.PostRefreshAsync(login.RefreshToken)).ReadTokensAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await Client.PostLogoutAsync(second.RefreshToken)).StatusCode);

        // Much later — a logout is never treated as an attack.
        factory.Clock.Advance(TimeSpan.FromHours(6));

        var reuse = await Client.PostRefreshAsync(second.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var token = await factory.FindTokenAsync(second.RefreshToken);
        output.WriteLine("── replaying a logged-out token, 6 hours later ──");
        Dump("token", token);

        Assert.Equal(RefreshTokenRevocationReason.Logout, token!.RevokedReason);

        var chain = await factory.ChainAsync(token.ChainId);
        output.WriteLine($"  revocation reasons in the chain: {string.Join(", ", chain.Select(t => t.RevokedReason?.ToString() ?? "null"))}");
        Assert.DoesNotContain(chain, t => t.RevokedReason == RefreshTokenRevocationReason.ReuseDetected);
    }

    [Fact]
    public async Task Chain_absolute_expiry_never_moves_no_matter_how_often_we_rotate()
    {
        var login = await Client.LoginAsync("engineer", "SEED_ENGINEER_PASSWORD");
        var first = await factory.FindTokenAsync(login.RefreshToken);
        var anchor = first!.ChainAbsoluteExpiry;

        var current = login.RefreshToken;
        for (var i = 0; i < 4; i++)
        {
            factory.Clock.Advance(TimeSpan.FromDays(3));
            current = (await (await Client.PostRefreshAsync(current)).ReadTokensAsync()).RefreshToken;
        }

        var latest = await factory.FindTokenAsync(current);
        output.WriteLine("── after 4 rotations spaced 3 days apart ──");
        output.WriteLine($"  absolute ceiling at sign-in   : {anchor:O}");
        output.WriteLine($"  absolute ceiling of newest    : {latest!.ChainAbsoluteExpiry:O}");
        output.WriteLine($"  expiry of the newest token    : {latest.ExpiresAt:O}");

        Assert.Equal(anchor, latest.ChainAbsoluteExpiry);
        Assert.True(latest.ExpiresAt <= anchor, "expires_at must never exceed the absolute ceiling");
    }
}

[CollectionDefinition(nameof(AuthCollection))]
public sealed class AuthCollection : ICollectionFixture<AuthTestFactory>;
