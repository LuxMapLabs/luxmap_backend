using System.Text.Json;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Serialization;

namespace LuxMap.Shared.Tests;

/// <summary>
/// The four values of Contract v1.7 section 3.1 <c>user_role</c>. They travel in the JWT claim, so web
/// and mobile hardcode them; pinned here so nobody changes them without a Contract version.
/// </summary>
public class UserRoleTests
{
    [Theory]
    [InlineData(UserRole.Superior, "superior")]
    [InlineData(UserRole.Manager, "manager")]
    [InlineData(UserRole.FieldEngineer, "field_engineer")]
    [InlineData(UserRole.SystemAdmin, "system_admin")]
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
