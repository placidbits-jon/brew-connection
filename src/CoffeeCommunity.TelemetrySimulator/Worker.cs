namespace CoffeeCommunity.TelemetrySimulator;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Telemetry simulator is idle until the brew demo is activated.");
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
