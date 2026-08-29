using System.Text.Json;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Serialization;

namespace LuxMap.Shared.Tests;

/// <summary>
/// ⚠️ Bốn giá trị này KHÔNG có trong Contract v1.1 — do BE-06 đặt. Chúng sẽ nằm trong claim
/// của JWT nên FE và mobile sẽ hardcode; khoá lại ở đây để không ai đổi ngầm trước khi
/// Contract được cập nhật ở FW-00.
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

        // CLAUDE.md và Contract mục 7 đều nêu đích danh: không có vai trò Người dân.
        foreach (var forbidden in new[] { "citizen", "public", "resident", "nguoidan" })
        {
            Assert.DoesNotContain(forbidden, string.Join(',', Enum.GetNames<UserRole>()), StringComparison.OrdinalIgnoreCase);
        }
    }
}
