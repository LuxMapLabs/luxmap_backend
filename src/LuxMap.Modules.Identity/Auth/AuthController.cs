using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Identity.Auth;

/// <summary>
/// Nhóm endpoint xác thực. CHƯA có trong Contract v1.1 — cần bổ sung ở FW-00.
/// BE-07 chỉ PHÁT token; việc kiểm token và chặn request theo địa bàn là BE-08.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("login")]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthTokenResponse>> LoginAsync(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request.Username!, request.Password!, cancellationToken);
        return Respond(result);
    }

    /// <summary>
    /// Refresh token là credential DUY NHẤT ở đây: endpoint này KHÔNG đọc header Authorization
    /// và không cần access token còn hạn. Refresh được phép bất cứ lúc nào.
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthTokenResponse>> RefreshAsync(
        [FromBody] RefreshRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.RefreshAsync(request.RefreshToken!, cancellationToken);
        return Respond(result);
    }

    /// <summary>Idempotent: token đã thu hồi hoặc không tồn tại vẫn trả 204.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LogoutAsync(
        [FromBody] LogoutRequest request,
        CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(request.RefreshToken, cancellationToken);
        return NoContent();
    }

    private ActionResult<AuthTokenResponse> Respond(AuthResult result)
    {
        if (result.Succeeded)
        {
            return Ok(AuthTokenResponse.From(result.Tokens!));
        }

        // Ném LuxMapException để middleware của BE-04 dựng body — mọi API cùng một hình dạng lỗi.
        throw result.Failure switch
        {
            AuthFailure.AccountLocked => new LuxMapException(
                KnownErrors.AccountLocked.Code,
                KnownErrors.AccountLocked.StatusCode,
                "Tài khoản đang bị khoá. Liên hệ quản trị."),

            AuthFailure.InvalidRefreshToken => new LuxMapException(
                KnownErrors.InvalidRefreshToken.Code,
                KnownErrors.InvalidRefreshToken.StatusCode,
                "Refresh token không hợp lệ."),

            // Sai tài khoản và sai mật khẩu dùng CHUNG một body: tách ra là tiết lộ tài khoản
            // nào tồn tại. Không đặt details trỏ vào field cụ thể.
            _ => new LuxMapException(
                KnownErrors.InvalidCredentials.Code,
                KnownErrors.InvalidCredentials.StatusCode,
                "Tài khoản hoặc mật khẩu không đúng."),
        };
    }
}
