using System.Globalization;
using System.Text.Json;

namespace CoffeeCommunity.Api.Infrastructure;

// One local API owns mutations. Share this gate with reset so no transaction can race a database drop.
public sealed class CommunityGraphGate { public SemaphoreSlim Semaphore { get; } = new(1, 1); }
public sealed class GraphRequestException(int status, string message) : Exception(message) { public int Status { get; } = status; }
public sealed record GraphQuery(string Label, string Language, string Command, object? Parameters);
public sealed record MeetRequest(string PersonSlug, string BadgeCode, string Context, string Location);
public sealed record BrewReactionRequest(string PersonSlug, string BrewSlug);
public sealed record ReconnectRequest(string PersonSlug, string TargetSlug);
public sealed record GameSessionRequest(string Slug, string GameSlug, string PersonSlug, string OpponentSlug);
public sealed record GameResultRequest(string SessionSlug, string WinnerSlug, string LoserSlug, int WinnerScore, int LoserScore);

public sealed class CommunityGraph(ArcadeDbClient db, CommunityGraphGate gate, DemoReadiness readiness)
{
    private readonly List<GraphQuery> _queries = [];
    private string? _transaction;
    private static string Str(JsonElement row, string property) => row.TryGetProperty(property, out var value) ? value.ToString() : "";
    // ArcadeDB DATETIME is timezone-free; all event writes in this demo use UTC.
    private static string OccurredAt(JsonElement row) => DateTime.Parse(Str(row, "occurredAt"),
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).ToString("O", CultureInfo.InvariantCulture);
    private static GraphRequestException Bad(string message) => new(400, message);
    private static void Required(params string[] values)
    {
        if (values.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 300)) throw Bad("Provide non-empty values of at most 300 characters.");
    }
    private static void Different(string person, string other)
    {
        if (person == other) throw Bad("Choose a different person.");
    }
    public async Task<object> RunAsync(Func<Task<object>> action, bool mutate, CancellationToken ct)
    {
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            if (!readiness.SchemaReady) throw new GraphRequestException(503, "Demo data is not ready.");
            if (mutate) _transaction = await db.BeginTransactionAsync(ct);
            var result = await action();
            if (_transaction is not null) await db.CommitTransactionAsync(_transaction, ct);
            return result;
        }
        catch
        {
            if (_transaction is not null)
            {
                try { await db.RollbackTransactionAsync(_transaction, CancellationToken.None); }
                catch (HttpRequestException) { /* Commit can have completed before its response was lost. */ }
            }
            throw;
        }
        finally { _transaction = null; gate.Semaphore.Release(); }
    }
    private async Task<JsonElement[]> Query(string label, string command, object? parameters, CancellationToken ct, string language = "sql")
    {
        _queries.Add(new(label, language, command, parameters));
        using var result = _transaction is null
            ? await db.QueryAsync(language, command, parameters, ct)
            : await db.CommandInTransactionAsync(_transaction, language, command, parameters, ct);
        return result.RootElement.GetProperty("result").EnumerateArray().Select(row => row.Clone()).ToArray();
    }
    private async Task<JsonElement> Record(string type, string slug, CancellationToken ct)
    {
        Required(slug);
        var rows = await Query($"Resolve {type}", $"SELECT FROM {type} WHERE slug = :slug", new { slug }, ct);
        return rows.FirstOrDefault().ValueKind == JsonValueKind.Undefined
            ? throw new GraphRequestException(404, $"{type} not found.") : rows[0];
    }
    private async Task Edge(string type, string from, string targetType, string to, string context, string location, CancellationToken ct, object? extra = null)
    {
        var predicate = type == "MET"
            ? "((@out.slug = :from AND @in.slug = :to) OR (@out.slug = :to AND @in.slug = :from))"
            : "@out.slug = :from AND @in.slug = :to";
        var values = new Dictionary<string, object?> { ["from"] = from, ["to"] = to, ["context"] = context, ["location"] = location,
            ["at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) };
        var set = "occurredAt = :at, context = :context, location = :location";
        if (extra is not null)
        {
            foreach (var property in JsonSerializer.SerializeToElement(extra).EnumerateObject())
            {
                values[property.Name] = property.Value.Clone();
                set += $", {property.Name} = :{property.Name}";
            }
            if (values.ContainsKey("sessionSlug")) predicate += " AND sessionSlug = :sessionSlug";
        }
        if ((await Query($"Check existing {type}", $"SELECT FROM {type} WHERE {predicate}", values, ct)).Length != 0) return;
        await Query($"Create {type}", $"CREATE EDGE {type} FROM (SELECT FROM Person WHERE slug = :from) TO (SELECT FROM {targetType} WHERE slug = :to) SET {set}", values, ct);
    }
    private object Saved(string message) => new { message, queries = _queries.ToArray() };
    public async Task<object> Meet(MeetRequest request, CancellationToken ct)
    {
        Required(request.BadgeCode, request.Context, request.Location);
        await Record("Person", request.PersonSlug, ct);
        var badge = await Record("BadgeLookup", request.BadgeCode, ct);
        if (Str(badge, "kind") != "badge") throw new GraphRequestException(404, "Person badge not found.");
        var target = Str(badge, "targetSlug");
        Different(request.PersonSlug, target);
        await Record("Person", target, ct);
        await Edge("MET", request.PersonSlug, "Person", target, request.Context.Trim(), request.Location.Trim(), ct);
        return Saved("Meeting recorded in both passports.");
    }
    public async Task<object> Reaction(BrewReactionRequest request, string type, CancellationToken ct)
    {
        await Record("Person", request.PersonSlug, ct);
        await Record("Brew", request.BrewSlug, ct);
        await Edge(type, request.PersonSlug, "Brew", request.BrewSlug, type == "TASTED" ? "Tasted a cup" : "Loved this cup", "Coffee community", ct);
        return Saved(type == "TASTED" ? "Tasting recorded." : "Favorite recorded.");
    }
    public async Task<object> Reconnect(ReconnectRequest request, CancellationToken ct)
    {
        Different(request.PersonSlug, request.TargetSlug);
        await Record("Person", request.PersonSlug, ct);
        await Record("Person", request.TargetSlug, ct);
        await Edge("WANTS_TO_RECONNECT", request.PersonSlug, "Person", request.TargetSlug, "Catch up after the conference", "Coffee community", ct);
        return Saved("Added to people to reconnect with.");
    }
    public async Task<object> Session(GameSessionRequest request, CancellationToken ct)
    {
        Required(request.Slug);
        Different(request.PersonSlug, request.OpponentSlug);
        var person = await Record("Person", request.PersonSlug, ct);
        var opponent = await Record("Person", request.OpponentSlug, ct);
        var game = await Record("Game", request.GameSlug, ct);
        var prior = await Query("Check session identity", "SELECT FROM GameSession WHERE slug = :slug", new { slug = request.Slug }, ct);
        if (prior.Length != 0)
        {
            var players = await Query("Session participants", "SELECT @out.slug AS slug FROM PLAYED_IN WHERE @in.slug = :slug", new { slug = request.Slug }, ct);
            if (Str(prior[0], "gameSlug") != request.GameSlug || !players.Select(p => Str(p, "slug")).ToHashSet().SetEquals([request.PersonSlug, request.OpponentSlug]))
                throw new GraphRequestException(409, "This session name already belongs to a different game or pair of players.");
        }
        else
        {
            await Query("Create game session", "INSERT INTO GameSession SET slug=:slug, name=:name, gameSlug=:gameSlug, game=(SELECT FROM Game WHERE slug=:gameSlug)",
                new { slug = request.Slug, name = $"{Str(game, "name")}: {Str(person, "name")} versus {Str(opponent, "name")}", gameSlug = request.GameSlug }, ct);
            await Edge("PLAYED_IN", request.PersonSlug, "GameSession", request.Slug, "Joined a game", "Game table", ct);
            await Edge("PLAYED_IN", request.OpponentSlug, "GameSession", request.Slug, "Joined a game", "Game table", ct);
        }
        return Saved("Game session recorded.");
    }
    public async Task<object> Result(GameResultRequest request, CancellationToken ct)
    {
        Different(request.WinnerSlug, request.LoserSlug);
        if (request.LoserScore < 0 || request.WinnerScore <= request.LoserScore) throw Bad("Winner score must exceed the non-negative loser score.");
        await Record("Person", request.WinnerSlug, ct);
        await Record("Person", request.LoserSlug, ct);
        await Record("GameSession", request.SessionSlug, ct);
        var players = await Query("Validate session participants", "SELECT @out.slug AS slug FROM PLAYED_IN WHERE @in.slug=:slug", new { slug = request.SessionSlug }, ct);
        if (!players.Select(p => Str(p, "slug")).ToHashSet().SetEquals([request.WinnerSlug, request.LoserSlug])) throw Bad("The result must name the two session participants.");
        var existing = await Query("Check recorded result", "SELECT @out.slug AS winner, @in.slug AS loser, score, opponentScore FROM BEAT_IN_GAME WHERE sessionSlug=:slug", new { slug = request.SessionSlug }, ct);
        if (existing.Length > 0)
        {
            var row = existing[0];
            if (Str(row, "winner") != request.WinnerSlug || Str(row, "loser") != request.LoserSlug || row.GetProperty("score").GetInt32() != request.WinnerScore || row.GetProperty("opponentScore").GetInt32() != request.LoserScore)
                throw new GraphRequestException(409, "This session already has a different result.");
        }
        else
        {
            await Query("Record session result", "UPDATE GameSession SET winnerSlug=:winner, winnerScore=:score, loserSlug=:loser, loserScore=:opponentScore WHERE slug=:slug",
                new { winner = request.WinnerSlug, score = request.WinnerScore, loser = request.LoserSlug, opponentScore = request.LoserScore, slug = request.SessionSlug }, ct);
            await Edge("BEAT_IN_GAME", request.WinnerSlug, "Person", request.LoserSlug, $"Won {request.WinnerScore}–{request.LoserScore}", "Game table", ct,
                new { score = request.WinnerScore, opponentScore = request.LoserScore, sessionSlug = request.SessionSlug });
        }
        return Saved("Game result recorded.");
    }
    public async Task<object> Passport(string slug, CancellationToken ct)
    {
        var person = await Record("Person", slug, ct);
        var timeline = new List<object>();
        foreach (var kind in new[] { "MET", "TASTED", "LOVED", "WANTS_TO_RECONNECT", "PLAYED_IN", "BEAT_IN_GAME" })
        {
            if (kind == "BEAT_IN_GAME")
            {
                var results = await Query("Passport game outcomes", "SELECT @out.slug AS winner, @out.name AS winnerName, @in.slug AS loser, @in.name AS loserName, score, opponentScore, sessionSlug, occurredAt, location FROM BEAT_IN_GAME WHERE @out.slug=:slug OR @in.slug=:slug", new { slug }, ct);
                foreach (var result in results)
                {
                    var won = Str(result, "winner") == slug;
                    var score = result.GetProperty(won ? "score" : "opponentScore").GetInt32();
                    var opponentScore = result.GetProperty(won ? "opponentScore" : "score").GetInt32();
                    var opponent = Str(result, won ? "loser" : "winner");
                    var opponentName = Str(result, won ? "loserName" : "winnerName");
                    var sessionSlug = Str(result, "sessionSlug");
                    timeline.Add(new { kind, slug = opponent, title = $"{(won ? "Won against" : "Lost to")} {opponentName}",
                        occurredAt = OccurredAt(result), context = $"{score}–{opponentScore} · Session {sessionSlug}",
                        location = Str(result, "location"), outcome = won ? "Won" : "Lost", score, opponentScore, sessionSlug });
                }
                continue;
            }
            var undirected = kind == "MET";
            var query = $"MATCH (p:Person {{slug: $slug}})-[e:{kind}]{(undirected ? "-" : "->")}(other) RETURN other.slug AS slug, other.name AS name, e.occurredAt AS occurredAt, e.context AS context, e.location AS location";
            var rows = await Query($"Passport {kind}", query, new { slug }, ct, "cypher");
            timeline.AddRange(rows.Select(r => (object)new { kind, slug = Str(r,"slug"), title = Str(r,"name"), occurredAt = OccurredAt(r), context = Str(r,"context"), location = Str(r,"location") }));
        }
        var ordered = timeline.Select(item => JsonSerializer.SerializeToElement(item)).OrderByDescending(r => Str(r,"occurredAt")).ThenBy(r => Str(r,"kind")).ThenBy(r => Str(r,"slug"));
        return new { person = new { slug, name = Str(person,"name") }, timeline = ordered, queries = _queries.ToArray() };
    }
    public async Task<object> Network(string slug, string target, CancellationToken ct)
    {
        var person = await Record("Person", slug, ct);
        await Record("Person", target, ct);
        var connections = await Query("Direct meetings (both directions)", "MATCH (p:Person {slug:$slug})-[:MET]-(other:Person) RETURN DISTINCT other.slug AS slug, other.name AS name ORDER BY slug", new { slug }, ct, "cypher");
        var edges = await Query("Social edges for breadth-first shortest-path traversal in API", "SELECT @out.slug AS source, @in.slug AS target FROM MET LIMIT 30000", null, ct);
        var people = await Query("People and shared interests", "SELECT slug, name, interests FROM Person LIMIT 30000", null, ct);
        var names = people.ToDictionary(p => Str(p,"slug"), p => Str(p,"name"));
        var adjacency = new Dictionary<string, HashSet<string>>();
        foreach (var edge in edges)
        {
            var source = Str(edge,"source"); var destination = Str(edge,"target");
            if (!adjacency.TryGetValue(source, out var forward)) adjacency[source] = forward = [];
            if (!adjacency.TryGetValue(destination, out var reverse)) adjacency[destination] = reverse = [];
            forward.Add(destination); reverse.Add(source);
        }
        var previous = new Dictionary<string, string?> { [slug] = null };
        var queue = new Queue<string>(); queue.Enqueue(slug);
        while (queue.TryDequeue(out var current) && !previous.ContainsKey(target))
        {
            if (!adjacency.TryGetValue(current, out var neighbors)) continue;
            foreach (var neighbor in neighbors.Order()) if (previous.TryAdd(neighbor, current)) queue.Enqueue(neighbor);
        }
        var path = new List<string>();
        if (previous.ContainsKey(target)) for (string? step = target; step is not null; step = previous[step]) path.Add(step);
        path.Reverse();
        static string[] Interests(JsonElement p) => p.TryGetProperty("interests", out var list) ? list.EnumerateArray().Select(i => i.GetString()!).ToArray() : [];
        var interests = Interests(person);
        var shared = people.Where(p => Str(p,"slug") != slug).Select(p => new { slug = Str(p,"slug"), name = Str(p,"name"), interests = Interests(p).Intersect(interests).ToArray() }).Where(p => p.interests.Length > 0).OrderByDescending(p => p.interests.Length).ThenBy(p => p.slug).Take(20);
        var rematches = await Query("Who beat me", "MATCH (winner:Person)-[e:BEAT_IN_GAME]->(p:Person {slug:$slug}) RETURN winner.slug AS slug, winner.name AS name, e.score AS score, e.opponentScore AS opponentScore, e.sessionSlug AS sessionSlug ORDER BY sessionSlug", new { slug }, ct, "cypher");
        var reconnects = await Query("People to reconnect with", "MATCH (p:Person {slug:$slug})-[e:WANTS_TO_RECONNECT]->(other:Person) RETURN other.slug AS slug, other.name AS name, e.context AS context ORDER BY slug", new { slug }, ct, "cypher");
        return new { person = new { slug, name = Str(person,"name") }, connections, paths = path.Count == 0 ? [] : new[] { new { slugs = path, names = path.Select(s => names[s]).ToArray() } }, sharedInterests = shared, rematches, reconnects, queries = _queries.ToArray() };
    }
}
