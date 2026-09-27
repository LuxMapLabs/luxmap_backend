using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Shared.Authorization;

/// <summary>
/// The capability matrix: every authorization policy of the API and the EXACT roles it admits.
/// This file is the only source <c>AuthorizationSetup</c> registers policies from.
/// </summary>
/// <remarks>
/// Declared in <c>LuxMap.Shared</c> rather than beside the registration in <c>LuxMap.Api</c>, because
/// the two ends live in different assemblies and the dependency only runs one way: the host
/// references the modules, so a module controller writing
/// <c>[Authorize(Policy = LuxMapPolicies.ManageAssets)]</c> could not see a name defined in the host.
/// The names are a CONTRACT between the two, which is exactly what Shared is for.
/// <para>
/// ⚠️ <b>A policy is a LIST of exact roles, never a rank</b> (Contract v1.7 section 2, replacing
/// D-14's "one exact role"). There is no "manager and above": a role is admitted because it is
/// named in <see cref="Matrix"/>, and a role that is not named is refused. Registration form v1.2
/// needs this — the Superior reads but never writes, and some work is split between the Manager and
/// the Field Engineer — and a single-role policy could only express it by leaving endpoints bare.
/// </para>
/// <para>
/// Every business endpoint names one of these policies; none relies on the fallback policy
/// (<c>CapabilityPolicyCoverageTests</c>). A capability without an endpoint yet is declared here so
/// its roles are decided once, before the ticket that builds it: <see cref="ControlLighting"/>
/// (D-R7) and <see cref="ManageUsers"/> (BE-33). Other capabilities — survey review, work orders,
/// fault decisions — are added WITH their ticket, not in advance.
/// </para>
/// </remarks>
public static class LuxMapPolicies
{
    /// <summary>Read the lighting network: map layers, asset inventory, topology.</summary>
    public const string ReadNetwork = "cap:read_network";

    /// <summary>Read lux readings.</summary>
    public const string ReadLuxReadings = "cap:read_lux_readings";

    /// <summary>Create, replace, delete and import poles, fixtures, segments and feeders.</summary>
    public const string ManageAssets = "cap:manage_assets";

    /// <summary>Record a relative light reading taken in the field.</summary>
    public const string RecordLuxReading = "cap:record_lux_reading";

    /// <summary>
    /// Force ON / OFF / AUTO on supported lighting devices (testbed demo). No endpoint yet (D-R7).
    /// </summary>
    public const string ControlLighting = "cap:control_lighting";

    /// <summary>Create accounts and assign roles and communes. No endpoint yet (BE-33).</summary>
    public const string ManageUsers = "cap:manage_users";

    /// <summary>Policy name → the roles it admits. Exhaustive: a policy missing here does not exist.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<UserRole>> Matrix { get; } =
        new Dictionary<string, IReadOnlyList<UserRole>>(StringComparer.Ordinal)
        {
            [ReadNetwork] = [UserRole.Superior, UserRole.Manager, UserRole.FieldEngineer, UserRole.SystemAdmin],
            [ReadLuxReadings] = [UserRole.Superior, UserRole.Manager, UserRole.FieldEngineer, UserRole.SystemAdmin],
            [ManageAssets] = [UserRole.Manager],
            [RecordLuxReading] = [UserRole.FieldEngineer],
            [ControlLighting] = [UserRole.Manager],
            [ManageUsers] = [UserRole.SystemAdmin],
        };
}
