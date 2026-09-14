using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CoffeeCommunity.Api.Infrastructure;

public sealed class ArcadeDbClient
{
    private readonly HttpClient _httpClient;
    private readonly ArcadeDbOptions _options;

    public ArcadeDbClient(HttpClient httpClient, IOptions<ArcadeDbOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress = _options.BaseUrl;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.UserName}:{_options.Password}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<bool> ServerReadyAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("/api/v1/health", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DatabaseExistsAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"/api/v1/exists/{_options.Database}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.TryGetProperty("result", out var result) && result.GetBoolean();
    }

    public async Task CreateDatabaseAsync(CancellationToken cancellationToken) =>
        await ServerCommandAsync($"create database {_options.Database}", cancellationToken);

    public async Task DropDatabaseAsync(CancellationToken cancellationToken) =>
        await ServerCommandAsync($"drop database {_options.Database}", cancellationToken);

    public async Task<JsonDocument> QueryAsync(string language, string command, object? parameters, CancellationToken cancellationToken)
    {
        var payload = new { language, command, @params = parameters };
        using var response = await _httpClient.PostAsJsonAsync($"/api/v1/query/{_options.Database}", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }

    public async Task<JsonDocument> CommandAsync(string language, string command, object? parameters, CancellationToken cancellationToken)
    {
        var payload = new { language, command, @params = parameters };
        using var response = await _httpClient.PostAsJsonAsync($"/api/v1/command/{_options.Database}", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }

    public async Task WriteTimeSeriesAsync(string lines, CancellationToken cancellationToken)
    {
        using var content = new StringContent(lines, Encoding.UTF8, "text/plain");
        using var response = await _httpClient.PostAsync($"/api/v1/ts/{_options.Database}/write?precision=ms", content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<string> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync($"/api/v1/begin/{_options.Database}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return response.Headers.TryGetValues("arcadedb-session-id", out var values)
            ? values.Single()
            : throw new HttpRequestException("ArcadeDB did not return a transaction session id.");
    }

    public Task CommitTransactionAsync(string sessionId, CancellationToken cancellationToken) =>
        CompleteTransactionAsync("commit", sessionId, cancellationToken);

    public Task RollbackTransactionAsync(string sessionId, CancellationToken cancellationToken) =>
        CompleteTransactionAsync("rollback", sessionId, cancellationToken);

    public async Task<JsonDocument> CommandInTransactionAsync(
        string sessionId,
        string language,
        string command,
        object? parameters,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/command/{_options.Database}")
        {
            Content = JsonContent.Create(new { language, command, @params = parameters })
        };
        request.Headers.Add("arcadedb-session-id", sessionId);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }

    private async Task ServerCommandAsync(string command, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("/api/v1/server", new { command }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task CompleteTransactionAsync(string action, string sessionId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/{action}/{_options.Database}");
        request.Headers.Add("arcadedb-session-id", sessionId);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"ArcadeDB returned {(int)response.StatusCode}: {body}", null, response.StatusCode);
    }
}
