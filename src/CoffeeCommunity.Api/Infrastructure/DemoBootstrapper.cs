namespace CoffeeCommunity.Api.Infrastructure;

public sealed class DemoBootstrapper(
    ArcadeDbClient arcadeDb,
    EmbeddingClient embedding,
    DemoReadiness readiness,
    ILogger<DemoBootstrapper> logger) : IHostedService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public Task StartAsync(CancellationToken cancellationToken) => RunAsync(null, cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ResetAsync(CancellationToken cancellationToken) => ResetAsync("story", cancellationToken);
    public Task ResetAsync(string profile, CancellationToken cancellationToken)
    {
        if (profile is not ("story" or "scale")) throw new ArgumentException("Seed profile must be story or scale.", nameof(profile));
        return RunAsync(profile, cancellationToken);
    }

    private async Task RunAsync(string? resetProfile, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            readiness.Starting();
            var exists = await arcadeDb.DatabaseExistsAsync(cancellationToken);
            var profile = resetProfile ?? "story";
            if (exists && resetProfile is null)
            {
                using var types = await arcadeDb.QueryAsync("sql", "SELECT name FROM schema:types", null, cancellationToken);
                if (types.RootElement.GetProperty("result").EnumerateArray().Any(t => t.GetProperty("name").GetString() == "EventConfiguration"))
                {
                    using var status = await arcadeDb.QueryAsync("sql", "SELECT FROM EventConfiguration WHERE slug = 'brew-connection-2026'", null, cancellationToken);
                    var records = status.RootElement.GetProperty("result");
                    if (records.GetArrayLength() > 0)
                    {
                        var record = records[0];
                        if (record.TryGetProperty("seedProfile", out var priorProfile) && priorProfile.GetString() == "scale") profile = "scale";
                        if (record.TryGetProperty("seedVersion", out var version) && version.GetString() == CommunitySchema.Version && record.TryGetProperty("seedComplete", out var complete) && complete.GetBoolean())
                        {
                            readiness.Ready();
                            return;
                        }
                    }
                }
            }
            // Seed marker is written before ingestion. Any interrupted seed is rebuilt on restart;
            // no partial graph, duplicate telemetry, or stale index can be mistaken for ready data.
            if (exists) await arcadeDb.DropDatabaseAsync(cancellationToken);
            await arcadeDb.CreateDatabaseAsync(cancellationToken);
            using (await arcadeDb.CommandAsync("sqlscript", CommunitySchema.Build(), null, cancellationToken)) { }
            using (await arcadeDb.CommandAsync("sql", "INSERT INTO EventConfiguration SET slug='brew-connection-2026', name='Brew Connection at TechCon', seedProfile=:profile, seedVersion=:version, seedComplete=false", new { profile, version = CommunitySchema.Version }, cancellationToken)) { }
            await new CommunitySeeder(arcadeDb, embedding, logger).SeedAsync(profile, cancellationToken);
            readiness.Ready();
        }
        catch (Exception exception)
        {
            readiness.Failed(exception);
            logger.LogError(exception, "Community schema/seed failed; restart or reset will rebuild incomplete data.");
            throw;
        }
        finally { _gate.Release(); }
    }
}
