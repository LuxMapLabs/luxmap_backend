using Asp.Versioning;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LuxMap.Modules.Identity.Accounts;

/// <summary>
/// The emailed-link endpoints (BE-33a). Both are anonymous by nature — whoever holds the link has no
/// password yet — so each opts out PER METHOD, never on the class (docs/agents/traps/auth-accounts.md, [AllowAnonymous] at class level).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth/password")]
public sealed class PasswordController(PasswordService service) : ControllerBase
{
    /// <summary>
    /// Sets the password from an invite or reset link. One use; every other link of the account and
    /// every live session end with it. Then sign in as usual.
    /// </summary>
    [HttpPost("set")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Set([FromBody] SetPasswordRequest request, CancellationToken ct)
    {
        await service.SetPasswordAsync(request, ct);
        return NoContent();
    }

    /// <summary>Changes only the signed-in user's password after checking old_password.</summary>
    /// <remarks>Same 12–1024 character new-password validation as /set. Ends refresh sessions and unused password links; sign in again.</remarks>
    [HttpPost("change")]
    [Authorize(Policy = LuxMapPolicies.ManageOwnProfile)]
    [EnableRateLimiting(LuxMapRateLimits.Login)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Change([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        await service.ChangePasswordAsync(request, ct);
        return NoContent();
    }

    /// <summary>
    /// Mails a link to set a new password. ALWAYS 202 with the same body, whether or not the address
    /// belongs to an account. Rate limited per client address.
    /// </summary>
    [HttpPost("forgot")]
    [AllowAnonymous]
    [EnableRateLimiting(LuxMapRateLimits.AccountMail)]
    [ProducesResponseType<ForgotPasswordResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ForgotPasswordResponse>> Forgot([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        await service.RequestResetAsync(request, ct);
        return StatusCode(StatusCodes.Status202Accepted, new ForgotPasswordResponse(ForgotPasswordResponse.Accepted));
    }
}
