using System.Globalization;
using System.Text.Json;
using System.Text;

namespace CoffeeCommunity.Api.Infrastructure;

/// <summary>One canonical public text for both Lucene and local embedding inference.</summary>
public static class CommunitySearchText
{
    public const string Version = "005-embeddinggemma-canonical-v3";
    private static string Bound(string text)
    {
        // A byte budget also bounds worst-case tokenization, including arbitrary Unicode,
        // leaving room for the retrieval prefix within the pinned model's 2048-token context.
        var result = new StringBuilder(); var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 1500) break;
            result.Append(rune); bytes += rune.Utf8SequenceLength;
        }
        return result.ToString();
    }
    public static string Roast(string name, string description, string origin, string process) => Bound($"{name}. {description}. Origin: {origin}. Process: {process}.");
    public static string Recipe(string name, RecipeDocument revision) => Bound($"{name}. {revision.Commentary}. Brewer: {revision.Equipment.Brewer}; filter: {revision.Equipment.Filter}; grinder: {revision.Equipment.Grinder}; grind: {revision.Grind.Clicks} clicks. " +
        string.Create(CultureInfo.InvariantCulture, $"{revision.CoffeeGrams} g coffee, {revision.WaterGrams} g water, {revision.TemperatureC} C. ") +
        string.Join(" ", revision.Steps.Select(s => string.Create(CultureInfo.InvariantCulture, $"At {s.AtSeconds} seconds: {s.Action}, {s.WaterGrams} g water."))));
    public static RecipeDocument Document(JsonElement row) => row.Deserialize<RecipeDocument>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("Current recipe revision missing.");
}

public sealed class CommunitySearchIndex(ArcadeDbClient db, EmbeddingClient embeddings)
{
    private static string Str(JsonElement row, string field) => row.TryGetProperty(field, out var value) ? value.ToString() : "";
    public async Task RebuildAsync(CancellationToken ct)
    {
        // Keep existing vertices, user revisions, notes, telemetry and scale aliases. The version
        // marker advances only after every canonical subject and existing embedding is refreshed.
        using var roasts = await db.QueryAsync("sql", "SELECT slug, name, description, searchText, out('FROM_LOT')[0].origin AS origin, out('FROM_LOT')[0].process AS process FROM RoastBatch ORDER BY slug", null, ct);
        using var recipes = await db.QueryAsync("sql", "SELECT slug, name, currentRevision.slug AS revisionSlug FROM Recipe ORDER BY slug", null, ct);
        var subjects = new List<(string Type, string Slug, string Text)>();
        foreach (var row in roasts.RootElement.GetProperty("result").EnumerateArray())
        {
            var description = Str(row, "description");
            if (description.Length == 0)
            {
                description = Str(row, "searchText");
                // Freeze the legacy source before canonicalizing, so interrupted upgrades do not nest text.
                using (await db.CommandAsync("sql", "UPDATE RoastBatch SET description=:description WHERE slug=:slug", new { description, slug = Str(row, "slug") }, ct)) { }
            }
            subjects.Add(("RoastBatch", Str(row, "slug"), CommunitySearchText.Roast(Str(row, "name"), description, Str(row, "origin"), Str(row, "process"))));
        }
        foreach (var row in recipes.RootElement.GetProperty("result").EnumerateArray())
        {
            using var revision = await db.QueryAsync("sql", "SELECT FROM RecipeRevision WHERE slug=:slug", new { slug = Str(row, "revisionSlug") }, ct);
            subjects.Add(("Recipe", Str(row, "slug"), CommunitySearchText.Recipe(Str(row, "name"), CommunitySearchText.Document(revision.RootElement.GetProperty("result")[0]))));
        }
        foreach (var chunk in subjects.Chunk(32))
        {
            var vectors = await embeddings.EmbedBatchAsync(chunk.Select(x => x.Text).ToArray(), ct);
            for (var i = 0; i < chunk.Length; i++)
            {
                var item = chunk[i];
                var transaction = await db.BeginTransactionAsync(ct);
                try
                {
                    using (await db.CommandInTransactionAsync(transaction, "sql", $"UPDATE {item.Type} SET searchText=:text WHERE slug=:slug", new { text = item.Text, slug = item.Slug }, ct)) { }
                    using var count = await db.CommandInTransactionAsync(transaction, "sql", "SELECT count(*) AS count FROM SearchEmbedding WHERE subjectSlug=:slug AND subjectType=:type", new { slug = item.Slug, type = item.Type }, ct);
                    var parameters = new { slug = item.Slug, embeddingSlug = item.Type + ":" + item.Slug, type = item.Type, text = item.Text, embedding = vectors[i], version = CommunitySearchText.Version };
                    if (count.RootElement.GetProperty("result")[0].GetProperty("count").GetInt32() == 0)
                    {
                        using (await db.CommandInTransactionAsync(transaction, "sql", $"INSERT INTO SearchEmbedding SET slug=:embeddingSlug, subjectSlug=:slug, subjectType=:type, text=:text, embedding=:embedding, dimensions=768, provider='ollama', model='embeddinggemma:300m', indexVersion=:version", parameters, ct)) { }
                        using (await db.CommandInTransactionAsync(transaction, "sql", $"UPDATE SearchEmbedding SET subject=(SELECT FROM {item.Type} WHERE slug=:slug) WHERE slug=:embeddingSlug", parameters, ct)) { }
                    }
                    else
                    {
                        using (await db.CommandInTransactionAsync(transaction, "sql", "UPDATE SearchEmbedding SET text=:text, embedding=:embedding, dimensions=768, provider='ollama', model='embeddinggemma:300m', indexVersion=:version WHERE subjectSlug=:slug AND subjectType=:type", parameters, ct)) { }
                    }
                    await db.CommitTransactionAsync(transaction, ct);
                }
                catch { try { await db.RollbackTransactionAsync(transaction, CancellationToken.None); } catch (HttpRequestException) { } throw; }
            }
        }
        using (await db.CommandAsync("sql", "UPDATE EventConfiguration SET searchIndexVersion=:version, embeddingProvider='ollama', embeddingModel='embeddinggemma:300m' WHERE slug='brew-connection-2026'", new { version = CommunitySearchText.Version }, ct)) { }
    }
}
