namespace LuxMap.Shared.Contracts.Enums;

/// <summary>
/// The four roles from CLAUDE.md and Contract section 7.
/// </summary>
/// <remarks>
/// ⚠️ NOT part of Contract v1.1 section 1 — the Contract only names the roles in prose, it never
/// fixes their wire values. The four strings below were chosen by BE-06 and end up in the JWT
/// <c>role</c> claim, so web and mobile will hardcode them. <b>They must be added to the Contract
/// at FW-00 and the version bumped</b> before WP5/WP6 build against them.
/// <para>
/// There is no Citizen role — both the Contract and CLAUDE.md state this explicitly.
/// </para>
/// </remarks>
public enum UserRole
{
    /// <summary>Managing authority — may cover several communes.</summary>
    ManagementAgency,

    /// <summary>Maintenance engineer — reviews faults, limited to the communes in the claim.</summary>
    MaintenanceEngineer,

    /// <summary>Survey and repair crew — files faults on site, limited to the communes in the claim.</summary>
    FieldCrew,

    /// <summary>Administrator — system-wide scope, claim carries the special value '*'.</summary>
    Administrator,
}
