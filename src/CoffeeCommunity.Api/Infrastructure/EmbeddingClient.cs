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
