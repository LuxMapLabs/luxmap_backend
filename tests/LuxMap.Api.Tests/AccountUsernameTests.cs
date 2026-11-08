using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Identity.Seeding;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

/// <summary>ACCT-USERNAME: this class owns mynh* accounts via its private email prefix.</summary>
public class AccountUsernameTests(AccountTestFactory factory) : IClassFixture<AccountTestFactory>, IAsyncLifetime
{
    private const string EmailPrefix = "acctusername-";
    private const string Route = "/api/v1/admin/users";

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => factory.QueryAsync(async db =>
    {
        var users = await db.Set<AppUser>().Where(user => user.Email.StartsWith(EmailPrefix)).ToListAsync();
        db.RemoveRange(users);
        return await db.SaveChangesAsync();
    });

    [Fact]
    public async Task The_first_two_accounts_receive_mynh1_and_mynh2_and_the_mail_contains_the_username()
    {
        using var client = await AdminAsync();
        var first = await CreateAsync(client);
        var second = await CreateAsync(client);
        Assert.Equal("mynh1", first.GetProperty("username").GetString());
        Assert.Equal("mynh2", second.GetProperty("username").GetString());
        var mail = Assert.Single(factory.Mail.To(first.GetProperty("email").GetString()!));
        Assert.Contains("Tên đăng nhập: mynh1", mail.Body);
        Assert.Contains("mynh1", mail.HtmlBody);
    }

    [Theory]
    [InlineData("mynh7", "mynh8")]
    [InlineData("MYNH3", "mynh4")]
    public async Task The_largest_exact_positive_suffix_is_used_case_insensitively(string existing, string expected)
    {
        await SeedAsync(existing);
        await SeedAsync("mynh09");
        await SeedAsync("mynh0");
        await SeedAsync("mynh99extra");
        await SeedAsync("mynhx99");
        using var client = await AdminAsync();
        var user = await CreateAsync(client);
        Assert.Equal(expected, user.GetProperty("username").GetString());
    }

    [Theory]
    [InlineData("chosen")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Any_supplied_username_is_rejected_without_creating_an_account_or_mail(string? username)
    {
        var email = NewEmail();
        using var client = await AdminAsync();
        var response = await client.PostAsJsonAsync(Route,
            new { username, email, full_name = "Nguyễn Hữu Mỹ", role = "system_admin" });
        await AssertRejectedAsync(response, email, "SERVER_OWNED_FIELD");
    }

    [Theory]
    [InlineData("王 Mỹ")]
    [InlineData("Nguyễn Mỹ2")]
    [InlineData("Анна")]
    [InlineData("---")]
    public async Task An_invalid_full_name_creates_neither_account_nor_mail(string fullName)
    {
        var email = NewEmail();
        using var client = await AdminAsync();
        var response = await client.PostAsJsonAsync(Route,
            new { email, full_name = fullName, role = "system_admin" });
        await AssertRejectedAsync(response, email, "VALIDATION_FAILED");
        var error = (await Json(response)).GetProperty("error");
        Assert.Equal("full_name", error.GetProperty("details").GetProperty("field").GetString());
    }

    [Fact]
    public async Task Five_parallel_creates_of_one_name_receive_mynh1_through_mynh5()
    {
        // Construct every client before concurrency; no lazy shared host/client dictionary.
        var clients = new List<HttpClient>();
        try
        {
            for (var i = 0; i < 5; i++) clients.Add(await AdminAsync());
            var users = await Task.WhenAll(clients.Select(client => CreateAsync(client)));
            Assert.Equal(new[] { "mynh1", "mynh2", "mynh3", "mynh4", "mynh5" },
                users.Select(user => user.GetProperty("username").GetString()).OrderBy(name => name));
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
        }
    }

    [Fact]
    public async Task Patch_of_the_full_name_keeps_the_generated_username()
    {
        using var client = await AdminAsync();
        var user = await CreateAsync(client);
        var response = await client.PatchAsJsonAsync($"{Route}/{user.GetProperty("user_id").GetString()}",
            new { full_name = "Đỗ Văn Đức", username = "must-not-replace" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("mynh1", (await Json(response)).GetProperty("username").GetString());
        Assert.Equal("mynh1", await factory.QueryAsync(db => db.Set<AppUser>()
            .Where(account => account.Email == user.GetProperty("email").GetString())
            .Select(account => account.Username).SingleAsync()));
    }

    private Task SeedAsync(string username) => factory.QueryAsync(async db =>
    {
        db.Add(new AppUser
        {
            Username = username, Email = NewEmail(), FullName = "Existing account",
            Role = UserRole.SystemAdmin, HasSystemWideScope = true,
            PasswordAlgorithm = IdentitySeeder.PasswordAlgorithm,
        });
        return await db.SaveChangesAsync();
    });

    private async Task AssertRejectedAsync(HttpResponseMessage response, string email, string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await Json(response)).GetProperty("error").GetProperty("code").GetString());
        Assert.False(await factory.QueryAsync(db => db.Set<AppUser>().AnyAsync(user => user.Email == email)));
        Assert.Empty(factory.Mail.To(email));
    }

    private static async Task<JsonElement> CreateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(Route,
            new { email = NewEmail(), full_name = "Nguyễn Hữu Mỹ", role = "system_admin" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("user");
    }

    private async Task<HttpClient> AdminAsync()
    {
        using var login = factory.CreateClient();
        var tokens = await login.LoginAsync("admin", "SEED_ADMIN_PASSWORD");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        return client;
    }

    private static string NewEmail() => $"{EmailPrefix}{Guid.NewGuid():N}@example.invalid";

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
