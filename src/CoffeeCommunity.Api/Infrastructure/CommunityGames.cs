using System.Globalization;
using System.Text.Json;

namespace CoffeeCommunity.Api.Infrastructure;

public sealed class CommunityGames(ArcadeDbClient db, DemoReadiness readiness)
{
    private readonly List<GraphQuery> queries = [];
    private static string Str(JsonElement row, string property) => row.TryGetProperty(property, out var value) ? value.ToString() : "";
    private static int Int(JsonElement row, string property) => row.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private async Task<JsonElement[]> Read(string label, string command, object? parameters, CancellationToken ct)
    {
        queries.Add(new(label, "sql", command, parameters));
        using var result = await db.QueryAsync("sql", command, parameters, ct);
        return result.RootElement.GetProperty("result").EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    public async Task<object> JourneyAsync(string slug, CancellationToken ct)
    {
        if (!readiness.SchemaReady) throw new GraphRequestException(503, "Demo data is not ready.");
        var people = await Read("Game persona", "SELECT slug, name, role FROM Person WHERE slug=:slug", new { slug }, ct);
        if (people.Length == 0) throw new GraphRequestException(404, "Person not found.");

        var catalog = await Read("Game catalog", "SELECT slug, name, category, mechanics, playerRange, playMinutes FROM Game ORDER BY name", null, ct);
        var sessions = await Read("Persona game graph", """
            MATCH {type: Person, as: person, where: (slug = :slug)}
              .outE('PLAYED_IN'){as: playerPlay}.inV(){as: session}
              .inE('PLAYED_IN'){as: opponentPlay}.outV(){as: opponent, where: (slug <> :slug)}
            RETURN session.slug AS sessionSlug, session.gameSlug AS gameSlug,
              session.playedAt AS playedAt, session.winnerSlug AS winnerSlug,
              session.winnerScore AS winnerScore, session.loserScore AS loserScore,
              opponent.slug AS opponentSlug, opponent.name AS opponentName,
              playerPlay.drink.slug AS playerBrewSlug, playerPlay.drinkNote AS playerDrinkNote,
              opponentPlay.drink.slug AS opponentBrewSlug, opponentPlay.drinkNote AS opponentDrinkNote
            ORDER BY playedAt
            """, new { slug }, ct);
        var playerGameEdges = await Read("Community game graph", """
            SELECT @out.slug AS personSlug, @out.name AS personName,
              @in.slug AS sessionSlug, @in.gameSlug AS gameSlug
            FROM PLAYED_IN ORDER BY gameSlug, personSlug
            """, null, ct);

        var brewSlugs = sessions.SelectMany(session => new[] { Str(session, "playerBrewSlug"), Str(session, "opponentBrewSlug") })
            .Where(value => !string.IsNullOrEmpty(value)).Distinct().ToArray();
        var brews = brewSlugs.Length == 0 ? [] : await Read("Opponent coffee provenance", """
            SELECT slug, name, out('USED_BATCH')[0].slug AS roastBatchSlug,
              out('USED_BATCH')[0].name AS coffeeName
            FROM Brew WHERE slug IN :slugs ORDER BY slug
            """, new { slugs = brewSlugs }, ct);
        var gamesBySlug = catalog.ToDictionary(game => Str(game, "slug"));
        var brewsBySlug = brews.ToDictionary(brew => Str(brew, "slug"));
        var history = sessions.Select(session =>
        {
            var winnerSlug = Str(session, "winnerSlug");
            var hasResult = !string.IsNullOrEmpty(winnerSlug);
            var won = winnerSlug == slug;
            var game = gamesBySlug[Str(session, "gameSlug")];
            var playedAt = DateTime.TryParse(Str(session, "playedAt"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed.ToString("O", CultureInfo.InvariantCulture) : null;
            object? Drink(string prefix)
            {
                var brewSlug = Str(session, $"{prefix}BrewSlug");
                if (string.IsNullOrEmpty(brewSlug) || !brewsBySlug.TryGetValue(brewSlug, out var brew)) return null;
                return new { slug = brewSlug, name = Str(brew, "name"), roastBatchSlug = Str(brew, "roastBatchSlug"), coffeeName = Str(brew, "coffeeName"), note = Str(session, $"{prefix}DrinkNote") };
            }
            return new
            {
                sessionSlug = Str(session, "sessionSlug"),
                gameSlug = Str(game, "slug"),
                gameName = Str(game, "name"),
                category = Str(game, "category"),
                playedAt,
                opponent = new { slug = Str(session, "opponentSlug"), name = Str(session, "opponentName") },
                drink = Drink("player"),
                opponentDrink = Drink("opponent"),
                outcome = !hasResult ? "In progress" : won ? "Win" : "Loss",
                score = hasResult ? Int(session, won ? "winnerScore" : "loserScore") : (int?)null,
                opponentScore = hasResult ? Int(session, won ? "loserScore" : "winnerScore") : (int?)null
            };
        }).ToArray();

        var selectedGames = history.Select(item => item.gameSlug).ToHashSet();
        var tableMatches = playerGameEdges
            .Where(edge => Str(edge, "personSlug") != slug && selectedGames.Contains(Str(edge, "gameSlug")))
            .GroupBy(edge => new { slug = Str(edge, "personSlug"), name = Str(edge, "personName") })
            .Select(group => new
            {
                group.Key.slug,
                group.Key.name,
                games = group.Select(edge => Str(edge, "gameSlug")).Distinct().Select(gameSlug => Str(gamesBySlug[gameSlug], "name")).Order().ToArray()
            })
            .OrderByDescending(match => match.games.Length).ThenBy(match => match.name).ToArray();
        var catalogView = catalog.Select(game =>
        {
            var gameSlug = Str(game, "slug");
            var edges = playerGameEdges.Where(edge => Str(edge, "gameSlug") == gameSlug).ToArray();
            return new
            {
                slug = gameSlug,
                name = Str(game, "name"),
                category = Str(game, "category"),
                mechanics = game.GetProperty("mechanics"),
                playerRange = Str(game, "playerRange"),
                playMinutes = Int(game, "playMinutes"),
                sessions = edges.Select(edge => Str(edge, "sessionSlug")).Distinct().Count(),
                players = edges.Select(edge => Str(edge, "personSlug")).Distinct().Count(),
                playedByPersona = selectedGames.Contains(gameSlug)
            };
        });

        return new
        {
            person = people[0],
            stats = new { games = selectedGames.Count, sessions = history.Length, wins = history.Count(item => item.outcome == "Win") },
            history,
            tableMatches,
            catalog = catalogView,
            queries
        };
    }
}
