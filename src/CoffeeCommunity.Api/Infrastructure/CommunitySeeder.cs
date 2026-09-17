using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CoffeeCommunity.Api.Infrastructure;

public sealed class CommunitySeeder(ArcadeDbClient db, EmbeddingClient embeddings, ILogger logger)
{
    public const long Epoch = 1789401600000;
    private readonly List<string> pending = [];
    private static string Json(object value) => JsonSerializer.Serialize(value);
    private async Task Statement(string sql, CancellationToken ct)
    {
        pending.Add(sql + ";");
        if (pending.Count >= 100) await Flush(ct);
    }
    private async Task Flush(CancellationToken ct)
    {
        if (pending.Count == 0) return;
        using var result = await db.CommandAsync("sqlscript", string.Join('\n', pending), null, ct);
        pending.Clear();
    }
    private Task Record(string type, object content, CancellationToken ct) => Statement($"INSERT INTO {type} CONTENT {Json(content)}", ct);
    private Task Edge(string type, string fromType, string from, string toType, string to, string context, CancellationToken ct) => Statement($"CREATE EDGE {type} FROM (SELECT FROM {fromType} WHERE slug={Json(from)}) TO (SELECT FROM {toType} WHERE slug={Json(to)}) SET occurredAt='2026-09-14 16:00:00', location='pour-over-bar', context={Json(context)}", ct);
    private static string Person(int i) => i switch { 0 => "maya-chen", 1 => "priya-nair", 2 => "luis-ortega", _ => $"attendee-{i:0000}" };
    private static string Batch(int i) => i == 0 ? "ethiopia-blueberry-bloom" : $"roast-batch-{i:0000}";
    private static string Recipe(int i) => i == 0 ? "blueberry-v60" : $"recipe-{i:000}";
    private static string Brew(int i) => i == 0 ? "blueberry-bloom-v60" : $"brew-{i:000}";
    private static readonly int[] EventTenMinuteTotals = [5, 10, 15, 20, 30, 40, 60, 70, 50, 30, 20, 10];
    private static int EventTastingCount(int minute)
    {
        var bucket = minute / 10;
        var minuteInBucket = minute % 10;
        var total = EventTenMinuteTotals[bucket];
        var baseline = total / 10;
        var remainder = total % 10;
        // Multiplication by 7 permutes 0–9, spreading any remainder across the interval.
        return baseline + ((minuteInBucket * 7 + bucket * 3) % 10 < remainder ? 1 : 0);
    }

    public async Task SeedAsync(string profile, CancellationToken ct)
    {
        var scale = profile == "scale";
        var people = scale ? 2040 : 40;
        var batches = scale ? 2025 : 25;
        for (var i = 0; i < people; i++)
            await Record("Person", new { slug = Person(i), name = i switch { 0 => "Maya Chen", 1 => "Priya Nair", 2 => "Luis Ortega", _ => $"Attendee {i:0000}" }, role = i switch { 0 => "Curious taster", 1 => "Community brewer", 2 => "Roaster and game host", _ => "Attendee" }, interests = new[] { i % 2 == 0 ? "fruit-forward" : "chocolate", "coffee" } }, ct);
        await Record("Event", new { slug = "brew-connection-2026", name = "Brew Connection at TechCon", startsAt = "2026-09-14 16:00:00" }, ct);
        for (var i = 0; i < 3; i++)
            await Record("VenueArea", new { slug = i == 0 ? "pour-over-bar" : $"area-{i}", name = new[] { "Pour-over bar", "Game lounge", "Roaster row" }[i], coords = $"POINT({(-83.0458 + i * .0002).ToString(CultureInfo.InvariantCulture)} 42.3314)", searchText = "Coffee brewers games and community", boundary = "POLYGON((-83.047 42.330,-83.044 42.330,-83.044 42.333,-83.047 42.333,-83.047 42.330))" }, ct);
        for (var i = 0; i < 8; i++)
        {
            await Record("Organization", new { slug = $"roaster-{i}", name = i == 0 ? "Great Lakes Roasters" : $"Community Roaster {i}" }, ct);
            await Record("VendorTable", new { slug = $"vendor-{i}", name = i == 0 ? "Great Lakes Coffee Table" : $"Vendor Table {i}", coords = $"POINT({(-83.0458 + i * .0001).ToString(CultureInfo.InvariantCulture)} 42.3314)", searchText = "Fresh local coffee roasted beans available", available = true, areaSlug = "pour-over-bar" }, ct);
        }
        for (var i = 0; i < batches; i++)
        {
            var text = i switch { 0 => "Blueberry jasmine floral fruit-forward Ethiopia natural coffee", 1 => "Blueberry label collector: dark smoky bitter roast, keyword-only match", 2 => "Summer orchard: ripe berry nectar, fragrant blossom, delicate bright cup", 3 => "Priya's favorite: stone fruit honey tea-like washed coffee", _ => $"Coffee origin {i % 12} {(i % 2 == 0 ? "cocoa caramel" : "citrus floral")} roast {i}" };
            await Record("CoffeeLot", new { slug = $"lot-{i:0000}", name = i == 0 ? "Ethiopia Guji Lot 17" : $"Origin Lot {i}", origin = i == 0 ? "Guji, Ethiopia" : $"Origin {i % 12}", process = "natural", searchText = text }, ct);
            await Record("RoastBatch", new { slug = Batch(i), name = i == 0 ? "Ethiopia Blueberry Bloom" : i == 1 ? "Blueberry Label Dark Roast" : i == 2 ? "Summer Orchard" : i == 3 ? "Priya's Honey Stonefruit" : $"Community Roast {i:0000}", description = text, searchText = text, scenario = i switch { 1 => "keyword-only", 2 => "semantic-only-candidate", 3 => "graph-personalized", _ => "provenance" } }, ct);
            await Record("RoastProfile", new { slug = $"profile-{i:0000}", phases = new[] { new { name = "drying", seconds = 240, temperatureC = 150 }, new { name = "development", seconds = 90, temperatureC = 205 } }, developmentSeconds = 90, expectedFlavors = new[] { "blueberry", "jasmine" }, searchText = text, narrative = "Gentle development preserves origin character." }, ct);
            await Statement($"UPDATE RoastProfile SET roastBatch=(SELECT FROM RoastBatch WHERE slug={Json(Batch(i))}) WHERE slug='profile-{i:0000}'", ct);
            await Edge("FROM_LOT", "RoastBatch", Batch(i), "CoffeeLot", $"lot-{i:0000}", "traceable origin", ct);
            await Edge("ROASTED", "Organization", $"roaster-{i % 8}", "RoastBatch", Batch(i), "light roast", ct);
            await Edge("SELLS", "VendorTable", $"vendor-{i % 8}", "RoastBatch", Batch(i), "available at convention", ct);
        }
        for (var i = 0; i < 20; i++)
        {
            await Record("Recipe", new { slug = Recipe(i), name = i == 0 ? "Blueberry Bloom V60" : $"Community Recipe {i}", searchText = i == 0 ? "Blueberry jasmine floral V60 pour over" : $"Balanced coffee recipe method {i % 4}" }, ct);
            for (var revision = 1; revision <= 2; revision++)
                await Record("RecipeRevision", new { slug = $"{Recipe(i)}-v{revision}", revision, steps = new[] { new { atSeconds = 0, waterGrams = 60, action = "Bloom" }, new { atSeconds = 45, waterGrams = 180, action = "Slow spiral pour" }, new { atSeconds = 90, waterGrams = 300, action = "Finish pour" } }, equipment = new { brewer = "V60", filter = "paper", grinder = "hand grinder" }, grind = new { clicks = 22 - revision }, temperatureC = 93, coffeeGrams = 20, waterGrams = 300, commentary = revision == 1 ? "Original community recipe" : "Slightly finer grind for a sweeter finish" }, ct);
            await Statement($"UPDATE Recipe SET currentRevision=(SELECT FROM RecipeRevision WHERE slug='{Recipe(i)}-v2') WHERE slug='{Recipe(i)}'", ct);
            await Statement($"UPDATE RecipeRevision SET author=(SELECT FROM Person WHERE slug='priya-nair'), recipe=(SELECT FROM Recipe WHERE slug='{Recipe(i)}') WHERE slug IN ['{Recipe(i)}-v1','{Recipe(i)}-v2']", ct);
        }
        for (var i = 0; i < 100; i++)
        {
            var brewName = i switch
            {
                0 => "Priya's Blueberry Bloom V60",
                1 => "Luis's Dark Roast V60",
                2 => "Maya's Summer Orchard Pour-over",
                3 => "Priya's Honey Stonefruit V60",
                _ => $"Community Brew {i}"
            };
            await Record("Brew", new { slug = Brew(i), name = brewName, startedAt = "2026-09-14 16:00:00", method = "v60", device = $"scale-{i % 8}", targetFlowRate = 4.0 }, ct);
            await Edge("BREWED", "Person", Person(i == 0 ? 1 : i % 40), "Brew", Brew(i), "shared a cup", ct);
            await Edge("USED_BATCH", "Brew", Brew(i), "RoastBatch", Batch(i % 25), "20 grams", ct);
            await Edge("USED_RECIPE", "Brew", Brew(i), "Recipe", Recipe(i % 20), "revision 2", ct);
            await Edge("TASTED", "Person", Person(i % 40), "Brew", Brew(i), "sweet and floral", ct);
        }
        await Edge("MET", "Person", Person(0), "Person", Person(1), "Met at the pour-over bar", ct);
        await Edge("MET", "Person", Person(1), "Person", Person(2), "Coffee and board games", ct);
        await Edge("WANTS_TO_RECONNECT", "Person", Person(0), "Person", Person(1), "Ask about the blueberry recipe", ct);
        await Edge("LOVED", "Person", Person(0), "Brew", Brew(0), "Blueberry and jasmine", ct);
        await Edge("LOVED", "Person", Person(1), "Brew", Brew(3), "Honey stonefruit favorite", ct);
        await Edge("LIKED_RECIPE", "Person", Person(0), "Recipe", Recipe(0), "Try at home", ct);
        var games = new[]
        {
            new { slug = "wingspan", name = "Wingspan", category = "Strategy", mechanics = new[] { "engine building", "card drafting" }, playerRange = "1–5", playMinutes = 70 },
            new { slug = "magic-the-gathering", name = "Magic the Gathering", category = "Trading card game", mechanics = new[] { "deck building", "dueling" }, playerRange = "2", playMinutes = 45 },
            new { slug = "monopoly", name = "Monopoly", category = "Classic board game", mechanics = new[] { "trading", "area control" }, playerRange = "2–8", playMinutes = 120 },
            new { slug = "bid-whist", name = "Bid Whist", category = "Trick-taking card game", mechanics = new[] { "bidding", "partnerships" }, playerRange = "4", playMinutes = 50 },
            new { slug = "pokemon-tcg", name = "Pokemon TCG", category = "Trading card game", mechanics = new[] { "deck building", "dueling" }, playerRange = "2", playMinutes = 35 },
        };
        foreach (var game in games) await Record("Game", game, ct);

        var gameSessions = new[]
        {
            new { slug = "maya-priya-wingspan", game = "wingspan", one = Person(0), two = Person(1), winner = Person(0), loser = Person(1), winnerScore = 91, loserScore = 84, playedAt = "2026-09-14 16:25:00", oneDrink = Brew(0), oneNote = "Blueberry and jasmine", twoDrink = Brew(3), twoNote = "Honeyed stone fruit" },
            new { slug = "maya-luis-magic", game = "magic-the-gathering", one = Person(0), two = Person(2), winner = Person(2), loser = Person(0), winnerScore = 2, loserScore = 1, playedAt = "2026-09-14 17:05:00", oneDrink = Brew(2), oneNote = "Berry nectar and blossom", twoDrink = Brew(1), twoNote = "Smoky cocoa" },
            new { slug = "priya-luis-monopoly", game = "monopoly", one = Person(1), two = Person(2), winner = Person(1), loser = Person(2), winnerScore = 1320, loserScore = 980, playedAt = "2026-09-14 17:40:00", oneDrink = Brew(3), oneNote = "Honeyed stone fruit", twoDrink = Brew(1), twoNote = "Smoky cocoa" },
            new { slug = "maya-priya-bid-whist", game = "bid-whist", one = Person(0), two = Person(1), winner = Person(1), loser = Person(0), winnerScore = 7, loserScore = 5, playedAt = "2026-09-14 18:20:00", oneDrink = Brew(0), oneNote = "Blueberry and jasmine", twoDrink = Brew(3), twoNote = "Honeyed stone fruit" },
            new { slug = "maya-luis-pokemon", game = "pokemon-tcg", one = Person(0), two = Person(2), winner = Person(0), loser = Person(2), winnerScore = 2, loserScore = 1, playedAt = "2026-09-14 19:00:00", oneDrink = Brew(2), oneNote = "Berry nectar and blossom", twoDrink = Brew(1), twoNote = "Smoky cocoa" },
        };
        foreach (var session in gameSessions)
        {
            var game = games.Single(g => g.slug == session.game);
            var oneName = session.one == Person(0) ? "Maya" : session.one == Person(1) ? "Priya" : "Luis";
            var twoName = session.two == Person(0) ? "Maya" : session.two == Person(1) ? "Priya" : "Luis";
            await Record("GameSession", new { session.slug, name = $"{game.name}: {oneName} versus {twoName}", gameSlug = session.game, session.playedAt, winnerSlug = session.winner, loserSlug = session.loser, session.winnerScore, session.loserScore }, ct);
            await Edge("PLAYED_IN", "Person", session.one, "GameSession", session.slug, $"Played {game.name}", ct);
            await Edge("PLAYED_IN", "Person", session.two, "GameSession", session.slug, $"Played {game.name}", ct);
            await Statement($"UPDATE PLAYED_IN SET drink=(SELECT FROM Brew WHERE slug={Json(session.oneDrink)}), drinkNote={Json(session.oneNote)} WHERE @out.slug={Json(session.one)} AND @in.slug={Json(session.slug)}", ct);
            await Statement($"UPDATE PLAYED_IN SET drink=(SELECT FROM Brew WHERE slug={Json(session.twoDrink)}), drinkNote={Json(session.twoNote)} WHERE @out.slug={Json(session.two)} AND @in.slug={Json(session.slug)}", ct);
            await Edge("BEAT_IN_GAME", "Person", session.winner, "Person", session.loser, $"{game.name}, {session.winnerScore}–{session.loserScore}; session {session.slug}", ct);
            await Statement($"UPDATE BEAT_IN_GAME SET score={session.winnerScore}, opponentScore={session.loserScore}, sessionSlug={Json(session.slug)} WHERE @out.slug={Json(session.winner)} AND @in.slug={Json(session.loser)} AND context={Json($"{game.name}, {session.winnerScore}–{session.loserScore}; session {session.slug}")}", ct);
            await Statement($"UPDATE GameSession SET game=(SELECT FROM Game WHERE slug={Json(session.game)}), area=(SELECT FROM VenueArea WHERE slug='area-1') WHERE slug={Json(session.slug)}", ct);
        }
        for (var i = 0; i < people; i++)
        {
            await Edge("ATTENDED", "Person", Person(i), "Event", "brew-connection-2026", "conference badge", ct);
            if (i < 8) await Edge("MEMBER_OF", "Person", Person(i), "Organization", $"roaster-{i}", "community member", ct);
            await Record("BadgeLookup", new { slug = $"badge-{i:0000}", targetSlug = Person(i), kind = "badge" }, ct);
            await Statement($"UPDATE BadgeLookup SET target=(SELECT FROM Person WHERE slug='{Person(i)}') WHERE slug='badge-{i:0000}'", ct);
        }
        await Record("Note", new { slug = "maya-blueberry-memory", ownerSlug = "maya-chen", visibility = "private", body = "Priya's blueberry brew: ask for the finer-grind revision.", searchText = "", createdAt = "2026-09-14 16:00:00", updatedAt = "2026-09-14 16:00:00" }, ct);
        await Statement("UPDATE Note SET owner=(SELECT FROM Person WHERE slug='maya-chen'), subject=(SELECT FROM Brew WHERE slug='blueberry-bloom-v60') WHERE slug='maya-blueberry-memory'", ct);
        await Record("Note", new { slug = "public-orchard-note", ownerSlug = "priya-nair", visibility = "public", body = "Summer orchard tastes like berry nectar and blossom.", searchText = "Summer orchard berry nectar fragrant blossom", createdAt = "2026-09-14 16:00:00" }, ct);
        await Statement("UPDATE Note SET owner=(SELECT FROM Person WHERE slug='priya-nair'), subject=(SELECT FROM RoastBatch WHERE slug='roast-batch-0002') WHERE slug='public-orchard-note'", ct);
        await Statement("UPDATE TASTED SET reaction='sweet and floral'", ct);
        await Statement("UPDATE LOVED SET reaction='loved'", ct);
        for (var i = 0; i < 20; i++)
            await Statement($"UPDATE USED_RECIPE SET revision=(SELECT FROM RecipeRevision WHERE slug='{Recipe(i)}-v2') WHERE @in.slug='{Recipe(i)}'", ct);
        await Record("BadgeLookup", new { slug = "short-blueberry", targetSlug = "ethiopia-blueberry-bloom", kind = "short-code" }, ct);
        await Statement("UPDATE BadgeLookup SET target=(SELECT FROM RoastBatch WHERE slug='ethiopia-blueberry-bloom') WHERE slug='short-blueberry'", ct);
        await Flush(ct);
        logger.LogInformation("Seed {Profile}: graph and documents written; generating local vectors.", profile);
        // Scale aliases stress the vector index while retrieval deduplicates by subject.
        var vectorCount = scale ? 20045 : 45;
        for (var i = 0; i < vectorCount; i++)
        {
            var subjectType = i < 25 || i >= 45 ? "RoastBatch" : "Recipe";
            var subjectSlug = subjectType == "Recipe" ? Recipe(i - 25) : Batch(i < 25 ? i : (i - 45) % batches);
            await Record("SearchEmbedding", new { slug = $"embedding-{i:00000}", subjectSlug, subjectType }, ct);
            await Statement($"UPDATE SearchEmbedding SET subject=(SELECT FROM {subjectType} WHERE slug={Json(subjectSlug)}) WHERE slug='embedding-{i:00000}'", ct);
        }
        await Flush(ct);
        await new CommunitySearchIndex(db, embeddings).RebuildAsync(ct);
        var samples = scale ? 2000000 : 6000;
        var lines = new StringBuilder(2_000_000);
        for (var i = 0; i < samples; i++)
        {
            var brew = i / (scale ? 20000 : 60);
            var second = i % (scale ? 20000 : 60);
            var flow = brew == 0 && second == 30 ? 12 : 4;
            lines.Append(CultureInfo.InvariantCulture, $"BrewTelemetry,brew_id={Brew(brew)},brewer_id={Person(brew == 0 ? 1 : brew % 40)},device=scale-{brew % 8},method=v60 water_grams={Math.Min(second * 4, 300)}.0,flow_rate={flow}.0,temperature_c={93 - (second % 60) * .02:F2} {Epoch + second * 1000L}\n");
            if ((i + 1) % 10000 == 0 || i + 1 == samples)
            {
                await db.WriteTimeSeriesAsync(lines.ToString(), ct);
                lines.Clear();
                if ((i + 1) % 250000 == 0) logger.LogInformation("Seed telemetry: {Samples}/{Total} samples", i + 1, samples);
            }
        }
        for (var i = 0; i < 120; i++) lines.Append(CultureInfo.InvariantCulture, $"EventActivity,event_id=brew-connection-2026,area=pour-over-bar,kind=tasting count={EventTastingCount(i)}.0,duration=30.0 {Epoch + i * 60000L}\n");
        await db.WriteTimeSeriesAsync(lines.ToString(), ct);
        using var done = await db.CommandAsync("sql", "UPDATE EventConfiguration SET seedComplete=true, telemetrySamples=:samples, embeddingCount=:vectorCount, peopleCount=:people, roastBatchCount=:batches, telemetryEpoch=:epoch, features={graph:true,documents:true,fullText:true,vectors:true,timeSeries:true,geospatial:true}, embeddingProvider='ollama' WHERE slug='brew-connection-2026'", new { samples, vectorCount, people, batches, epoch = Epoch }, ct);
        logger.LogInformation("Seed {Profile} complete: {People} people, {Batches} coffees, {Vectors} vectors, {Samples} samples.", profile, people, batches, vectorCount, samples);
    }
}
