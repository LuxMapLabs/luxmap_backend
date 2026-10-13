namespace LuxMap.Shared.Http;

/// <summary>
/// Rate-limit policy names. The host registers them (<c>RateLimitSetup</c>); module controllers name
/// them in <c>[EnableRateLimiting]</c>, which is why they live here and not in the host.
/// </summary>
public static class LuxMapRateLimits
{
    /// <summary>
    /// Anonymous endpoints that send mail (BE-33a, D-2): 5 requests per 15 minutes per client address
    /// by default. Without it, anyone could make the server mail an address over and over.
    /// </summary>
    public const string AccountMail = "account-mail";

    /// <summary>CITIZEN-QR P2b (D-8): the public QR lookup — 30 per 10 minutes per client address by default. Also slows code guessing.</summary>
    public const string PublicLookup = "public-lookup";

    /// <summary>CITIZEN-QR P2b (D-8): the public report form — 10 per hour per client address by default.</summary>
    public const string PublicReport = "public-report";
}
