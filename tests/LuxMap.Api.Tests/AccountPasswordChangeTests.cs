using System.Net;
using System.Net.Http.Json;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

public partial class AccountAdminTests
{
    private const string ChangePasswordRoute = "/api/v1/auth/password/change";
    private const string ChangedPassword = "my-new-password-for-tests";

    [Theory]
    [InlineData("field_engineer")]
    [InlineData("manager")]
    [InlineData("superior")]
    [InlineData("system_admin")]
    public async Task Every_role_changes_only_its_own_password_and_ends_old_refresh_sessions(string role)
    {
        var account = await CreateActiveFieldEngineerAsync();
        Assert.Equal(HttpStatusCode.OK, (await (await AdminAsync()).PatchAsJsonAsync($"/api/v1/admin/users/{account.UserId}", new { role })).StatusCode);
        var session = await (await factory.CreateClient().PostLoginAsync(account.Username, NewPassword)).ReadTokensAsync();
        var client = Bearer(session.AccessToken);
        await ForgotAsync(account.Email);
        var oldLink = factory.Mail.LatestTokenFor(account.Email);
        var response = await client.PostAsJsonAsync(ChangePasswordRoute, new { old_password = NewPassword, new_password = ChangedPassword });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostLoginAsync(account.Username, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().PostLoginAsync(account.Username, ChangedPassword)).StatusCode);
        var refresh = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refresh_token = session.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetPasswordAsync(oldLink, "another-long-password")).StatusCode);
        var reason = await FindReasonAsync(session.RefreshToken);
        Assert.Equal(RefreshTokenRevocationReason.PasswordReset, reason);
    }

    [Fact]
    public async Task Wrong_old_password_does_not_change_hash_links_or_sessions()
    {
        var account = await CreateActiveFieldEngineerAsync();
        var session = await (await factory.CreateClient().PostLoginAsync(account.Username, NewPassword)).ReadTokensAsync();
        var before = await factory.QueryAsync(db => db.Set<AppUser>().AsNoTracking().SingleAsync(u => u.UserId == account.UserId));
        await ForgotAsync(account.Email);
        var tokenCount = await factory.QueryAsync(db => db.Set<AccountToken>().CountAsync(t => t.UserId == account.UserId));
        var response = await Bearer(session.AccessToken).PostAsJsonAsync(ChangePasswordRoute, new { old_password = "wrong-old-password", new_password = ChangedPassword });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await Json(response)).GetProperty("error");
        Assert.Equal("VALIDATION_FAILED", error.GetProperty("code").GetString());
        Assert.Equal("old_password", error.GetProperty("details").GetProperty("field").GetString());
        var after = await factory.QueryAsync(db => db.Set<AppUser>().AsNoTracking().SingleAsync(u => u.UserId == account.UserId));
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.PasswordSetAt, after.PasswordSetAt);
        Assert.Equal(tokenCount, await factory.QueryAsync(db => db.Set<AccountToken>().CountAsync(t => t.UserId == account.UserId)));
        Assert.Null(await FindReasonAsync(session.RefreshToken));
    }

    [Theory]
    [InlineData(null, ChangedPassword)]
    [InlineData("", ChangedPassword)]
    [InlineData(NewPassword, null)]
    [InlineData(NewPassword, "short")]
    public async Task Change_password_validates_presence_and_the_same_minimum_as_reset(string? oldPassword, string? newPassword)
    {
        var account = await CreateActiveFieldEngineerAsync();
        var session = await (await factory.CreateClient().PostLoginAsync(account.Username, NewPassword)).ReadTokensAsync();
        var response = await Bearer(session.AccessToken).PostAsJsonAsync(ChangePasswordRoute, new { old_password = oldPassword, new_password = newPassword });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await FindReasonAsync(session.RefreshToken));
    }

    [Fact]
    public async Task Change_password_rejects_oversized_values_and_cannot_target_another_user()
    {
        var account = await CreateActiveFieldEngineerAsync();
        var other = await CreateActiveFieldEngineerAsync();
        var session = await (await factory.CreateClient().PostLoginAsync(account.Username, NewPassword)).ReadTokensAsync();
        var client = Bearer(session.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(ChangePasswordRoute,
            new { old_password = NewPassword, new_password = new string('x', 1025) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(ChangePasswordRoute,
            new { old_password = new string('x', 1025), new_password = ChangedPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(ChangePasswordRoute,
            new { old_password = NewPassword, new_password = ChangedPassword, user_id = other.UserId })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().PostLoginAsync(other.Username, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Change_password_requires_authentication_and_is_not_marked_anonymous()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(ChangePasswordRoute,
            new { old_password = NewPassword, new_password = ChangedPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.Single(e =>
            e.Metadata.GetMetadata<ControllerActionDescriptor>() is { ControllerName: "Password", ActionName: "Change" });
        Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), a => a.Policy == "cap:manage_own_profile");
    }

    [Fact]
    public async Task Reset_link_and_authenticated_change_serialize_without_reusing_old_credentials()
    {
        var account = await CreateActiveFieldEngineerAsync();
        var session = await (await factory.CreateClient().PostLoginAsync(account.Username, NewPassword)).ReadTokensAsync();
        await ForgotAsync(account.Email);
        var reset = factory.Mail.LatestTokenFor(account.Email);
        var responses = await Task.WhenAll(
            Bearer(session.AccessToken).PostAsJsonAsync(ChangePasswordRoute, new { old_password = NewPassword, new_password = ChangedPassword }),
            SetPasswordAsync(reset, "password-from-the-reset-link"));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Concurrent_changes_cannot_both_reuse_the_same_old_password()
    {
        var account = await CreateActiveFieldEngineerAsync();
        var session = await (await factory.CreateClient().PostLoginAsync(account.Username, NewPassword)).ReadTokensAsync();
        var client = Bearer(session.AccessToken);
        var responses = await Task.WhenAll(client.PostAsJsonAsync(ChangePasswordRoute, new { old_password = NewPassword, new_password = ChangedPassword }),
            client.PostAsJsonAsync(ChangePasswordRoute, new { old_password = NewPassword, new_password = "a-second-new-password" }));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
    }
}
