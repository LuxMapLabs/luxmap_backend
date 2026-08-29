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

    /// <summary>
    /// Module Identity CỐ Ý ném lỗi khi thiếu khoá ký JWT (BE-07 fail-fast), nên test phải cấp
    /// khoá giả — đúng như host thật đọc từ .env.
    /// </summary>
    private static IConfiguration ConfigurationWithSigningKey()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "khoa-gia-chi-dung-trong-test-dai-hon-32-byte",
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
