using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

/// <summary>
/// Every role against one representative endpoint of every capability, over real HTTP with real
/// tokens — the matrix of Contract v1.7 section 2 as a caller experiences it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ The expected answers are written out HERE, as literals, not read from
/// <c>LuxMapPolicies.Matrix</c>. A test that derived its expectation from the matrix would agree with
/// any matrix, including a wrong one: take the manager out of <c>ManageAssets</c> and both sides move
/// together. Changing who may do what therefore means editing this table too, in the same diff.
/// </para>
/// <para>
/// The write probes send an EMPTY body. An admitted role gets past authorization and is stopped by
/// validation (400); a refused role never reaches the action (403 <c>ROLE_FORBIDDEN</c>). So the test
/// tells the two apart without writing a single row. <c>ControlLighting</c> and <c>ManageUsers</c> have
/// no production endpoint yet and go through the probes of <c>ScopeTestController</c>.
/// </para>
/// </remarks>
[Collection(nameof(ScopeCollection))]
public class RoleCapabilityMatrixTests(ScopeTestFixture factory, ITestOutputHelper output)
{
    private static readonly string[] Roles = ["system_admin", "superior", "manager", "field_engineer"];

    /// <summary>Seeded account per role, with the <c>.env</c> variable holding its password.</summary>
    private static readonly Dictionary<string, (string Username, string PasswordVariable)> Accounts = new()
    {
        ["system_admin"] = ("admin", "SEED_ADMIN_PASSWORD"),
        ["superior"] = ("agency", "SEED_AGENCY_PASSWORD"),
        ["manager"] = ("engineer", "SEED_ENGINEER_PASSWORD"),
        ["field_engineer"] = ("crew", "SEED_CREW_PASSWORD"),
    };

    /// <summary>Capability → (probe, roles admitted). The source of truth for THIS test.</summary>
    private static readonly Dictionary<string, (string Method, string Url, string[] Admitted)> Expected = new()
    {
        ["ReadNetwork"] = ("GET", "/api/v1/assets/segments", ["system_admin", "superior", "manager", "field_engineer"]),
        ["ReadLuxReadings"] = ("GET", "/api/v1/lux-readings", ["system_admin", "superior", "manager", "field_engineer"]),
        ["ManageAssets"] = ("POST", "/api/v1/assets/segments", ["manager"]),
        ["RecordLuxReading"] = ("POST", "/api/v1/lux-readings", ["field_engineer"]),
        ["ControlLighting"] = ("GET", "/api/v1/_scope/manager-only", ["manager"]),
        ["ManageUsers"] = ("GET", "/api/v1/_scope/admin-only", ["system_admin"]),
    };

    private static readonly string[] RetiredRoleValues =
        ["management_agency", "maintenance_engineer", "field_crew", "administrator"];

    public static TheoryData<string, string> EveryRoleOnEveryCapability()
    {
        var data = new TheoryData<string, string>();
        foreach (var capability in Expected.Keys)
        {
            foreach (var role in Roles)
            {
                data.Add(capability, role);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryRoleOnEveryCapability))]
    public async Task Each_role_is_admitted_or_refused_exactly_as_the_matrix_says(string capability, string role)
    {
        var (method, url, admitted) = Expected[capability];
        var client = await ClientForAsync(role);

        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { });
        }

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        var shouldPass = admitted.Contains(role);

        output.WriteLine($"  {role,-15} {capability,-17} {method} {url} → HTTP {(int)response.StatusCode}");

        if (shouldPass)
        {
            var expectedStatus = method == "POST" ? HttpStatusCode.BadRequest : HttpStatusCode.OK;
            Assert.Equal(expectedStatus, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains(ErrorCodes.RoleForbidden, body);
        }
    }

    /// <summary>
    /// After migration <c>RenameUserRolesToRegistrationV12</c> no row and no constraint still speaks
    /// the retired vocabulary.
    /// </summary>
    [Fact]
    public async Task No_retired_role_value_survives_in_the_database()
    {
        var distinct = await factory.QueryAsync(db => db.Database
            .SqlQueryRaw<string>("SELECT DISTINCT role AS \"Value\" FROM app_user")
            .ToListAsync());

        var check = await factory.QueryAsync(db => db.Database
            .SqlQueryRaw<string>(
                "SELECT pg_get_constraintdef(oid) AS \"Value\" FROM pg_constraint WHERE conname = 'ck_app_user_role'")
            .SingleAsync());

        output.WriteLine($"  DISTINCT role: {string.Join(", ", distinct.Order(StringComparer.Ordinal))}");
        output.WriteLine($"  ck_app_user_role: {check}");

        Assert.All(distinct, value => Assert.Contains(value, Roles));
        foreach (var retired in RetiredRoleValues)
        {
            Assert.DoesNotContain($"'{retired}'", check, StringComparison.Ordinal);
        }

        foreach (var current in Roles)
        {
            Assert.Contains($"'{current}'", check, StringComparison.Ordinal);
        }
    }

    private async Task<HttpClient> ClientForAsync(string role)
    {
        var (username, passwordVariable) = Accounts[role];
        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(username, passwordVariable);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }
}
