using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Shared.Serialization;

public static class JsonConventionExtensions
{
    /// <summary>
    /// Applies the Contract's JSON conventions to BOTH pipelines: minimal APIs
    /// (<c>Microsoft.AspNetCore.Http.Json.JsonOptions</c>) and MVC controllers
    /// (<c>Microsoft.AspNetCore.Mvc.JsonOptions</c>). They read two different options objects —
    /// configuring only one silently leaves the other returning camelCase.
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
