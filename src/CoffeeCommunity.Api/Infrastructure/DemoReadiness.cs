namespace CoffeeCommunity.Api.Infrastructure;

public sealed class DemoReadiness
{
    private volatile bool _schemaReady;
    private volatile bool _seedReady;
    private volatile string? _error;

    public bool SchemaReady => _schemaReady;

    public void Starting()
    {
        _schemaReady = false;
        _seedReady = false;
        _error = null;
    }

    public void Ready()
    {
        _schemaReady = true;
        _seedReady = true;
        _error = null;
    }

    public void Failed(Exception exception) => _error = exception.Message;

    public object Snapshot(bool databaseReady, bool embeddingReady) => new
    {
        application = "Coffee Community",
        arcadeDbVersion = "26.9.1",
        process = "ready",
        database = databaseReady ? "ready" : "starting",
        schema = _schemaReady ? "ready" : "starting",
        seed = _seedReady ? "ready" : "starting",
        embedding = embeddingReady ? "ready" : "unavailable",
        embeddingProvider = "ollama",
        embeddingModel = "embeddinggemma:300m",
        error = _error,
        demoRoutes = new[]
        {
            "/demo/story", "/demo/passport/maya-chen", "/demo/discover", "/demo/network/maya-chen", "/demo/games/maya-chen",
            "/demo/brews/blueberry-bloom-v60", "/demo/pulse", "/demo/map", "/demo/transactions", "/demo/lab"
        }
    };
}
