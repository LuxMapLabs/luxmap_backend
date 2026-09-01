namespace LuxMap.Modules.Identity.Auth;

/// <summary>
/// Claim names inside the access token. BE-08 compares these strings EXACTLY — do not change the
/// casing, do not camelCase them, do not translate them.
/// </summary>
public static class AuthClaims
{
    /// <summary>User id, e.g. <c>USR-001</c>.</summary>
    public const string Subject = "sub";

    /// <summary>A SINGLE string carrying the BE-06 value (<c>administrator</c>, <c>field_crew</c>, ...).</summary>
    public const string Role = "role";

    /// <summary>
    /// ALWAYS an array, even with a single commune. Administrators carry <c>["*"]</c> — a one-element
    /// array, NOT the bare string <c>"*"</c>.
    /// </summary>
    public const string CommuneIds = "commune_ids";

    /// <summary>The special system-wide scope value from Contract section 7.</summary>
    public const string AllCommunes = "*";
}
