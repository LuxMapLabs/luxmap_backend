using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Shared.Serialization;

public static class JsonConventionExtensions
{
    /// <summary>
    /// Áp quy ước JSON của contract cho CẢ hai đường: minimal API
    /// (<c>Microsoft.AspNetCore.Http.Json.JsonOptions</c>) và MVC controller
    /// (<c>Microsoft.AspNetCore.Mvc.JsonOptions</c>). Hai đường này đọc hai options khác nhau —
    /// chỉ cấu hình một bên thì endpoint kiểu còn lại sẽ âm thầm trả camelCase.
    /// </summary>
    public static IServiceCollection AddLuxMapJsonConventions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.ConfigureHttpJsonOptions(options =>
            LuxMapJsonOptions.Configure(options.SerializerOptions));

        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
            LuxMapJsonOptions.Configure(options.JsonSerializerOptions));

        return services;
    }
}
