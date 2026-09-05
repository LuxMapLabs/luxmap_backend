using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Shared.Authorization;

/// <summary>
/// Role policy names. Use the constants; do not scatter magic strings through the code.
/// </summary>
/// <remarks>
/// Declared in <c>LuxMap.Shared</c> rather than beside the registration in <c>LuxMap.Api</c>, because
/// the two ends live in different assemblies and the dependency only runs one way: the host
/// references the modules, so a module controller writing
/// <c>[Authorize(Policy = LuxMapPolicies.Administrator)]</c> could not see a name defined in the host.
/// The names are a CONTRACT between the two, which is exactly what Shared is for.
/// <para>
/// ⚠️ A policy is one EXACT role, not a rank. <see cref="MaintenanceEngineer"/> admits maintenance
/// engineers and nobody else — not administrators, not the managing authority. Attaching it to a read
/// endpoint to mean "engineers and above" locks out the two roles above it. Reads that everyone
/// signed in may perform carry NO policy: <c>SetFallbackPolicy</c> already requires authentication.
/// </para>
/// </remarks>
public static class LuxMapPolicies
{
    public const string ManagementAgency = "role:management_agency";
    public const string MaintenanceEngineer = "role:maintenance_engineer";
    public const string FieldCrew = "role:field_crew";
    public const string Administrator = "role:administrator";

    public static string For(UserRole role) => role switch
    {
        UserRole.ManagementAgency => ManagementAgency,
        UserRole.MaintenanceEngineer => MaintenanceEngineer,
        UserRole.FieldCrew => FieldCrew,
        UserRole.Administrator => Administrator,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "No policy defined for this role."),
    };
}
