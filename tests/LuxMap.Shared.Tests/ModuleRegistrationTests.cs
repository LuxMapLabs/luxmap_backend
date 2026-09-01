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

/// <summary>BE-01 — every module plugs into the shared DI seam.</summary>
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

    /// <summary>
    /// The Identity module DELIBERATELY throws when the JWT signing key is missing (BE-07 fail-fast),
    /// so the test supplies a dummy key — exactly as the real host reads one from .env.
    /// </summary>
    private static IConfiguration ConfigurationWithSigningKey()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "dummy-key-used-only-in-tests-longer-than-32-bytes",
            })
            .Build();

    [Fact]
    public void Every_module_registers_without_throwing()
    {
        var services = new ServiceCollection();

        services.AddLuxMapModules(ConfigurationWithSigningKey(), AllModules());

        Assert.NotNull(services.BuildServiceProvider());
    }

    [Fact]
    public void Identity_module_refuses_to_start_without_a_jwt_signing_key()
    {
        var services = new ServiceCollection();
        var withoutKey = new ConfigurationBuilder().Build();

        var error = Assert.Throws<InvalidOperationException>(
            () => services.AddLuxMapModules(withoutKey, AllModules()));

        Assert.Contains("JWT_SIGNING_KEY", error.Message, StringComparison.Ordinal);
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
