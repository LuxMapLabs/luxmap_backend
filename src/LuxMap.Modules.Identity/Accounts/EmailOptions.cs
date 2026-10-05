namespace LuxMap.Modules.Identity.Accounts;

/// <summary>
/// Outgoing mail for account invitations and password resets (BE-33a), read from the environment like
/// <c>StorageOptions</c> and the JWT signing key.
/// </summary>
/// <remarks>
/// Required in EVERY environment (D-5): a missing value stops startup rather than creating accounts
/// that never receive their invitation. Development and CI point at Mailpit (<c>localhost:1025</c>,
/// no credentials), so no real mailbox is needed to run the API. Like <c>StorageOptions</c>, this
/// validates CONFIGURATION only and never contacts the mail server at startup.
/// </remarks>
public sealed record EmailOptions
{
    public const string HostVariable = "SMTP_HOST";
    public const string PortVariable = "SMTP_PORT";
    public const string UsernameVariable = "SMTP_USERNAME";
    public const string PasswordVariable = "SMTP_PASSWORD";
    public const string FromAddressVariable = "SMTP_FROM_ADDRESS";
    public const string FromNameVariable = "SMTP_FROM_NAME";
    public const string WebAppBaseUrlVariable = "WEB_APP_BASE_URL";

    /// <summary>The web page that redeems invite and reset links; agreed with WP5.</summary>
    public const string SetPasswordPath = "/set-password";

    public required string Host { get; init; }

    public required int Port { get; init; }

    /// <summary>Empty for a relay that takes no login (Mailpit). Set together with <see cref="Password"/>.</summary>
    public string? Username { get; init; }

    public string? Password { get; init; }

    public required string FromAddress { get; init; }

    public required string FromName { get; init; }

    /// <summary>Origin of the web app, e.g. <c>http://localhost:5173</c>; links are built on it.</summary>
    public required Uri WebAppBaseUrl { get; init; }

    /// <summary>
    /// A server that takes a login must be reached over TLS, never in clear: the password would cross
    /// the network readable. Without a login (local Mailpit) there is nothing to protect.
    /// </summary>
    public bool RequiresTls => Username is not null;

    public static EmailOptions FromEnvironment()
    {
        var host = Required(HostVariable);
        var port = Required(PortVariable);
        if (!int.TryParse(port, out var number) || number is < 1 or > 65535)
        {
            throw new InvalidOperationException($"{PortVariable} must be a port number, got '{port}'.");
        }

        var baseUrl = Required(WebAppBaseUrlVariable);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"{WebAppBaseUrlVariable} must be an absolute http(s) URL, got '{baseUrl}'.");
        }

        var username = Optional(UsernameVariable);
        var password = Optional(PasswordVariable);
        if ((username is null) != (password is null))
        {
            throw new InvalidOperationException(
                $"Set both {UsernameVariable} and {PasswordVariable}, or neither (a relay without login).");
        }

        return new EmailOptions
        {
            Host = host,
            Port = number,
            Username = username,
            Password = password,
            FromAddress = Required(FromAddressVariable),
            FromName = Required(FromNameVariable),
            WebAppBaseUrl = uri,
        };
    }

    /// <summary>The link a mail carries. The token rides in the query; the page sends no Referer.</summary>
    public Uri SetPasswordLink(string rawToken)
        => new(WebAppBaseUrl, $"{SetPasswordPath}?token={Uri.EscapeDataString(rawToken)}");

    private static string? Optional(string variable)
        => Environment.GetEnvironmentVariable(variable) is { } value && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string Required(string variable)
        => Optional(variable) ?? throw new InvalidOperationException(
            $"{variable} is not set. Run `cp .env.example .env` at the repository root; development uses "
            + "Mailpit from `docker compose up -d mailpit`.");
}
