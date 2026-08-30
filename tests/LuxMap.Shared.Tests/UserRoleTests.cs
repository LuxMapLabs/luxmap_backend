using System.Text.Json;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Serialization;

namespace LuxMap.Shared.Tests;

/// <summary>
/// ⚠️ These four values are NOT in Contract v1.1 — BE-06 chose them. They travel in the JWT claim, so
/// web and mobile will hardcode them; pinned here so nobody changes them quietly before the Contract
/// is updated at FW-00.
/// </summary>
public class UserRoleTests
{
    [Theory]
    [InlineData(UserRole.ManagementAgency, "management_agency")]
    [InlineData(UserRole.MaintenanceEngineer, "maintenance_engineer")]
    [InlineData(UserRole.FieldCrew, "field_crew")]
    [InlineData(UserRole.Administrator, "administrator")]
    public void Role_serializes_to_the_agreed_string(UserRole role, string expected)
        => Assert.Equal(expected, JsonSerializer.Serialize(role, LuxMapJsonOptions.Default).Trim('"'));

    [Fact]
    public void There_are_exactly_four_roles_and_none_of_them_is_a_citizen()
    {
        Assert.Equal(4, Enum.GetValues<UserRole>().Length);

        // Both CLAUDE.md and Contract section 7 state it explicitly: there is no Citizen role.
        foreach (var forbidden in new[] { "citizen", "public", "resident", "nguoidan" })
        {
            Assert.DoesNotContain(forbidden, string.Join(',', Enum.GetNames<UserRole>()), StringComparison.OrdinalIgnoreCase);
        }
    }
}
