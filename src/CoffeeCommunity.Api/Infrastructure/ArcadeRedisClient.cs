namespace CoffeeCommunity.Api.Infrastructure;

// The HTTP Redis executor shares the server's transient map across requests.
// 26.9.1 TCP connections each own a separate map, so opening a socket per request
// would misleadingly return 1 every time. ArcadeDbClient disables mutation retries.
public sealed class ArcadeRedisClient(ArcadeDbClient database)
{
    public const string CounterKey = "coffee_demo:brew-connection-2026:live-counter";
    public async Task<long> Counter(bool increment, CancellationToken ct)
    {
        using var result = await database.CommandAsync("redis", (increment ? "INCR " : "GET ") + CounterKey, null, ct);
        var rows = result.RootElement.GetProperty("result");
        if (rows.GetArrayLength() == 0) return 0;
        var row = rows[0];
        if (!row.TryGetProperty("value", out var value) || value.ValueKind == System.Text.Json.JsonValueKind.Null) return 0;
        return value.ValueKind == System.Text.Json.JsonValueKind.Number ? value.GetInt64() : long.Parse(value.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
    }
}
