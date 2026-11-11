using System.ComponentModel.DataAnnotations;
using System.Net;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Identity.Profiles;

/// <summary>
/// <c>PATCH /auth/me</c> (ACCT-6). Only <c>full_name</c> is self-service: <c>email</c> is where password reset
/// links go and what a Google sign-in matches on, so changing it stays with the administrator.
/// </summary>
public sealed class UpdateProfileRequest
{
    // No [Required]: it would reject a blank name before the service can name the field in the error.
    [MaxLength(ProfileLimits.FullNameMaxLength)]
    public string? FullName { get; init; }
}

public static class ProfileLimits
{
    public const int FullNameMinLength = 2;
    public const int FullNameMaxLength = 256;
}

public sealed class ProfileService(
    LuxMapDbContext db, ICurrentActorAccessor actor, AuthService auth, TimeProvider clock)
{
    public async Task<CurrentUserResponse> UpdateAsync(UpdateProfileRequest request, CancellationToken ct)
    {
        // DataAnnotations stop at the controller; the rule that matters (length after trimming) lives here.
        var fullName = request.FullName?.Trim() ?? string.Empty;
        if (fullName.Length < ProfileLimits.FullNameMinLength)
        {
            throw new LuxMapException(
                ErrorCodes.ValidationFailed,
                HttpStatusCode.BadRequest,
                $"full_name must have at least {ProfileLimits.FullNameMinLength} characters.",
                new Dictionary<string, object?> { ["field"] = "full_name" });
        }

        var userId = actor.UserId ?? throw StaleToken();
        var user = await db.Set<AppUser>().SingleOrDefaultAsync(u => u.UserId == userId, ct) ?? throw StaleToken();
        user.FullName = fullName;
        user.UpdatedAt = UtcMicrosecondClock.UtcNow(clock);
        await db.SaveChangesAsync(ct);

        // Answer with what GET /auth/me would now say, so the client has one object to replace its copy with.
        return await auth.FindCurrentUserAsync(userId, ct) ?? throw StaleToken();
    }

    // Same answer as GET /auth/me gives for a token that names nobody.
    private static LuxMapException StaleToken()
        => new(ErrorCodes.Unauthenticated, HttpStatusCode.Unauthorized,
            "This access token no longer identifies an account. Sign in again.");
}
