using CoffeeCommunity.Api.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddOpenApi();
builder.Services.Configure<ArcadeDbOptions>(builder.Configuration.GetSection(ArcadeDbOptions.SectionName));
builder.Services.Configure<EmbeddingOptions>(builder.Configuration.GetSection(EmbeddingOptions.SectionName));
builder.Services.AddHttpClient<ArcadeDbClient>();
builder.Services.AddHttpClient<EmbeddingClient>();
builder.Services.AddSingleton<DemoReadiness>();
builder.Services.AddSingleton<DemoBootstrapper>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<DemoBootstrapper>());
builder.Services.AddHealthChecks()
    .AddCheck<ArcadeDbHealthCheck>("arcadedb", tags: ["ready"])
    .AddCheck<SchemaHealthCheck>("schema", tags: ["ready"])
    .AddCheck<EmbeddingHealthCheck>("embedding", tags: ["ready"]);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/api/demo/status", async (DemoReadiness readiness, ArcadeDbClient arcadeDb, EmbeddingClient embedding, CancellationToken ct) =>
{
    var databaseReady = await arcadeDb.DatabaseExistsAsync(ct);
    var embeddingReady = await embedding.ReadyAsync(ct);
    return Results.Ok(readiness.Snapshot(databaseReady, embeddingReady));
});

app.MapPost("/api/demo/reset", async (DemoBootstrapper bootstrapper, CancellationToken ct) =>
{
    await bootstrapper.ResetAsync(ct);
    return Results.Ok(new { message = "Demo data reset to the deterministic foundation state." });
});

app.MapDefaultEndpoints();
app.Run();
