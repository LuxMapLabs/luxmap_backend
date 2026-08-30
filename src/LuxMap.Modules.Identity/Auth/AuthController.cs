using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Identity.Auth;

/// <summary>
/// The authentication endpoints. NOT yet in Contract v1.1 — to be added at FW-00.
/// BE-07 only ISSUES tokens; validating them and enforcing commune scope is BE-08.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
// BE-08 makes authentication mandatory application-wide. These three endpoints are the way in to
// obtain a token, so they must stay open — and that has to be declared explicitly.
[AllowAnonymous]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("login")]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthTokenResponse>> LoginAsync(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request.Username!, request.Password!, cancellationToken);
        return Respond(result);
    }

    /// <summary>
    /// The refresh token is the ONLY credential here: this endpoint does NOT read the Authorization
    /// header and does not need a valid access token. Refreshing is allowed at any time.
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthTokenResponse>> RefreshAsync(
        [FromBody] RefreshRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.RefreshAsync(request.RefreshToken!, cancellationToken);
        return Respond(result);
    }

    /// <summary>Idempotent: an already-revoked or unknown token still returns 204.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LogoutAsync(
        [FromBody] LogoutRequest request,
        CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(request.RefreshToken, cancellationToken);
        return NoContent();
    }

    private ActionResult<AuthTokenResponse> Respond(AuthResult result)
    {
        if (result.Succeeded)
        {
            return Ok(AuthTokenResponse.From(result.Tokens!));
        }

        // Throw LuxMapException so the BE-04 middleware builds the body — one error shape for every API.
        throw result.Failure switch
        {
            AuthFailure.AccountLocked => new LuxMapException(
                KnownErrors.AccountLocked.Code,
                KnownErrors.AccountLocked.StatusCode,
                "This account is locked. Contact an administrator."),

            AuthFailure.InvalidRefreshToken => new LuxMapException(
                KnownErrors.InvalidRefreshToken.Code,
                KnownErrors.InvalidRefreshToken.StatusCode,
                "The refresh token is not valid."),

            // Wrong username and wrong password SHARE one body: separating them reveals which
            // accounts exist. Never point details at a specific field.
            _ => new LuxMapException(
                KnownErrors.InvalidCredentials.Code,
                KnownErrors.InvalidCredentials.StatusCode,
                "Incorrect username or password."),
        };
    }
}
