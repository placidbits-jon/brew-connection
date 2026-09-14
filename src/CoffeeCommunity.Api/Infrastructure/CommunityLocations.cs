using System.Text.Json;

namespace CoffeeCommunity.Api.Infrastructure;

public sealed class CommunityLocations(ArcadeDbClient db, ArcadeRedisClient redis, CommunityGraphGate gate, DemoReadiness readiness)
{
    private readonly List<DiscoveryQuery> queries = [];
    private static string Str(JsonElement row, string key) => row.TryGetProperty(key, out var value) ? value.ToString() : "";
    private async Task<JsonElement[]> Query(string label, string sql, object? parameters, CancellationToken ct, bool explain = false)
    {
        using var result = await db.QueryAsync("sql", sql, parameters, ct);
        string? plan = null;
        if (explain)
        {
            using var explanation = await db.QueryAsync("sql", "EXPLAIN " + sql, parameters, ct);
            plan = explanation.RootElement.TryGetProperty("explain", out var value) ? value.ToString() : explanation.RootElement.ToString();
        }
        queries.Add(new(label, "sql", sql, parameters, plan));
        return result.RootElement.GetProperty("result").EnumerateArray().Select(x => x.Clone()).ToArray();
    }
    private async Task<object> Read(Func<Task<object>> action, CancellationToken ct)
    {
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            if (!readiness.SchemaReady) throw new GraphRequestException(503, "Demo data is not ready.");
            return await action();
        }
        finally { gate.Semaphore.Release(); }
    }
    public Task<object> Lookup(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 100) throw new GraphRequestException(400, "Enter a badge or short code of 1–100 characters.");
        return Read(async () =>
        {
            var rows = await Query("Persistent exact key lookup", "SELECT slug, kind, target.slug AS targetSlug, target.name AS targetName, target.@type AS targetType FROM BadgeLookup WHERE slug=:code", new { code }, ct, true);
            if (rows.Length == 0) throw new GraphRequestException(404, "Badge or short code not found.");
            var row = rows[0]; var type = Str(row, "targetType"); var slug = Str(row, "targetSlug");
            var route = type == "Person" ? "/demo/passport/" + Uri.EscapeDataString(slug) : "/demo/coffee/" + Uri.EscapeDataString(slug);
            return new { code, kind = Str(row, "kind"), persistent = true, target = new { slug, name = Str(row, "targetName"), type, route }, queries };
        }, ct);
    }
    public Task<object> Counter(bool increment, CancellationToken ct) => Read(async () =>
    {
        var events = await Query("Stable event record", "SELECT slug, name FROM Event WHERE slug=:slug", new { slug = "brew-connection-2026" }, ct);
        var value = await redis.Counter(increment, ct);
        queries.Add(new("ArcadeDB Redis commands over HTTP; server RAM only", "redis", (increment ? "INCR " : "GET ") + ArcadeRedisClient.CounterKey, null));
        return new { key = ArcadeRedisClient.CounterKey, value, delta = increment ? 1 : 0, transient = true, restartBehavior = "Stored only in ArcadeDB server memory. Resets to zero when the database container restarts; persistent badge and short-code records survive.", @event = events.Single(), queries };
    }, ct);
    public Task<object> Map(double latitude, double longitude, double radius, string area, CancellationToken ct)
    {
        if (!double.IsFinite(latitude) || latitude < -90 || latitude > 90 || !double.IsFinite(longitude) || longitude < -180 || longitude > 180)
            throw new GraphRequestException(400, "Latitude must be −90 to 90 and longitude −180 to 180, both finite.");
        if (!double.IsFinite(radius) || radius <= 0 || radius > 10000) throw new GraphRequestException(400, "Radius must be greater than zero and at most 10,000 meters.");
        if (string.IsNullOrWhiteSpace(area) || area.Length > 100) throw new GraphRequestException(400, "Choose a venue area.");
        return Read(async () =>
        {
            var areas = await Query("Selected venue boundary", "SELECT slug, name, coords, boundary FROM VenueArea WHERE slug=:area", new { area }, ct);
            if (areas.Length == 0) throw new GraphRequestException(404, "Venue area not found.");
            var point = FormattableString.Invariant($"POINT({longitude} {latitude})");
            var boundary = Str(areas[0], "boundary");
            var vendors = await Query("Native indexed containment and distance in meters", "SELECT slug, name, coords, areaSlug, available, geo.distance(coords, geo.geomFromText(:point), 'm') AS distanceMeters FROM VendorTable WHERE geo.within(coords, geo.geomFromText(:boundary))=true AND geo.distance(coords, geo.geomFromText(:point), 'm') <= :radius ORDER BY distanceMeters, slug", new { point, boundary, radius }, ct, true);
            var slugs = vendors.Select(v => Str(v, "slug")).ToArray();
            var offers = slugs.Length == 0 ? [] : await Query("Graph-linked coffees sold at nearby tables", "SELECT @out.slug AS vendorSlug, @in.slug AS slug, @in.name AS name FROM SELLS WHERE @out.slug IN :slugs", new { slugs }, ct);
            var results = vendors.Select(v => new { slug = Str(v, "slug"), name = Str(v, "name"), coords = Str(v, "coords"), distanceMeters = v.GetProperty("distanceMeters").GetDouble(), contained = true, areaSlug = Str(v, "areaSlug"), available = v.GetProperty("available").GetBoolean(), coffees = offers.Where(o => Str(o, "vendorSlug") == Str(v, "slug")).Select(o => new { slug = Str(o, "slug"), name = Str(o, "name"), route = "/demo/coffee/" + Uri.EscapeDataString(Str(o, "slug")) }).ToArray() }).ToArray();
            return new { latitude, longitude, radius, area = areas[0], results, queries, distanceModel = "ArcadeDB geo.distance in meters; geo.within checks the stored area polygon. All three seeded areas share the convention boundary." };
        }, ct);
    }
}

public static class CommunityLocationsEndpoints
{
    public static void MapCommunityLocations(this WebApplication app)
    {
        var group = app.MapGroup("/api/demo");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (GraphRequestException error) { return Results.Json(new { message = error.Message }, statusCode: error.Status); }
            catch (Exception error) when (error is IOException or HttpRequestException || error is OperationCanceledException && !context.HttpContext.RequestAborted.IsCancellationRequested) { context.HttpContext.RequestServices.GetRequiredService<ILogger<ArcadeRedisClient>>().LogWarning(error, "ArcadeDB Redis counter request failed"); return Results.Json(new { message = "ArcadeDB Redis counter is unavailable. A lost response may have incremented the value; refresh before trying again." }, statusCode: 503); }
        });
        group.MapGet("/lookup", (string? code, CommunityLocations service, CancellationToken ct) => service.Lookup(code ?? "badge-0001", ct));
        group.MapGet("/counter", (CommunityLocations service, CancellationToken ct) => service.Counter(false, ct));
        group.MapPost("/counter", (CommunityLocations service, CancellationToken ct) => service.Counter(true, ct));
        group.MapGet("/map", (double? latitude, double? longitude, double? radius, string? area, CommunityLocations service, CancellationToken ct) => service.Map(latitude ?? 42.3314, longitude ?? -83.0458, radius ?? 100, area ?? "pour-over-bar", ct));
    }
}
