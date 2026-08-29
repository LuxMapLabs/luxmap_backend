using Microsoft.AspNetCore.Authorization;

namespace LuxMap.Api.Authorization;

/// <summary>
/// Claim <c>commune_ids</c> mang <c>"*"</c> thì vai trò bắt buộc phải là Quản trị.
/// </summary>
/// <remarks>
/// ĐÂY KHÔNG PHẢI chống client giả mạo — <c>commune_ids</c> nằm trong JWT đã ký, client không sửa
/// được nếu không có khoá. Đây là lớp chặn LỖI Ở PHÍA PHÁT TOKEN: BE-06 không có ràng buộc DB nào
/// buộc <c>has_system_wide_scope</c> đi cùng <c>role = 'administrator'</c>, nên một câu UPDATE tay
/// hoặc một bug ở BE-33 là đủ để BE-07 phát <c>["*"]</c> cho tài khoản thường.
/// </remarks>
public sealed class CommuneScopeConsistencyRequirement : IAuthorizationRequirement;

public sealed class CommuneScopeConsistencyHandler(ILogger<CommuneScopeConsistencyHandler> logger)
    : AuthorizationHandler<CommuneScopeConsistencyRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CommuneScopeConsistencyRequirement requirement)
    {
        var principal = context.User;

        if (principal.Identity?.IsAuthenticated != true)
        {
            // Chưa xác thực thì để RequireAuthenticatedUser lo, ở đây không kết luận gì.
            return Task.CompletedTask;
        }

        if (CommuneScopeAccessor.HasWildcardClaim(principal) && !CommuneScopeAccessor.IsAdministrator(principal))
        {
            // Error chứ không phải Warning: đây là dấu hiệu BUG ở phía phát token, không phải
            // dấu hiệu bị tấn công.
            logger.LogError(
                "Token của {Subject} mang commune_ids '*' nhưng vai trò là {Role}, không phải Quản trị. "
                + "Kiểm tra has_system_wide_scope của tài khoản này ở BE-06/BE-33.",
                principal.FindFirst(LuxMap.Modules.Identity.Auth.AuthClaims.Subject)?.Value ?? "(không rõ)",
                principal.FindFirst(LuxMap.Modules.Identity.Auth.AuthClaims.Role)?.Value ?? "(không rõ)");

            context.Fail(new AuthorizationFailureReason(this, "commune_ids '*' không khớp vai trò."));
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
