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
/// Every refusal looks the same (missing device, no secret issued, wrong secret, malformed header): one constant-time compare
/// is always made, and nothing in the answer says which part was wrong. The secret is never logged.
/// </para>
/// </remarks>
public sealed class DeviceAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, LuxMapDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string Prefix = DeviceAuth.Scheme + " ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        var credential = header[Prefix.Length..];
        var dot = credential.IndexOf('.', StringComparison.Ordinal);
        if (credential.Length > DeviceAuth.MaxCredentialLength || dot <= 0 || dot == credential.Length - 1)
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
}
