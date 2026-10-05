using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Identity.Accounts;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-33a: a system admin creates accounts, the person sets a password from the emailed link, and the
/// admin can change, lock and re-invite. Every account here is a throwaway under
/// <see cref="AccountTestFactory.UsernamePrefix"/>; the seeded accounts are only used to sign in.
/// </summary>
public class AccountAdminTests(AccountTestFactory factory, ITestOutputHelper output)
    : IClassFixture<AccountTestFactory>, IAsyncLifetime
{
    private const string NewPassword = "a-long-enough-new-password";

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => factory.DeleteTestAccountsAsync();

    [Fact]
    public async Task The_admin_invites_by_mail_and_the_link_sets_the_first_password()
    {
        var commune = await factory.SeededCommuneIdAsync();
        var username = AccountTestFactory.NewUsername();
        var email = $"{username}@example.invalid";

        var created = await CreateAsync(new { username, email, full_name = "Kỹ sư mới", role = "field_engineer", commune_ids = new[] { commune } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await Json(created);
        output.WriteLine(body.ToString());

        var user = body.GetProperty("user");
        Assert.True(body.GetProperty("invitation_sent").GetBoolean());
        Assert.Equal("invited", user.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, user.GetProperty("password_set_at").ValueKind);
        Assert.Equal([commune], user.GetProperty("commune_ids").EnumerateArray().Select(c => c.GetString()));

        var mail = Assert.Single(factory.Mail.To(email));
        Assert.Contains(username, mail.Body);
        Assert.Contains("http://localhost:5173/set-password?token=", mail.Body);
        Assert.Contains("http://localhost:5173/set-password?token=", mail.HtmlBody);

        // Invited: no password yet, and sign-in says exactly what a wrong password says.
        var early = await factory.CreateClient().PostLoginAsync(username, NewPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, early.StatusCode);
        Assert.Contains(ErrorCodes.InvalidCredentials, await early.Content.ReadAsStringAsync());

        var token = factory.Mail.LatestTokenFor(email);
        Assert.Equal(HttpStatusCode.NoContent, (await SetPasswordAsync(token, NewPassword)).StatusCode);

        var tokens = await (await factory.CreateClient().PostLoginAsync(username, NewPassword)).ReadTokensAsync();
        var me = await Json(await Bearer(tokens.AccessToken).GetAsync("/api/v1/auth/me"));
        Assert.Equal([commune], me.GetProperty("commune_ids").EnumerateArray().Select(c => c.GetString()));

        var again = await SetPasswordAsync(token, "yet-another-long-password");
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains(AccountErrors.InvalidAccountToken, await again.Content.ReadAsStringAsync());

        var detail = await Json(await (await AdminAsync()).GetAsync($"/api/v1/admin/users/{user.GetProperty("user_id").GetString()}"));
        Assert.Equal("active", detail.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Only_the_raw_token_reaches_the_mail_and_the_database_keeps_its_hash()
    {
        var email = (await CreateFieldEngineerAsync()).Email;
        var token = factory.Mail.LatestTokenFor(email);

        var stored = await factory.QueryAsync(db => db.Set<AccountToken>().AsNoTracking()
            .Where(link => link.User.Email == email).Select(link => link.TokenHash).SingleAsync());
        Assert.NotEqual(token, stored);
        Assert.Equal(Modules.Identity.Auth.RefreshTokenGenerator.Hash(token), stored);
    }

    [Fact]
    public async Task The_other_roles_cannot_manage_accounts()
    {
        foreach (var (username, variable) in new[] { ("engineer", "SEED_ENGINEER_PASSWORD"), ("agency", "SEED_AGENCY_PASSWORD"), ("crew", "SEED_CREW_PASSWORD") })
        {
            var client = Bearer((await factory.CreateClient().LoginAsync(username, variable)).AccessToken);
            var list = await client.GetAsync("/api/v1/admin/users");
            output.WriteLine($"  {username}: GET /admin/users → {(int)list.StatusCode}");
            Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
            Assert.Contains(ErrorCodes.RoleForbidden, await list.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData("field_engineer", null, "This role needs at least one commune.")]
    [InlineData("manager", new string[0], "This role needs at least one commune.")]
    [InlineData("superior", new[] { "COM-NOPE" }, "Unknown commune(s): COM-NOPE.")]
    public async Task A_scoped_role_needs_communes_that_exist(string role, string[]? communes, string message)
    {
        var username = AccountTestFactory.NewUsername();
        var response = await CreateAsync(new { username, email = $"{username}@example.invalid", full_name = "Thiếu xã", role, commune_ids = communes });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await Json(response)).GetProperty("error");
        Assert.Equal(ErrorCodes.ValidationFailed, error.GetProperty("code").GetString());
        Assert.Equal("commune_ids", error.GetProperty("details").GetProperty("field").GetString());
        Assert.Equal(message, error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_system_admin_takes_no_communes_and_covers_all_of_them()
    {
        var commune = await factory.SeededCommuneIdAsync();
        var username = AccountTestFactory.NewUsername();

        var refused = await CreateAsync(new { username, email = $"{username}@example.invalid", full_name = "Quản trị", role = "system_admin", commune_ids = new[] { commune } });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var created = await Json(await CreateAsync(new { username, email = $"{username}@example.invalid", full_name = "Quản trị", role = "system_admin" }));
        Assert.Equal(["*"], created.GetProperty("user").GetProperty("commune_ids").EnumerateArray().Select(c => c.GetString()));
        Assert.True(await factory.QueryAsync(db => db.Set<AppUser>().Where(u => u.Username == username).Select(u => u.HasSystemWideScope).SingleAsync()));
    }

    [Fact]
    public async Task A_username_or_email_already_used_in_any_case_is_409()
    {
        var first = await CreateFieldEngineerAsync();

        var sameName = await CreateAsync(new { username = first.Username.ToUpperInvariant(), email = $"other-{first.Email}", full_name = "Trùng", role = "field_engineer", commune_ids = new[] { await factory.SeededCommuneIdAsync() } });
        var sameMail = await CreateAsync(new { username = AccountTestFactory.NewUsername(), email = first.Email.ToUpperInvariant(), full_name = "Trùng", role = "field_engineer", commune_ids = new[] { await factory.SeededCommuneIdAsync() } });

        Assert.Equal(HttpStatusCode.Conflict, sameName.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, sameMail.StatusCode);
        Assert.True((await Json(sameName)).GetProperty("error").GetProperty("details").TryGetProperty("username", out _));
        Assert.True((await Json(sameMail)).GetProperty("error").GetProperty("details").TryGetProperty("email", out _));
    }

    [Fact]
    public async Task A_failed_mail_still_creates_the_account_and_a_resent_invitation_replaces_the_old_link()
    {
        factory.Mail.Fail = true;
        CreatedAccount account;
        try
        {
            var username = AccountTestFactory.NewUsername();
            var response = await CreateAsync(new { username, email = $"{username}@example.invalid", full_name = "Thư lỗi", role = "field_engineer", commune_ids = new[] { await factory.SeededCommuneIdAsync() } });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await Json(response);
            Assert.False(body.GetProperty("invitation_sent").GetBoolean());
            account = new CreatedAccount(body.GetProperty("user").GetProperty("user_id").GetString()!, username, $"{username}@example.invalid");
        }
        finally
        {
            factory.Mail.Fail = false;
        }

        var admin = await AdminAsync();
        Assert.True((await Json(await admin.PostAsync($"/api/v1/admin/users/{account.UserId}/invite", null))).GetProperty("invitation_sent").GetBoolean());
        var first = factory.Mail.LatestTokenFor(account.Email);
        Assert.True((await Json(await admin.PostAsync($"/api/v1/admin/users/{account.UserId}/invite", null))).GetProperty("invitation_sent").GetBoolean());
        var second = factory.Mail.LatestTokenFor(account.Email);

        Assert.Equal(HttpStatusCode.BadRequest, (await SetPasswordAsync(first, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SetPasswordAsync(second, NewPassword)).StatusCode);

        var resend = await admin.PostAsync($"/api/v1/admin/users/{account.UserId}/invite", null);
        Assert.Equal(HttpStatusCode.Conflict, resend.StatusCode);
        Assert.Contains(AccountErrors.AccountAlreadyActive, await resend.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_invitation_lives_72_hours_and_is_refused_once_expired()
    {
        var account = await CreateFieldEngineerAsync();
        var token = factory.Mail.LatestTokenFor(account.Email);

        Assert.Equal(TimeSpan.FromHours(72), await LifetimeAsync(token));
        await ExpireAsync(token);

        var response = await SetPasswordAsync(token, NewPassword);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(AccountErrors.InvalidAccountToken, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_short_password_is_refused_and_the_link_stays_usable()
    {
        var account = await CreateFieldEngineerAsync();
        var token = factory.Mail.LatestTokenFor(account.Email);

        Assert.Equal(HttpStatusCode.BadRequest, (await SetPasswordAsync(token, "short")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SetPasswordAsync(token, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Locking_ends_every_session_and_unlocking_lets_the_account_back_in()
    {
        var account = await CreateActiveFieldEngineerAsync();
        var session = await (await factory.CreateClient().PostLoginAsync(account.Username, NewPassword)).ReadTokensAsync();
        var admin = await AdminAsync();

        var locked = await Json(await admin.PostAsync($"/api/v1/admin/users/{account.UserId}/lock", null));
        Assert.Equal("locked", locked.GetProperty("status").GetString());

        var reasons = await factory.QueryAsync(db => db.Set<RefreshToken>().AsNoTracking()
            .Where(t => t.UserId == account.UserId).Select(t => t.RevokedReason).ToListAsync());
        Assert.All(reasons, reason => Assert.Equal(RefreshTokenRevocationReason.AccountLocked, reason));

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostRefreshAsync(session.RefreshToken)).StatusCode);
        var login = await factory.CreateClient().PostLoginAsync(account.Username, NewPassword);
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Contains(ErrorCodes.AccountLocked, await login.Content.ReadAsStringAsync());

        Assert.Equal("active", (await Json(await admin.PostAsync($"/api/v1/admin/users/{account.UserId}/unlock", null))).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().PostLoginAsync(account.Username, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task An_admin_cannot_lock_their_own_account()
    {
        var adminId = await factory.QueryAsync(db => db.Set<AppUser>().Where(u => u.Username == "admin").Select(u => u.UserId).SingleAsync());
        var response = await (await AdminAsync()).PostAsync($"/api/v1/admin/users/{adminId}/lock", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(AccountErrors.CannotLockSelf, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Patch_changes_communes_and_role_and_keeps_the_scope_flag_in_step()
    {
        var home = await factory.SeededCommuneIdAsync();
        var second = await factory.QueryAsync(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var unit = new AdministrativeUnit { Name = $"Account test {Guid.NewGuid():N}" };
            db.Add(unit);
            await db.SaveChangesAsync();
            return unit.CommuneId;
        });

        try
        {
            var account = await CreateFieldEngineerAsync();
            var admin = await AdminAsync();
            async Task<HttpResponseMessage> Patch(object body)
                => await admin.PatchAsJsonAsync($"/api/v1/admin/users/{account.UserId}", body);

            var both = await Json(await Patch(new { commune_ids = new[] { second, home } }));
            Assert.Equal(new[] { home, second }.Order(StringComparer.Ordinal), both.GetProperty("commune_ids").EnumerateArray().Select(c => c.GetString()));

            var manager = await Json(await Patch(new { role = "manager", full_name = "Đổi tên" }));
            Assert.Equal("manager", manager.GetProperty("role").GetString());
            Assert.Equal(2, manager.GetProperty("commune_ids").GetArrayLength());
            Assert.Equal("Đổi tên", manager.GetProperty("full_name").GetString());

            var promoted = await Json(await Patch(new { role = "system_admin" }));
            Assert.Equal(["*"], promoted.GetProperty("commune_ids").EnumerateArray().Select(c => c.GetString()));
            Assert.Equal(0, await factory.QueryAsync(db => db.Set<AppUserCommune>().CountAsync(a => a.UserId == account.UserId)));

            var noCommunes = await Patch(new { role = "field_engineer" });
            Assert.Equal(HttpStatusCode.BadRequest, noCommunes.StatusCode);

            var demoted = await Json(await Patch(new { role = "field_engineer", commune_ids = new[] { home } }));
            Assert.Equal([home], demoted.GetProperty("commune_ids").EnumerateArray().Select(c => c.GetString()));
            Assert.False(await factory.QueryAsync(db => db.Set<AppUser>().Where(u => u.UserId == account.UserId).Select(u => u.HasSystemWideScope).SingleAsync()));
        }
        finally
        {
            await factory.DeleteTestAccountsAsync();
            await factory.QueryAsync(async db =>
            {
                #pragma warning disable RS0030 // Test TEARDOWN: bulk delete is the only way to clean up under an empty scope. BE-36 removes the need entirely — a fresh database per run.
                return await db.Set<AdministrativeUnit>().Where(u => u.CommuneId == second).ExecuteDeleteAsync();
                #pragma warning restore RS0030
            });
        }
    }

    [Fact]
    public async Task Patch_refuses_an_email_another_account_uses()
    {
        var first = await CreateFieldEngineerAsync();
        var second = await CreateFieldEngineerAsync();

        var response = await (await AdminAsync()).PatchAsJsonAsync($"/api/v1/admin/users/{second.UserId}", new { email = first.Email });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var same = await (await AdminAsync()).PatchAsJsonAsync($"/api/v1/admin/users/{second.UserId}", new { email = second.Email.ToUpperInvariant() });
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
    }

    /// <summary>
    /// Run against the service inside a transaction that is rolled back: the other admins are locked
    /// only where this transaction can see it, so no concurrent test ever meets an admin-less database.
    /// </summary>
    [Fact]
    public async Task The_last_active_system_admin_can_be_neither_demoted_nor_locked()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        await db.Database.ExecuteSqlRawAsync("UPDATE app_user SET is_locked = true WHERE role = 'system_admin' AND username <> 'admin'");
        var adminId = await db.Set<AppUser>().Where(u => u.Username == "admin").Select(u => u.UserId).SingleAsync();

        var demote = await Assert.ThrowsAsync<LuxMapException>(() =>
            service.UpdateAsync(adminId, new UpdateUserRequest { Role = UserRole.Manager, CommuneIds = [] }, CancellationToken.None));
        var lockIt = await Assert.ThrowsAsync<LuxMapException>(() => service.LockAsync(adminId, CancellationToken.None));

        Assert.Equal(AccountErrors.LastSystemAdmin, demote.Code);
        Assert.Equal(AccountErrors.LastSystemAdmin, lockIt.Code);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task The_list_filters_by_status_and_role_and_rejects_an_unknown_status()
    {
        var invited = await CreateFieldEngineerAsync();
        var active = await CreateActiveFieldEngineerAsync();
        var admin = await AdminAsync();

        var invitedIds = await Ids(await admin.GetAsync("/api/v1/admin/users?status=invited&role=field_engineer&page_size=200"));
        var activeIds = await Ids(await admin.GetAsync("/api/v1/admin/users?status=active&page_size=200"));

        Assert.Contains(invited.UserId, invitedIds);
        Assert.DoesNotContain(active.UserId, invitedIds);
        Assert.Contains(active.UserId, activeIds);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/admin/users?status=sleeping")).StatusCode);

        static async Task<string[]> Ids(HttpResponseMessage response)
            => (await Json(response)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("user_id").GetString()!).ToArray();
    }

    [Fact]
    public async Task Forgot_password_answers_the_same_for_any_address_and_mails_only_a_usable_account()
    {
        var account = await CreateActiveFieldEngineerAsync();
        var unknown = $"{AccountTestFactory.NewUsername()}@example.invalid";

        var forKnown = await ForgotAsync(account.Email);
        var forUnknown = await ForgotAsync(unknown);

        Assert.Equal(HttpStatusCode.Accepted, forKnown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, forUnknown.StatusCode);
        Assert.Equal(await forKnown.Content.ReadAsStringAsync(), await forUnknown.Content.ReadAsStringAsync());
        Assert.Empty(factory.Mail.To(unknown));
        Assert.Equal("LuxMap — Đặt lại mật khẩu", factory.Mail.To(account.Email).Last().Subject);
    }

    [Fact]
    public async Task A_reset_link_replaces_the_password_ends_old_sessions_and_lives_one_hour()
    {
        var account = await CreateActiveFieldEngineerAsync();
        var session = await (await factory.CreateClient().PostLoginAsync(account.Username, NewPassword)).ReadTokensAsync();

        await ForgotAsync(account.Email);
        var token = factory.Mail.LatestTokenFor(account.Email);
        Assert.Equal(HttpStatusCode.NoContent, (await SetPasswordAsync(token, "the-replacement-password")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostLoginAsync(account.Username, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().PostLoginAsync(account.Username, "the-replacement-password")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostRefreshAsync(session.RefreshToken)).StatusCode);
        Assert.Equal(RefreshTokenRevocationReason.PasswordReset, (await FindReasonAsync(session.RefreshToken)));

        await ForgotAsync(account.Email);
        var late = factory.Mail.LatestTokenFor(account.Email);
        Assert.Equal(TimeSpan.FromHours(1), await LifetimeAsync(late));
        await ExpireAsync(late);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetPasswordAsync(late, "too-late-a-password")).StatusCode);
    }

    [Fact]
    public async Task Forgot_password_sends_an_invited_account_a_new_invitation_and_a_locked_account_nothing()
    {
        var invited = await CreateFieldEngineerAsync();
        await ForgotAsync(invited.Email);
        Assert.Equal("LuxMap — Lời mời tạo tài khoản", factory.Mail.To(invited.Email).Last().Subject);
        Assert.Equal(2, factory.Mail.To(invited.Email).Count);

        var locked = await CreateActiveFieldEngineerAsync();
        await (await AdminAsync()).PostAsync($"/api/v1/admin/users/{locked.UserId}/lock", null);
        var before = factory.Mail.To(locked.Email).Count;
        Assert.Equal(HttpStatusCode.Accepted, (await ForgotAsync(locked.Email)).StatusCode);
        Assert.Equal(before, factory.Mail.To(locked.Email).Count);
    }

    [Fact]
    public async Task The_database_refuses_a_system_wide_scope_on_any_other_role()
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => factory.QueryAsync(async db =>
        {
            db.Add(new AppUser
            {
                Username = AccountTestFactory.NewUsername(),
                Email = $"{Guid.NewGuid():N}@example.invalid",
                FullName = "Scope mismatch",
                Role = UserRole.Manager,
                HasSystemWideScope = true,
                PasswordAlgorithm = Modules.Identity.Seeding.IdentitySeeder.PasswordAlgorithm,
            });
            return await db.SaveChangesAsync();
        }));

        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal("ck_app_user_system_wide_scope_matches_role", postgres.ConstraintName);
    }

    private sealed record CreatedAccount(string UserId, string Username, string Email);

    /// <summary>How long the link was issued for, read from its row.</summary>
    private Task<TimeSpan> LifetimeAsync(string rawToken)
    {
        var hash = Modules.Identity.Auth.RefreshTokenGenerator.Hash(rawToken);
        return factory.QueryAsync(db => db.Set<AccountToken>().AsNoTracking()
            .Where(link => link.TokenHash == hash).Select(link => link.ExpiresAt - link.CreatedAt).SingleAsync());
    }

    /// <summary>
    /// Moves the link's expiry one second into the past. The host clock is a FakeTimeProvider, which
    /// cannot go back: winding it forward would leave every later access token not yet valid.
    /// </summary>
    private Task ExpireAsync(string rawToken)
    {
        var hash = Modules.Identity.Auth.RefreshTokenGenerator.Hash(rawToken);
        return factory.QueryAsync(async db =>
        {
            var link = await db.Set<AccountToken>().SingleAsync(l => l.TokenHash == hash);
            link.ExpiresAt = factory.Clock.GetUtcNow().UtcDateTime.AddSeconds(-1);
            return await db.SaveChangesAsync();
        });
    }

    private async Task<CreatedAccount> CreateFieldEngineerAsync()
    {
        var username = AccountTestFactory.NewUsername();
        var email = $"{username}@example.invalid";
        var response = await CreateAsync(new { username, email, full_name = "Kỹ sư thử", role = "field_engineer", commune_ids = new[] { await factory.SeededCommuneIdAsync() } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return new CreatedAccount((await Json(response)).GetProperty("user").GetProperty("user_id").GetString()!, username, email);
    }

    private async Task<CreatedAccount> CreateActiveFieldEngineerAsync()
    {
        var account = await CreateFieldEngineerAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await SetPasswordAsync(factory.Mail.LatestTokenFor(account.Email), NewPassword)).StatusCode);
        return account;
    }

    private async Task<HttpResponseMessage> CreateAsync(object body)
        => await (await AdminAsync()).PostAsJsonAsync("/api/v1/admin/users", body);

    private Task<HttpResponseMessage> SetPasswordAsync(string token, string password)
        => factory.CreateClient().PostAsJsonAsync("/api/v1/auth/password/set", new { token, new_password = password });

    private Task<HttpResponseMessage> ForgotAsync(string email)
        => factory.CreateClient().PostAsJsonAsync("/api/v1/auth/password/forgot", new { email });

    private Task<RefreshTokenRevocationReason?> FindReasonAsync(string rawRefreshToken)
    {
        var hash = Modules.Identity.Auth.RefreshTokenGenerator.Hash(rawRefreshToken);
        return factory.QueryAsync(db => db.Set<RefreshToken>().AsNoTracking()
            .Where(t => t.TokenHash == hash).Select(t => t.RevokedReason).SingleAsync());
    }

    private async Task<HttpClient> AdminAsync()
        => Bearer((await factory.CreateClient().LoginAsync("admin", "SEED_ADMIN_PASSWORD")).AccessToken);

    private HttpClient Bearer(string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}

/// <summary>The forgot-password endpoint refuses the third request in a window of two.</summary>
public class AccountMailRateLimitTests(TightRateLimitAccountTestFactory factory) : IClassFixture<TightRateLimitAccountTestFactory>
{
    [Fact]
    public async Task The_third_request_from_one_address_is_429_with_retry_after()
    {
        var client = factory.CreateClient();
        var email = $"{AccountTestFactory.NewUsername()}@example.invalid";

        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/auth/password/forgot", new { email })).StatusCode);
        }

        var refused = await client.PostAsJsonAsync("/api/v1/auth/password/forgot", new { email });
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.Contains("Retry-After"));
        Assert.Contains(ErrorCodes.RateLimited, await refused.Content.ReadAsStringAsync());
    }
}
