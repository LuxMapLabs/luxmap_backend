using Microsoft.AspNetCore.Hosting;

namespace LuxMap.Api.Tests;

/// <summary>
/// Settings every test host needs because the app refuses to start without them.
/// </summary>
public static class TestHostSettings
{
    /// <summary>The one browser origin the test hosts allow, exactly as a browser would send it.</summary>
    public const string WebOrigin = "https://localhost:3000";

    /// <summary>
    /// <c>Cors:AllowedOrigins</c> is required and validated at startup, and the test hosts run as
    /// Production, which has no value for it.
    /// </summary>
    public static IWebHostBuilder UseTestCorsOrigin(this IWebHostBuilder builder)
        => builder.UseSetting("Cors:AllowedOrigins:0", WebOrigin);
}
