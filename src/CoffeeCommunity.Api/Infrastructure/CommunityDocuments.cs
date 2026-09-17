using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoffeeCommunity.Api.Infrastructure;

public sealed record RecipeStep(int AtSeconds, double WaterGrams, string Action);
public sealed record RecipeEquipment(string Brewer, string Filter, string Grinder);
public sealed record RecipeGrind(int Clicks);
public sealed record RecipeDocument(RecipeStep[] Steps, RecipeEquipment Equipment, RecipeGrind Grind, double TemperatureC, double CoffeeGrams, double WaterGrams, string Commentary);
public sealed record CreateRecipeRequest(string Slug, string Name, string PersonSlug, RecipeDocument Revision);
public sealed record PublishRecipeRequest(string PersonSlug, int ExpectedRevision, RecipeDocument Revision);
public sealed record CreateNoteRequest(string Slug, string PersonSlug, string SubjectType, string SubjectSlug, string Visibility, string Body);

public sealed class CommunityDocuments(ArcadeDbClient db, CommunityGraphGate gate, DemoReadiness readiness, EmbeddingClient embeddings)
{
    private readonly List<GraphQuery> queries = [];
    private string? transaction;
    private static string Str(JsonElement row, string field) => row.TryGetProperty(field, out var value) ? value.ToString() : "";
    private static JsonElement? Element(JsonElement row, string field) => row.TryGetProperty(field, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? value.Clone() : null;
    private static Dictionary<string, JsonElement> Pick(JsonElement row, params string[] fields) => fields.Where(field => row.TryGetProperty(field, out _)).ToDictionary(field => field, field => row.GetProperty(field).Clone());
    private static GraphRequestException Bad(string message) => new(400, message);
    private static void Text(string? value, int max = 300)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max) throw Bad($"Provide non-empty text of at most {max} characters.");
    }
    private static void Slug(string? value)
    {
        if (value is null || !Regex.IsMatch(value, "^[a-zA-Z0-9][a-zA-Z0-9-]{0,119}$")) throw Bad("Use a slug containing letters, numbers, and hyphens, at most 120 characters.");
    }
    private static void Validate(RecipeDocument? document)
    {
        if (document?.Steps is not { Length: > 0 and <= 50 } || document.Equipment is null || document.Grind is null) throw Bad("Provide steps, equipment, and grind settings.");
        Text(document.Equipment.Brewer); Text(document.Equipment.Filter); Text(document.Equipment.Grinder); Text(document.Commentary, 10000);
        if (document.Grind.Clicks is < 1 or > 500 || document.TemperatureC is < 1 or > 100 || document.CoffeeGrams is <= 0 or > 1000 || document.WaterGrams is <= 0 or > 20000) throw Bad("Recipe quantities are outside their supported range.");
        var previousTime = -1; double previousWater = -1;
        foreach (var step in document.Steps)
        {
            if (step is null || step.AtSeconds < previousTime || step.AtSeconds > 86400 || step.AtSeconds < 0 || step.WaterGrams < previousWater || step.WaterGrams <= 0 || step.WaterGrams > document.WaterGrams) throw Bad("Steps must have increasing time and cumulative water within the recipe total.");
            Text(step.Action, 1000); previousTime = step.AtSeconds; previousWater = step.WaterGrams;
        }
    }
    public async Task<object> Run(Func<Task<object>> action, bool mutate, CancellationToken ct)
    {
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            if (!readiness.SchemaReady) throw new GraphRequestException(503, "Demo data is not ready.");
            if (mutate) transaction = await db.BeginTransactionAsync(ct);
            var result = await action();
            if (transaction is not null) await db.CommitTransactionAsync(transaction, ct);
            return result;
        }
        catch
        {
            if (transaction is not null) try { await db.RollbackTransactionAsync(transaction, CancellationToken.None); } catch (HttpRequestException) { }
            throw;
        }
        finally { transaction = null; gate.Semaphore.Release(); }
    }
    private async Task<JsonElement[]> Query(string label, string command, object? parameters, CancellationToken ct, string language = "sql")
    {
        queries.Add(new(label, language, command, parameters));
        using var result = transaction is null ? await db.QueryAsync(language, command, parameters, ct) : await db.CommandInTransactionAsync(transaction, language, command, parameters, ct);
        return result.RootElement.GetProperty("result").EnumerateArray().Select(r => r.Clone()).ToArray();
    }
    private async Task<JsonElement> Record(string type, string slug, CancellationToken ct)
    {
        Text(slug);
        var rows = await Query($"Resolve {type}", $"SELECT FROM {type} WHERE slug = :slug", new { slug }, ct);
        return rows.Length == 0 ? throw new GraphRequestException(404, $"{type} not found.") : rows[0];
    }
    private Task<JsonElement[]> Revisions(string slug, CancellationToken ct) => Query("Immutable recipe history", "SELECT @rid, @type, slug, revision, steps, equipment, grind, temperatureC, coffeeGrams, waterGrams, commentary, fingerprint, author.@rid AS author, recipe.@rid AS recipe FROM RecipeRevision WHERE recipe.slug = :slug ORDER BY revision", new { slug }, ct);
    private async Task<object> RecipeResult(string slug, string persona, CancellationToken ct)
    {
        var recipeRows = await Query("Recipe vertex and current document link", "SELECT @rid, @type, slug, name, searchText, currentRevision.@rid AS currentRevision FROM Recipe WHERE slug=:slug", new { slug }, ct);
        if (recipeRows.Length == 0) throw new GraphRequestException(404, "Recipe not found.");
        var recipe = recipeRows[0];
        var revisions = await Revisions(slug, ct);
        var pointer = await Query("Current revision link", "SELECT currentRevision.slug AS slug FROM Recipe WHERE slug = :slug", new { slug }, ct);
        var currentRevision = revisions.Single(r => Str(r, "slug") == Str(pointer[0], "slug"));
        var notes = await VisibleNotes(persona, "Recipe", slug, ct);
        return new { recipe, currentRevision, revisions, notes, queries = queries.ToArray() };
    }
    public async Task<object> Recipe(string slug, string persona, CancellationToken ct)
    {
        await Record("Person", persona, ct);
        return await RecipeResult(slug, persona, ct);
    }
    private async Task InsertRevision(string slug, int version, string person, RecipeDocument document, CancellationToken ct)
    {
        // Explicit fields prevent clients from overwriting document links, revision identity, or metadata.
        var values = new { slug = $"{slug}-v{version}", revision = version, steps = document.Steps, equipment = document.Equipment, grind = document.Grind,
            temperatureC = document.TemperatureC, coffeeGrams = document.CoffeeGrams, waterGrams = document.WaterGrams, commentary = document.Commentary,
            fingerprint = Fingerprint(person, document) };
        await Query("Insert immutable revision", "INSERT INTO RecipeRevision CONTENT :document", new { document = JsonSerializer.SerializeToElement(values, new JsonSerializerOptions(JsonSerializerDefaults.Web)) }, ct);
        await Query("Link revision author and recipe", "UPDATE RecipeRevision SET author=(SELECT FROM Person WHERE slug=:person), recipe=(SELECT FROM Recipe WHERE slug=:recipe) WHERE slug=:slug", new { person, recipe = slug, slug = $"{slug}-v{version}" }, ct);
        await Query("Advance current revision in same transaction", "UPDATE Recipe SET currentRevision=(SELECT FROM RecipeRevision WHERE slug=:revision) WHERE slug=:slug", new { revision = $"{slug}-v{version}", slug }, ct);
    }
    private static string Fingerprint(string person, RecipeDocument document) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { person, document }))));
    public async Task<object> Create(CreateRecipeRequest request, CancellationToken ct)
    {
        Slug(request.Slug); Text(request.Name); Validate(request.Revision); await Record("Person", request.PersonSlug, ct);
        if ((await Query("Check recipe identity", "SELECT FROM Recipe WHERE slug=:slug", new { slug = request.Slug }, ct)).Length != 0) throw new GraphRequestException(409, "Recipe slug already exists.");
        var searchText = CommunitySearchText.Recipe(request.Name, request.Revision);
        var vector = await embeddings.EmbedAsync(searchText, ct);
        await Query("Create recipe vertex", "INSERT INTO Recipe SET slug=:slug, name=:name, searchText=:searchText", new { slug = request.Slug, name = request.Name, searchText }, ct);
        await InsertRevision(request.Slug, 1, request.PersonSlug, request.Revision, ct);
        await IndexRecipe(request.Slug, searchText, vector, ct);
        return await RecipeResult(request.Slug, request.PersonSlug, ct);
    }
    public async Task<object> Publish(string slug, PublishRecipeRequest request, CancellationToken ct)
    {
        Validate(request.Revision); if (request.ExpectedRevision is < 1 or > 100000) throw Bad("Provide the expected current revision number.");
        await Record("Person", request.PersonSlug, ct); var recipe = await Record("Recipe", slug, ct);
        var revisions = await Revisions(slug, ct);
        var version = request.ExpectedRevision + 1;
        var existing = revisions.FirstOrDefault(r => r.GetProperty("revision").GetInt32() == version);
        if (existing.ValueKind != JsonValueKind.Undefined)
        {
            if (Str(existing, "fingerprint") != Fingerprint(request.PersonSlug, request.Revision)) throw new GraphRequestException(409, "This revision was already published with different content. Reload the current revision.");
            return await RecipeResult(slug, request.PersonSlug, ct);
        }
        var pointer = await Query("Check optimistic revision", "SELECT currentRevision.revision AS revision FROM Recipe WHERE slug=:slug", new { slug }, ct);
        if (pointer[0].GetProperty("revision").GetInt32() != request.ExpectedRevision) throw new GraphRequestException(409, "The current revision changed. Reload before publishing.");
        var searchText = CommunitySearchText.Recipe(Str(recipe, "name"), request.Revision);
        var vector = await embeddings.EmbedAsync(searchText, ct);
        // Gate serializes the single local API; the HTTP transaction makes the insert and pointer atomic.
        await InsertRevision(slug, version, request.PersonSlug, request.Revision, ct);
        await IndexRecipe(slug, searchText, vector, ct);
        return await RecipeResult(slug, request.PersonSlug, ct);
    }
    private async Task IndexRecipe(string slug, string text, float[] embedding, CancellationToken ct)
    {
        await Query("Update canonical public search text", "UPDATE Recipe SET searchText=:text WHERE slug=:slug", new { slug, text }, ct);
        var existing = await Query("Find recipe embedding", "SELECT slug FROM SearchEmbedding WHERE subjectType='Recipe' AND subjectSlug=:slug", new { slug }, ct);
        var parameters = new { slug, embeddingSlug = "Recipe:" + slug, text, embedding, version = CommunitySearchText.Version };
        if (existing.Length == 0)
        {
            await Query("Index locally embedded current revision atomically", "INSERT INTO SearchEmbedding SET slug=:embeddingSlug, subjectSlug=:slug, subjectType='Recipe', text=:text, embedding=:embedding, provider='ollama', model='embeddinggemma:300m', dimensions=768, indexVersion=:version", parameters, ct);
            await Query("Link indexed recipe subject", "UPDATE SearchEmbedding SET subject=(SELECT FROM Recipe WHERE slug=:slug) WHERE slug=:embeddingSlug", parameters, ct);
        }
        else
            await Query("Refresh locally embedded current revision atomically", "UPDATE SearchEmbedding SET text=:text, embedding=:embedding, provider='ollama', model='embeddinggemma:300m', dimensions=768, indexVersion=:version WHERE subjectType='Recipe' AND subjectSlug=:slug", parameters, ct);
    }
    private static void SubjectType(string type)
    {
        if (type is not ("Person" or "Brew" or "RoastBatch" or "Recipe" or "GameSession")) throw Bad("Choose a Person, Brew, RoastBatch, Recipe, or GameSession subject.");
    }
    private Task<JsonElement[]> VisibleNotes(string persona, string? type, string? slug, CancellationToken ct) => Query("Notes visible to selected demo persona",
        "SELECT *, owner.slug AS ownerSlug, subject.@type AS subjectType, subject.slug AS subjectSlug FROM Note WHERE (visibility='public' OR owner.slug=:persona)" +
        (type is null ? "" : " AND subject.@type=:type AND subject.slug=:slug") + " ORDER BY slug", new { persona, type, slug }, ct);
    public async Task<object> Notes(string persona, string? type, string? slug, CancellationToken ct)
    {
        await Record("Person", persona, ct);
        if (type is not null || slug is not null) { SubjectType(type!); await Record(type!, slug!, ct); }
        var notes = await VisibleNotes(persona, type, slug, ct);
        return new { notes, queries = queries.ToArray() };
    }
    public async Task<object> AddNote(CreateNoteRequest request, CancellationToken ct)
    {
        Slug(request.Slug); SubjectType(request.SubjectType); Text(request.Body, 10000);
        if (request.Visibility is not ("private" or "public")) throw Bad("Visibility must be private or public.");
        await Record("Person", request.PersonSlug, ct); await Record(request.SubjectType, request.SubjectSlug, ct);
        if ((await Query("Check note identity", "SELECT slug FROM Note WHERE slug=:slug", new { slug = request.Slug }, ct)).Length != 0) throw new GraphRequestException(409, "Note slug already exists.");
        await Query("Create linked plain-text note", $"INSERT INTO Note SET slug=:slug, owner=(SELECT FROM Person WHERE slug=:person), ownerSlug=:person, subject=(SELECT FROM {request.SubjectType} WHERE slug=:subject), visibility=:visibility, body=:body, searchText=:searchText, createdAt=:at, updatedAt=:at",
            new { slug = request.Slug, person = request.PersonSlug, subject = request.SubjectSlug, visibility = request.Visibility, body = request.Body, searchText = request.Visibility == "public" ? request.Body : "", at = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) }, ct);
        return await Notes(request.PersonSlug, request.SubjectType, request.SubjectSlug, ct);
    }
    public async Task<object> Coffee(string slug, string persona, CancellationToken ct)
    {
        var graphRows = await Query("Single Cypher provenance graph", """
            MATCH (viewer:Person {slug:$persona}), (batch:RoastBatch {slug:$slug})
            OPTIONAL MATCH (batch)-[:FROM_LOT]->(lot:CoffeeLot)
            OPTIONAL MATCH (roaster:Organization)-[:ROASTED]->(batch)
            OPTIONAL MATCH (vendor:VendorTable)-[:SELLS]->(batch)
            OPTIONAL MATCH (brew:Brew)-[:USED_BATCH]->(batch)
            OPTIONAL MATCH (brewer:Person)-[:BREWED]->(brew)
            OPTIONAL MATCH (brew)-[:USED_RECIPE]->(recipe:Recipe)
            OPTIONAL MATCH (taster:Person)-[tasted:TASTED]->(brew)
            OPTIONAL MATCH (lover:Person)-[loved:LOVED]->(brew)
            RETURN batch{.*} AS batch, lot{.*} AS lot, roaster{.*} AS roaster, vendor{.*} AS vendor,
                   brew{.*} AS brew, brewer{.*} AS brewer, recipe{.*} AS recipe,
                   taster{.*} AS taster, tasted{.*} AS tasted, lover{.*} AS lover, loved{.*} AS loved
            ORDER BY brew.slug, taster.slug, lover.slug
            """, new { slug, persona }, ct, "cypher");
        if (graphRows.Length == 0)
        {
            // Preserve the endpoint's specific not-found responses without adding reads to the normal path.
            await Record("Person", persona, ct); await Record("RoastBatch", slug, ct);
            throw new GraphRequestException(404, "Provenance graph not found.");
        }
        var batch = Element(graphRows[0], "batch")!.Value;
        var lot = Element(graphRows[0], "lot"); var roaster = Element(graphRows[0], "roaster"); var vendor = Element(graphRows[0], "vendor");
        var profiles = await Query("Nested roast profile document", "SELECT FROM RoastProfile WHERE roastBatch.slug=:slug", new { slug }, ct);
        var pinnedRows = await Query("Historical revisions pinned on USED_RECIPE", "SELECT @out.slug AS brewSlug, revision.slug AS slug, revision.revision AS revision, revision.steps AS steps, revision.equipment AS equipment, revision.grind AS grind, revision.temperatureC AS temperatureC, revision.coffeeGrams AS coffeeGrams, revision.waterGrams AS waterGrams, revision.commentary AS commentary, revision.fingerprint AS fingerprint FROM USED_RECIPE WHERE @out IN (SELECT expand(@out) FROM USED_BATCH WHERE @in.slug=:slug)", new { slug }, ct);
        var pinned = pinnedRows.ToDictionary(row => Str(row, "brewSlug"), row => Pick(row, "slug", "revision", "steps", "equipment", "grind", "temperatureC", "coffeeGrams", "waterGrams", "commentary", "fingerprint"));
        var brewRows = new Dictionary<string, JsonElement>(); var brewers = new Dictionary<string, JsonElement>(); var recipes = new Dictionary<string, JsonElement>();
        var reactions = new Dictionary<string, List<object>>(); var reactionKeys = new HashSet<string>();
        void AddReaction(string brewSlug, string kind, JsonElement? person, JsonElement? edge)
        {
            if (person is null || edge is null || !reactionKeys.Add($"{brewSlug}:{kind}:{Str(person.Value, "slug")}")) return;
            if (!reactions.TryGetValue(brewSlug, out var list)) reactions[brewSlug] = list = [];
            var text = Str(edge.Value, "reaction") is { Length: > 0 } reaction ? reaction : Str(edge.Value, "context");
            list.Add(new { person = person.Value, kind, reaction = text });
        }
        foreach (var row in graphRows)
        {
            var brew = Element(row, "brew"); if (brew is null) continue;
            var brewSlug = Str(brew.Value, "slug"); if (brewSlug.Length == 0) continue;
            brewRows[brewSlug] = brew.Value;
            if (Element(row, "brewer") is { } brewer) brewers[brewSlug] = brewer;
            if (Element(row, "recipe") is { } recipe) recipes[brewSlug] = recipe;
            AddReaction(brewSlug, "TASTED", Element(row, "taster"), Element(row, "tasted"));
            AddReaction(brewSlug, "LOVED", Element(row, "lover"), Element(row, "loved"));
        }
        var brews = new List<object>();
        foreach (var (brewSlug, brew) in brewRows.OrderBy(entry => entry.Key))
        {
            brews.Add(new { brew, brewer = brewers.TryGetValue(brewSlug, out var brewer) ? brewer : (JsonElement?)null, recipe = recipes.TryGetValue(brewSlug, out var recipe) ? recipe : (JsonElement?)null, revision = pinned.GetValueOrDefault(brewSlug), reactions = reactions.GetValueOrDefault(brewSlug, []) });
        }
        var notes = await VisibleNotes(persona, "RoastBatch", slug, ct);
        return new { batch, lot, roaster, vendor, roastProfile = profiles.Length == 0 ? (JsonElement?)null : profiles[0], brews, notes, queries = queries.ToArray() };
    }
}
