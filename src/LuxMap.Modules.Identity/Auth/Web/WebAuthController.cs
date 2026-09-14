using Asp.Versioning;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Identity.Auth.Web;

/// <summary>
/// The browser endpoint group of Contract section 2.10.2. The refresh token lives ONLY in the
/// HttpOnly cookie: it is never read from a body and never written to one.
/// </summary>
/// <remarks>
/// A separate group rather than a mode of <see cref="AuthController"/>: the mobile DTOs, response
/// shape and the Kotlin types generated from them stay exactly what BE-07 shipped, and a client
/// states which group it belongs to by the URL it calls — nothing is inferred from headers.
/// <para>
/// ⚠️ <c>Set-Cookie</c> is appended only as the LAST step before a successful return. The error
/// middleware calls <c>Response.Clear()</c>, which drops every header already appended, so a cookie
/// written before something throws would silently vanish. The failure branches never touch the
/// cookie at all: a request that lost a concurrent refresh must not delete the cookie the winning
/// request has just set.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth/web")]
[AllowAnonymous]
[RequireAllowedOrigin]
public sealed class WebAuthController(AuthService authService) : ControllerBase
{
    [HttpPost("login")]
    [SetsRefreshTokenCookie]
    [ProducesResponseType<WebAuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<WebAuthTokenResponse>> LoginAsync(
        [FromBody] WebLoginRequest request,
        CancellationToken cancellationToken)
    {
        var kind = request.RememberMe ? RefreshTokenSessionKind.WebPersistent : RefreshTokenSessionKind.WebSession;

        // A browser that already holds a web session has it revoked by this sign-in — the only way
        // to switch between "remember me" and not is to sign in again.
        var result = await authService.LoginAsync(
            request.Username!, request.Password!, kind, RefreshTokenCookie.Read(Request), cancellationToken);
        return Respond(result);
    }

    /// <summary>No body. The refresh token is read from the cookie and from nowhere else.</summary>
    [HttpPost("refresh")]
    [ReadsRefreshTokenCookie]
    [SetsRefreshTokenCookie]
    [ProducesResponseType<WebAuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<WebAuthTokenResponse>> RefreshAsync(CancellationToken cancellationToken)
    {
        var result = await authService.RefreshAsync(
            RefreshTokenCookie.Read(Request), AuthEndpointGroup.Web, cancellationToken);
        return Respond(result);
    }

    /// <summary>No body. Always 204, and always clears the cookie.</summary>
    [HttpPost("logout")]
    [ReadsRefreshTokenCookie]
    [SetsRefreshTokenCookie]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(RefreshTokenCookie.Read(Request), AuthEndpointGroup.Web, cancellationToken);

        RefreshTokenCookie.Delete(Response);
        return NoContent();
    }

    private ActionResult<WebAuthTokenResponse> Respond(AuthResult result)
    {
        if (!result.Succeeded)
        {
            throw AuthFailureErrors.ToException(result.Failure);
        }

        var tokens = result.Tokens!;
        RefreshTokenCookie.Append(
            Response,
            tokens.RefreshToken,
            tokens.SessionKind == RefreshTokenSessionKind.WebPersistent ? tokens.RefreshTokenExpiresAt : null);

        return Ok(WebAuthTokenResponse.From(tokens));
    }
}
