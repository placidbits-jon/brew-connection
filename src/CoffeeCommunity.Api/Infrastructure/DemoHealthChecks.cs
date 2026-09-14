using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CoffeeCommunity.Api.Infrastructure;

public sealed class ArcadeDbHealthCheck(ArcadeDbClient arcadeDb) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await arcadeDb.ServerReadyAsync(cancellationToken)
                ? HealthCheckResult.Healthy("ArcadeDB HTTP endpoint is ready.")
                : HealthCheckResult.Unhealthy("ArcadeDB HTTP endpoint is unavailable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("ArcadeDB readiness check failed.", exception);
        }
    }
}

public sealed class SchemaHealthCheck(DemoReadiness readiness) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(readiness.SchemaReady
            ? HealthCheckResult.Healthy("Foundation schema is ready.")
            : HealthCheckResult.Unhealthy("Foundation schema is still starting."));
}

public sealed class EmbeddingHealthCheck(EmbeddingClient embedding) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await embedding.ReadyAsync(cancellationToken)
            ? HealthCheckResult.Healthy("Local embedding endpoint is ready.")
            : HealthCheckResult.Unhealthy("Local embedding endpoint is unavailable.");
}
