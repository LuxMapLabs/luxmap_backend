using System.Net;
using Asp.Versioning;
using LuxMap.Modules.Identity.Entities;
using Microsoft.AspNetCore.Http;
using LuxMap.Persistence.Conventions;
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
// BE-08 makes authentication mandatory application-wide, and the way in to obtain a token must stay
// open — but that is declared PER ENDPOINT, not on the class.
//
// ⚠️ It used to sit on the class, and moving it was not tidying up. [AllowAnonymous] declared farther
// away BEATS an [Authorize] on a method, so the first endpoint here that needed a token (`me`) would
// have shipped reachable without one. The compiler says so (ASP0026) and it is right: an opt-out that
// covers endpoints written later is an opt-out nobody re-reads.
public sealed class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthTokenResponse>> LoginAsync(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(
            request.Username!, request.Password!, RefreshTokenSessionKind.Mobile,
            supersededWebRefreshToken: null, cancellationToken);
        return Respond(result);
    }

    /// <summary>
    /// The refresh token is the ONLY credential here: this endpoint does NOT read the Authorization
    /// header and does not need a valid access token. Refreshing is allowed at any time.
    /// </summary>
    /// <summary>
    /// Open registration. Creates an IDENTITY, never a PERMISSION. DEPRECATED (Contract v1.7, D-R11).
    /// </summary>
    /// <remarks>
    /// 🔴 Registration form v1.2 has no self-registration: a system admin creates the account and assigns
    /// its role and communes. This endpoint stays until BE-33a adds <c>POST /admin/users</c>, so there is
    /// never a moment with no way to create an account; <c>[Obsolete]</c> publishes
    /// <c>deprecated: true</c> in the spec so mobile drops the screen now.
    /// <para>
    /// The new account signs in immediately but sees NO data: it is created with the lowest role and
    /// no commune assignment, and the BE-08 query filter admits nothing on an empty scope. An
    /// administrator grants access separately (BE-33).
    /// </para>
    /// <para>
    /// Deliberately returns NO token. The account calls <c>POST /auth/login</c> like everyone else, so
    /// exactly one code path issues tokens and opens refresh chains.
    /// </para>
    /// </remarks>
    [HttpPost("register")]
    [AllowAnonymous]
    [Obsolete("DEPRECATED in Contract v1.7 (D-R11): a system admin creates accounts. Removed by BE-33a.")]
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterResponse>> RegisterAsync(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await authService.RegisterAsync(
            request.Username!, request.Email!, request.FullName!, request.Password!, cancellationToken);

        if (!outcome.Succeeded)
        {
            throw new LuxMapException(
                KnownErrors.IdentifierTaken.Code,
                KnownErrors.IdentifierTaken.StatusCode,
                "That username or email address is already registered.",
                outcome.TakenFields);
        }

        var user = outcome.User!;
        return StatusCode(StatusCodes.Status201Created, new RegisterResponse(
            user.UserId,
            user.Username,
            user.Email,
            user.FullName,
            ContractEnum.ToDbValue(user.Role),
            [],
            RegisterResponse.PendingAssignmentMessage));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthTokenResponse>> RefreshAsync(
        [FromBody] RefreshRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.RefreshAsync(request.RefreshToken!, AuthEndpointGroup.Mobile, cancellationToken);
        return Respond(result);
    }

    /// <summary>
    /// The signed-in user, read from the database (Contract section 4.7).
    /// </summary>
    /// <remarks>
    /// The ONE endpoint of this controller that needs an access token. The other four opt OUT with
    /// <see cref="AllowAnonymousAttribute"/> individually; this one is covered by the application-wide
    /// fallback policy, and says so with an explicit <see cref="AuthorizeAttribute"/>.
    /// <para>
    /// It serves BOTH endpoint groups: mobile and web receive the same access token in the response
    /// body and send it the same way, so there is no <c>/auth/web/me</c> — only the REFRESH token
    /// differs between the groups, and this endpoint never touches it.
    /// </para>
    /// <para>
    /// Answers from the row, not from the claims: see <see cref="AuthService.FindCurrentUserAsync"/>.
    /// A locked account is NOT refused here; its access token keeps working on every endpoint until it
    /// expires, and answering 403 on this one alone would be a rule that exists nowhere else.
    /// </para>
    /// </remarks>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserResponse>> MeAsync(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(AuthClaims.Subject)?.Value;

        var me = userId is null
            ? null
            : await authService.FindCurrentUserAsync(userId, cancellationToken);

        // Both branches are 401 and share one message. A token with no subject and a token naming a
        // deleted account are the same thing to the caller: this credential identifies nobody. A 404
        // would be the wrong shape anyway — the resource is "me", and it is the token that is stale.
        return me is not null
            ? Ok(me)
            : throw new LuxMapException(
                ErrorCodes.Unauthenticated,
                HttpStatusCode.Unauthorized,
                "This access token no longer identifies an account. Sign in again.");
    }

    /// <summary>Idempotent: an already-revoked or unknown token still returns 204.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LogoutAsync(
        [FromBody] LogoutRequest request,
        CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(request.RefreshToken, AuthEndpointGroup.Mobile, cancellationToken);
        return NoContent();
    }

    private ActionResult<AuthTokenResponse> Respond(AuthResult result)
    {
        if (result.Succeeded)
        {
            return Ok(AuthTokenResponse.From(result.Tokens!));
        }

        throw AuthFailureErrors.ToException(result.Failure);
    }
}
