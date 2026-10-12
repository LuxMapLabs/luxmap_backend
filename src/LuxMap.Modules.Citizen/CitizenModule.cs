using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Citizen;

/// <summary>CITIZEN-QR — QR codes on poles (P2a); citizen reports and their review come in P2b / P2c (D-19).</summary>
public sealed class CitizenModule : ILuxMapModule
{
    public string Name => "Citizen";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<QrCodeService>();
    }
}
