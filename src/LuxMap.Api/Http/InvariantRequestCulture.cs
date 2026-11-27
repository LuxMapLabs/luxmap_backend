using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace LuxMap.Api.Http;

/// <summary>
/// Every request runs under the invariant culture, whatever the host machine's culture is.
/// </summary>
/// <remarks>
/// The form value provider parses numbers with the CURRENT culture and allows a thousands separator. On a Windows host set to
/// vi-VN, <c>lat=10.82</c> in a multipart form then reads as 1082; on the Linux VPS (invariant) it reads as 10.82. Clients cannot
/// serve both, and the web once sent <c>10,82</c> to please a Windows dev machine, which the VPS read as 1082 (400 on every
/// citizen report, 26/11/2026). Pinning the request culture makes the dev machine read numbers the way the VPS does.
/// No request culture provider is registered: a client's <c>Accept-Language</c> must not change how its numbers are read.
/// The query string is invariant already; JSON goes through System.Text.Json, which ignores culture.
/// </remarks>
public static class InvariantRequestCulture
{
    public static IApplicationBuilder UseLuxMapInvariantCulture(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseRequestLocalization(new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture),
            SupportedCultures = [CultureInfo.InvariantCulture],
            SupportedUICultures = [CultureInfo.InvariantCulture],
            RequestCultureProviders = [],
        });
    }
}
