using System.Diagnostics;
using System.Text.Json;

namespace CoffeeCommunity.Api.Infrastructure;

public sealed record LabExample(string Id, string Label, string Language, string Command, object? Parameters, string PlanStatus, string Description);
public sealed record LabPlan(string Status, string? Command, string? Text);
public sealed record LabResult(string Id, string Label, string Language, string Command, object? Parameters, JsonElement[] Records, int RecordCount, double ExecutionMs, LabPlan Plan, string? EmptyMessage, LabResult[]? Checks = null, string[]? Limitations = null);

public sealed class CommunityQueryLab(ArcadeDbClient db, EmbeddingClient embeddings, CommunityGraphGate gate, DemoReadiness readiness)
{
    private const string SqlBrew = "SELECT @rid AS rid, slug, name, method FROM Brew WHERE slug=:slug LIMIT 1";
    private const string CypherBrew = "MATCH (b:Brew {slug:$slug}) RETURN elementId(b) AS rid, b.slug AS slug, b.name AS name, b.method AS method LIMIT 1";
    private static readonly string[] Limitations = [
        "These are bounded, curated reads of the running database; they do not establish support for arbitrary queries or every feature of a language.",
        "The compatibility summary does not execute transaction writes, rollback, isolation, durability, retention, or Redis mutation checks. Use the transaction demo and dedicated verification scripts for those checks.",
        "Redis GET runs through ArcadeDB's HTTP command executor and reads transient server memory. An unset key may return no value; no Redis writes are issued.",
        "SQL EXPLAIN text is returned by ArcadeDB. Cypher EXPLAIN is not exposed by this lab; Redis GET has no query execution plan.",
        "The optional transaction examples return no rows until the transaction demo commits its Brew. Reset removes that Brew."
    ];
    private static readonly LabExample[] Examples = [
        new("sql-brew", "Seeded Brew through SQL", "sql", SqlBrew, new { slug = "blueberry-bloom-v60" }, "available", "Read the seeded Brew by its unique slug; compare its RID with Cypher."),
        new("cypher-brew", "The same seeded Brew through Cypher", "cypher", CypherBrew, new { slug = "blueberry-bloom-v60" }, "not-available", "Cypher reads the same persistent vertex and RID as SQL."),
        new("sql-transaction-brew", "Committed Brew through SQL", "sql", SqlBrew, new { slug = "transaction-blueberry-v60" }, "available", "Optional: run the successful transaction first. An empty result means no committed transaction Brew exists."),
        new("cypher-transaction-brew", "Committed Brew through Cypher", "cypher", CypherBrew, new { slug = "transaction-blueberry-v60" }, "not-available", "Optional: inspect the transaction's SQL-created Brew as a graph vertex with the same RID."),
        new("redis-counter", "Transient counter: GET only", "redis", "GET " + ArcadeRedisClient.CounterKey, null, "not-applicable", "Read the counter through ArcadeDB's actual HTTP Redis executor; never increment it."),
        new("fulltext-coffee", "Native Lucene full-text search", "sql", "SELECT slug, name, $score AS score FROM RoastBatch WHERE SEARCH_INDEX('RoastBatch[searchText]', :expression)=true ORDER BY $score DESC, slug LIMIT 10", new { expression = "blueberry" }, "available", "Lucene returns coffee documents and scores for a fixed public-text search."),
        new("vector-coffee", "Native 768-dimensional vector neighbors", "sql", "SELECT record.subjectSlug AS slug, record.subjectType AS type, distance FROM (SELECT expand(vector.neighbors('SearchEmbedding[embedding]', :embedding, :k))) LIMIT 10", new { text = "fruity blueberry floral coffee", purpose = "query", dimensions = 768, k = 10 }, "available", "Generate a local query embedding, then pass its actual 768 values to the native COSINE vector index. Scale aliases may appear."),
        new("timeseries-brew", "Native time-series samples", "sql", "SELECT ts, water_grams, flow_rate, temperature_c FROM BrewTelemetry WHERE ts BETWEEN :from AND :to AND brew_id=:slug AND device=:device ORDER BY ts LIMIT 10", new { from = 1789401600000L, to = 1789401659999L, slug = "blueberry-bloom-v60", device = "scale-0" }, "available", "Read ten authored samples using native time-series tags and a fixed timestamp range."),
        new("geo-vendors", "Native geospatial containment and distance", "sql", "SELECT slug, name, geo.distance(coords, geo.geomFromText(:point), 'm') AS distanceMeters FROM VendorTable WHERE geo.within(coords, geo.geomFromText(:boundary))=true AND geo.distance(coords, geo.geomFromText(:point), 'm') <= :radius ORDER BY distanceMeters, slug LIMIT 10", new { point = "POINT(-83.0458 42.3314)", boundary = "POLYGON((-83.047 42.330,-83.044 42.330,-83.044 42.333,-83.047 42.333,-83.047 42.330))", radius = 100 }, "available", "Use native spatial functions and the geospatial index to find tables within the seeded convention boundary."),
        new("compatibility-summary", "Run read-only compatibility summary", "summary", "Run the seven fixed seeded read examples", null, "not-applicable", "Report actual runtime read results and explicit limitations. No transaction or other write tests run.")
    ];

    public static object Catalog() => new { examples = Examples, limitations = Limitations };

    public async Task<LabResult> Run(string id, CancellationToken ct)
    {
        var example = Examples.SingleOrDefault(x => x.Id == id) ?? throw new GraphRequestException(400, "Choose an example id from the query lab catalog.");
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            if (!readiness.SchemaReady) throw new GraphRequestException(503, "Demo data is not ready.");
            if (id != "compatibility-summary") return await Execute(example, ct);
            var watch = Stopwatch.StartNew();
            var checks = new List<LabResult>();
            foreach (var item in Examples.Where(x => x.Id != "compatibility-summary" && !x.Id.Contains("transaction-brew", StringComparison.Ordinal)))
                checks.Add(await Execute(item, ct));
            var records = checks.Select(x => JsonSerializer.SerializeToElement(new { id = x.Id, status = "read-completed", recordCount = x.RecordCount, executionMs = x.ExecutionMs })).ToArray();
            return new(example.Id, example.Label, example.Language, example.Command, null, records, records.Length, watch.Elapsed.TotalMilliseconds, new("not-applicable", null, "Inspect each read's plan below."), null, checks.ToArray(), Limitations);
        }
        finally { gate.Semaphore.Release(); }
    }

    private async Task<LabResult> Execute(LabExample example, CancellationToken ct)
    {
        object? parameters = example.Parameters;
        // No caller-controlled language, command, embedding, or parameter is accepted.
        if (example.Id == "vector-coffee")
        {
            var embedding = await embeddings.EmbedAsync("fruity blueberry floral coffee", ct, "query");
            if (embedding.Length != 768 || embedding.Any(x => !float.IsFinite(x))) throw new GraphRequestException(503, "The local embedding service returned an invalid vector.");
            parameters = new { embedding, k = 10 };
        }
        var watch = Stopwatch.StartNew();
        using var result = example.Language == "redis"
            ? await db.CommandAsync("redis", example.Command, null, ct)
            : await db.QueryAsync(example.Language, example.Command, parameters, ct);
        watch.Stop();
        var records = result.RootElement.GetProperty("result").EnumerateArray().Select(x => x.Clone()).ToArray();
        LabPlan plan;
        if (example.Language == "sql")
        {
            using var explanation = await db.QueryAsync("sql", "EXPLAIN " + example.Command, parameters, ct);
            plan = new("available", "EXPLAIN " + example.Command, explanation.RootElement.TryGetProperty("explain", out var text) ? text.ToString() : explanation.RootElement.ToString());
        }
        else plan = new(example.PlanStatus, null, example.Language == "redis" ? "Redis GET has no SQL execution plan." : "Cypher EXPLAIN is not exposed by this lab.");
        var emptyMessage = records.Length == 0 ? example.Id.Contains("transaction-brew", StringComparison.Ordinal)
            ? "No committed transaction Brew exists. Run the successful transaction demo, then retry this read."
            : example.Language == "redis" ? "The transient counter key is unset." : "The fixed read returned no records in the current dataset." : null;
        return new(example.Id, example.Label, example.Language, example.Command, parameters, records, records.Length, watch.Elapsed.TotalMilliseconds, plan, emptyMessage);
    }
}

public static class CommunityQueryLabEndpoints
{
    public static void MapCommunityQueryLab(this WebApplication app)
    {
        var group = app.MapGroup("/api/demo/lab");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (GraphRequestException error) { return Results.Json(new { message = error.Message }, statusCode: error.Status); }
            catch (JsonException) { return Results.BadRequest(new { message = "Send exactly one JSON property: id." }); }
            catch (HttpRequestException error)
            {
                context.HttpContext.RequestServices.GetRequiredService<ILogger<CommunityQueryLab>>().LogWarning(error, "Curated query lab read failed");
                return Results.Json(new { message = "The database or local embedding service could not complete this read. Retry when the demo is ready." }, statusCode: 503);
            }
        });
        group.MapGet("", () => CommunityQueryLab.Catalog());
        group.MapPost("/run", async (HttpRequest request, CommunityQueryLab lab, CancellationToken ct) =>
        {
            if (request.Query.Count != 0 || request.ContentLength > 1024)
                throw new GraphRequestException(400, "Send only a catalog id in the JSON body; overrides are not accepted.");
            // Read with an explicit bound, including chunked requests with no Content-Length.
            using var body = new MemoryStream();
            var buffer = new byte[1025];
            int count;
            while ((count = await request.Body.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, 1025 - (int)body.Length)), ct)) > 0)
            {
                body.Write(buffer, 0, count);
                if (body.Length > 1024) throw new GraphRequestException(400, "The query lab request must be at most 1024 bytes.");
            }
            using var json = JsonDocument.Parse(body.ToArray());
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new GraphRequestException(400, "Send exactly one JSON property: id.");
            var fields = root.EnumerateObject().ToArray();
            if (fields.Length != 1 || fields[0].Name != "id" || fields[0].Value.ValueKind != JsonValueKind.String)
                throw new GraphRequestException(400, "Send exactly one JSON string property: id. Commands, languages, and parameters cannot be overridden.");
            return await lab.Run(fields[0].Value.GetString()!, ct);
        });
    }
}
