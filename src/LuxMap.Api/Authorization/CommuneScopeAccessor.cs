using System.Security.Claims;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Api.Authorization;

/// <summary>
/// Rút phạm vi địa bàn từ <see cref="ClaimsPrincipal"/> của request hiện tại.
/// </summary>
/// <remarks>
/// ⚠️ PHẢI đăng ký là <b>singleton</b>, không phải scoped. Model của EF Core dựng MỘT LẦN rồi
/// được cache, và biểu thức query filter giữ tham chiếu tới đúng instance accessor lúc dựng
/// model. Nếu accessor là scoped thì mọi request sau sẽ dùng lại phạm vi của request đầu tiên —
/// rò dữ liệu im lặng, đúng loại lỗi BE-08 sinh ra để chặn.
/// Singleton đọc <see cref="IHttpContextAccessor"/> nên vẫn lấy đúng người dùng của từng request.
/// </remarks>
public sealed class CommuneScopeAccessor(IHttpContextAccessor httpContextAccessor) : ICommuneScopeAccessor
{
    public CommuneScope Scope => FromPrincipal(httpContextAccessor.HttpContext?.User);

    /// <summary>
    /// Fail đóng ở mọi nhánh: chưa xác thực, claim thiếu hẳn, hoặc claim rỗng đều ra
    /// <see cref="CommuneScope.Empty"/> — KHÔNG bao giờ hiểu là "không có ràng buộc".
    /// </summary>
    public static CommuneScope FromPrincipal(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return CommuneScope.Empty;
        }

        // commune_ids là MẢNG trong JWT nên vào ClaimsPrincipal thành NHIỀU claim cùng tên.
        // Phải dùng FindAll, FindFirst chỉ lấy được phần tử đầu.
        var communeIds = principal.FindAll(AuthClaims.CommuneIds).Select(claim => claim.Value).ToArray();

        if (communeIds.Length == 0)
        {
            return CommuneScope.Empty;
        }

        if (!communeIds.Contains(AuthClaims.AllCommunes, StringComparer.Ordinal))
        {
            return CommuneScope.ForCommunes(communeIds);
        }

        // Phòng vệ chiều sâu: '*' chỉ có nghĩa khi vai trò đúng là Quản trị. Trường hợp lệch đã
        // bị chặn ở CommuneScopeConsistencyHandler; ở đây fail đóng lần nữa phòng khi ai đó
        // dùng accessor ngoài đường pipeline có authorization.
        return IsAdministrator(principal) ? CommuneScope.SystemWide : CommuneScope.Empty;
    }

    public static bool IsAdministrator(ClaimsPrincipal principal)
        => string.Equals(
            principal.FindFirst(AuthClaims.Role)?.Value,
            ContractEnum.ToDbValue(UserRole.Administrator),
            StringComparison.Ordinal);

    public static bool HasWildcardClaim(ClaimsPrincipal principal)
        => principal.FindAll(AuthClaims.CommuneIds)
            .Any(claim => string.Equals(claim.Value, AuthClaims.AllCommunes, StringComparison.Ordinal));
}
