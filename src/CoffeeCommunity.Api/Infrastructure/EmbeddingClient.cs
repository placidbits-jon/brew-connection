using Microsoft.Extensions.Options;

namespace CoffeeCommunity.Api.Infrastructure;

public sealed class EmbeddingOptions
{
    public const string SectionName = "Embedding";
    public required Uri BaseUrl { get; init; }
}

public sealed class EmbeddingClient
{
    private readonly HttpClient _httpClient;

    public EmbeddingClient(HttpClient httpClient, IOptions<EmbeddingOptions> options)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = options.Value.BaseUrl;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken, string purpose = "document")
    {
        using var response = await _httpClient.PostAsJsonAsync("/api/embed", new { text, purpose }, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var vector = json.RootElement.GetProperty("embedding").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (vector.Length != 768) throw new InvalidOperationException("Embedding provider must produce 768 dimensions.");
        return vector;
    }

    public async Task<float[][]> EmbedBatchAsync(string[] texts, CancellationToken ct)
    {
        using var response = await _httpClient.PostAsJsonAsync("/api/embed/batch", new { texts, purpose = "document" }, ct);
        response.EnsureSuccessStatusCode();
        using var json = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var vectors = json.RootElement.GetProperty("embeddings").EnumerateArray().Select(v => v.EnumerateArray().Select(x => x.GetSingle()).ToArray()).ToArray();
        if (vectors.Length != texts.Length || vectors.Any(v => v.Length != 768 || v.Any(x => !float.IsFinite(x)))) throw new InvalidOperationException("Invalid embedding batch.");
        return vectors;
    }
    public async Task<System.Text.Json.JsonElement> InterpretAsync(string text, CancellationToken ct)
    {
        using var response = await _httpClient.PostAsJsonAsync("/api/interpret", new { text }, ct);
        if (!response.IsSuccessStatusCode) throw new GraphRequestException(503, "The local language helper is unavailable. Use the search box directly.");
        using var json = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return json.RootElement.Clone();
    }

    public async Task<bool> ReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}
