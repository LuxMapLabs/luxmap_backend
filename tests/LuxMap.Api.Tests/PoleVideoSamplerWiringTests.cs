using LuxMap.Modules.Survey.Processing.Frames;
using LuxMap.Shared.Media;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LuxMap.Api.Tests;

/// <summary>POLE-VIDEO PV-b: the host wires the sampler port to Survey's implementation and refuses impossible settings at startup.</summary>
public sealed class PoleVideoSamplerWiringTests
{
    private sealed class SettingsFactory(IReadOnlyDictionary<string, string> settings) : NoDatabaseTestHost
    {
        protected override void ConfigureHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
        }
    }

    private const string Origin = "https://localhost:3000";

    [Fact]
    public void The_port_resolves_to_the_survey_sampler_with_default_options_and_one_shared_instance()
    {
        using var host = new SettingsFactory(new Dictionary<string, string> { ["Cors:AllowedOrigins:0"] = Origin });
        host.CreateClient();

        var sampler = host.Services.GetRequiredService<IClipFrameSampler>();
        Assert.IsType<FfmpegClipFrameSampler>(sampler);
        Assert.Same(sampler, host.Services.GetRequiredService<IClipFrameSampler>());
        var options = host.Services.GetRequiredService<IOptions<PoleVideoOptions>>().Value;
        Assert.Equal(12, options.MaximumFrames);
        Assert.Equal(1, options.MaximumConcurrentClips);
    }

    [Fact]
    public void Settings_come_from_the_PoleVideo_section()
    {
        using var host = new SettingsFactory(new Dictionary<string, string>
            { ["Cors:AllowedOrigins:0"] = Origin, ["PoleVideo:MaximumFrames"] = "6", ["PoleVideo:MaximumSeconds"] = "20" });
        host.CreateClient();

        var options = host.Services.GetRequiredService<IOptions<PoleVideoOptions>>().Value;
        Assert.Equal((6, 20), (options.MaximumFrames, options.MaximumSeconds));
    }

    [Theory]
    [InlineData("PoleVideo:MaximumFrames", "0")]
    [InlineData("PoleVideo:MinimumSeconds", "40")]
    [InlineData("PoleVideo:FramesPerSecond", "NaN")]
    public void Startup_stops_on_an_impossible_setting(string key, string value)
    {
        using var host = new SettingsFactory(new Dictionary<string, string> { ["Cors:AllowedOrigins:0"] = Origin, [key] = value });

        var exception = Record.Exception(() => host.CreateClient());

        Assert.NotNull(exception);
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException) messages.Add(current.Message);
        Assert.Contains("Invalid PoleVideo options", string.Join(" | ", messages), StringComparison.Ordinal);
    }
}
