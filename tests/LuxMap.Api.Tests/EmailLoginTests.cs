using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Identity.Seeding;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LuxMap.Api.Tests;

[CollectionDefinition(nameof(EmailLoginCollection))]
public sealed class EmailLoginCollection : ICollectionFixture<AuthTestFactory>;

[Collection(nameof(EmailLoginCollection))]
public sealed class EmailLoginTests(AuthTestFactory factory) : IAsyncLifetime
{
    private const string Prefix = "email-login-";
    private const string Password = "Email-login-password-42!";
    private const string OtherPassword = "Other-email-password-43!";

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => factory.QueryAsync(async db =>
    {
#pragma warning disable RS0030 // Test teardown: sessions/links/assignments cascade; only this class's accounts.
        return await db.Set<AppUser>().Where(user => user.Username.StartsWith(Prefix)).ExecuteDeleteAsync();
#pragma warning restore RS0030
    });

    // (a), (b), (e): exact subject assertions through both endpoint groups.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Email_and_username_sign_in_identify_the_same_account(bool web)
    {
        var user = await CreateAsync();
        await AssertSubjectAsync(await LoginAsync(web, user.Email), user.UserId);
        await AssertSubjectAsync(await LoginAsync(web, $"  {user.Email.ToUpperInvariant()}  "), user.UserId);
        await AssertSubjectAsync(await LoginAsync(web, user.Username), user.UserId);
        await AssertSubjectAsync(await LoginAsync(web, $"  {user.Username.ToUpperInvariant()}  "), user.UserId);
    }

    // (c), (d): one literal code/message, including invited and locked accounts with a wrong password.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failures_keep_the_existing_code_and_message_and_do_not_reveal_lock_or_invitation(bool web)
    {
        var user = await CreateAsync();
        var invited = await CreateAsync(invited: true);
        var locked = await CreateAsync(locked: true);

        await AssertInvalidAsync(await LoginAsync(web, user.Email, OtherPassword));
        await AssertInvalidAsync(await LoginAsync(web, user.Username, OtherPassword));
        await AssertInvalidAsync(await LoginAsync(web, $"{Prefix}{Guid.NewGuid():N}@unknown.test"));
        await AssertInvalidAsync(await LoginAsync(web, invited.Email));
        await AssertInvalidAsync(await LoginAsync(web, locked.Email, OtherPassword));

        using var blocked = await LoginAsync(web, locked.Email);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        var error = (await BodyAsync(blocked)).GetProperty("error");
        Assert.Equal("ACCOUNT_LOCKED", error.GetProperty("code").GetString());
        Assert.Equal("This account is locked. Contact an administrator.", error.GetProperty("message").GetString());
    }

    // (f): direct insertion preserves the legacy username-with-@ scenario.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Username_wins_over_another_accounts_email_even_when_its_password_is_wrong(bool web)
    {
        var identifier = $"{Prefix}{Guid.NewGuid():N}@collision.test";
        var first = await CreateAsync(username: identifier);
        await CreateAsync(email: identifier, password: OtherPassword);

        await AssertSubjectAsync(await LoginAsync(web, $"  {identifier.ToUpperInvariant()}  "), first.UserId);
        await AssertInvalidAsync(await LoginAsync(web, identifier, OtherPassword));
    }

    // (g): bypass RequireFreeAsync to exercise case-sensitive DB uniqueness with legacy data.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ambiguous_email_fails_closed_for_either_accounts_correct_password(bool web)
    {
        var email = $"{Prefix}{Guid.NewGuid():N}@ambiguous.test";
        await CreateAsync(email: email);
        await CreateAsync(email: email.ToUpperInvariant(), password: OtherPassword);

        await AssertInvalidAsync(await LoginAsync(web, email));
        await AssertInvalidAsync(await LoginAsync(web, email.ToUpperInvariant(), OtherPassword));
    }

    // (h): use the public PATCH path, then fresh login requests (no stale tracked entities).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admin_email_change_immediately_replaces_the_login_email(bool web)
    {
        var user = await CreateAsync();
        await AssertSubjectAsync(await LoginAsync(web, user.Email), user.UserId);
        var newEmail = $"{Prefix}{Guid.NewGuid():N}@changed.test";
        using var admin = factory.CreateClient();
        var tokens = await admin.LoginAsync("admin", "SEED_ADMIN_PASSWORD");
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var patched = await admin.PatchAsJsonAsync($"/api/v1/admin/users/{user.UserId}", new { email = newEmail });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        Assert.Equal(newEmail, (await BodyAsync(patched)).GetProperty("email").GetString());

        await AssertInvalidAsync(await LoginAsync(web, user.Email));
        await AssertSubjectAsync(await LoginAsync(web, newEmail), user.UserId);
        await AssertSubjectAsync(await LoginAsync(web, user.Username), user.UserId);
    }

    private Task<AppUser> CreateAsync(
        string? username = null, string? email = null, string password = Password,
        bool invited = false, bool locked = false)
        => factory.QueryAsync(async db =>
        {
            var name = username ?? $"{Prefix}{Guid.NewGuid():N}";
            var user = new AppUser
            {
                Username = name,
                Email = email ?? $"{Prefix}{Guid.NewGuid():N}@example.test",
                FullName = "Email login probe",
                Role = UserRole.FieldEngineer,
                IsLocked = locked,
                PasswordAlgorithm = IdentitySeeder.PasswordAlgorithm,
                PasswordSetAt = invited ? null : DateTime.UtcNow,
            };
            if (!invited)
            {
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, password);
            }

            db.Set<AppUser>().Add(user);
            await db.SaveChangesAsync();
            return user;
        });

    private async Task<HttpResponseMessage> LoginAsync(bool web, string identifier, string password = Password)
    {
        using var client = web ? factory.CreateWebClient() : factory.CreateClient();
        return web
            ? await client.PostWebLoginAsync(identifier, password, rememberMe: false)
            : await client.PostLoginAsync(identifier, password);
    }

    private static async Task AssertSubjectAsync(HttpResponseMessage response, string userId)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var accessToken = (await BodyAsync(response)).GetProperty("access_token").GetString()!;
            Assert.Equal(userId, new JsonWebToken(accessToken).Subject);
        }
    }

    private static async Task AssertInvalidAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var error = (await BodyAsync(response)).GetProperty("error");
            Assert.Equal("INVALID_CREDENTIALS", error.GetProperty("code").GetString());
            Assert.Equal("Incorrect username or password.", error.GetProperty("message").GetString());
            Assert.Equal(["correlation_id"], error.GetProperty("details").EnumerateObject().Select(property => property.Name));
        }
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
