using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Identity.Accounts;

/// <summary>
/// Account management for the system admin (BE-33a, D-R11). Replaces <c>POST /auth/register</c>: there
/// is no self-registration, the admin creates the account and the person sets a password from the
/// emailed invitation.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/users")]
[ClientSurface(ClientSurface.Web)]
public sealed class AdminUsersController(UserAdminService service) : ControllerBase
{
    /// <summary>
    /// Creates the account with no password and mails an invitation link (72 hours, single use).
    /// <c>invitation_sent: false</c> means the account exists but the mail failed — resend it.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = LuxMapPolicies.ManageUsers)]
    [ProducesResponseType<CreateUserResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateUserResponse>> Create([FromBody] CreateUserRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await service.CreateAsync(request, ct));

    /// <summary>Every account, oldest first. Filters: <c>role</c> (comma-separated), <c>status</c> (invited / active / locked).</summary>
    [HttpGet]
    [Authorize(Policy = LuxMapPolicies.ManageUsers)]
    public Task<PagedResult<UserAccountItem>> List(
        [FromQuery(Name = "role")] string? roles,
        [FromQuery(Name = "status")] string? status,
        PageQuery page,
        CancellationToken ct)
        => service.ListAsync(roles, status, page.ToPageRequest(), ct);

    [HttpGet("{id}")]
    [Authorize(Policy = LuxMapPolicies.ManageUsers)]
    public Task<UserAccountItem> Detail(string id, CancellationToken ct) => service.GetAsync(id, ct);

    /// <summary>
    /// Changes <c>full_name</c>, <c>email</c>, <c>role</c> and <c>commune_ids</c>; absent fields stay.
    /// <c>username</c> never changes. Takes effect at the account's next token refresh, within 60 minutes.
    /// </summary>
    [HttpPatch("{id}")]
    [Authorize(Policy = LuxMapPolicies.ManageUsers)]
    public Task<UserAccountItem> Update(string id, [FromBody] UpdateUserRequest request, CancellationToken ct)
        => service.UpdateAsync(id, request, ct);

    /// <summary>Blocks sign-in and revokes every session. Not your own account, not the last active system admin.</summary>
    [HttpPost("{id}/lock")]
    [Authorize(Policy = LuxMapPolicies.ManageUsers)]
    public Task<UserAccountItem> Lock(string id, CancellationToken ct) => service.LockAsync(id, ct);

    [HttpPost("{id}/unlock")]
    [Authorize(Policy = LuxMapPolicies.ManageUsers)]
    public Task<UserAccountItem> Unlock(string id, CancellationToken ct) => service.UnlockAsync(id, ct);

    /// <summary>Mails a new invitation link to an account that has not set its password; the earlier link stops working.</summary>
    [HttpPost("{id}/invite")]
    [Authorize(Policy = LuxMapPolicies.ManageUsers)]
    public Task<InvitationResponse> Invite(string id, CancellationToken ct) => service.InviteAsync(id, ct);
}
