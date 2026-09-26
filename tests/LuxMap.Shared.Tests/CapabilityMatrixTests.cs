using System.Text.Json;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Serialization;

namespace LuxMap.Shared.Tests;

/// <summary>
/// Pins the capability matrix of Contract v1.7 section 2 exactly, in the wire values the JWT
/// carries. Runs without Docker, so a changed matrix is caught even where the HTTP suite cannot run.
/// </summary>
/// <remarks>
/// The table below is a COPY on purpose. The matrix is the single source for the code; this is the
/// Contract's statement of it, and the two may only change together.
/// </remarks>
public class CapabilityMatrixTests
{
    private static readonly Dictionary<string, string[]> Contract = new()
    {
        ["cap:read_network"] = ["field_engineer", "manager", "superior", "system_admin"],
        ["cap:read_lux_readings"] = ["field_engineer", "manager", "superior", "system_admin"],
        ["cap:manage_assets"] = ["manager"],
        ["cap:record_lux_reading"] = ["field_engineer"],
        ["cap:control_lighting"] = ["manager"],
        ["cap:manage_users"] = ["system_admin"],
    };

    [Fact]
    public void The_matrix_declares_exactly_the_capabilities_of_the_Contract()
        => Assert.Equal(
            Contract.Keys.Order(StringComparer.Ordinal),
            LuxMapPolicies.Matrix.Keys.Order(StringComparer.Ordinal));

    [Fact]
    public void Each_capability_admits_exactly_the_roles_of_the_Contract()
    {
        foreach (var (capability, roles) in LuxMapPolicies.Matrix)
        {
            var wire = roles.Select(Wire).Order(StringComparer.Ordinal).ToArray();
            Assert.True(
                Contract[capability].SequenceEqual(wire),
                $"{capability}: Contract says [{string.Join(", ", Contract[capability])}], matrix says [{string.Join(", ", wire)}]");
        }
    }

    /// <summary>
    /// No capability is empty: <c>RequireClaim(role)</c> with no values admits EVERY role, so an
    /// emptied capability is an open one, not a closed one. The host refuses to start on it too.
    /// </summary>
    [Fact]
    public void No_capability_is_empty()
        => Assert.All(LuxMapPolicies.Matrix, entry => Assert.NotEmpty(entry.Value));

    /// <summary>No role appears twice in one capability — a duplicate would hide a later edit.</summary>
    [Fact]
    public void No_capability_names_a_role_twice()
        => Assert.All(LuxMapPolicies.Matrix.Values, roles => Assert.Equal(roles.Count, roles.Distinct().Count()));

    private static string Wire(UserRole role)
        => JsonSerializer.Serialize(role, LuxMapJsonOptions.Default).Trim('"');
}
