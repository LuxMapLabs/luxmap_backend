using System.Net;
using System.Text.Json;
using LuxMap.Modules.Identity.Entities;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

/// <summary>
/// Xoay vòng, dùng lại token và đồng thời. Mọi khẳng định đều đối chiếu TRẠNG THÁI THẬT
/// trong database, không chỉ tin mã trả về của service.
/// </summary>
[Collection(nameof(AuthCollection))]
public class AuthRotationTests(AuthTestFactory factory, ITestOutputHelper output)
{
    private HttpClient Client => factory.CreateClient();

    private void Dump(string label, RefreshToken? t)
        => output.WriteLine(t is null
            ? $"  {label}: (không có trong DB)"
            : $"  {label}: id={t.Id} chain={t.ChainId.ToString()[..8]} revoked_at={(t.RevokedAt?.ToString("HH:mm:ss") ?? "null")} "
              + $"reason={(t.RevokedReason?.ToString() ?? "null")} replaced_by={(t.ReplacedByTokenId?.ToString() ?? "null")}");

    [Fact]
    public async Task Rotation_revokes_the_old_token_and_links_it_to_the_new_one()
    {
        var first = await Client.LoginAsync("engineer", "SEED_ENGINEER_PASSWORD");
        var second = await (await Client.PostRefreshAsync(first.RefreshToken)).ReadTokensAsync();

        var oldToken = await factory.FindTokenAsync(first.RefreshToken);
        var newToken = await factory.FindTokenAsync(second.RefreshToken);

        output.WriteLine("── sau một lần xoay vòng ──");
        Dump("token cũ", oldToken);
        Dump("token mới", newToken);

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

        output.WriteLine($"── hai request đồng thời: {winners.Length} thắng, {losers.Length} thua ──");

        Assert.Single(winners);
        Assert.Single(losers);

        var issued = await winners[0].ReadTokensAsync();
        var winnerToken = await factory.FindTokenAsync(issued.RefreshToken);
        var original = await factory.FindTokenAsync(login.RefreshToken);

        Dump("token gốc", original);
        Dump("token request THẮNG phát ra", winnerToken);

        // Điểm mấu chốt: request thua KHÔNG được đụng tới token của request thắng.
        Assert.NotNull(winnerToken);
        Assert.Null(winnerToken.RevokedAt);
        Assert.Null(winnerToken.RevokedReason);

        // Và chuỗi vẫn còn đúng một token sống.
        var alive = (await factory.ChainAsync(original!.ChainId)).Count(t => t.RevokedAt is null);
        output.WriteLine($"  token còn sống trong chuỗi: {alive}");
        Assert.Equal(1, alive);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_inside_the_grace_window_does_not_kill_the_chain()
    {
        var login = await Client.LoginAsync("engineer", "SEED_ENGINEER_PASSWORD");
        var rotated = await (await Client.PostRefreshAsync(login.RefreshToken)).ReadTokensAsync();

        // Vẫn nằm trong 30 giây kể từ lúc thu hồi.
        factory.Clock.Advance(TimeSpan.FromSeconds(5));

        var reuse = await Client.PostRefreshAsync(login.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var live = await factory.FindTokenAsync(rotated.RefreshToken);
        output.WriteLine("── dùng lại sau 5 giây (trong ân hạn) ──");
        Dump("token đang sống", live);

        Assert.NotNull(live);
        Assert.Null(live.RevokedAt);

        // Chuỗi vẫn dùng được: refresh tiếp vẫn thành công.
        var stillWorks = await Client.PostRefreshAsync(rotated.RefreshToken);
        output.WriteLine($"  refresh tiếp theo trên chuỗi: HTTP {(int)stillWorks.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_after_the_grace_window_revokes_that_chain_only()
    {
        var victim = await Client.LoginAsync("engineer", "SEED_ENGINEER_PASSWORD");
        var otherDevice = await Client.LoginAsync("engineer", "SEED_ENGINEER_PASSWORD");

        var rotated = await (await Client.PostRefreshAsync(victim.RefreshToken)).ReadTokensAsync();

        // Quá cửa sổ ân hạn 30 giây.
        factory.Clock.Advance(TimeSpan.FromSeconds(31));

        var reuse = await Client.PostRefreshAsync(victim.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var killed = await factory.FindTokenAsync(rotated.RefreshToken);
        var survivor = await factory.FindTokenAsync(otherDevice.RefreshToken);

        output.WriteLine("── dùng lại sau 31 giây (quá ân hạn) ──");
        Dump("token của chuỗi bị tấn công", killed);
        Dump("token của chuỗi thiết bị khác", survivor);

        Assert.NotNull(killed);
        Assert.NotNull(killed.RevokedAt);
        Assert.Equal(RefreshTokenRevocationReason.ReuseDetected, killed.RevokedReason);

        // Chuỗi khác của CÙNG người dùng không bị đụng.
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

        // Rất lâu sau — logout không bao giờ bị coi là tấn công.
        factory.Clock.Advance(TimeSpan.FromHours(6));

        var reuse = await Client.PostRefreshAsync(second.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var token = await factory.FindTokenAsync(second.RefreshToken);
        output.WriteLine("── dùng lại token đã logout, 6 giờ sau ──");
        Dump("token", token);

        Assert.Equal(RefreshTokenRevocationReason.Logout, token!.RevokedReason);

        var chain = await factory.ChainAsync(token.ChainId);
        output.WriteLine($"  lý do thu hồi trong chuỗi: {string.Join(", ", chain.Select(t => t.RevokedReason?.ToString() ?? "null"))}");
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
        output.WriteLine("── sau 4 lần xoay vòng cách nhau 3 ngày ──");
        output.WriteLine($"  trần tuyệt đối lúc đăng nhập : {anchor:O}");
        output.WriteLine($"  trần tuyệt đối của token mới : {latest!.ChainAbsoluteExpiry:O}");
        output.WriteLine($"  hạn của token mới            : {latest.ExpiresAt:O}");

        Assert.Equal(anchor, latest.ChainAbsoluteExpiry);
        Assert.True(latest.ExpiresAt <= anchor, "expires_at không được vượt trần tuyệt đối");
    }
}

[CollectionDefinition(nameof(AuthCollection))]
public sealed class AuthCollection : ICollectionFixture<AuthTestFactory>;
