using CoffeeCommunity.Api.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddOpenApi();
builder.Services.Configure<ArcadeDbOptions>(builder.Configuration.GetSection(ArcadeDbOptions.SectionName));
builder.Services.Configure<EmbeddingOptions>(builder.Configuration.GetSection(EmbeddingOptions.SectionName));
// Retrying a POST can duplicate committed edges or telemetry after a lost response.
#pragma warning disable EXTEXP0001 // Scoped opt-out for non-idempotent database commands.
builder.Services.AddHttpClient<ArcadeDbClient>().RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<EmbeddingClient>(client => client.Timeout = TimeSpan.FromMinutes(10)).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddSingleton<DemoReadiness>();
builder.Services.AddSingleton<DemoBootstrapper>();
builder.Services.AddTransient<DemoExplorer>();
builder.Services.AddSingleton<CommunityGraphGate>();
builder.Services.AddTransient<CommunityGraph>();
builder.Services.AddTransient<CommunityDocuments>();
builder.Services.AddTransient<CommunityDiscovery>();
builder.Services.AddTransient<ArcadeRedisClient>();
builder.Services.AddTransient<CommunityLocations>();
builder.Services.AddTransient<CommunityTelemetry>();
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

app.MapPost("/api/demo/reset", async (string? profile, DemoBootstrapper bootstrapper, CancellationToken ct) =>
{
    var selectedProfile = profile ?? "story";
    if (selectedProfile is not ("story" or "scale"))
        return Results.BadRequest(new { message = "Profile must be story or scale." });
    await bootstrapper.ResetAsync(selectedProfile, ct);
    return Results.Ok(new { message = $"Demo data reset to the deterministic {selectedProfile} state.", profile = selectedProfile });
});

app.MapGet("/api/demo/schema", async (DemoExplorer explorer, DemoReadiness readiness, CancellationToken ct) =>
{
    if (!readiness.SchemaReady) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    return Results.Ok(await explorer.SchemaAsync(ct));
});

app.MapGet("/api/demo/story", async (string? persona, DemoExplorer explorer, DemoReadiness readiness, CancellationToken ct) =>
{
    if (!readiness.SchemaReady) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    var story = await explorer.StoryAsync(persona ?? "maya", ct);
    return story is null ? Results.NotFound(new { message = "Persona not found." }) : Results.Ok(story);
});

app.MapCommunityGraph();
app.MapCommunityDocuments();
app.MapCommunityDiscovery();
app.MapCommunityLocations();
app.MapCommunityTelemetry();
app.MapDefaultEndpoints();
app.Run();
