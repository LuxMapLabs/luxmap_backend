using System.Globalization;
using System.Net;
using System.Text.Json;
using LuxMap.Api.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

/// <summary>
/// <see cref="InvariantRequestCulture"/>: a multipart number reads the same on a vi-VN Windows host as on the Linux VPS.
/// The host here starts every request under vi-VN — where '.' is the thousands separator — before the API's own pipeline runs.
/// </summary>
public sealed class InvariantRequestCultureTests
{
    [Fact]
    public async Task A_form_number_with_a_decimal_point_reads_as_written_on_a_vi_VN_host()
    {
        Assert.Equal(10.8231234, await PostAsync("10.8231234"));
    }

    [Fact]
    public async Task The_client_language_does_not_change_how_a_form_number_reads()
    {
        Assert.Equal(106.6297123, await PostAsync("106.6297123", acceptLanguage: "vi-VN"));
    }

    /// <summary>
    /// A comma is not a decimal sign on the wire: it reads as a thousands separator, so "10,82" is 1082 and a coordinate
    /// range check refuses it. The web sent this until 26/11/2026 — every citizen report with a position got 400.
    /// </summary>
    [Fact]
    public async Task A_form_number_with_a_decimal_comma_is_not_read_as_a_fraction()
    {
        Assert.Equal(1082, await PostAsync("10,82"));
    }

    private static async Task<double?> PostAsync(string value, string? acceptLanguage = null)
    {
        await using var host = new ViVnHostFactory();
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/_test/form-number")
        {
            Content = new MultipartFormDataContent { { new StringContent(value), "value" } },
        };
        if (acceptLanguage is not null) request.Headers.AcceptLanguage.ParseAdd(acceptLanguage);

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var read = body.RootElement.GetProperty("value");
        return read.ValueKind == JsonValueKind.Null ? null : read.GetDouble();
    }

    private sealed class ViVnHostFactory : NoDatabaseTestHost
    {
        protected override void ConfigureHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseTestCorsOrigin();
            builder.ConfigureServices(services =>
            {
                services.AddControllers().AddApplicationPart(typeof(TestEndpointsController).Assembly);
                services.AddTransient<IStartupFilter, ViVnMachineCulture>();
            });
        }
    }

    /// <summary>Runs ahead of the API's pipeline: what a Windows machine set to Vietnamese gives every request.</summary>
    private sealed class ViVnMachineCulture : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, inner) =>
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("vi-VN");
                await inner(context);
            });
            next(app);
        };
    }
}
