using LuxMap.Api.Authorization;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

/// <summary>
/// Architecture rule of Contract v1.7 section 2: every business endpoint names a capability policy
/// from <see cref="LuxMapPolicies.Matrix"/>, and no policy exists outside it.
/// </summary>
/// <remarks>
/// <b>Why the fallback policy is no longer enough.</b> It only demands a signed-in caller, so an
/// endpoint that relies on it admits ALL four roles — including the read-only Superior on a write.
/// That is exactly how <c>POST /lux-readings</c> stood until v1.7. A new endpoint that forgets its
/// policy looks fine in every behavioural test; this is the thing that goes red instead.
/// </remarks>
[Collection(nameof(ScopeCollection))]
public class CapabilityPolicyCoverageTests(ScopeTestFixture factory, ITestOutputHelper output)
{
    /// <summary>
    /// Endpoints that need a signed-in caller but deliberately no capability: they concern the
    /// caller themself, not the network. Adding one means editing this list, which shows in a diff.
    /// </summary>
    private static readonly string[] IdentityOnlyEndpoints =
    [
        "GET /api/v1/auth/me",
    ];

    [Fact]
    public void Every_endpoint_that_is_not_anonymous_names_a_capability_policy()
    {
        var bare = ProductionEndpoints.Of(factory.Services)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Where(endpoint => !IdentityOnlyEndpoints.Contains(ProductionEndpoints.Describe(endpoint)))
            .Where(endpoint => !PoliciesOf(endpoint).Any(LuxMapPolicies.Matrix.ContainsKey))
            .Select(ProductionEndpoints.Describe)
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreach (var route in bare)
        {
            output.WriteLine($"  không có capability: {route}");
        }

        Assert.Empty(bare);
    }

    [Fact]
    public void Every_policy_an_endpoint_names_is_in_the_matrix()
    {
        var unknown = ProductionEndpoints.Of(factory.Services)
            .SelectMany(endpoint => PoliciesOf(endpoint)
                .Where(policy => !LuxMapPolicies.Matrix.ContainsKey(policy))
                .Select(policy => $"{ProductionEndpoints.Describe(endpoint)} → {policy}"))
            .ToArray();

        Assert.Empty(unknown);
    }

    /// <summary>The exception list must not outlive the endpoints it excuses.</summary>
    [Fact]
    public void Every_identity_only_endpoint_exists_and_carries_no_capability()
    {
        var byRoute = ProductionEndpoints.Of(factory.Services)
            .ToDictionary(ProductionEndpoints.Describe, StringComparer.Ordinal);

        foreach (var route in IdentityOnlyEndpoints)
        {
            Assert.True(byRoute.TryGetValue(route, out var endpoint), $"{route} không còn tồn tại");
            Assert.Empty(PoliciesOf(endpoint!));
            Assert.Null(endpoint!.Metadata.GetMetadata<IAllowAnonymous>());
        }
    }

    /// <summary>
    /// What <c>AuthorizationSetup</c> REGISTERED is what the matrix says — nothing added on the way.
    /// </summary>
    /// <remarks>
    /// The matrix could be right and the registration still wrong: a stray value in
    /// <c>RequireClaim</c> would admit a role the matrix never named. So this reads the policies
    /// back from the host rather than trusting the loop that built them.
    /// </remarks>
    [Fact]
    public async Task Each_registered_policy_admits_exactly_the_roles_the_matrix_names()
    {
        var provider = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        foreach (var (name, roles) in LuxMapPolicies.Matrix)
        {
            var policy = await provider.GetPolicyAsync(name);
            Assert.NotNull(policy);

            var claims = Assert.Single(policy!.Requirements.OfType<ClaimsAuthorizationRequirement>());
            var admitted = claims.AllowedValues!.Order(StringComparer.Ordinal).ToArray();
            var expected = roles.Select(ContractEnum.ToDbValue).Order(StringComparer.Ordinal).ToArray();

            output.WriteLine($"  {name,-26} {string.Join(", ", admitted)}");
            Assert.Equal(expected, admitted);

            // The '*'-versus-role check stays on every policy, exactly as before the matrix.
            Assert.Single(policy.Requirements.OfType<CommuneScopeConsistencyRequirement>());
        }
    }

    /// <summary>
    /// The four values retired by Contract v1.7 are admitted by no registered policy.
    /// </summary>
    /// <remarks>
    /// A token minted before the migration still carries one of them for up to 60 minutes. The only
    /// thing that keeps such a token from passing is that no policy lists the old value — and a
    /// "compatibility" entry is exactly the kind of addition that looks harmless in review.
    /// </remarks>
    [Fact]
    public async Task No_registered_policy_admits_a_retired_role_value()
    {
        string[] retired = ["management_agency", "maintenance_engineer", "field_crew", "administrator"];
        var provider = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        foreach (var name in LuxMapPolicies.Matrix.Keys)
        {
            var policy = await provider.GetPolicyAsync(name);
            var admitted = policy!.Requirements.OfType<ClaimsAuthorizationRequirement>()
                .SelectMany(requirement => requirement.AllowedValues ?? []);

            Assert.Empty(admitted.Intersect(retired, StringComparer.Ordinal));
        }
    }

    private static IEnumerable<string> PoliciesOf(RouteEndpoint endpoint)
        => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(data => data.Policy)
            .OfType<string>();
}
