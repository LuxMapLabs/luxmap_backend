using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

/// <summary>
/// Exactly seven endpoints may be reached without signing in, and all seven exist to hand out a
/// token. There is no guest actor, and no data endpoint is readable anonymously.
/// </summary>
/// <remarks>
/// <b>Why a test and not just the fallback policy.</b> <c>SetFallbackPolicy</c> protects an endpoint
/// somebody FORGOT to think about, which is the common mistake. It does nothing about an endpoint
/// somebody deliberately opens: one <c>[AllowAnonymous]</c> on a controller and that surface is public,
/// with every other test still green. This is the thing that goes red instead.
/// <para>
/// It asserts the WHOLE list rather than spot-checking, so a new anonymous endpoint fails here even
/// though nobody thought to write a test for it. Adding one on purpose means editing
/// <see cref="TokenIssuingEndpoints"/>, which shows up in a diff and needs a reason.
/// </para>
/// <para>
/// Pinned decisions: no Citizen role (<c>UserRoleTests</c>), and "bỏ actor guest" — the team's
/// decision of 20/09/2026, which was already true and is now guarded.
/// </para>
/// </remarks>
[Collection(nameof(ScopeCollection))]
public class AnonymousEndpointTests(ScopeTestFixture factory, ITestOutputHelper output)
{
    /// <summary>
    /// The complete list. Every one of them issues or revokes a token, and none reads business data.
    /// </summary>
    /// <remarks>
    /// They cannot require a token: you would need one to sign in, and signing in is how you get one.
    /// Measured rather than assumed — removing these turns <c>POST /auth/login</c> into a 401 and takes
    /// 66 of 73 auth tests with it.
    /// </remarks>
    private static readonly string[] TokenIssuingEndpoints =
    [
        "POST /api/v1/auth/login",
        "POST /api/v1/auth/logout",
        "POST /api/v1/auth/refresh",
        "POST /api/v1/auth/register",
        "POST /api/v1/auth/web/login",
        "POST /api/v1/auth/web/logout",
        "POST /api/v1/auth/web/refresh",
    ];

    [Fact]
    public void Only_the_token_issuing_endpoints_may_be_reached_without_signing_in()
    {
        var production = ProductionEndpoints();

        var anonymous = production
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Describe)
            .Order(StringComparer.Ordinal)
            .ToArray();

        output.WriteLine($"  ẩn danh ({anonymous.Length}):");
        foreach (var route in anonymous)
        {
            output.WriteLine($"    {route}");
        }

        Assert.Equal(TokenIssuingEndpoints, anonymous);
    }

    /// <summary>
    /// The endpoints that serve business data are NOT anonymous — stated positively, one by one.
    /// </summary>
    /// <remarks>
    /// The list above would also pass if the application somehow exposed no data endpoints at all.
    /// This half says the surface being protected is really there.
    /// </remarks>
    [Fact]
    public void Every_endpoint_that_serves_data_requires_a_token()
    {
        var production = ProductionEndpoints();

        var open = production
            .Where(endpoint => !Describe(endpoint).Contains("/auth/", StringComparison.Ordinal))
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Describe)
            .ToArray();

        Assert.Empty(open);

        // …and there really is a surface here: assets, imports and lux readings.
        var dataEndpoints = production.Count(endpoint =>
            !Describe(endpoint).Contains("/auth/", StringComparison.Ordinal));

        output.WriteLine($"  endpoint dữ liệu đang được bảo vệ: {dataEndpoints}");
        Assert.True(dataEndpoints >= 14, $"Chỉ thấy {dataEndpoints} endpoint dữ liệu — bộ lọc assembly có đang giấu mất gì không?");
    }

    /// <summary>
    /// <c>GET /auth/me</c> sits in the auth controller but is NOT one of the seven.
    /// </summary>
    /// <remarks>
    /// It reads a profile rather than issuing a token, so it is the one endpoint of that controller
    /// which needs one. Kept as its own case because "everything under /auth is open" is the
    /// assumption that put <c>[AllowAnonymous]</c> on the class in the first place.
    /// </remarks>
    [Fact]
    public void The_profile_endpoint_is_not_among_them_even_though_it_lives_under_auth()
    {
        Assert.DoesNotContain("GET /api/v1/auth/me", TokenIssuingEndpoints);

        var me = ProductionEndpoints().Single(endpoint => Describe(endpoint) == "GET /api/v1/auth/me");

        Assert.Null(me.Metadata.GetMetadata<IAllowAnonymous>());
    }

    private RouteEndpoint[] ProductionEndpoints() => Tests.ProductionEndpoints.Of(factory.Services);

    private static string Describe(RouteEndpoint endpoint) => Tests.ProductionEndpoints.Describe(endpoint);
}
