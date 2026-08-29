using LuxMap.Shared.Authorization;

namespace LuxMap.Shared.Tests;

/// <summary>
/// Luật thu hẹp theo tham số <c>commune_id</c> (Contract mục 7) và hành vi fail đóng của
/// <see cref="CommuneScope"/>.
/// </summary>
public class CommuneScopeTests
{
    [Fact]
    public void Empty_scope_allows_nothing()
    {
        Assert.False(CommuneScope.Empty.Allows("COM-001"));
        Assert.False(CommuneScope.Empty.IsSystemWide);
        Assert.Empty(CommuneScope.Empty.CommuneIds);
    }

    [Fact]
    public void System_wide_scope_allows_any_commune()
    {
        Assert.True(CommuneScope.SystemWide.Allows("COM-001"));
        Assert.True(CommuneScope.SystemWide.Allows("COM-999"));
    }

    [Fact]
    public void Scope_allows_only_what_the_claim_lists()
    {
        var scope = CommuneScope.ForCommunes(["COM-001", "COM-002"]);

        Assert.True(scope.Allows("COM-001"));
        Assert.True(scope.Allows("COM-002"));
        Assert.False(scope.Allows("COM-003"));
    }

    [Fact]
    public void No_requested_commune_means_no_narrowing()
        => Assert.Null(CommuneFilter.Narrow(CommuneScope.ForCommunes(["COM-001"]), null));

    [Fact]
    public void Requesting_an_allowed_commune_narrows_to_it()
    {
        var narrowed = CommuneFilter.Narrow(CommuneScope.ForCommunes(["COM-001", "COM-002"]), ["COM-002"]);
        Assert.Equal(["COM-002"], narrowed);
    }

    [Fact]
    public void Requesting_a_forbidden_commune_throws_403()
    {
        var error = Assert.Throws<LuxMap.Shared.Http.LuxMapException>(
            () => CommuneFilter.Narrow(CommuneScope.ForCommunes(["COM-001"]), ["COM-009"]));

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Equal(LuxMap.Shared.Contracts.Errors.ErrorCodes.CommuneForbidden, error.Code);
    }

    [Fact]
    public void One_forbidden_commune_poisons_the_whole_request()
    {
        // Trộn xã hợp lệ với xã ngoài phạm vi vẫn phải 403, không được lặng lẽ bỏ cái sai.
        Assert.Throws<LuxMap.Shared.Http.LuxMapException>(
            () => CommuneFilter.Narrow(CommuneScope.ForCommunes(["COM-001"]), ["COM-001", "COM-009"]));
    }

    [Fact]
    public void Administrator_may_request_any_commune()
        => Assert.Equal(["COM-777"], CommuneFilter.Narrow(CommuneScope.SystemWide, ["COM-777"]));
}
