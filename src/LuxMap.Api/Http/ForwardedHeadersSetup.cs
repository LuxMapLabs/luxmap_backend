using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace LuxMap.Api.Http;

/// <summary>
/// CITIZEN-QR P2b (D-8) — the client address behind a reverse proxy. The rate limiters partition by
/// <c>RemoteIpAddress</c>; behind a proxy that is the proxy's address, so every citizen would share one bucket.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only proxies named in configuration are trusted</b> (<c>ForwardedHeaders:KnownProxies</c>,
/// <c>ForwardedHeaders:KnownNetworks</c>). ASP.NET Core's defaults trust loopback; those are CLEARED, so with nothing
/// configured an <c>X-Forwarded-For</c> header is ignored entirely. Trusting it from anyone would let a client pick its own
/// address — and its own rate-limit bucket — with one header.
/// </para>
/// <para>
/// <c>ForwardLimit = 1</c>: one proxy in front of the API. A deployment with a chain must name every hop.
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

    /// <summary>First in the pipeline, so the correlation log line, the rate limiters and everything else see the real client.</summary>
    public static WebApplication UseLuxMapForwardedHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseForwardedHeaders();
        return app;
    }
}
