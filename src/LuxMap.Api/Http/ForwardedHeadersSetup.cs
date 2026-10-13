using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace LuxMap.Api.Http;

/// <summary>
/// CITIZEN-QR P2b (D-8) — the client address behind a reverse proxy. The rate limiters partition by
/// <c>RemoteIpAddress</c>; behind a proxy that is the proxy's address, so every citizen would share one bucket.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Nothing configured ⇒ the middleware is NOT in the pipeline at all.</b> ASP.NET Core reads EMPTY
/// <c>KnownProxies</c> and <c>KnownIPNetworks</c> as "skip the check" — it would then honour <c>X-Forwarded-For</c> from
/// ANY peer, letting a client pick its own address and its own rate-limit bucket with one header (Codex review P2b, P1).
/// Clearing the loopback defaults without adding anything is that trap, so an unconfigured host simply never forwards.
/// </para>
/// <para>
/// Configured (<c>ForwardedHeaders:KnownProxies</c>, <c>ForwardedHeaders:KnownNetworks</c>): exactly those, the loopback defaults
/// removed. <c>ForwardLimit = 1</c> — one proxy in front of the API; a chain must name every hop.
/// </para>
/// </remarks>
public static class ForwardedHeadersSetup
{
    public const string SectionName = "ForwardedHeaders";

    public static IServiceCollection AddLuxMapForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var proxies = (configuration.GetSection($"{SectionName}:KnownProxies").Get<string[]>() ?? [])
            .Select(value => IPAddress.TryParse(value, out var address) ? address
                : throw new InvalidOperationException($"{SectionName}:KnownProxies has '{value}', which is not an IP address."))
            .ToArray();
        var networks = (configuration.GetSection($"{SectionName}:KnownNetworks").Get<string[]>() ?? [])
            .Select(value => System.Net.IPNetwork.TryParse(value, out var network) ? network
                : throw new InvalidOperationException($"{SectionName}:KnownNetworks has '{value}', which is not a CIDR network."))
            .ToArray();

        services.AddSingleton(new TrustedProxies(proxies.Length + networks.Length > 0));
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in proxies) options.KnownProxies.Add(proxy);
            foreach (var network in networks) options.KnownIPNetworks.Add(network);
        });

        return services;
    }

    /// <summary>First in the pipeline when a proxy is configured, so the log line and the rate limiters see the real client.</summary>
    public static WebApplication UseLuxMapForwardedHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (app.Services.GetRequiredService<TrustedProxies>().Any)
        {
            app.UseForwardedHeaders();
        }

        return app;
    }

    /// <summary>Whether any proxy or network is trusted — without one, forwarding stays off (see the remarks).</summary>
    public sealed record TrustedProxies(bool Any);
}
