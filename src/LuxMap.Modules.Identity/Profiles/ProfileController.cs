using Asp.Versioning;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Identity.Profiles;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
public sealed class ProfileController(ProfileService profiles) : ControllerBase
{
    /// <summary>Changes the caller's own <c>full_name</c>; answers with the same body as <c>GET /auth/me</c>.</summary>
    [HttpPatch("auth/me")]
    [Authorize(Policy = LuxMapPolicies.ManageOwnProfile)]
    [ProducesResponseType<CurrentUserResponse>(200)]
    [ProducesResponseType<ApiErrorResponse>(400)]
    [ProducesResponseType<ApiErrorResponse>(401)]
    [ProducesResponseType<ApiErrorResponse>(403)]
    public Task<CurrentUserResponse> Update([FromBody] UpdateProfileRequest request, CancellationToken ct)
        => profiles.UpdateAsync(request, ct);
}
