using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Identity.Accounts;

/// <summary>
/// Nullable personal details. Setters track PATCH omission separately from explicit null.
/// </summary>
public abstract class UserPersonalDetailsRequest
{
    private readonly HashSet<string> supplied = [];
    internal bool Has(string property) => supplied.Contains(property);
    private DateOnly? valueDateOfBirth;
    public DateOnly? DateOfBirth { get => valueDateOfBirth; init { valueDateOfBirth = value; supplied.Add(nameof(DateOfBirth)); } }
    private string? valueGender;
    [AllowedValues(null, "male", "female", "other")]
    public string? Gender { get => valueGender; init { valueGender = value; supplied.Add(nameof(Gender)); } }
    private string? valueAddress;
    [MaxLength(500)]
    public string? Address { get => valueAddress; init { valueAddress = value; supplied.Add(nameof(Address)); } }
    private string? valuePhoneNumber;
    [MaxLength(32)]
    public string? PhoneNumber { get => valuePhoneNumber; init { valuePhoneNumber = value; supplied.Add(nameof(PhoneNumber)); } }
    private string? valueIdentityNumber;
    [MaxLength(64)]
    public string? IdentityNumber { get => valueIdentityNumber; init { valueIdentityNumber = value; supplied.Add(nameof(IdentityNumber)); } }
    private string? valueIdentityType;
    [AllowedValues(null, "citizen_id", "identity_card", "passport", "other")]
    public string? IdentityType { get => valueIdentityType; init { valueIdentityType = value; supplied.Add(nameof(IdentityType)); } }
}

/// <summary>POST /admin/users: no password; an invitation link is sent by email.</summary>
public sealed class CreateUserRequest : UserPersonalDetailsRequest
{
    /// <summary>Server-owned: omit this field, including null. Presence is rejected (ACCT-2).</summary>
    public JsonElement Username { get; init; }

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

    /// <summary>Exactly one for superior / manager / field_engineer; must be absent for system_admin (ACCT-1).</summary>
    public IReadOnlyList<string>? CommuneIds { get; init; }
}

/// <summary>
/// <c>PATCH /admin/users/{id}</c>. An absent field stays as it is; <c>username</c> never changes (D-11).
/// </summary>
public sealed class UpdateUserRequest : UserPersonalDetailsRequest
{
    // Capture legacy email attempts without exposing an editable email field in Swagger.
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; init; }

    [MinLength(2)]
    [MaxLength(256)]
    public string? FullName { get; init; }

    public UserRole? Role { get; init; }

    /// <summary>Sets the single assigned commune when present (ACCT-1).</summary>
    public IReadOnlyList<string>? CommuneIds { get; init; }
}

/// <summary>Admin changes the email separately from the personal-details PATCH.</summary>
public sealed class ChangeUserEmailRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string? Email { get; init; }
}

/// <summary>The change is persisted even if SMTP fails; retry the same email to resend notification.</summary>
public sealed record ChangeUserEmailResponse(UserAccountItem User, bool NotificationSent);

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

/// <summary>POST /auth/password/change: the signed-in caller changes only their own password.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangePasswordRequest
{
    [Required, MaxLength(1024)]
    public string? OldPassword { get; init; }

    [Required, MinLength(SetPasswordRequest.MinimumPasswordLength), MaxLength(1024)]
    public string? NewPassword { get; init; }
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
/// <param name="CommuneIds">The single assigned commune, or <c>["*"]</c> for a system admin — the same answer as <c>GET /auth/me</c>.</param>
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
    DateTime? PasswordSetAt,
    string? AvatarUrl,
    DateOnly? DateOfBirth = null,
    string? Gender = null,
    string? Address = null,
    string? PhoneNumber = null,
    string? IdentityNumber = null,
    string? IdentityType = null);

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
