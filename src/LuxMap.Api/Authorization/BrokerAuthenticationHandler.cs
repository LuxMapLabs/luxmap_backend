using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using LuxMap.Modules.Telemetry.Mqtt;
using LuxMap.Shared.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LuxMap.Api.Authorization;

/// <summary>
/// Authenticates the MQTT broker's callback by <c>Authorization: Broker &lt;key&gt;</c> (LIGHT-CTRL LC-12 M-2). The key is
/// <c>Mqtt:CallbackKey</c>; compared by SHA-256 digest in fixed time. No key configured ⇒ nothing ever authenticates.
/// </summary>
public sealed class BrokerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, MqttOptions mqtt)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        var space = header.IndexOf(' ', StringComparison.Ordinal);
        if (space <= 0 || !header.AsSpan(0, space).Equals(BrokerAuth.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var given = header[space..].Trim();
        if (mqtt.CallbackKey is not { Length: > 0 } key
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(given)), SHA256.HashData(Encoding.UTF8.GetBytes(key))))
        {
            return Task.FromResult(AuthenticateResult.Fail("Wrong broker key."));
        }

        var identity = new ClaimsIdentity([new Claim(BrokerAuth.Claim, "emqx")], BrokerAuth.Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), BrokerAuth.Scheme)));
    }
}
