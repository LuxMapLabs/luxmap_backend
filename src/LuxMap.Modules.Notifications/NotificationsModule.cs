using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Notifications;

/// <summary>
/// Notifications module — in-app notices read by polling (BE-27). Owns the <c>notification</c> table;
/// WorkOrders, Survey and Faults stage notices through <see cref="Notifier"/> inside their own saves.
/// </summary>
public sealed class NotificationsModule : ILuxMapModule
{
    public string Name => "Notifications";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
        => services.AddScoped<NotificationService>();
}
