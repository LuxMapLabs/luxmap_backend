using System.Net;

namespace LuxMap.Shared.Contracts.Errors;

/// <param name="Code">Mã trong <see cref="ErrorCodes"/>.</param>
/// <param name="StatusCode">HTTP status mà Contract quy định cho mã này.</param>
/// <param name="Reference">Mục trong Contract v1.1 đặc tả mã này.</param>
public sealed record KnownError(string Code, HttpStatusCode StatusCode, string Reference);

/// <summary>
/// Registry các mã lỗi Contract đã đặc tả đích danh, kèm HTTP status tương ứng.
/// Chỉ chép lại những gì Contract đã nêu — mã mới phải vào Contract trước.
/// </summary>
public static class KnownErrors
{
    public static readonly KnownError BboxTooLarge =
        new(ErrorCodes.BboxTooLarge, HttpStatusCode.RequestEntityTooLarge, "mục 2.1");

    public static readonly KnownError PoleNotFound =
        new(ErrorCodes.PoleNotFound, HttpStatusCode.NotFound, "mục 2.8");

    public static readonly KnownError LocationRequired =
        new(ErrorCodes.LocationRequired, HttpStatusCode.BadRequest, "mục 2.8");

    public static readonly KnownError FaultTypeNotReportable =
        new(ErrorCodes.FaultTypeNotReportable, HttpStatusCode.BadRequest, "mục 2.8");

    /// <summary>Contract mục 2.8: trùng <c>client_op_id</c> trả <b>200</b>, KHÔNG phải lỗi.</summary>
    public static readonly KnownError DuplicateOp =
        new(ErrorCodes.DuplicateOp, HttpStatusCode.OK, "mục 2.8");

    public static readonly KnownError CommuneForbidden =
        new(ErrorCodes.CommuneForbidden, HttpStatusCode.Forbidden, "mục 7");

    // BE-07 — nhóm /auth. Contract chưa đặc tả, cần bổ sung ở FW-00.
    public static readonly KnownError InvalidCredentials =
        new(ErrorCodes.InvalidCredentials, HttpStatusCode.Unauthorized, "BE-07, chưa có trong Contract");

    public static readonly KnownError AccountLocked =
        new(ErrorCodes.AccountLocked, HttpStatusCode.Forbidden, "BE-07, chưa có trong Contract");

    public static readonly KnownError InvalidRefreshToken =
        new(ErrorCodes.InvalidRefreshToken, HttpStatusCode.Unauthorized, "BE-07, chưa có trong Contract");

    public static IReadOnlyList<KnownError> All { get; } =
    [
        BboxTooLarge, PoleNotFound, LocationRequired,
        FaultTypeNotReportable, DuplicateOp, CommuneForbidden,
        InvalidCredentials, AccountLocked, InvalidRefreshToken,
    ];

    public static KnownError? Find(string code)
        => All.FirstOrDefault(error => string.Equals(error.Code, code, StringComparison.Ordinal));
}
