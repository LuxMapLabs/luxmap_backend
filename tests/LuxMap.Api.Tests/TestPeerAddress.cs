using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace LuxMap.Api.Tests;

/// <summary>TestServer has no client address; this sets one from a header, before the forwarded-headers middleware runs.</summary>
internal sealed class TestPeerAddress : IStartupFilter
{
    public const string Header = "X-Test-Peer";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(Header, out var peer)) context.Connection.RemoteIpAddress = IPAddress.Parse(peer!);
            await nextMiddleware();
        });
        next(app);
    };
}
