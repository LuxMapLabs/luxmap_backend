using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using LuxMap.Modules.Identity.Accounts;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace LuxMap.Api.Tests;

/// <summary>
/// A real host for the BE-33a tests: account mail goes to <see cref="Mail"/> instead of an SMTP server,
/// and <see cref="Clock"/> can be wound past a link's expiry without waiting.
/// </summary>
public class AccountTestFactory : WebApplicationFactory<Program>
{
    /// <summary>Every username a test creates starts with this, so teardown finds exactly its own rows.</summary>
    public const string UsernamePrefix = "acct-";

    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public RecordingEmailSender Mail { get; } = new();

    /// <summary>High enough that the forgot-password tests never meet the limiter; one class lowers it on purpose.</summary>
    protected virtual int AccountMailPermitLimit => 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseTestCorsOrigin();
        builder.UseSetting("RateLimits:AccountMail:PermitLimit", AccountMailPermitLimit.ToString());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Mail);
        });
    }

    public async Task<T> QueryAsync<T>(Func<LuxMapDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<LuxMapDbContext>());
    }

    public Task<string> SeededCommuneIdAsync()
        => QueryAsync(db => db.Set<AdministrativeUnit>()
            .Where(unit => unit.SeedKey == SeedKeys.StudySite)
            .Select(unit => unit.CommuneId)
            .SingleAsync());

    /// <summary>A unique username under <see cref="UsernamePrefix"/>.</summary>
    public static string NewUsername() => $"{UsernamePrefix}{Guid.NewGuid():N}"[..24];

    /// <summary>
    /// Removes every account a test created; links, sessions and assignments cascade with them. Called
    /// after EACH test, never from the factory's Dispose: by then the host and its services are gone.
    /// </summary>
    public Task DeleteTestAccountsAsync()
        => QueryAsync(async db =>
        {
            #pragma warning disable RS0030 // Test TEARDOWN: bulk delete is the only way to clean up under an empty scope. BE-36 removes the need entirely — a fresh database per run.
            return await db.Set<AppUser>().Where(user => user.Username.StartsWith(UsernamePrefix)).ExecuteDeleteAsync();
            #pragma warning restore RS0030
        });
}

/// <summary>Same host with the account-mail limiter down to two requests, to watch it refuse the third.</summary>
public sealed class TightRateLimitAccountTestFactory : AccountTestFactory
{
    protected override int AccountMailPermitLimit => 2;
}

/// <summary>Keeps what would have been mailed. <see cref="Fail"/> makes the next sends throw, like an SMTP outage.</summary>
public sealed partial class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> sent = new();

    public bool Fail { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (Fail)
        {
            throw new InvalidOperationException("SMTP server unreachable (test).");
        }

        sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> To(string address)
        => sent.Where(message => string.Equals(message.ToAddress, address, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>The raw token in the newest mail to <paramref name="address"/>, read back out of its link.</summary>
    public string LatestTokenFor(string address)
    {
        var message = To(address).LastOrDefault()
            ?? throw new InvalidOperationException($"No mail was sent to {address}.");
        var match = LinkToken().Match(message.Body);
        return match.Success
            ? Uri.UnescapeDataString(match.Groups[1].Value)
            : throw new InvalidOperationException("The mail carries no set-password link.");
    }

    [GeneratedRegex(@"/set-password\?token=([A-Za-z0-9_\-%]+)")]
    private static partial Regex LinkToken();
}
