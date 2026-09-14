using System.Text.Json;

namespace CoffeeCommunity.Api.Infrastructure;

public sealed record TransactionSnapshot(Dictionary<string, long> Counts, JsonElement[] Brew, JsonElement[] Note, JsonElement[] Edges);

public sealed class CommunityTransactions(ArcadeDbClient db, CommunityGraphGate gate, DemoReadiness readiness)
{
    public const string BrewSlug = "transaction-blueberry-v60";
    private readonly List<GraphQuery> queries = [];
    private string? session;
    private static readonly string[] DurableTypes = CommunitySchema.Vertices.Concat(CommunitySchema.Edges)
        .Concat(["RecipeRevision", "Note", "RoastProfile", "EventConfiguration", "BadgeLookup", "SearchEmbedding", "SchemaMigration", "TelemetryRun"]).ToArray();

    private async Task<JsonElement[]> Query(string label, string sql, object? parameters, CancellationToken ct, string language = "sql")
    {
        queries.Add(new(label, language, sql, parameters));
        using var result = session is null ? await db.QueryAsync(language, sql, parameters, ct)
            : await db.CommandInTransactionAsync(session, language, sql, parameters, ct);
        return result.RootElement.GetProperty("result").EnumerateArray().Select(x => x.Clone()).ToArray();
    }
    private async Task<TransactionSnapshot> Snapshot(string slug, CancellationToken ct)
    {
        var counts = new Dictionary<string, long>();
        var hasRuns = (await Query("Optional replay document type", "SELECT name FROM schema:types WHERE name='TelemetryRun'", null, ct)).Length != 0;
        foreach (var type in DurableTypes)
            counts[type] = type == "TelemetryRun" && !hasRuns ? 0 : (await Query($"Count durable {type}", $"SELECT count(*) AS total FROM {type}", null, ct))[0].GetProperty("total").GetInt64();
        var brew = await Query("SQL brew identity", "SELECT @rid AS rid, slug, name, operationId FROM Brew WHERE slug=:slug", new { slug }, ct);
        var note = await Query("Linked tasting note", "SELECT @rid AS rid, slug, body, ownerSlug, visibility, subject.asString() AS subjectRid FROM Note WHERE slug=:slug", new { slug = slug + "-note" }, ct);
        var edges = new List<JsonElement>();
        foreach (var type in new[] { "BREWED", "USED_BATCH", "USED_RECIPE", "TASTED" })
            edges.AddRange(await Query($"Verify {type} for operation", $"SELECT @rid AS rid, @type AS type, @out.slug AS fromSlug, @in.slug AS toSlug" +
                (type == "USED_RECIPE" ? ", revision.asString() AS revisionRid, revision.slug AS revisionSlug" : "") +
                $" FROM {type} WHERE @out.slug=:slug OR @in.slug=:slug ORDER BY @rid", new { slug }, ct));
        return new(counts, brew, note, edges.ToArray());
    }
    public async Task<object> Execute(string? scenario, CancellationToken ct)
    {
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            if (!readiness.SchemaReady) throw new GraphRequestException(503, "Demo data is not ready.");
            var slug = scenario == "rollback" ? "transaction-rollback-v60" : BrewSlug;
            var operationId = scenario == "rollback" ? "phase7-tasting-rollback" : "phase7-tasting-commit";
            var before = await Snapshot(slug, ct);
            TransactionSnapshot? staged = null;
            var steps = new List<string>();
            var outcome = before.Brew.Length == 0 ? "ready" : "committed";
            if (scenario is not null)
            {
                if (before.Brew.Length != 0)
                {
                    if (before.Brew[0].GetProperty("operationId").GetString() != operationId)
                        throw new GraphRequestException(409, "The authored brew identity belongs to another operation.");
                    outcome = "already-committed";
                    steps.Add("The stable operation identity already exists; no writes repeated.");
                }
                else
                {
                    // Fixed authored inputs and unique Brew.slug make a response-loss retry safe.
                    if ((await Query("Resolve pinned immutable revision", "SELECT @rid AS rid FROM RecipeRevision WHERE slug='blueberry-v60-v2'", null, ct)).Length != 1)
                        throw new GraphRequestException(409, "The authored revision is missing; restore story data.");
                    foreach (var (type, target) in new[] { ("Person", "priya-nair"), ("Person", "maya-chen"), ("RoastBatch", "ethiopia-blueberry-bloom"), ("Recipe", "blueberry-v60") })
                        if ((await Query($"Resolve authored {type}", $"SELECT @rid AS rid FROM {type} WHERE slug=:target", new { target }, ct)).Length != 1)
                            throw new GraphRequestException(409, "An authored tasting dependency is missing; restore story data.");
                    session = await db.BeginTransactionAsync(ct);
                    steps.Add("BEGIN HTTP transaction (all SQL statements share its session).");
                    try
                    {
                        await Query("Create tasting brew", "INSERT INTO Brew SET slug=:slug, name=:name, operationId=:operationId, startedAt='2026-09-14 16:30:00', method='v60', targetFlowRate=4", new { slug, name = scenario == "rollback" ? "Rolled Back Tasting" : "Transaction Blueberry V60", operationId }, ct);
                        await Edge("BREWED", "Person", "priya-nair", "Brew", slug, ct);
                        await Edge("USED_BATCH", "Brew", slug, "RoastBatch", "ethiopia-blueberry-bloom", ct);
                        steps.Add("Staged a Brew plus BREWED and USED_BATCH edges inside the transaction.");
                        if (scenario == "rollback")
                        {
                            staged = await Snapshot(slug, ct);
                            steps.Add("Controlled halfway failure: stop before the recipe link, tasting and note.");
                            await db.RollbackTransactionAsync(session, CancellationToken.None);
                            session = null;
                            outcome = "rolled-back";
                            steps.Add("ROLLBACK acknowledged; fresh SQL verifies every checked graph/document type count and operation record.");
                        }
                        else
                        {
                            await Edge("USED_RECIPE", "Brew", slug, "Recipe", "blueberry-v60", ct, true);
                            await Edge("TASTED", "Person", "maya-chen", "Brew", slug, ct);
                            await Query("Create public linked tasting note", "INSERT INTO Note SET slug=:noteSlug, name='Transaction tasting memory', ownerSlug='maya-chen', owner=(SELECT FROM Person WHERE slug='maya-chen'), subject=(SELECT FROM Brew WHERE slug=:slug), visibility='public', body='Blueberry and jasmine, shared by Priya.', searchText='Blueberry jasmine shared Priya', createdAt='2026-09-14 16:30:00'", new { slug, noteSlug = slug + "-note" }, ct);
                            staged = await Snapshot(slug, ct);
                            steps.Add("Staged pinned revision 2, Maya's TASTED edge and her public linked Note.");
                            // Do not describe a failed commit response as rollback: it can mean commit succeeded.
                            try { await db.CommitTransactionAsync(session, CancellationToken.None); }
                            catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
                            {
                                session = null;
                                throw new GraphRequestException(503, "Commit acknowledgement was lost; outcome is unknown. Reload or retry this same scenario to reconcile its stable operation identity.");
                            }
                            session = null;
                            outcome = "committed";
                            steps.Add("COMMIT acknowledged. Read the SQL-created Brew immediately through Cypher.");
                        }
                    }
                    catch
                    {
                        if (session is not null)
                        {
                            try { await db.RollbackTransactionAsync(session, CancellationToken.None); }
                            catch (HttpRequestException) { /* Never report rollback success from this error path. */ }
                            session = null;
                        }
                        throw;
                    }
                }
            }
            var after = await Snapshot(slug, ct);
            if (outcome == "rolled-back" && (!before.Counts.All(x => after.Counts[x.Key] == x.Value) || after.Brew.Length != 0 || after.Note.Length != 0 || after.Edges.Length != 0))
                throw new GraphRequestException(503, "Rollback verification did not match the initial durable state.");
            var cypher = await Query("Read SQL-created Brew through Cypher", "MATCH (b:Brew {slug:$slug}) RETURN elementId(b) AS rid, b.slug AS slug, b.name AS name", new { slug }, ct, "cypher");
            var sameRecord = after.Brew.Length == 1 && cypher.Length == 1 && after.Brew[0].GetProperty("rid").ToString() == cypher[0].GetProperty("rid").ToString();
            return new { outcome, operationId, brewSlug = slug, before, after, staged, steps, sql = after.Brew, cypher, sameRecord, queries = queries.ToArray() };
        }
        finally { gate.Semaphore.Release(); }
    }
    private Task<JsonElement[]> Edge(string type, string fromType, string from, string toType, string to, CancellationToken ct, bool revision = false) =>
        Query($"Create {type}", $"CREATE EDGE {type} FROM (SELECT FROM {fromType} WHERE slug=:from) TO (SELECT FROM {toType} WHERE slug=:to) SET occurredAt='2026-09-14 16:30:00', context='Transaction tasting', location='pour-over-bar'" + (revision ? ", revision=(SELECT FROM RecipeRevision WHERE slug='blueberry-v60-v2')" : ""), new { from, to }, ct);
}
