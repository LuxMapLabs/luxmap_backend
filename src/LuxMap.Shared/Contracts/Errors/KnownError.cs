using System.Net;

namespace LuxMap.Shared.Contracts.Errors;

/// <param name="Code">A code from <see cref="ErrorCodes"/>.</param>
/// <param name="StatusCode">The HTTP status the Contract prescribes for it.</param>
/// <param name="Reference">Where in Contract v1.1 the code is specified.</param>
public sealed record KnownError(string Code, HttpStatusCode StatusCode, string Reference);

/// <summary>
/// Registry of every error code the Contract names explicitly, paired with its HTTP status.
/// Only mirrors what the Contract already says — a new code goes into the Contract first.
/// </summary>
public static class KnownErrors
{
    public static readonly KnownError BboxTooLarge =
        new(ErrorCodes.BboxTooLarge, HttpStatusCode.RequestEntityTooLarge, "section 2.1");

    public static readonly KnownError PoleNotFound =
        new(ErrorCodes.PoleNotFound, HttpStatusCode.NotFound, "section 2.8");

    public static readonly KnownError LocationRequired =
        new(ErrorCodes.LocationRequired, HttpStatusCode.BadRequest, "section 2.8");

    public static readonly KnownError FaultTypeNotReportable =
        new(ErrorCodes.FaultTypeNotReportable, HttpStatusCode.BadRequest, "section 2.8");

    /// <summary>Contract section 2.8: a duplicate <c>client_op_id</c> returns <b>200</b>, not an error.</summary>
    public static readonly KnownError DuplicateOp =
        new(ErrorCodes.DuplicateOp, HttpStatusCode.OK, "section 2.8");

    public static readonly KnownError CommuneForbidden =
        new(ErrorCodes.CommuneForbidden, HttpStatusCode.Forbidden, "section 7");

    // The /auth group — Contract v1.2 section 2.10.6.
    public static readonly KnownError InvalidCredentials =
        new(ErrorCodes.InvalidCredentials, HttpStatusCode.Unauthorized, "section 2.10.6");

    public static readonly KnownError AccountLocked =
        new(ErrorCodes.AccountLocked, HttpStatusCode.Forbidden, "section 2.10.6");

    public static readonly KnownError InvalidRefreshToken =
        new(ErrorCodes.InvalidRefreshToken, HttpStatusCode.Unauthorized, "section 2.10.6");

    public static readonly KnownError OriginNotAllowed =
        new(ErrorCodes.OriginNotAllowed, HttpStatusCode.Forbidden, "section 2.10.2");

    /// <summary>BE-08 — also absent from the Contract.</summary>
    public static readonly KnownError Unauthenticated =
        new(ErrorCodes.Unauthenticated, HttpStatusCode.Unauthorized, "BE-08, not yet in the Contract");

    /// <summary>BE-07 open registration.</summary>
    public static readonly KnownError IdentifierTaken =
        new(ErrorCodes.IdentifierTaken, HttpStatusCode.Conflict, "section 2.10.6");

    public static IReadOnlyList<KnownError> All { get; } =
    [
        BboxTooLarge, PoleNotFound, LocationRequired,
        FaultTypeNotReportable, DuplicateOp, CommuneForbidden,
        InvalidCredentials, AccountLocked, InvalidRefreshToken, Unauthenticated,
        IdentifierTaken, OriginNotAllowed,
    ];

    public static KnownError? Find(string code)
        => All.FirstOrDefault(error => string.Equals(error.Code, code, StringComparison.Ordinal));
}
