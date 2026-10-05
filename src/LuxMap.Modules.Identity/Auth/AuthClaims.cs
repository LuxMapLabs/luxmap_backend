namespace LuxMap.Modules.Identity.Auth;

/// <summary>
/// Claim names inside the access token. BE-08 compares these strings EXACTLY — do not change the
/// casing, do not camelCase them, do not translate them.
/// </summary>
public static class AuthClaims
{
    /// <summary>User id, e.g. <c>USR-001</c>.</summary>
    public const string Subject = "sub";

    /// <summary>A SINGLE string carrying the Contract section 3.1 value (<c>system_admin</c>, <c>field_engineer</c>, ...).</summary>
    public const string Role = "role";

    /// <summary>
    /// ALWAYS an array, even with a single commune. Administrators carry <c>["*"]</c> — a one-element
    /// array, NOT the bare string <c>"*"</c>.
    /// </summary>
    public const string CommuneIds = "commune_ids";

    /// <summary>The special system-wide scope value from Contract section 7.</summary>
    public const string AllCommunes = "*";

    /// <summary>
    /// The communes an account may reach: <c>["*"]</c> for system-wide scope, otherwise its assignments
    /// in ordinal order.
    /// </summary>
    /// <remarks>
    /// ONE rule for the token, <c>GET /auth/me</c> and the admin screens (BE-33a). Copies would let them
    /// disagree about the same account, and no client could tell which one is lying.
    /// </remarks>
    public static IReadOnlyList<string> CommuneIdsFor(bool hasSystemWideScope, IEnumerable<string> assignedCommuneIds)
        => hasSystemWideScope ? [AllCommunes] : assignedCommuneIds.Order(StringComparer.Ordinal).ToArray();
}
