using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Identity.Profiles;
using LuxMap.Modules.Identity.Seeding;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

/// <summary>ACCT-6: a signed-in user edits their own full_name; nothing else is reachable from this endpoint.</summary>
public sealed class ProfileTests(AccountTestFactory factory) : IClassFixture<AccountTestFactory>, IAsyncLifetime
{
    private const string Me = "/api/v1/auth/me";
    private readonly List<string> users = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
        => await factory.QueryAsync(async db =>
        {
            db.RemoveRange(await db.Set<AppUser>().Where(u => users.Contains(u.UserId)).ToListAsync());
            return await db.SaveChangesAsync(); // Only our accounts; tokens and assignments cascade.
        });

    [Fact]
    public void Patch_me_is_not_anonymous()
    {
        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Single(e => e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() == typeof(ProfileController));
        Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    [Theory]
    [InlineData(UserRole.Superior)]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.FieldEngineer)]
    [InlineData(UserRole.SystemAdmin)]
    public async Task Every_role_renames_itself_and_gets_the_me_body_back(UserRole role)
    {
        var (client, id) = await Account(role);
        var before = await Row(id);

        var response = await Patch(client, new { full_name = "  Trần Thị Bích Ngọc  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("Trần Thị Bích Ngọc", body.GetProperty("full_name").GetString());
        Assert.Equal(id, body.GetProperty("user_id").GetString());
        var me = await Json(await client.GetAsync(Me));
        Assert.Equal(me.ToString(), body.ToString()); // One shape, whichever route you ask.
        var after = await Row(id);
        Assert.Equal("Trần Thị Bích Ngọc", after.FullName);
        Assert.Equal(UtcMicrosecondClock.UtcNow(factory.Clock), after.UpdatedAt);
        Assert.NotEqual(before.UpdatedAt, after.UpdatedAt);
    }

    [Fact]
    public async Task Only_the_callers_own_row_changes_and_login_fields_stay()
    {
        var (client, id) = await Account(UserRole.Manager);
        var (_, otherId) = await Account(UserRole.Manager);
        var before = await Row(id);
        var otherBefore = await Row(otherId);

        // email / role / username are not part of the request: they are ignored, never applied.
        var response = await Patch(client, new
        {
            full_name = "Tên mới", email = "taken-over@example.invalid", role = "system_admin", username = "root", user_id = otherId,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await Row(id);
        Assert.Equal("Tên mới", after.FullName);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.Username, after.Username);
        Assert.Equal(before.Role, after.Role);
        Assert.Equal(before.HasSystemWideScope, after.HasSystemWideScope);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        var otherAfter = await Row(otherId);
        Assert.Equal(otherBefore.FullName, otherAfter.FullName);
        Assert.Equal(otherBefore.UpdatedAt, otherAfter.UpdatedAt);
    }

    [Fact]
    public async Task Avatar_is_untouched_by_a_rename()
    {
        var (client, id) = await Account(UserRole.Manager);
        var uploaded = DateTime.UtcNow.AddMinutes(-5);
        await factory.QueryAsync(async db =>
        {
            var user = await db.Set<AppUser>().SingleAsync(u => u.UserId == id);
            user.AvatarObjectKey = $"original/{id}.jpg";
            user.AvatarByteCount = 10;
            user.AvatarThumbnailKey = $"thumb/{id}.jpg";
            user.AvatarThumbnailBytes = 5;
            user.AvatarUpdatedAt = uploaded;
            return await db.SaveChangesAsync();
        });
        var before = await Row(id);

        var body = await Json(await Patch(client, new { full_name = "Tên khác" }));

        var after = await Row(id);
        Assert.Equal(before.AvatarObjectKey, after.AvatarObjectKey);
        Assert.Equal(before.AvatarUpdatedAt, after.AvatarUpdatedAt);
        Assert.StartsWith("/api/v1/auth/me/avatar?v=", body.GetProperty("avatar_url").GetString());
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"full_name":null}""")]
    [InlineData("""{"full_name":""}""")]
    [InlineData("""{"full_name":"    "}""")]
    [InlineData("""{"full_name":" a "}""")]
    public async Task Missing_blank_or_one_character_names_are_400_on_full_name(string json)
    {
        var (client, id) = await Account(UserRole.FieldEngineer);
        var before = await Row(id);

        var response = await client.PatchAsync(Me, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        await Error(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.Equal("full_name", (await Json(response)).GetProperty("error").GetProperty("details").GetProperty("field").GetString());
        Assert.Equal(before.FullName, (await Row(id)).FullName);
    }

    [Fact]
    public async Task A_name_longer_than_the_limit_is_400_and_the_limit_itself_is_accepted()
    {
        var (client, id) = await Account(UserRole.Manager);

        await Error(await Patch(client, new { full_name = new string('a', ProfileLimits.FullNameMaxLength + 1) }),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);

        var longest = new string('b', ProfileLimits.FullNameMaxLength);
        Assert.Equal(HttpStatusCode.OK, (await Patch(client, new { full_name = longest })).StatusCode);
        Assert.Equal(longest, (await Row(id)).FullName);
    }

    [Fact]
    public async Task No_token_is_401()
    {
        var response = await Patch(factory.CreateClient(), new { full_name = "Ai đó" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<(HttpClient Client, string Id)> Account(UserRole role)
    {
        var commune = await factory.SeededCommuneIdAsync();
        var user = new AppUser
        {
            Username = "profile-" + Guid.NewGuid().ToString("N"), Email = "profile-" + Guid.NewGuid().ToString("N") + "@example.invalid",
            FullName = "Profile test", Role = role, HasSystemWideScope = role == UserRole.SystemAdmin,
            PasswordAlgorithm = IdentitySeeder.PasswordAlgorithm,
        };
        await factory.QueryAsync(async db =>
        {
            db.Add(user);
            await db.SaveChangesAsync();
            if (role != UserRole.SystemAdmin)
            {
                db.Add(new AppUserCommune { UserId = user.UserId, CommuneId = commune, AssignedAt = factory.Clock.GetUtcNow().UtcDateTime });
            }
            return await db.SaveChangesAsync();
        });
        users.Add(user.UserId);
        var token = factory.Services.GetRequiredService<AccessTokenIssuer>().Issue(user, role == UserRole.SystemAdmin ? ["*"] : [commune]);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token.Token);
        return (client, user.UserId);
    }

    private Task<AppUser> Row(string id) => factory.QueryAsync(db => db.Set<AppUser>().AsNoTracking().SingleAsync(u => u.UserId == id));

    private static Task<HttpResponseMessage> Patch(HttpClient client, object body)
        => client.PatchAsJsonAsync(Me, body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await Json(response)).GetProperty("error").GetProperty("code").GetString());
    }
}
