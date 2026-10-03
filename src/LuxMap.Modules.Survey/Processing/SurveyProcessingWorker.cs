using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LuxMap.Modules.Survey.Processing;

public sealed class SurveyProcessingWorker(SurveyProcessor processor, IOptions<SurveyProcessingOptions> options,
    ILogger<SurveyProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await processor.ProcessOneAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Survey worker iteration failed; lease recovery will retry."); }
            await Task.Delay(TimeSpan.FromSeconds(options.Value.PollSeconds), stoppingToken);
        }
    }
}
