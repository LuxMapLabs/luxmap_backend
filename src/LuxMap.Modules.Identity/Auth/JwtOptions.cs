namespace LuxMap.Modules.Identity.Auth;

/// <summary>
/// JWT issuance settings. Non-secret values live in <c>appsettings.json</c> under <c>Jwt</c>; the
/// signing key does NOT — see <see cref="SigningKey"/>.
/// </summary>
public sealed record JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Environment variable holding the signing key, loaded from <c>.env</c> like the BE-03 database password.</summary>
    public const string SigningKeyEnvironmentVariable = "JWT_SIGNING_KEY";

    /// <summary>HS256 requires at least a 256-bit key.</summary>
    public const int MinimumSigningKeyBytes = 32;

    /// <summary>BE-08 compares this string EXACTLY. One character out and every request is rejected.</summary>
    public string Issuer { get; init; } = "luxmap-api";

    public string Audience { get; init; } = "luxmap-clients";

    /// <summary>Access tokens live 60 minutes — this value goes straight into <c>expires_in</c>.</summary>
    public int AccessTokenMinutes { get; init; } = 60;

    /// <summary>Sliding refresh window: each rotation grants another 30 days from that moment.</summary>
    public int RefreshSlidingDays { get; init; } = 30;

    /// <summary>Absolute ceiling measured from the first sign-in. Rotation never pushes it further out.</summary>
    public int RefreshAbsoluteDays { get; init; } = 90;

    /// <summary>
    /// Replaying a just-rotated token within this many seconds counts as a benign retry. Retrying on a
    /// weak connection is normal behaviour, in the same spirit as <c>client_op_id</c>.
    /// </summary>
    public int ReuseGraceSeconds { get; init; } = 30;

    /// <summary>The HS256 signing key. Never hardcoded, never committed, never defaulted.</summary>
    public required string SigningKey { get; init; }

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(AccessTokenMinutes);

    public TimeSpan ReuseGraceWindow => TimeSpan.FromSeconds(ReuseGraceSeconds);

    /// <summary>A missing or too-short key STOPS startup immediately rather than running on.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SigningKey))
        {
            throw new InvalidOperationException(
                $"{SigningKeyEnvironmentVariable} is not set. Run `cp .env.example .env` at the repository "
                + "root and put a long random key in it.");
        }

        var bytes = System.Text.Encoding.UTF8.GetByteCount(SigningKey);
        if (bytes < MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"{SigningKeyEnvironmentVariable} is only {bytes} bytes; HS256 requires at least "
                + $"{MinimumSigningKeyBytes}.");
        }
    }
}
