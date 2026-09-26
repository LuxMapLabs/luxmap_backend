namespace LuxMap.Shared.Contracts.Enums;

/// <summary>
/// The four sign-in roles of registration form FA26SE222 v1.2 (section 3.2.c) and Contract section 3.1.
/// </summary>
/// <remarks>
/// The wire values (<c>superior</c>, <c>manager</c>, <c>field_engineer</c>, <c>system_admin</c>) travel
/// in the JWT <c>role</c> claim, so web and mobile hardcode them. Renamed from the BE-06 set in
/// Contract v1.7 (migration <c>RenameUserRolesToRegistrationV12</c>); the mapping is one-to-one.
/// <para>
/// There is no Citizen role. The registration form's Citizen reports through a QR code on the pole
/// and never holds an account (D-R1, 25/09/2026).
/// </para>
/// </remarks>
public enum UserRole
{
    /// <summary>Oversees the network: map, fault statistics, reports. Read-only. May cover several communes.</summary>
    Superior,

    /// <summary>Manages assets and coordinates survey, inspection and repair. Limited to the communes in the claim.</summary>
    Manager,

    /// <summary>Night survey capture, field inspection and repair (the former survey crew). Limited to the communes in the claim.</summary>
    FieldEngineer,

    /// <summary>Accounts, roles, configuration, monitoring — system-wide scope, claim carries the special value '*'.</summary>
    SystemAdmin,
}
