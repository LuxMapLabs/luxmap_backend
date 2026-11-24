using System.Net;
using LuxMap.Shared.Http;
using System.Text.RegularExpressions;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Shared.Contracts.Errors;

namespace LuxMap.Modules.Identity.Accounts;

internal static class UserPersonalDetails
{
    internal static string? Mask(string? value) => value is null ? null : new string('*', Math.Max(4, value.Length - 4)) + (value.Length > 4 ? value[^4..] : "");
    internal static void Apply(AppUser user, UserPersonalDetailsRequest request, DateOnly today)
    {
        if (request.Has(nameof(request.DateOfBirth))) {
            if (request.DateOfBirth > today) throw Invalid("date_of_birth", "Date of birth cannot be in the future.");
            user.DateOfBirth = request.DateOfBirth;
        }
        if (request.Has(nameof(request.Gender))) user.Gender = request.Gender;
        if (request.Has(nameof(request.Address))) user.Address = Clean(request.Address);
        if (request.Has(nameof(request.PhoneNumber))) {
            var phone = Clean(request.PhoneNumber);
            if (phone is not null) {
                phone = Regex.Replace(phone, @"[\s().-]", "");
                if (!Regex.IsMatch(phone, @"^\+?[0-9]{7,15}$")) throw Invalid("phone_number", "Use 7 to 15 digits with an optional leading +.");
            }
            user.PhoneNumber = phone;
        }
        if (request.Has(nameof(request.IdentityType))) user.IdentityType = request.IdentityType;
        if (request.Has(nameof(request.IdentityNumber))) user.IdentityNumber = Clean(request.IdentityNumber)?.ToUpperInvariant();
        if ((user.IdentityType is null) != (user.IdentityNumber is null))
            throw Invalid("identity_number", "identity_type and identity_number must both be set or both be null.");
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static LuxMapException Invalid(string field, string message) => new(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, message,
        new Dictionary<string, object?> { ["field"] = field });
}
