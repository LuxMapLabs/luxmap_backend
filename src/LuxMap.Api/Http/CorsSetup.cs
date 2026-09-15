using LuxMap.Shared.Contracts;

namespace LuxMap.Api.Http;

/// <summary>
/// The origins a browser may call this API from with credentials (Contract section 2.10.2).
/// </summary>
/// <remarks>
/// Required, and validated at startup like <c>JwtOptions</c>: a missing or malformed list STOPS the
/// app rather than running with CORS silently off. Development gets its value from
/// <c>appsettings.Development.json</c>; a deployment supplies <c>Cors__AllowedOrigins__0</c>, ... from
/// the environment.
/// <para>
/// Development, and ONLY Development, also accepts an <c>http</c> origin on a loopback host, because
/// that is what a Vite dev server actually serves on. Outside Development the rule is unchanged, so a
/// deployment cannot inherit the relaxation by copying a dev value.
/// </para>
/// <para>
/// The same list backs the <c>ORIGIN_NOT_ALLOWED</c> guard on <c>/api/v1/auth/web/*</c>: that guard
/// reads it through the default CORS policy, so there is exactly one allowlist.
/// </para>
/// </remarks>
public sealed record CorsOriginsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; init; } = [];

    /// <summary>
    /// Each entry must be exactly what a browser sends in <c>Origin</c>: an absolute <c>https</c>
    /// origin with no path, no trailing slash, no default port and no wildcard. Anything else could
    /// never match a real request, so accepting it would only hide a typo until the first login.
    /// </summary>
    /// <param name="allowInsecureLoopback">
    /// Development only. Widens the scheme rule to <c>http</c> for <c>localhost</c>, <c>127.0.0.1</c>
    /// and <c>[::1]</c> — nothing else. A plain-http origin on any other host stays rejected even in
    /// Development: the point is to reach a dev server on this machine, not to turn the check off.
    /// </param>
    public void Validate(bool allowInsecureLoopback = false)
    {
        if (AllowedOrigins.Length == 0)
        {
            throw new InvalidOperationException(
                $"{SectionName}:AllowedOrigins is empty. Set at least one https origin, e.g. "
                + $"{SectionName}__AllowedOrigins__0=https://app.example.vn.");
        }

        foreach (var origin in AllowedOrigins)
        {
            if (!IsAcceptableOrigin(origin, allowInsecureLoopback))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:AllowedOrigins entry '{origin}' is not an acceptable origin. Expected "
                    + "scheme://host[:port] exactly as a browser sends it: no path, no trailing slash, "
                    + "no default port, no '*'. https everywhere; http only on localhost, 127.0.0.1 or "
                    + "[::1], and only in Development.");
            }
        }
    }

    private static bool IsAcceptableOrigin(string? origin, bool allowInsecureLoopback)
    {
        if (string.IsNullOrWhiteSpace(origin)
            || origin.Contains('*', StringComparison.Ordinal)
            || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            // The canonical form is what closes the door on a path, a trailing slash or a default
            // port sneaking in: a browser never sends those, so an entry carrying one can only be a
            // typo that would fail silently at the first request.
            || !string.Equals(origin, uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps
               || (allowInsecureLoopback && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }
}

public static class CorsSetup
{
    public static IServiceCollection AddLuxMapCors(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var options = configuration.GetSection(CorsOriginsOptions.SectionName).Get<CorsOriginsOptions>()
            ?? new CorsOriginsOptions();
        options.Validate(allowInsecureLoopback: environment.IsDevelopment());

        // The DEFAULT policy, deliberately unnamed: the web auth Origin guard asks the policy provider
        // for it with no name, so the guard and CORS can never disagree about the list.
        services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
            .WithOrigins(options.AllowedOrigins)
            .AllowCredentials()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders(ApiHeaders.CorrelationId)));

        return services;
    }

    /// <summary>
    /// Must sit INSIDE the error handling (so a 401/500 built there still carries the CORS headers
    /// and the browser can read it) and BEFORE authentication (so a preflight is answered without a
    /// token).
    /// </summary>
    public static WebApplication UseLuxMapCors(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseCors();

        return app;
    }
}
