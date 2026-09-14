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

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("/api/embed", new { text }, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var vector = json.RootElement.GetProperty("embedding").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (vector.Length != 768) throw new InvalidOperationException("Embedding provider must produce 768 dimensions.");
        return vector;
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
