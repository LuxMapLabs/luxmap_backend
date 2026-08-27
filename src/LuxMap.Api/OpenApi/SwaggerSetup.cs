using LuxMap.Shared.Contracts;
using Microsoft.OpenApi;

namespace LuxMap.Api.OpenApi;

public static class SwaggerSetup
{
    public const string DocumentName = "v1";

    /// <summary>Bật/tắt qua cấu hình <c>Swagger:Enabled</c>. Mặc định TẮT (appsettings.json),
    /// chỉ appsettings.Development.json bật lên.</summary>
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
                    "WP2 — GIS + IoT + Computer Vision quản lý chiếu sáng đường nông thôn. "
                    + $"Base URL {ApiRoutes.BasePath}. Quy ước theo API Contract v1.1: JSON snake_case, "
                    + "enum là chuỗi thường, thời gian ISO 8601 UTC hậu tố Z, toạ độ EPSG:4326.",
            });

            AddBearerSecurity(options);

            // Contract mục 0 phân biệt hai kiểu thời gian. Khai tường minh thay vì tin
            // Swashbuckle suy đúng: sai chỗ này thì FM-04 sinh Instant thay cho LocalDate.
            options.MapType<DateTime>(() => new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Format = "date-time",
                Description = "ISO 8601 UTC, hậu tố Z. Ví dụ 2026-08-20T04:00:00Z.",
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
                Description = "Ngày không giờ, YYYY-MM-DD. Ví dụ 2023-01-04.",
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

        // BE-05 chỉ khai security scheme cho tài liệu và nút Authorize trên UI.
        // Chưa validate token — đó là BE-07.
        options.AddSecurityDefinition(schemeId, new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Dán JWT vào đây, KHÔNG kèm tiền tố 'Bearer '.",
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
