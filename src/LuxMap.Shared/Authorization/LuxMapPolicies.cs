using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Shared.Authorization;

/// <summary>
/// Role policy names. Use the constants; do not scatter magic strings through the code.
/// </summary>
/// <remarks>
/// Declared in <c>LuxMap.Shared</c> rather than beside the registration in <c>LuxMap.Api</c>, because
/// the two ends live in different assemblies and the dependency only runs one way: the host
/// references the modules, so a module controller writing
/// <c>[Authorize(Policy = LuxMapPolicies.SystemAdmin)]</c> could not see a name defined in the host.
/// The names are a CONTRACT between the two, which is exactly what Shared is for.
/// <para>
/// ⚠️ A policy is one EXACT role, not a rank. <see cref="Manager"/> admits managers and nobody else —
/// not system admins, not superiors. Attaching it to a read endpoint to mean "managers and above"
/// locks out the roles it was meant to include. Reads that everyone
/// signed in may perform carry NO policy: <c>SetFallbackPolicy</c> already requires authentication.
/// </para>
/// </remarks>
public static class LuxMapPolicies
{
    public const string Superior = "role:superior";
    public const string Manager = "role:manager";
    public const string FieldEngineer = "role:field_engineer";
    public const string SystemAdmin = "role:system_admin";

    public static string For(UserRole role) => role switch
    {
        UserRole.Superior => Superior,
        UserRole.Manager => Manager,
        UserRole.FieldEngineer => FieldEngineer,
        UserRole.SystemAdmin => SystemAdmin,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "No policy defined for this role."),
    };
}
