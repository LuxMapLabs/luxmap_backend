using System.ComponentModel.DataAnnotations;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Identity.Accounts;

/// <summary>
/// <c>POST /admin/users</c>. No password: the account receives an invitation link by email (D-1).
/// </summary>
public sealed class CreateUserRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(256)]
    public string? Username { get; init; }

    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string? Email { get; init; }

    [Required]
    [MinLength(2)]
    [MaxLength(256)]
    public string? FullName { get; init; }

    [Required]
    public UserRole? Role { get; init; }

    /// <summary>At least one for superior / manager / field_engineer; must be absent or empty for system_admin (D-8).</summary>
    public IReadOnlyList<string>? CommuneIds { get; init; }
}

/// <summary>
/// <c>PATCH /admin/users/{id}</c>. An absent field stays as it is; <c>username</c> never changes (D-11).
/// </summary>
public sealed class UpdateUserRequest
{
    [EmailAddress]
    [MaxLength(256)]
    public string? Email { get; init; }

    [MinLength(2)]
    [MaxLength(256)]
    public string? FullName { get; init; }

    public UserRole? Role { get; init; }

    /// <summary>Replaces the whole assignment list when present.</summary>
    public IReadOnlyList<string>? CommuneIds { get; init; }
}

/// <summary><c>POST /auth/password/set</c>: redeem an invite or reset link.</summary>
public sealed class SetPasswordRequest
{
    [Required]
    [MaxLength(1024)]
    public string? Token { get; init; }

    /// <summary>
    /// Minimum 12 characters and no composition rules, following NIST SP 800-63B: length beats
    /// character-class requirements. The 1024 ceiling stops a long password being used to hammer PBKDF2.
    /// </summary>
    [Required]
    [MinLength(MinimumPasswordLength)]
    [MaxLength(1024)]
    public string? NewPassword { get; init; }

    public const int MinimumPasswordLength = 12;
}

/// <summary><c>POST /auth/password/forgot</c>.</summary>
public sealed class ForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string? Email { get; init; }
}

/// <summary>
/// One account as the admin screens see it.
/// </summary>
/// <param name="Role">A Contract section 3.1 <c>user_role</c> string.</param>
/// <param name="CommuneIds">The assigned communes, or <c>["*"]</c> for a system admin — the same answer as <c>GET /auth/me</c>.</param>
/// <param name="Status"><c>invited</c> (no password yet), <c>active</c> or <c>locked</c>; derived when read, never stored (D-13).</param>
public sealed record UserAccountItem(
    string UserId,
    string Username,
    string Email,
    string FullName,
    string Role,
    IReadOnlyList<string> CommuneIds,
    string Status,
    DateTime CreatedAt,
    DateTime? PasswordSetAt);

/// <summary>
/// What creating an account returns: the account, and whether the invitation mail went out (D-3).
/// <c>false</c> means the account exists and the admin should resend the invitation.
/// </summary>
public sealed record CreateUserResponse(UserAccountItem User, bool InvitationSent);

/// <summary><c>POST /admin/users/{id}/invite</c>: a new link replaces any earlier one.</summary>
public sealed record InvitationResponse(bool InvitationSent, DateTime ExpiresAt);

/// <summary>What <c>POST /auth/password/forgot</c> always answers, whether or not the address is known.</summary>
public sealed record ForgotPasswordResponse(string Message)
{
    public const string Accepted =
        "If an account uses this address, a link to set a new password has been sent to it.";
}

/// <summary>Account status values, as <see cref="UserAccountItem.Status"/> spells them.</summary>
public static class AccountStatus
{
    public const string Invited = "invited";
    public const string Active = "active";
    public const string Locked = "locked";

    public static readonly IReadOnlyList<string> All = [Invited, Active, Locked];
}

/// <summary>Error codes of the account endpoints. Module-local, like the work-order and fault codes.</summary>
public static class AccountErrors
{
    /// <summary>404 — no account with that id.</summary>
    public const string UserNotFound = "USER_NOT_FOUND";

    /// <summary>409 — the change would leave no active system admin (D-9, D-10).</summary>
    public const string LastSystemAdmin = "LAST_SYSTEM_ADMIN";

    /// <summary>409 — an admin cannot lock their own account (D-9).</summary>
    public const string CannotLockSelf = "CANNOT_LOCK_SELF";

    /// <summary>409 — the account already set its password; send a reset link instead of an invitation.</summary>
    public const string AccountAlreadyActive = "ACCOUNT_ALREADY_ACTIVE";

    /// <summary>
    /// 400 — the link is unknown, expired or already used. Deliberately ONE code: telling them apart
    /// helps nobody but someone probing tokens.
    /// </summary>
    public const string InvalidAccountToken = "INVALID_ACCOUNT_TOKEN";
}

internal static class AccountTokenLifetimes
{
    /// <summary>D-1: an invitation lives 72 hours.</summary>
    public static readonly TimeSpan Invite = TimeSpan.FromHours(72);

    /// <summary>D-1: a reset link lives 1 hour.</summary>
    public static readonly TimeSpan Reset = TimeSpan.FromHours(1);

    public static TimeSpan For(Entities.AccountTokenPurpose purpose)
        => purpose == Entities.AccountTokenPurpose.Invite ? Invite : Reset;
}
