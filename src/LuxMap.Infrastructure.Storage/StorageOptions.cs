namespace LuxMap.Infrastructure.Storage;

/// <summary>
/// Connection settings for the object store, read from the environment exactly like the BE-03
/// database password and the BE-07 signing key.
/// </summary>
/// <remarks>
/// Same two-tier convention as the rest of the repository: non-secret values could sit in
/// <c>appsettings.json</c>, secrets never do — they come from the environment, loaded from
/// <c>.env</c> by <c>DotNetEnv</c> at the top of <c>Program.cs</c>.
/// <para>
/// ⚠️ This validates CONFIGURATION only. It does not contact MinIO, and startup does not depend on
/// MinIO being reachable — the repository has no precedent for pinging an external service at boot,
/// not even PostgreSQL. Buckets are created by the <c>minio-mc</c> sidecar in docker compose, not by
/// this application.
/// </para>
/// </remarks>
public sealed record StorageOptions
{
    public const string EndpointVariable = "MINIO_ENDPOINT";
    public const string AccessKeyVariable = "MINIO_ACCESS_KEY";
    public const string SecretKeyVariable = "MINIO_SECRET_KEY";

    /// <summary>Base URL of the S3 API, e.g. <c>http://localhost:9000</c>.</summary>
    public required string Endpoint { get; init; }

    public required string AccessKey { get; init; }

    public required string SecretKey { get; init; }

    /// <summary>
    /// MinIO addresses buckets as a path segment (<c>/luxmap-survey/...</c>) rather than as a
    /// subdomain. Virtual-host style would resolve <c>luxmap-survey.localhost</c> and fail.
    /// </summary>
    public bool ForcePathStyle { get; init; } = true;

    public static StorageOptions FromEnvironment()
        => new()
        {
            Endpoint = Required(EndpointVariable),
            AccessKey = Required(AccessKeyVariable),
            SecretKey = Required(SecretKeyVariable),
        };

    /// <summary>
    /// A missing variable STOPS startup, naming the variable and the command that fixes it — the same
    /// shape as <c>LuxMapConnectionString.FromEnvironment</c> and <c>JwtOptions.Validate</c>.
    /// </summary>
    private static string Required(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{variable} is not set. Run `cp .env.example .env` at the repository root and start "
                + "the object store with `docker compose up -d minio`.");
        }

        return value;
    }
}
