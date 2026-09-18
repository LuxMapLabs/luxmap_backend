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

    /// <summary>
    /// 415 — the uploaded bytes are not a JPEG (BE-11). Decided by the magic bytes, never by the file
    /// name or the declared content type, so a renamed PNG is rejected exactly like an honest one.
    /// </summary>
    public const string UnsupportedImageFormat = "UNSUPPORTED_IMAGE_FORMAT";

    /// <summary>
    /// 415 — the upload is not a file type this endpoint reads (BE-12a: <c>.csv</c> or <c>.geojson</c>).
    /// </summary>
    /// <remarks>
    /// The same code the BE-04 status-code page already produces for a bare 415, so a client sees one
    /// code for one meaning whether the rejection came from the framework or from a handler.
    /// </remarks>
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";

    /// <summary>
    /// 404 — the asset does not exist, OR it lies outside the caller's commune scope (BE-12a).
    /// </summary>
    /// <remarks>
    /// ONE code for both, deliberately, and Contract section 7 requires it: answering 403 for an
    /// out-of-scope asset would confirm that the id exists.
    /// </remarks>
    public const string AssetNotFound = "ASSET_NOT_FOUND";

    /// <summary>
    /// 409 — <c>(commune_id, external_ref)</c> is already taken in that commune (BE-12a).
    /// </summary>
    /// <remarks>
    /// The natural key that makes CSV import idempotent. Through CRUD the collision is an explicit
    /// conflict rather than an upsert: a single POST states an intent to CREATE, and quietly
    /// overwriting somebody else's row instead would be a different operation than the one asked for.
    /// </remarks>
    public const string ExternalRefTaken = "EXTERNAL_REF_TAKEN";

    /// <summary>
    /// 409 — the asset cannot be deleted because other rows still reference it (BE-12).
    /// </summary>
    /// <remarks>
    /// Raised when a <c>DELETE</c> is refused by a foreign key, not by a rule in code. <c>fault</c> and
    /// <c>lux_reading</c> point at <c>pole</c> with <c>RESTRICT</c>, so a pole carrying research data
    /// cannot be removed and the database is what says so.
    /// <para>
    /// ⚠️ It also fires one level deeper. <c>fixture</c> CASCADES from <c>pole</c>, so deleting a pole
    /// deletes its lamps — and if a <c>fault</c> references one of those lamps, that cascade hits
    /// <c>fk_fault_fixture_fixture_id</c> and the whole statement aborts. The pole looks unreferenced
    /// and still cannot go. <c>details</c> names what is holding it so the caller is not left guessing.
    /// </para>
    /// <para>
    /// Distinct from <see cref="ExternalRefTaken"/>, which is also 409 but means a UNIQUE collision on
    /// the way IN. This one is a reference collision on the way OUT.
    /// </para>
    /// </remarks>
    public const string AssetInUse = "ASSET_IN_USE";

    /// <summary>
    /// 409 — a write would link two assets that sit in DIFFERENT communes (BE-REVIEW-02, D-5): a
    /// pole to a feeder of another commune, for example.
    /// </summary>
    /// <remarks>
    /// Not <see cref="CommuneForbidden"/>: both communes may well be inside the caller's scope, so
    /// nothing is forbidden to them — the two rows simply may not be joined. It is a consistency
    /// conflict on the way in, the mirror image of <see cref="AssetInUse"/> on the way out, which is
    /// why it is a 409 and not a 403. The composite foreign key planned in
    /// <c>docs/contract-drift.md</c> (D-10) will raise the same refusal from the database.
    /// </remarks>
    public const string CrossCommuneReference = "CROSS_COMMUNE_REFERENCE";

    /// <summary>
    /// 403 — the caller is signed in but their ROLE is not admitted by the endpoint's policy
    /// (BE-REVIEW-02, D-4).
    /// </summary>
    /// <remarks>
    /// Split from <see cref="CommuneForbidden"/>, which Contract section 7 reserves for a
    /// <c>commune_id</c> outside the caller's scope. Before this code existed every bare 403 was
    /// reported as a commune problem, so an engineer refused by the Administrator policy was told they
    /// were outside their territory.
    /// </remarks>
    public const string RoleForbidden = "ROLE_FORBIDDEN";

    /// <summary>
    /// 409 — the pole already carries a lamp in service; retire it first (BE-REVIEW-02, D-11).
    /// </summary>
    /// <remarks>
    /// Enforced by the partial unique index <c>ux_fixture_pole_id_active</c>
    /// (<c>pole_id WHERE removed_date IS NULL</c>), so the database says no however the row arrives.
    /// A lamp with <c>removed_date</c> already set is history, not a second active lamp, and is not
    /// refused.
    /// </remarks>
    public const string PoleHasActiveFixture = "POLE_HAS_ACTIVE_FIXTURE";

    /// <summary>
    /// 400 — the body carried a field the SERVER owns (BE-42): a display id, or <c>commune_id</c>.
    /// </summary>
    /// <remarks>
    /// Rejected loudly rather than ignored. Silently dropping a field the caller believed in leaves
    /// them thinking they set something they did not — and for <c>commune_id</c> that belief would be
    /// about which commune owns the record.
    /// </remarks>
    public const string ServerOwnedField = "SERVER_OWNED_FIELD";

    // ── Authentication — Contract v1.2 section 2.10.6, except UNAUTHENTICATED (BE-08) ───

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
    /// 403 — a <c>/api/v1/auth/web/*</c> request whose <c>Origin</c> is missing, not on the
    /// allowlist, or <c>null</c> (section 2.10.2).
    /// </summary>
    public const string OriginNotAllowed = "ORIGIN_NOT_ALLOWED";

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
