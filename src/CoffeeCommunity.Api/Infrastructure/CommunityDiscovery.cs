using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoffeeCommunity.Api.Infrastructure;

public sealed record DiscoveryQuery(string Label, string Language, string Command, object? Parameters, string? Plan = null);
public sealed record DiscoveryResult(string Slug, string Type, string Name, string Description, double KeywordScore, double? VectorDistance, double KeywordContribution, double VectorContribution, double GraphContribution, double TotalScore, bool Available, string[] Explanations);
public sealed record DiscoveryAnswer(string Query, string Mode, string Persona, DiscoveryResult[] Results, DiscoveryQuery[] Queries, object Model);
public sealed record DiscoveryQuestion(string Text, string? Persona, string? Type, bool AvailableOnly = false);

public sealed class CommunityDiscovery(ArcadeDbClient db, EmbeddingClient embeddings, CommunityGraphGate gate, DemoReadiness readiness)
{
    private readonly List<DiscoveryQuery> queries = [];
    private static string Str(JsonElement row, string key) => row.TryGetProperty(key, out var value) ? value.ToString() : "";
    private static double Number(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
    private async Task<JsonElement[]> Query(string label, string command, object? parameters, CancellationToken ct, string language = "sql", bool explain = false)
    {
        using var result = await db.QueryAsync(language, command, parameters, ct);
        string? plan = null;
        if (explain)
        {
            using var explanation = await db.QueryAsync(language, "EXPLAIN " + command, parameters, ct);
            plan = explanation.RootElement.TryGetProperty("explain", out var value) ? value.GetString() : explanation.RootElement.ToString();
        }
        queries.Add(new(label, language, command, parameters, plan));
        return result.RootElement.GetProperty("result").EnumerateArray().Select(x => x.Clone()).ToArray();
    }
    public async Task<DiscoveryAnswer> Search(string query, string mode, string persona, string type, string syntax, string? similarTo, bool availableOnly, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 300) throw new GraphRequestException(400, "Enter a search of 1–300 characters.");
        if (mode is not ("keyword" or "semantic" or "hybrid" or "personalized" or "cross-model")) throw new GraphRequestException(400, "Choose keyword, semantic, hybrid, personalized, or one-query mode.");
        if (type is not ("all" or "RoastBatch" or "Recipe")) throw new GraphRequestException(400, "Choose all, RoastBatch, or Recipe.");
        if (syntax is not ("plain" or "phrase" or "fuzzy" or "stemming" or "autocomplete" or "morelike")) throw new GraphRequestException(400, "Choose a supported full-text example.");
        var tokens = Regex.Matches(query.ToLowerInvariant(), @"[\p{L}\p{N}]+" ).Select(x => x.Value).ToArray();
        if (tokens.Length == 0) throw new GraphRequestException(400, "Include at least one word in the search.");
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            if (!readiness.SchemaReady) throw new GraphRequestException(503, "Demo data is not ready.");
            if (mode == "cross-model")
            {
                if (type == "Recipe") throw new GraphRequestException(400, "One-query mode currently demonstrates coffee results. Choose Coffees or Coffees and recipes.");
                if (syntax != "plain") throw new GraphRequestException(400, "One-query mode uses the Words full-text example.");
                return await SearchCrossModel(query, persona, tokens, ct);
            }
            var people = await Query("Resolve selected persona", "SELECT slug, name FROM Person WHERE slug=:persona", new { persona }, ct);
            if (people.Length == 0) throw new GraphRequestException(404, "Persona not found.");
            var types = type == "all" ? new[] { "RoastBatch", "Recipe" } : [type];
            var keyword = new Dictionary<string, double>();
            var vector = new Dictionary<string, double>();
            var graph = new Dictionary<string, List<string>>();
            if (mode != "semantic")
            {
                var expression = syntax switch
                {
                    "phrase" => '"' + string.Join(' ', tokens) + '"',
                    "fuzzy" => string.Join(' ', tokens.Select(t => t + "~2")),
                    "autocomplete" => string.Join(' ', tokens.Take(tokens.Length - 1).Append(tokens[^1] + "*")),
                    _ => string.Join(' ', tokens)
                };
                foreach (var subjectType in types)
                {
                    string command; object parameters;
                    if (syntax == "morelike")
                    {
                        if (string.IsNullOrWhiteSpace(similarTo)) throw new GraphRequestException(400, "Choose a source result for More like this.");
                        var sources = await Query("Resolve More like this source", $"SELECT @rid AS rid FROM {subjectType} WHERE slug=:slug", new { slug = similarTo }, ct);
                        if (sources.Length == 0) continue;
                        command = $"SELECT slug, $score AS score FROM {subjectType} WHERE SEARCH_INDEX_MORE('{subjectType}[searchText]', :sources, {{minTermFreq:1,minDocFreq:1,excludeSource:true}})=true ORDER BY $score DESC, slug LIMIT 100";
                        parameters = new { sources = sources.Select(r => Str(r, "rid")).ToArray() };
                    }
                    else
                    {
                        command = $"SELECT slug, $score AS score FROM {subjectType} WHERE SEARCH_INDEX('{subjectType}[searchText]', :expression)=true ORDER BY $score DESC, slug LIMIT 100";
                        parameters = new { expression };
                    }
                    var hits = await Query(syntax == "morelike" ? "Lucene representative-term similarity" : $"Lucene BM25 ({syntax}; EnglishAnalyzer)", command, parameters, ct, explain: true);
                    foreach (var hit in hits) keyword[subjectType + ":" + Str(hit, "slug")] = Number(hit, "score");
                }
                if (syntax == "morelike" && !queries.Any(q => q.Command.Contains("SEARCH_INDEX_MORE"))) throw new GraphRequestException(404, "More like this source not found in the selected type.");
            }
            if (mode != "keyword")
            {
                var embedding = await embeddings.EmbedAsync(query, ct, "query");
                var count = await Query("Vector corpus size (include scale aliases before deduplication)", "SELECT count(*) AS count FROM SearchEmbedding", null, ct);
                var k = (int)Number(count[0], "count");
                if (k > 0)
                {
                    // Request the complete indexed neighborhood before type/availability filtering so
                    // repeated scale aliases cannot crowd a distinct subject out of the candidate set.
                    var hits = await Query("768-dimensional COSINE neighbors from LSM_VECTOR index", "SELECT record.subjectSlug AS slug, record.subjectType AS type, min(distance) AS distance FROM (SELECT expand(vector.neighbors('SearchEmbedding[embedding]', :embedding, :k))) GROUP BY record.subjectSlug, record.subjectType", new { embedding, k }, ct, explain: true);
                    foreach (var hit in hits)
                    {
                        var subjectType = Str(hit, "type"); if (!types.Contains(subjectType)) continue;
                        var key = subjectType + ":" + Str(hit, "slug"); var distance = Number(hit, "distance");
                        if (!vector.TryGetValue(key, out var old) || distance < old) vector[key] = distance;
                    }
                }
            }
            if (mode == "personalized")
            {
                var paths = await Query("Actual acquaintance → favorite brew → available coffee paths", "MATCH (p:Person {slug:$persona})-[:MET]-(friend:Person)-[:LOVED]->(brew:Brew)-[:USED_BATCH]->(batch:RoastBatch)<-[:SELLS]-(vendor:VendorTable) WHERE vendor.available = true RETURN p.name AS person, friend.name AS friend, brew.name AS brew, batch.slug AS slug, vendor.name AS vendor", new { persona }, ct, "cypher");
                foreach (var path in paths)
                {
                    var key = "RoastBatch:" + Str(path, "slug");
                    if (!graph.TryGetValue(key, out var reasons)) graph[key] = reasons = [];
                    reasons.Add($"{Str(path, "person")} — MET → {Str(path, "friend")} — LOVED → {Str(path, "brew")} — USED_BATCH → this coffee. Sold by {Str(path, "vendor")} (available).");
                }
            }
            var availabilityRows = await Query("Current vendor availability", "SELECT @in.slug AS slug, @out.name AS vendor, @out.available AS available FROM SELLS", null, ct);
            var availability = availabilityRows.Where(r => r.TryGetProperty("available", out var a) && a.ValueKind == JsonValueKind.True).GroupBy(r => Str(r, "slug")).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(r => Str(r, "vendor"))));
            var keys = keyword.Keys.Union(vector.Keys).Union(graph.Keys).ToHashSet();
            var results = new List<DiscoveryResult>();
            var maxKeyword = keyword.Values.DefaultIfEmpty(1).Max();
            foreach (var subjectType in types)
            {
                var slugs = keys.Where(k => k.StartsWith(subjectType + ":", StringComparison.Ordinal)).Select(k => k[(subjectType.Length + 1)..]).ToArray();
                if (slugs.Length == 0) continue;
                var rows = await Query("Resolve indexed candidates by unique slug", $"SELECT slug, name, searchText FROM {subjectType} WHERE slug IN :slugs", new { slugs }, ct);
                foreach (var row in rows)
                {
                    var slug = Str(row, "slug"); var key = subjectType + ":" + slug;
                    var available = subjectType == "Recipe" || availability.ContainsKey(slug);
                    if (availableOnly && !available) continue;
                    var keywordScore = keyword.GetValueOrDefault(key);
                    double? distance = vector.TryGetValue(key, out var d) ? d : null;
                    var keywordContribution = mode == "keyword" ? keywordScore : .35 * keywordScore / Math.Max(maxKeyword, .00001);
                    var vectorContribution = distance.HasValue ? (mode == "semantic" ? 1 : .65) * Math.Clamp(1 - distance.Value, -1, 1) : 0;
                    var graphContribution = mode == "personalized" && graph.ContainsKey(key) ? .5 : 0;
                    var reasons = new List<string>();
                    if (keywordScore > 0) reasons.Add($"Lucene {(syntax == "morelike" ? "representative-term" : "BM25")} score {keywordScore:F3} from canonical public text.");
                    if (distance.HasValue) reasons.Add($"Local embeddinggemma:300m cosine distance {distance:F4} (lower is closer).");
                    if (graph.TryGetValue(key, out var paths)) reasons.AddRange(paths.Distinct());
                    reasons.Add(subjectType == "Recipe" ? "Current recipe revision is available to read." : available ? $"Available at {availability[slug]}." : "No currently available vendor offer.");
                    results.Add(new(slug, subjectType, Str(row, "name"), Str(row, "searchText"), keywordScore, distance, keywordContribution, vectorContribution, graphContribution, keywordContribution + vectorContribution + graphContribution, available, reasons.ToArray()));
                }
            }
            return new(query, mode, persona, results.OrderByDescending(r => r.TotalScore).ThenBy(r => r.Slug, StringComparer.Ordinal).Take(20).ToArray(), queries.ToArray(), new { provider = "ollama", model = "embeddinggemma:300m", dimensions = 768, similarity = "COSINE", indexVersion = CommunitySearchText.Version, ranking = mode is "hybrid" or "personalized" ? "API: 0.35 × normalized BM25 + 0.65 × cosine similarity" + (mode == "personalized" ? " + 0.50 for an available acquaintance favorite path" : "") : mode == "keyword" ? "Lucene score descending" : "Indexed cosine distance ascending", filtering = "Type and availability applied after indexed retrieval; aliases deduplicated by subject." });
        }
        finally { gate.Semaphore.Release(); }
    }
    private async Task<DiscoveryAnswer> SearchCrossModel(string query, string persona, string[] tokens, CancellationToken ct)
    {
        var embedding = await embeddings.EmbedAsync(query, ct, "query");
        var expression = string.Join(' ', tokens);
        const string command = """
            SELECT record.subjectSlug AS slug,
                   record.subject.name AS name,
                   record.subject.searchText AS description,
                   min(distance) AS vectorDistance
            FROM (SELECT expand(vector.neighbors('SearchEmbedding[embedding]', :embedding, :k)))
            WHERE record.subjectType = 'RoastBatch'
              AND record.subjectSlug IN (
                SELECT slug FROM RoastBatch
                WHERE SEARCH_INDEX('RoastBatch[searchText]', :expression)=true
              )
              AND record.subjectSlug IN (
                SELECT slug FROM (
                  MATCH
                    {type: Person, as: person, where: (slug = :persona)}
                    .both('MET'){type: Person, as: friend}
                    .out('LOVED'){type: Brew, as: brew}
                    .out('USED_BATCH'){type: RoastBatch, as: roast}
                    .in('SELLS'){type: VendorTable, as: vendor, where: (available = true)}
                  RETURN roast.slug AS slug
                )
              )
            GROUP BY record.subjectSlug, record.subject.name, record.subject.searchText
            ORDER BY vectorDistance, slug
            LIMIT 20
            """;
        var rows = await Query(
            "One cross-model personalized retrieval",
            command,
            new { embedding, k = 25000, expression, persona },
            ct,
            explain: true);
        var results = rows.Select(row =>
        {
            var distance = Number(row, "vectorDistance");
            return new DiscoveryResult(
                Str(row, "slug"),
                "RoastBatch",
                Str(row, "name"),
                Str(row, "description"),
                0,
                distance,
                0,
                Math.Clamp(1 - distance, -1, 1),
                0,
                Math.Clamp(1 - distance, -1, 1),
                true,
                [
                    $"Lucene matched “{expression}” in canonical public text.",
                    $"The native vector index returned cosine distance {distance:F4} (lower is closer).",
                    "The selected persona reaches this coffee through MET → LOVED → USED_BATCH.",
                    "An available vendor SELLS this coffee."
                ]);
        }).ToArray();
        return new(
            query,
            "cross-model",
            persona,
            results,
            queries.ToArray(),
            new
            {
                provider = "ollama",
                model = "embeddinggemma:300m",
                dimensions = 768,
                similarity = "COSINE",
                indexVersion = CommunitySearchText.Version,
                ranking = "ArcadeDB: one parameterized SQL retrieval intersects vector neighbors, Lucene matches, a live community path, and vendor availability; cosine distance orders the surviving coffees.",
                filtering = "This is intersection semantics, not the broader weighted union used by Personalized mode. The query embedding is computed locally before the statement runs."
            });
    }
    public async Task<object> Ask(DiscoveryQuestion request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 500) throw new GraphRequestException(400, "Enter a question of 1–500 characters.");
        var interpreted = await embeddings.InterpretAsync(request.Text, ct);
        var query = Str(interpreted, "query");
        if (string.IsNullOrWhiteSpace(query) || query.Length > 300) throw new GraphRequestException(503, "The local helper did not return a usable search. Use the search box directly.");
        var discovery = await Search(query, "personalized", request.Persona ?? "maya-chen", request.Type ?? "all", "plain", null, request.AvailableOnly, ct);
        return new { question = request.Text, interpretedQuery = query, interpreter = interpreted, discovery };
    }
}
