using System.Globalization;
using System.Net;
using System.Text;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;

namespace LuxMap.Modules.Identity.Accounts;

/// <summary>ACCT-2: Vietnamese last word + earlier initials; foreign Latin names use the same rule.</summary>
public static class UsernameGenerator
{
    /// <summary>Builds the base only; allocating its numeric suffix requires a database transaction.</summary>
    public static string BaseName(string fullName)
    {
        var normalized = new StringBuilder();
        foreach (var character in fullName.Normalize(NormalizationForm.FormD))
        {
            if (char.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;

            var letter = character is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(character);
            if (char.IsWhiteSpace(letter) || letter == '-') normalized.Append(' ');
            else if (letter is >= 'a' and <= 'z') normalized.Append(letter);
            else throw Invalid("full_name must contain only Vietnamese/Latin letters, whitespace and hyphens.");
        }

        var words = normalized.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) throw Invalid("full_name must contain letters to generate a username.");
        return words[^1] + string.Concat(words.SkipLast(1).Select(word => word[0]));
    }

    private static LuxMapException Invalid(string message)
        => new(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, message,
            new Dictionary<string, object?> { ["field"] = "full_name" });
}
