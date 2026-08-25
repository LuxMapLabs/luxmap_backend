using LuxMap.Modules.Admin;
using LuxMap.Modules.Assets;
using LuxMap.Modules.Faults;
using LuxMap.Modules.Identity;
using LuxMap.Modules.Survey;
using LuxMap.Modules.Telemetry;
using LuxMap.Modules.WorkOrders;
using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Shared.Tests;

/// <summary>BE-01 — mọi module đều cắm được vào seam DI chung.</summary>
public class ModuleRegistrationTests
{
    private static ILuxMapModule[] AllModules() =>
    [
        new IdentityModule(),
        new AssetsModule(),
        new SurveyModule(),
        new FaultsModule(),
        new WorkOrdersModule(),
        new TelemetryModule(),
        new AdminModule(),
    ];

    [Fact]
    public void Every_module_registers_without_throwing()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddLuxMapModules(configuration, AllModules());

        Assert.NotNull(services.BuildServiceProvider());
    }

    [Fact]
    public void Module_names_are_unique()
    {
        var names = AllModules().Select(m => m.Name).ToArray();

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void All_seven_domain_modules_are_present()
    {
        string[] expected = ["Identity", "Assets", "Survey", "Faults", "WorkOrders", "Telemetry", "Admin"];

        Assert.Equal(expected, AllModules().Select(m => m.Name));
    }
}
