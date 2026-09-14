using CoffeeCommunity.Embedding;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<LocalModels>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<LocalModels>());
builder.Services.AddHealthChecks().AddCheck<LocalModelsHealthCheck>("embedding-model");
var app = builder.Build();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.MapGet("/api/models", (LocalModels models) => Results.Ok(models.Status));
app.MapPost("/api/embed", async (EmbeddingRequest request, LocalModels models, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 8000 || !ValidPurpose(request.Purpose))
        return Results.BadRequest(new { error = "Provide 1–8000 characters and purpose query or document." });
    if (!models.EmbeddingReady) return Results.StatusCode(503);
    var vectors = await models.EmbedAsync([request.Text], request.Purpose, cancellationToken);
    return Results.Ok(new { embedding = vectors[0], dimensions = 768, provider = "ollama", model = LocalModels.EmbeddingModel, digest = LocalModels.EmbeddingDigest });
});
app.MapPost("/api/embed/batch", async (EmbeddingBatchRequest request, LocalModels models, CancellationToken cancellationToken) =>
{
    if (request.Texts is null || request.Texts.Length is < 1 or > 64 || request.Texts.Any(text => string.IsNullOrWhiteSpace(text) || text.Length > 8000) || !ValidPurpose(request.Purpose))
        return Results.BadRequest(new { error = "Provide 1–64 nonempty texts (maximum 8000 characters each) and purpose query or document." });
    if (!models.EmbeddingReady) return Results.StatusCode(503);
    var vectors = await models.EmbedAsync(request.Texts, request.Purpose, cancellationToken);
    return Results.Ok(new { embeddings = vectors, dimensions = 768, provider = "ollama", model = LocalModels.EmbeddingModel, digest = LocalModels.EmbeddingDigest });
});
app.MapPost("/api/interpret", async (EmbeddingRequest request, LocalModels models, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 500) return Results.BadRequest(new { error = "Provide 1–500 characters." });
    if (!models.InterpreterReady) return Results.StatusCode(503);
    return Results.Ok(new { query = await models.InterpretAsync(request.Text, cancellationToken), model = LocalModels.InterpreterModel, provider = "ollama" });
});
app.MapDefaultEndpoints();
app.Run();
static bool ValidPurpose(string? purpose) => purpose is null or "document" or "query";
public sealed record EmbeddingRequest(string Text, string? Purpose = null);
public sealed record EmbeddingBatchRequest(string[] Texts, string? Purpose = null);
