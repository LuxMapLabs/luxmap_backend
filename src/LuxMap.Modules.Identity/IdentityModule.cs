using LuxMap.Modules.Identity.Auth;
using LuxMap.Modules.Identity.Seeding;
using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Identity;

/// <summary>
/// Identity module — AppUser, AdministrativeUnit, RefreshToken, JWT, role and territorial
/// authorization (BE-06..BE-08).
/// BE-07 ISSUES access and refresh tokens; VALIDATING them is BE-08's job.
/// </summary>
public sealed class IdentityModule : ILuxMapModule
{
    public string Name => "Identity";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<IdentitySeeder>();

        // Non-secret values come from appsettings; the signing key comes ONLY from the environment.
        var options = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? new JwtOptions { SigningKey = string.Empty };

        options = options with
        {
            SigningKey = Environment.GetEnvironmentVariable(JwtOptions.SigningKeyEnvironmentVariable)
                ?? configuration[$"{JwtOptions.SectionName}:SigningKey"]
                ?? string.Empty,
        };

        // A missing key STOPS startup rather than quietly running with a default.
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AccessTokenIssuer>();
        services.AddScoped<AuthService>();
    }
}
