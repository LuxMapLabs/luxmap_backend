using System.Security.Claims;
using System.Text.Encodings.Web;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LuxMap.Api.Authorization;

/// <summary>
/// Authenticates an IoT device by <c>Authorization: Device &lt;node_id&gt;.&lt;secret&gt;</c> (LIGHT-CTRL LC-2).
/// </summary>
/// <remarks>
/// <para>
/// The device row is read UNFILTERED — there is no commune scope yet, that is what this handler is about to establish — and
/// only to compare the secret. The principal then carries the device's single commune in <c>commune_ids</c>, so everything
/// after authentication runs through the ordinary scope (<see cref="CommuneScopeAccessor"/>) like any request.
/// </para>
/// <para>
/// Every refusal answers the same 401 (missing device, no secret issued, wrong secret, malformed header), and nothing in the
/// answer says which part was wrong. A credential that PARSES always costs one constant-time digest compare, found or not; a
/// malformed or oversized header is refused before any lookup. The secret is never logged (the <c>Authorization</c> header is
/// masked by <c>SensitivePropertyScrubber</c>).
/// </para>
/// <para>
/// The header follows RFC 9110 §11: the scheme word is case-insensitive and may be followed by one or more spaces; the
/// credential itself carries no whitespace.
/// </para>
/// </remarks>
public sealed class DeviceAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, LuxMapDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        var space = header.IndexOf(' ', StringComparison.Ordinal);
        if (space <= 0 || !header.AsSpan(0, space).Equals(DeviceAuth.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        // 1*SP after the scheme (RFC 9110 §11.6.2); the credential is a single token with no whitespace in it.
        var credential = header[space..].TrimStart(' ');
        var dot = credential.IndexOf('.', StringComparison.Ordinal);
        if (credential.Length > DeviceAuth.MaxCredentialLength || dot <= 0 || dot == credential.Length - 1
            || credential.Any(char.IsWhiteSpace))
        {
            return AuthenticateResult.Fail("Malformed device credential.");
        }

        var nodeId = credential[..dot];
        var secret = credential[(dot + 1)..];

        var device = await db.Set<IotNode>().IgnoreQueryFilters().AsNoTracking()
            .Where(node => node.NodeId == nodeId)
            .Select(node => new { node.NodeId, node.CommuneId, node.CredentialHash })
            .FirstOrDefaultAsync(Context.RequestAborted);

        if (!DeviceSecret.Matches(secret, device?.CredentialHash) || device is null)
        {
            return AuthenticateResult.Fail("Unknown device or wrong secret.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(DeviceAuth.NodeIdClaim, device.NodeId),
                new Claim(AuthClaims.CommuneIds, device.CommuneId),
            ],
            DeviceAuth.Scheme);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), DeviceAuth.Scheme));
    }

    /// <summary>A 401 names the scheme it wants (RFC 9110 §11.6.1), so firmware can tell "send Device credentials" from other errors.</summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = DeviceAuth.Scheme;
        return base.HandleChallengeAsync(properties);
    }
}
