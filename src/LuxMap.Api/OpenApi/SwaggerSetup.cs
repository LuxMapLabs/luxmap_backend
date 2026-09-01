using LuxMap.Shared.Contracts;
using Microsoft.OpenApi;

namespace LuxMap.Api.OpenApi;

public static class SwaggerSetup
{
    public const string DocumentName = "v1";

    /// <summary>Toggled by the <c>Swagger:Enabled</c> setting. OFF by default (appsettings.json);
    /// only appsettings.Development.json turns it on.</summary>
    public static bool SwaggerEnabled(this IConfiguration configuration)
        => configuration.GetValue("Swagger:Enabled", defaultValue: false);

    public static IServiceCollection AddLuxMapSwagger(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(DocumentName, new OpenApiInfo
            {
                Title = "LuxMap API",
                Version = "v1",
                Description =
                    "WP2 — GIS + IoT + Computer Vision platform for rural street-lighting assets. "
                    + $"Base URL {ApiRoutes.BasePath}. Conventions from API Contract v1.1: snake_case JSON, "
                    + "lowercase string enums, ISO 8601 UTC timestamps with a Z suffix, EPSG:4326 coordinates.",
            });

            AddBearerSecurity(options);

            // Contract section 0 distinguishes two time types. Declare them explicitly rather than
            // trusting Swashbuckle to infer correctly: getting this wrong makes FM-04 generate
            // Instant where LocalDate belongs.
            options.MapType<DateTime>(() => new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Format = "date-time",
                Description = "ISO 8601 UTC with a Z suffix. Example: 2026-08-20T04:00:00Z.",
            });
            options.MapType<DateTime?>(() => new OpenApiSchema
            {
                Type = JsonSchemaType.String | JsonSchemaType.Null,
                Format = "date-time",
            });
            options.MapType<DateOnly>(() => new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Format = "date",
                Description = "Date without a time component, YYYY-MM-DD. Example: 2023-01-04.",
            });
            options.MapType<DateOnly?>(() => new OpenApiSchema
            {
                Type = JsonSchemaType.String | JsonSchemaType.Null,
                Format = "date",
            });

            options.DocumentFilter<ContractEnumDocumentFilter>();
            options.SupportNonNullableReferenceTypes();
        });

        return services;
    }

    private static void AddBearerSecurity(Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions options)
    {
        const string schemeId = "Bearer";

        // BE-05 only declares the security scheme for the docs and the Authorize button.
        // Token validation arrives with BE-08.
        options.AddSecurityDefinition(schemeId, new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste the JWT here WITHOUT the 'Bearer ' prefix.",
        });

        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(schemeId, document)] = [],
        });
    }

    public static WebApplication UseLuxMapSwagger(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Configuration.SwaggerEnabled())
        {
            return app;
        }

        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint($"/swagger/{DocumentName}/swagger.json", "LuxMap API v1");
            options.DocumentTitle = "LuxMap API v1";
        });

        return app;
    }
}
