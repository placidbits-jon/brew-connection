using System.Net.Http.Json;
namespace CoffeeCommunity.TelemetrySimulator;

public sealed class Worker(IHttpClientFactory clients, ILogger<Worker> logger) : BackgroundService
{
    private sealed record Run(string RunId, string BrewSlug);
    private sealed record Pending(Run[] Runs);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var http = clients.CreateClient("api");
                var pending = await http.GetFromJsonAsync<Pending>("/api/demo/telemetry/pending", stoppingToken);
                foreach (var run in pending?.Runs ?? [])
                {
                    for (var offset = 0; offset < 60; offset += 10)
                    {
                        var samples = Enumerable.Range(offset, 10).Select(second => new { second, waterGrams = second * 4.0, flowRate = run.BrewSlug == "blueberry-bloom-v60" && second == 30 ? 12.0 : 4.0, temperatureC = 93 - second * .02 });
                        using var response = await http.PostAsJsonAsync($"/api/demo/telemetry/runs/{run.RunId}/ingest", new { samples }, stoppingToken);
                        response.EnsureSuccessStatusCode();
                        await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
                    }
                    logger.LogInformation("Replayed 60 deterministic samples for {Brew} as {Run}.", run.BrewSlug, run.RunId);
                }
            }
            catch (HttpRequestException error) { logger.LogWarning("Telemetry API is temporarily unavailable: {Message}", error.Message); }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { logger.LogWarning("Telemetry API request timed out; pending runs will be retried."); }
            catch (System.Text.Json.JsonException error) { logger.LogWarning("Telemetry API returned an unreadable response: {Message}", error.Message); }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
