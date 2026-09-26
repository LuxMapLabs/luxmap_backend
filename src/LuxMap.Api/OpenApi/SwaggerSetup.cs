using LuxMap.Modules.Identity.Auth.Web;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
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
            AddRefreshTokenCookieSecurity(options);
            options.OperationFilter<RefreshTokenCookieOperationFilter>();
            options.OperationFilter<CapabilityOperationFilter>();

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

            options.SchemaFilter<JsonElementFieldSchemaFilter>();
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
            Description = BearerDescription(),
        });

        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(schemeId, document)] = [],
        });
    }

    /// <summary>
    /// The access token's authorization claims and the capability matrix, rendered from
    /// <see cref="LuxMapPolicies.Matrix"/> so the spec and the pipeline cannot disagree.
    /// </summary>
    private static string BearerDescription()
    {
        var matrix = string.Join("; ", LuxMapPolicies.Matrix.Select(entry =>
            $"{entry.Key} = {string.Join(", ", entry.Value.Select(ContractEnum.ToDbValue))}"));

        return "Paste the JWT here WITHOUT the 'Bearer ' prefix. "
            + "Claims: role (one of superior, manager, field_engineer, system_admin — Contract v1.7 section 3.1) "
            + "and commune_ids (always an array; [\"*\"] for system_admin only). "
            + "Every business operation requires one capability, named in x-luxmap-capability, admitting exactly "
            + "the roles in x-luxmap-roles — no ranking between roles. "
            + $"Matrix: {matrix}. "
            + "401 UNAUTHENTICATED: token missing, invalid or expired. "
            + "403 ROLE_FORBIDDEN: the role is not in the capability's list. "
            + "403 COMMUNE_FORBIDDEN: a commune_id outside the caller's scope, or [\"*\"] on a role other than system_admin.";
    }

    /// <summary>
    /// Declared here, applied per operation by <see cref="RefreshTokenCookieOperationFilter"/>
    /// (Contract section 2.10.3).
    /// </summary>
    private static void AddRefreshTokenCookieSecurity(Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions options)
    {
        options.AddSecurityDefinition(RefreshTokenCookieOperationFilter.SchemeId, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Cookie,
            Name = RefreshTokenCookie.Name,
            Description = "The web refresh token. HttpOnly: set by /api/v1/auth/web/login and /refresh, sent "
                + "back by the browser, never readable from JavaScript.",
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
