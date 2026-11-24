using System.Net;
using System.Net.Http.Json;
using LuxMap.Modules.Identity.Entities;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

public partial class AccountAdminTests
{
    [Fact]
    public async Task Email_change_notifies_only_the_new_mailbox_and_keeps_the_password()
    {
        var account = await CreateActiveFieldEngineerAsync();
        var oldMailCount = factory.Mail.To(account.Email).Count;
        var newEmail = $"{AccountTestFactory.NewUsername()}@example.invalid";
        var before = await factory.QueryAsync(db => db.Set<AppUser>().AsNoTracking().SingleAsync(u => u.UserId == account.UserId));
        var response = await (await AdminAsync()).PatchAsJsonAsync($"/api/v1/admin/users/{account.UserId}/email", new { email = newEmail });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.True(body.GetProperty("notification_sent").GetBoolean());
        Assert.Equal(newEmail, body.GetProperty("user").GetProperty("email").GetString());
        var message = Assert.Single(factory.Mail.To(newEmail));
        Assert.Equal("LuxMap — Đổi email thành công", message.Subject);
        Assert.Contains("mật khẩu cũ", message.Body);
        Assert.Contains(newEmail, message.HtmlBody);
        Assert.Equal(oldMailCount, factory.Mail.To(account.Email).Count);
        var after = await factory.QueryAsync(db => db.Set<AppUser>().AsNoTracking().SingleAsync(u => u.UserId == account.UserId));
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.PasswordSetAt, after.PasswordSetAt);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().PostLoginAsync(newEmail, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().PostLoginAsync(account.Username, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostLoginAsync(account.Email, NewPassword)).StatusCode);
    }

    [Theory]
    [InlineData("new@example.invalid")]
    [InlineData(null)]
    public async Task Profile_patch_rejects_email_even_null_without_partial_changes(string? email)
    {
        var account = await CreateFieldEngineerAsync();
        var response = await (await AdminAsync()).PatchAsJsonAsync($"/api/v1/admin/users/{account.UserId}", new { email, full_name = "Must not change" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await Json(response)).GetProperty("error");
        Assert.Equal("VALIDATION_FAILED", error.GetProperty("code").GetString());
        Assert.Equal("email", error.GetProperty("details").GetProperty("field").GetString());
        var row = await factory.QueryAsync(db => db.Set<AppUser>().AsNoTracking().SingleAsync(u => u.UserId == account.UserId));
        Assert.Equal(account.Email, row.Email);
        Assert.NotEqual("Must not change", row.FullName);
    }

    [Fact]
    public async Task Email_notification_failure_is_explicit_and_same_address_can_retry()
    {
        var account = await CreateActiveFieldEngineerAsync();
        var newEmail = $"{AccountTestFactory.NewUsername()}@example.invalid";
        var admin = await AdminAsync();
        var route = $"/api/v1/admin/users/{account.UserId}/email";
        try
        {
            factory.Mail.Fail = true;
            var failed = await admin.PatchAsJsonAsync(route, new { email = newEmail });
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
            Assert.False((await Json(failed)).GetProperty("notification_sent").GetBoolean());
            Assert.Equal(newEmail, (await Json(failed)).GetProperty("user").GetProperty("email").GetString());
        }
        finally { factory.Mail.Fail = false; }
        var retry = await admin.PatchAsJsonAsync(route, new { email = newEmail });
        Assert.True((await Json(retry)).GetProperty("notification_sent").GetBoolean());
        Assert.Single(factory.Mail.To(newEmail));
    }

    [Fact]
    public async Task Email_change_invalidates_password_links_sent_to_the_old_mailbox()
    {
        var account = await CreateActiveFieldEngineerAsync();
        await ForgotAsync(account.Email);
        var token = factory.Mail.LatestTokenFor(account.Email);
        var response = await (await AdminAsync()).PatchAsJsonAsync($"/api/v1/admin/users/{account.UserId}/email",
            new { email = $"{AccountTestFactory.NewUsername()}@example.invalid" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetPasswordAsync(token, "a-different-long-password")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid-email")]
    public async Task Email_endpoint_requires_a_valid_address(string? email)
    {
        var account = await CreateFieldEngineerAsync();
        var response = await (await AdminAsync()).PatchAsJsonAsync($"/api/v1/admin/users/{account.UserId}/email", new { email });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Only_admin_can_change_email_and_missing_user_is_not_found()
    {
        var account = await CreateActiveFieldEngineerAsync();
        var route = $"/api/v1/admin/users/{account.UserId}/email";
        var body = new { email = "unused@example.invalid" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PatchAsJsonAsync(route, body)).StatusCode);
        foreach (var (username, passwordVariable) in new[] { ("engineer", "SEED_ENGINEER_PASSWORD"), ("crew", "SEED_CREW_PASSWORD"), ("agency", "SEED_AGENCY_PASSWORD") })
        {
            var client = Bearer((await factory.CreateClient().LoginAsync(username, passwordVariable)).AccessToken);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync(route, body)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await (await AdminAsync()).PatchAsJsonAsync("/api/v1/admin/users/USR-999999999/email", body)).StatusCode);
    }
}
