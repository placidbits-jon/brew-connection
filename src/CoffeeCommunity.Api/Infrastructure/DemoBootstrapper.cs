namespace CoffeeCommunity.Api.Infrastructure;

public sealed class DemoBootstrapper(
    ArcadeDbClient arcadeDb,
    DemoReadiness readiness,
    ILogger<DemoBootstrapper> logger) : IHostedService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task StartAsync(CancellationToken cancellationToken) => BootstrapAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            readiness.Starting();
            if (await arcadeDb.DatabaseExistsAsync(cancellationToken))
            {
                await arcadeDb.DropDatabaseAsync(cancellationToken);
            }

            await BootstrapCoreAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            readiness.Failed(exception);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            readiness.Starting();
            await BootstrapCoreAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            readiness.Failed(exception);
            logger.LogError(exception, "ArcadeDB foundation bootstrap failed.");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task BootstrapCoreAsync(CancellationToken cancellationToken)
    {
        if (!await arcadeDb.DatabaseExistsAsync(cancellationToken))
        {
            await arcadeDb.CreateDatabaseAsync(cancellationToken);
        }

        if (!await FoundationMigrationAppliedAsync(cancellationToken))
        {
            const string foundationMigration = """
                CREATE DOCUMENT TYPE SchemaMigration;
                CREATE PROPERTY SchemaMigration.id STRING;
                CREATE PROPERTY SchemaMigration.appliedAt DATETIME;
                CREATE INDEX ON SchemaMigration (id) UNIQUE_HASH;
                CREATE DOCUMENT TYPE EventConfiguration;
                CREATE PROPERTY EventConfiguration.slug STRING;
                CREATE PROPERTY EventConfiguration.name STRING;
                CREATE PROPERTY EventConfiguration.seedProfile STRING;
                CREATE INDEX ON EventConfiguration (slug) UNIQUE_HASH;
                INSERT INTO SchemaMigration SET id = '001-foundation', appliedAt = sysdate();
                """;
            using var _ = await arcadeDb.CommandAsync("sqlscript", foundationMigration, null, cancellationToken);
            logger.LogInformation("Applied ArcadeDB migration 001-foundation.");
        }

        using (var seedStatus = await arcadeDb.QueryAsync(
                   "sql",
                   "SELECT count(*) AS count FROM EventConfiguration WHERE slug = :slug",
                   new { slug = "brew-connection-2026" },
                   cancellationToken))
        {
            var seedExists = seedStatus.RootElement.GetProperty("result")[0].GetProperty("count").GetInt64() == 1;
            if (!seedExists)
            {
                using var _ = await arcadeDb.CommandAsync(
                    "sql",
                    "INSERT INTO EventConfiguration SET slug = :slug, name = :name, seedProfile = :seedProfile",
                    new
                    {
                        slug = "brew-connection-2026",
                        name = "Brew Connection at TechCon",
                        seedProfile = "foundation"
                    },
                    cancellationToken);
                logger.LogInformation("Applied deterministic foundation seed.");
            }
        }

        readiness.Ready();
    }

    private async Task<bool> FoundationMigrationAppliedAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var result = await arcadeDb.QueryAsync(
                "sql",
                "SELECT count(*) AS count FROM SchemaMigration WHERE id = :id",
                new { id = "001-foundation" },
                cancellationToken);
            return result.RootElement.GetProperty("result")[0].GetProperty("count").GetInt64() == 1;
        }
        catch (HttpRequestException exception) when (
            exception.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.InternalServerError)
        {
            return false;
        }
    }
}
