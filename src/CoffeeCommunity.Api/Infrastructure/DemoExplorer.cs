using System.Text.Json;

namespace CoffeeCommunity.Api.Infrastructure;

/// <summary>Curated, read-only views for the schema and seed-story slide checkpoints.</summary>
public sealed class DemoExplorer(ArcadeDbClient arcadeDb)
{
    public async Task<object> SchemaAsync(CancellationToken ct)
    {
        var types = (await RowsAsync("SELECT FROM schema:types", null, ct))
            .OrderBy(type => type.GetProperty("name").GetString()).ToArray();
        // schema:types exposes logical index names; schema:indexes also includes physical buckets.
        var indexes = types.SelectMany(type => type.TryGetProperty("indexes", out var values)
                ? values.EnumerateArray().Select(index => index.Clone()) : [])
            .OrderBy(index => index.GetProperty("name").GetString()).ToArray();
        return new { types, indexes };
    }

    public async Task<object?> StoryAsync(string persona, CancellationToken ct)
    {
        var slug = persona switch
        {
            "maya" => "maya-chen",
            "priya" => "priya-nair",
            "luis" => "luis-ortega",
            _ => persona
        };
        var people = await RowsAsync("SELECT slug, name, role FROM Person WHERE slug = :slug", new { slug }, ct);
        if (people.Length == 0) return null;

        var configuration = await RowsAsync(
            "SELECT seedProfile FROM EventConfiguration WHERE slug = :slug",
            new { slug = "brew-connection-2026" }, ct);
        // The whitelist is intentional: these identifiers never come from a request.
        var counts = new List<object>();
        foreach (var type in new[] { "Person", "VendorTable", "RoastBatch", "Recipe", "Brew" })
        {
            var rows = await RowsAsync($"SELECT count(*) AS count FROM {type}", null, ct);
            counts.Add(new { type, count = rows[0].GetProperty("count").GetInt64() });
        }

        var connections = await RowsAsync("""
            MATCH (a:Person {slug: $slug})-[meeting:MET]-(b:Person)
            RETURN b.slug AS slug, b.name AS name, meeting.context AS context ORDER BY slug
            """, new { slug }, ct, "cypher");
        var tastings = await RowsAsync("""
            MATCH (a:Person {slug: $slug})-[:TASTED]->(b:Brew)
            RETURN b.slug AS slug, b.name AS name ORDER BY slug
            """, new { slug }, ct, "cypher");
        var rematches = await RowsAsync("""
            MATCH (winner:Person)-[:BEAT_IN_GAME]->(a:Person {slug: $slug})
            RETURN winner.slug AS slug, winner.name AS name ORDER BY slug
            """, new { slug }, ct, "cypher");
        return new
        {
            profile = configuration[0].GetProperty("seedProfile").GetString(),
            persona = people[0],
            counts,
            connections = connections.DistinctBy(person => person.GetProperty("slug").GetString()),
            tastings,
            rematches
        };
    }

    private async Task<JsonElement[]> RowsAsync(string query, object? parameters, CancellationToken ct, string language = "sql")
    {
        using var result = await arcadeDb.QueryAsync(language, query, parameters, ct);
        return result.RootElement.GetProperty("result").EnumerateArray().Select(row => row.Clone()).ToArray();
    }
}
