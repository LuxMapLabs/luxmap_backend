namespace LuxMap.Shared.Contracts.Errors;

/// <summary>
/// Error codes named explicitly by Contract v1.1. Only codes the Contract already specifies belong
/// here — a new code goes into the Contract first.
/// </summary>
public static class ErrorCodes
{
    /// <summary>413 — the bbox covers more than 2000 poles (section 2.1).</summary>
    public const string BboxTooLarge = "BBOX_TOO_LARGE";

    /// <summary>404 — <c>pole_id</c> does not exist (section 2.8).</summary>
    public const string PoleNotFound = "POLE_NOT_FOUND";

    /// <summary>400 — neither <c>pole_id</c> nor <c>location</c> was supplied (section 2.8).</summary>
    public const string LocationRequired = "LOCATION_REQUIRED";

    /// <summary>400 — <c>fault_type</c> belongs to the engine-only set (section 2.8).</summary>
    public const string FaultTypeNotReportable = "FAULT_TYPE_NOT_REPORTABLE";

    /// <summary>200 (NOT an error) — <c>client_op_id</c> already handled, the existing record is returned (sections 2.8, 5.8).</summary>
    public const string DuplicateOp = "DUPLICATE_OP";

    /// <summary>403 — a <c>commune_id</c> outside the caller's claim was requested (section 7).</summary>
    public const string CommuneForbidden = "COMMUNE_FORBIDDEN";

    // ── Below this line: NOT in Contract v1.1 ────────────────────────────────
    // Infrastructure codes added because every API must share one error shape.
    // They must be folded into the Contract at FW-00 — don't leave the front end guessing.

    /// <summary>400 — the request failed validation. Per-field detail lives in <c>details</c>.</summary>
    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>500 — unhandled failure. The message is deliberately generic; detail lives in the log under the correlation id.</summary>
    public const string InternalError = "INTERNAL_ERROR";

    // ── Authentication (BE-07, BE-08) — also absent from Contract v1.1 ───────

    /// <summary>
    /// 401 — wrong username OR wrong password. Deliberately ONE code for both: splitting them
    /// tells an attacker which accounts exist.
    /// </summary>
    public const string InvalidCredentials = "INVALID_CREDENTIALS";

    /// <summary>403 — correct password but the account is locked.</summary>
    public const string AccountLocked = "ACCOUNT_LOCKED";

    /// <summary>
    /// 401 — refresh token unknown, expired, revoked, or replayed. Again deliberately ONE code:
    /// distinguishing them helps an attacker probe which tokens once existed.
    /// </summary>
    public const string InvalidRefreshToken = "INVALID_REFRESH_TOKEN";

    /// <summary>
    /// 401 — access token missing, badly signed, expired, wrong issuer, or wrong audience.
    /// ONE code for EVERY cause: splitting them helps an attacker probe the configuration.
    /// </summary>
    public const string Unauthenticated = "UNAUTHENTICATED";

    /// <summary>
    /// 409 — the username or email is already taken. Registration is open and internal-only, so a
    /// clear answer is worth more than hiding which identifiers exist; see docs/contract-drift.md.
    /// </summary>
    public const string IdentifierTaken = "IDENTIFIER_TAKEN";
}
