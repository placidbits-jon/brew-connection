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

public sealed class CommunityDocuments(ArcadeDbClient db, CommunityGraphGate gate, DemoReadiness readiness)
{
    private readonly List<GraphQuery> queries = [];
    private string? transaction;
    private static string Str(JsonElement row, string field) => row.TryGetProperty(field, out var value) ? value.ToString() : "";
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
    private async Task<JsonElement[]> Query(string label, string command, object? parameters, CancellationToken ct)
    {
        queries.Add(new(label, "sql", command, parameters));
        using var result = transaction is null ? await db.QueryAsync("sql", command, parameters, ct) : await db.CommandInTransactionAsync(transaction, "sql", command, parameters, ct);
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
        await Query("Create recipe vertex", "INSERT INTO Recipe SET slug=:slug, name=:name, searchText=:searchText", new { slug = request.Slug, name = request.Name, searchText = request.Name }, ct);
        await InsertRevision(request.Slug, 1, request.PersonSlug, request.Revision, ct);
        return await RecipeResult(request.Slug, request.PersonSlug, ct);
    }
    public async Task<object> Publish(string slug, PublishRecipeRequest request, CancellationToken ct)
    {
        Validate(request.Revision); if (request.ExpectedRevision is < 1 or > 100000) throw Bad("Provide the expected current revision number.");
        await Record("Person", request.PersonSlug, ct); await Record("Recipe", slug, ct);
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
        // Gate serializes the single local API; the HTTP transaction makes the insert and pointer atomic.
        await InsertRevision(slug, version, request.PersonSlug, request.Revision, ct);
        return await RecipeResult(slug, request.PersonSlug, ct);
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
    private async Task<JsonElement?> Related(string edge, string slug, bool outgoing, CancellationToken ct)
    {
        var field = outgoing ? "@in" : "@out"; var source = outgoing ? "@out" : "@in";
        var rows = await Query($"Follow {edge}", $"SELECT expand({field}) FROM {edge} WHERE {source}.slug=:slug", new { slug }, ct);
        return rows.Length == 0 ? null : rows[0];
    }
    public async Task<object> Coffee(string slug, string persona, CancellationToken ct)
    {
        await Record("Person", persona, ct);
        var batch = await Record("RoastBatch", slug, ct);
        var lot = await Related("FROM_LOT", slug, true, ct); var roaster = await Related("ROASTED", slug, false, ct); var vendor = await Related("SELLS", slug, false, ct);
        var profiles = await Query("Nested roast profile document", "SELECT FROM RoastProfile WHERE roastBatch.slug=:slug", new { slug }, ct);
        var brewRows = await Query("Brews using this batch", "SELECT expand(@out) FROM USED_BATCH WHERE @in.slug=:slug", new { slug }, ct);
        var brews = new List<object>();
        foreach (var brew in brewRows)
        {
            var brewSlug = Str(brew, "slug"); var brewer = await Related("BREWED", brewSlug, false, ct); var recipe = await Related("USED_RECIPE", brewSlug, true, ct);
            var pinned = await Query("Historical revision pinned on USED_RECIPE", "SELECT expand(revision) FROM USED_RECIPE WHERE @out.slug=:slug", new { slug = brewSlug }, ct);
            var reactions = new List<object>();
            foreach (var kind in new[] { "TASTED", "LOVED" })
            {
                var rows = await Query($"Attendee {kind} reactions", $"SELECT @out.slug AS personSlug, reaction, context FROM {kind} WHERE @in.slug=:slug", new { slug = brewSlug }, ct);
                foreach (var row in rows) reactions.Add(new { person = await Record("Person", Str(row, "personSlug"), ct), kind, reaction = Str(row, "reaction") is { Length: > 0 } reaction ? reaction : Str(row, "context") });
            }
            brews.Add(new { brew, brewer, recipe, revision = pinned.Length == 0 ? (JsonElement?)null : pinned[0], reactions });
        }
        var notes = await VisibleNotes(persona, "RoastBatch", slug, ct);
        return new { batch, lot, roaster, vendor, roastProfile = profiles.Length == 0 ? (JsonElement?)null : profiles[0], brews, notes, queries = queries.ToArray() };
    }
}
