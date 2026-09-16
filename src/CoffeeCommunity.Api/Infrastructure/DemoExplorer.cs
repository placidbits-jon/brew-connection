using System.Text.Json;

namespace CoffeeCommunity.Api.Infrastructure;

/// <summary>Curated, read-only views for the schema and seed-story slide checkpoints.</summary>
public sealed class DemoExplorer(ArcadeDbClient arcadeDb)
{
    public async Task<object> SchemaAsync(CancellationToken ct)
    {
        const string command = "SELECT FROM schema:types";
        var types = (await RowsAsync(command, null, ct))
            .OrderBy(type => type.GetProperty("name").GetString()).ToArray();
        // schema:types exposes logical index names; schema:indexes also includes physical buckets.
        var indexes = types.SelectMany(type => type.TryGetProperty("indexes", out var values)
                ? values.EnumerateArray().Select(index => index.Clone()) : [])
            .OrderBy(index => index.GetProperty("name").GetString()).ToArray();
        return new { types, indexes, queries = new[] { new GraphQuery("Schema metadata", "sql", command, null) } };
    }

    public async Task<object?> StoryAsync(string persona, CancellationToken ct)
    {
        var queries = new List<GraphQuery>();
        async Task<JsonElement[]> Read(string label, string command, object? parameters, string language = "sql")
        {
            var rows = await RowsAsync(command, parameters, ct, language);
            queries.Add(new(label, language, command, parameters));
            return rows;
        }
        var slug = persona switch
        {
            "maya" => "maya-chen",
            "priya" => "priya-nair",
            "luis" => "luis-ortega",
            _ => persona
        };
        var people = await Read("Story persona", "SELECT slug, name, role FROM Person WHERE slug = :slug", new { slug });
        if (people.Length == 0) return null;

        var configuration = await Read("Story profile",
            "SELECT seedProfile FROM EventConfiguration WHERE slug = :slug",
            new { slug = "brew-connection-2026" });
        // The whitelist is intentional: these identifiers never come from a request.
        var counts = new List<object>();
        foreach (var type in new[] { "Person", "VendorTable", "RoastBatch", "Recipe", "Brew" })
        {
            var rows = await Read("Story counts", $"SELECT count(*) AS count FROM {type}", null);
            counts.Add(new { type, count = rows[0].GetProperty("count").GetInt64() });
        }

        var connections = await Read("Story meetings","""
            MATCH (a:Person {slug: $slug})-[meeting:MET]-(b:Person)
            RETURN b.slug AS slug, b.name AS name, meeting.context AS context ORDER BY slug
            """, new { slug }, "cypher");
        var tastings = await Read("Story tastings","""
            MATCH (a:Person {slug: $slug})-[:TASTED]->(b:Brew)
            RETURN b.slug AS slug, b.name AS name ORDER BY slug
            """, new { slug }, "cypher");
        var rematches = await Read("Story rematches","""
            MATCH (winner:Person)-[:BEAT_IN_GAME]->(a:Person {slug: $slug})
            RETURN winner.slug AS slug, winner.name AS name ORDER BY slug
            """, new { slug }, "cypher");
        return new
        {
            profile = configuration[0].GetProperty("seedProfile").GetString(),
            persona = people[0],
            counts,
            connections = connections.DistinctBy(person => person.GetProperty("slug").GetString()),
            tastings,
            rematches,
            queries
        };
    }

    private async Task<JsonElement[]> RowsAsync(string query, object? parameters, CancellationToken ct, string language = "sql")
    {
        using var result = await arcadeDb.QueryAsync(language, query, parameters, ct);
        return result.RootElement.GetProperty("result").EnumerateArray().Select(row => row.Clone()).ToArray();
    }
}
