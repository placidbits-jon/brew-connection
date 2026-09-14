# ArcadeDB Coffee Community Demo

Build a locally orchestrated Angular, ASP.NET Core, and ArcadeDB application that tells a coherent coffee-community story while demonstrating ArcadeDB's graph, document, key/value, full-text, vector, time-series, and geospatial capabilities. Pair every audience-facing capability with a plain-text Markdown slide that explains the value and gives a stable deep link plus an exact action for activating the feature during the live demo.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

## Confirmed Scope and Working Assumptions

- The primary outcome is a 15-20 minute technical demonstration, rather than a production event platform.
- All services and model inference run locally under Aspire; the demo has no cloud dependency.
- The app represents one convention and uses deterministic fictional attendees, vendors, coffees, and interactions.
- Authentication is represented by a demo-persona switcher; production identity and authorization are out of scope.
- Brew telemetry is simulated and does not require physical coffee equipment.
- The ArcadeDB version is pinned only after the compatibility spike proves every required feature against one release.
- `docs/arcadedb-coffee-demo.md` is the Obsidian Slides deck. It uses normal Markdown and `---` between slides, with no YAML frontmatter that could be mistaken for a slide separator.
- Every feature slide includes: the audience takeaway, the ArcadeDB model or query feature, 2-4 talking points, an `Activate demo` link, an exact interaction, and the expected visible result.
- Demo deep links use stable `/demo/...` Angular routes and select deterministic seed records. Reloading a deep link must reconstruct the same starting state.

## Planned User Experience and Data Model

### Primary screens

- `/demo/story` - guided landing page and demo-persona selector.
- `/demo/passport/:personSlug` - coffees tasted, people met, games played, and reconnect targets.
- `/demo/meet/:personSlug` - simulated badge scan that creates a `MET` relationship.
- `/demo/coffee/:roastBatchSlug` - lot, roaster, vendor, brew, recipe, and tasting provenance.
- `/demo/recipes/:recipeSlug` - current structured recipe revision, prior revisions, reactions, and follow action.
- `/demo/discover` - full-text, semantic, hybrid, and personalized discovery modes.
- `/demo/network/:personSlug` - community graph, shortest paths, and recommendation explanations.
- `/demo/brews/:brewSlug` - live simulated telemetry and recipe target comparison.
- `/demo/pulse` - live counters and time-bucketed event activity.
- `/demo/map` - nearby vendors, brewers, games, and availability.
- `/demo/transactions` - intentional failure and rollback demonstration.
- `/demo/lab` - raw query, language, parameters, result, elapsed time, and query-plan inspection.

### Graph records

- Vertex types: `Person`, `Organization`, `Event`, `VenueArea`, `VendorTable`, `CoffeeLot`, `RoastBatch`, `Recipe`, `Brew`, `Game`, and `GameSession`.
- Edge types: `ATTENDED`, `MET`, `WANTS_TO_RECONNECT`, `MEMBER_OF`, `ROASTED`, `SELLS`, `FROM_LOT`, `BREWED`, `USED_BATCH`, `USED_RECIPE`, `TASTED`, `LOVED`, `LIKED_RECIPE`, `PLAYED_IN`, and `BEAT_IN_GAME`.
- Put occurrence-specific properties such as `occurredAt`, location, context, reaction, and score on the relevant edge.
- Model `Brew` and `GameSession` as vertices because each can connect multiple participants, locations, recipes, and outcomes.

### Document records

- `RecipeRevision` stores immutable nested steps, water additions, equipment, grind settings, temperature, and author commentary. `Recipe.currentRevision` links to it.
- `Note` stores owner and subject links, visibility, body, and timestamps.
- `RoastProfile` stores process phases, temperatures, development timing, expected flavors, and narrative.
- `EventConfiguration` stores demo-safe configuration and feature flags.

### Specialized records and indexes

- Full-text indexes cover recipe text, roast narratives, flavor descriptions, public notes, vendors, and venue areas.
- Vector indexes cover normalized text composed from coffee, roast, flavor, and recipe fields; embeddings come from a local model.
- `BrewTelemetry` is a time-series type tagged by brew, brewer, device, and method, with water weight, flow rate, and temperature fields.
- `EventActivity` is a time-series type tagged by event, area, and activity kind, with count and duration fields.
- Key/value entries resolve badge and demo short codes to stable record IDs and hold intentionally transient live counters.
- Durable lookup values use uniquely indexed documents so restart behavior is explicit and testable.
- Venue areas and tables have coordinates suitable for distance and containment queries.

## Slide Contract

Each feature checkpoint adds or updates one slide in `docs/arcadedb-coffee-demo.md` using this shape:

```markdown
## <Audience-facing capability>

**ArcadeDB:** <model, index, transaction, or query language>

- <why this capability matters>
- <what is stored or queried differently>
- <advantage visible in this scenario>

**Activate demo:** [Open <screen>](http://localhost:4200/demo/<stable-route>)

**Action:** <one exact click, selection, scan, search, or toggle>

**Look for:** <deterministic visible result that proves the capability>

---
```

The deck begins with scenario and architecture slides, ends with operational takeaways, and keeps feature slides in the same order as the live walkthrough. Slide links are conveniences; the displayed route and action must still be understandable if Obsidian does not hand the link to a browser.

## Phase 0: Compatibility Spike and Architecture Decisions

Status: Complete

- [x] Identify a specific ArcadeDB image release that supports graph, document, Redis, full-text, vector, native time-series, geospatial, HTTP transactions, Studio, and the required plugins.
- [x] Start that image manually with persistent storage and explicit plugin configuration.
- [x] Prove `/api/v1/health`, database initialization, parameterized HTTP queries, and one commit and rollback transaction.
- [x] Execute minimal SQL or Cypher examples for every planned record type, index, and query language.
- [x] Verify vector dimensions and similarity against the chosen local embedding model.
- [x] Verify full-text ranking, fuzzy search, stemming, and query-plan inspection.
- [x] Verify Redis transient counters separately from persistent indexed-document lookup.
- [x] Verify time-series schema creation, ingestion, time bucketing, and retention behavior.
- [x] Verify geospatial distance or containment query syntax.
- [x] Record all selected versions, ports, plugins, limitations, and fallback decisions in an architecture decision note.
- [x] Create the initial Obsidian deck with title, scenario, architecture, and “why one multi-model database” slides.
- [x] Add a compatibility slide listing the exact proven ArcadeDB capabilities and the `/demo/lab` route that will expose their smoke tests.

### Verification Plan

- Run a checked-in compatibility script against a clean ArcadeDB container; expect every capability probe to pass and the process to exit zero.
- Open `docs/arcadedb-coffee-demo.md` with Obsidian Slides; expect each `---` boundary to render as one slide and all Markdown to remain legible.

### Phase Summary

Pinned `arcadedata/arcadedb:26.9.1` after validating the JVM image with Redis, MongoDB, PostgreSQL, and Gremlin plugins. Added `scripts/verify-arcadedb-compatibility.sh`, which creates a disposable database and proves Cypher graph operations, nested documents, 768-dimensional vector ranking, BM25 full-text search and its query plan, transient and persistent Redis-style access, time-series ingestion and aggregation, geospatial ordering, HTTP commit/rollback, and a deep integrity check. A manual container restart preserved the two seeded coffees. Selected the published 768-dimensional `embeddinggemma` output contract, with the compact local hosting mechanism still isolated behind `IEmbeddingService`. Recorded version-specific HTTP and DDL findings in `docs/architecture-decisions.md` and created the initial Obsidian deck in `docs/arcadedb-coffee-demo.md`. Verification passed on 2026-09-14; deck syntax follows Obsidian's documented Markdown and `---` separator contract, with visual rehearsal retained for Phase 8.

## Phase 1: Aspire and Application Foundation

Status: Complete

- [x] Create the Aspire AppHost and configure the pinned ArcadeDB container, database volume, ports, credentials, health checks, and startup dependencies.
- [x] Create the ASP.NET Core API, Angular application, local embedding resource, and telemetry simulator as AppHost resources.
- [x] Implement an API-only `ArcadeDbClient` over `HttpClient` for parameterized query, command, and transaction requests.
- [x] Add readiness endpoints that distinguish process health, database readiness, schema readiness, and embedding-model readiness.
- [x] Add a schema migration runner and an idempotent seed runner.
- [x] Add a demo-state reset command that restores deterministic story data without rebuilding developer tooling.
- [x] Create the `/demo/story` screen with service status, persona selection, reset control, and links to every demo route.
- [x] Add a foundation slide whose activation link opens `/demo/story` and whose action reveals the Aspire resource state.

### Verification Plan

- Run the documented single startup command; expect Aspire to report all resources healthy and `/demo/story` to load.
- Restart ArcadeDB; expect persistent data to survive and migrations to remain idempotent.
- Activate the foundation slide's link and action; expect the status shown in the UI to match Aspire resource health.

### Phase Summary

Created a file-based Aspire 13.5.3 AppHost that orchestrates the pinned ArcadeDB container with persistent storage and all demo protocol endpoints, ASP.NET Core API, Angular 22 application, deterministic 768-dimensional embedding service, and an idle telemetry simulator. The API now owns an HTTP-only ArcadeDB client with parameterized query/command and transaction-session operations, independent migration and seed checks, readiness health checks, and a database reset endpoint. The responsive `/demo/story` screen provides persona selection, stable links for the planned walkthrough, live resource state, and reset controls. Added a matching Obsidian foundation slide and developer README. Validation on 2026-09-14: every Aspire resource reached healthy state; API and Angular builds passed; Angular tests passed 2/2; reset returned ready state; the migration marker survived an ArcadeDB restart; and the embedding service returned exactly 768 dimensions. Browser automation was unavailable in the current tool session, so final visual rehearsal remains in Phase 8; the live route returned HTTP 200 and its compiled interaction is backed by the verified status API.

## Phase 2: Schema and Deterministic Story Data

Status: Complete

- [x] Implement the graph, document, index, time-series, and geospatial schema defined above.
- [x] Add unique, hash, ordered, full-text, and vector indexes appropriate to their access patterns.
- [x] Create a `story` seed profile with roughly 40 people, 8 vendors, 25 roast batches, 20 recipes, 100 brews, and authored relationship chains.
- [x] Seed deliberate keyword-only, semantic-only, graph-personalized, provenance, game-rematch, and telemetry scenarios.
- [x] Create a reproducible `scale` profile with thousands of people and coffees, tens of thousands of embeddings, and millions of telemetry samples.
- [x] Store stable slugs separately from ArcadeDB RIDs so slide links survive reseeding.
- [x] Create a schema slide linked to `/demo/lab?view=schema` and a seed-story slide linked to `/demo/story?persona=maya`.

### Verification Plan

- Reset and seed each profile twice; expect stable counts, slugs, authored outcomes, and no duplicate indexed keys.
- Execute schema introspection and representative indexed queries; expect every planned type and index to exist.
- Activate both phase slides; expect the schema explorer and named persona story to display deterministic records.

### Phase Summary

Implemented migration `002-community` with all planned vertex, edge, document, native time-series, and geospatial types, plus logical hash, ordered, full-text, and 768-dimensional vector indexes. The story seed contains 40 people, 8 vendors, 25 roast batches, 20 recipes (two immutable revision documents each), 100 brews, 45 indexed embeddings, and 6,000 telemetry samples. The scale seed retains the authored story and grows to 2,040 people, 2,025 batches, 20,045 indexed embeddings, and 2,000,000 telemetry samples. Stable slugs, durable badges and short codes, linked recipe revisions, attributed game results, provenance, notes, and the fixed 30-second pour-rate spike are seeded reproducibly.

Added read-only `/api/demo/schema` and `/api/demo/story` endpoints, a live schema explorer at `/demo/lab?view=schema`, and a seeded persona summary at `/demo/story?persona=maya`. Persona changes update the URL and reload correctly. Meetings appear for both participants. Added the two matching Obsidian slides and `docs/seed-profiles.md` with reset and verification commands. Reset accepts only `story` or `scale`; startup preserves completed current seeds and rebuilds foundation or interrupted seeds. Database HTTP retries are disabled to avoid replaying mutations.

Decisions for later phases: the local foundation embedding provider remains explicitly labeled `deterministic-compatibility`. Phase 2 seeds and verifies the keyword distractor, synonym-rich Summer Orchard candidate excluded from the `blueberry` keyword results, and Priya's graph favorite; semantic ranking and its golden-query proof still belong to Phase 5. These fixtures do not claim that token-hash vectors provide semantic inference. Native time-series retention is omitted for the fixed rehearsal epoch so seed samples survive future rehearsals; Phase 6 owns retention examples. Notes index `ownerSlug` plus visibility while retaining owner links because ArcadeDB 26.9.1 rejects a linked vertex as a hash key.

Verification on 2026-09-14: both profiles reset and seeded twice with stable full slug snapshots, exact database counts, no duplicate keys, and actual native telemetry COUNT of 6,000 / 2,000,000. Schema introspection, hash/full-text query plans, vector lookup, geospatial ordering, immutable revision provenance, game scores, short-code resolution, and the telemetry anomaly passed. API restart preserved the scale seed; an intentionally incomplete marker and rogue record triggered a clean story rebuild on restart. Angular tests passed 4/4, production build passed, and real Chromium checks passed both slide interactions, persona selection/reload, schema details, and mobile width with no browser errors. `scripts/verify-demo-checkpoints.py` also passed incoming/outgoing meetings, missing-persona 404, invalid-profile 400 without data changes, and logical schema indexes. The local Aspire session required `ASPIRE_DCP_USE_DEVELOPER_CERTIFICATE=false` due to DCP's developer-certificate root-CA error; the workaround is documented. Phase 3 remains untouched.

## Phase 3: Community Graph and Coffee Passport

Status: Complete

- [x] Implement meeting capture with a simulated badge scan and an attributed `MET` edge.
- [x] Implement tasting, love, reconnect, game session, and game-result commands.
- [x] Implement `/demo/passport/:personSlug` as a timeline of coffee, people, and game interactions.
- [x] Implement `/demo/network/:personSlug` with direct connections, multi-hop paths, shared interests, and rematch candidates.
- [x] Add a query-inspection drawer that shows the Cypher or SQL used for each graph result.
- [x] Add slides for “record a meeting,” “coffee passport,” “who beat me,” “shortest social path,” and “people to reconnect with.”
- [x] Give every graph slide a stable persona-specific deep link, one exact action, and a named expected vertex or relationship.

### Verification Plan

- Run graph integration tests against ArcadeDB; expect the authored meeting, tasting, game, and shortest-path results.
- Trigger each mutation twice where idempotency is expected; expect no unintended duplicate edge.
- Follow every graph slide in deck order; expect each action to reveal its stated deterministic result.

### Phase Summary

Implemented the graph API and Angular screens at `/demo/meet/:personSlug`, `/demo/passport/:personSlug`, and `/demo/network/:personSlug`. Badge scans resolve persistent indexed badges and record attributed meetings visible from both participants. Added tasting, love, reconnect, game-session, and game-result commands. Writes use HTTP transactions and a shared local gate with graph reads and reset; repeated scans, reactions, session creation, and matching results are idempotent. Conflicting participants/results and invalid input receive explicit errors. No schema version change or seed rebuild is required for this phase.

The passport combines meetings, cups, reactions, reconnect intent, participation, and viewer-relative game wins/losses, including scores and session metadata. The network shows direct meetings, a shortest social path, shared interests, rematch candidates, and reconnect context. Shortest paths use breadth-first traversal in the API over SQL-loaded `MET` edges (bidirectional); the query-inspection drawer explicitly identifies that computation rather than claiming a native shortest-path query. Drawers display executed SQL/Cypher and parameters and retain the most recent mutation statements. Invalid path targets remain editable, and scans show persisted names, context, and location.

Added five Obsidian slides for meeting capture, coffee passport, rematches, shortest paths, and reconnects, plus `docs/community-graph.md`. The meeting slide uses `badge-0003` so the authored Maya → Priya → Luis path remains two hops throughout the walkthrough. The passport defaults to the existing Blueberry Bloom brew and a new `demo-coffee-cards` session against Priya.

Verification on 2026-09-14: `scripts/verify-community-graph.py --reset` passed against ArcadeDB, including four concurrent copies of all six commands, reverse scans, two- and three-hop paths, disconnected targets, both game perspectives, missing-record 404, invalid/null/blank input 400, and conflicting-session/result 409. Angular tests passed 15/15; API and production frontend builds passed without warnings. Passport timestamps normalize to UTC ISO 8601; real browser checks verified the 16:00 UTC seed renders at noon in Detroit and an invalid path target can be corrected without leaving the page. Real Chromium rehearsal passed all five slide actions, all mutation forms, repeated badge scanning, actual query inspection, route reload, mobile layout, and missing-persona errors. Code review findings were fixed and scoped re-review passed. Restored the story seed, reran Phase 2 seed/checkpoint regression checks successfully, and stopped Aspire. Phase 4 remains untouched.

## Phase 4: Documents, Recipes, Notes, and Provenance

Status: Complete

- [x] Implement structured recipe creation and immutable revision publishing.
- [x] Implement plain-text notes attached by links to people, brews, roast batches, recipes, and game sessions.
- [x] Implement coffee provenance from lot through roast, vendor, brew, recipe revision, and attendee reaction.
- [x] Implement `/demo/recipes/:recipeSlug` with nested recipe steps and revision comparison.
- [x] Implement `/demo/coffee/:roastBatchSlug` with an interactive provenance graph and document detail panels.
- [x] Demonstrate that graph vertices retain flexible document properties while explicit document records support embedded structures and revisions.
- [x] Add slides for “structured recipe,” “plain-text memory,” “recipe revision,” and “bean-to-cup provenance.”

### Verification Plan

- Publish a new recipe revision; expect the old document to remain immutable and `Recipe.currentRevision` to change atomically.
- Query notes by owner and linked subject; expect private notes to remain visible only to the selected demo persona.
- Follow the provenance slide link and action; expect the authored lot, roaster, vendor, brewer, recipe, and reaction chain.
- Run each document slide checkpoint; expect its nested or linked fields to match the slide's talking points.

### Phase Summary

Implemented `CommunityDocuments` and its API endpoints for structured recipe creation, immutable revision publication, typed linked notes, and coffee provenance. Publication validates the nested recipe, uses an expected-revision check plus content/author fingerprint for retry handling, and inserts the revision and advances `Recipe.currentRevision` in one HTTP transaction. It shares the graph/reset gate. Existing revision documents and `USED_RECIPE.revision` links remain unchanged. Explicit link-ID projections avoid ArcadeDB recursive serialization of the recipe/revision cycle. No schema or seed-version change was required.

Added `/demo/recipes/:recipeSlug` with editable pour steps, equipment, grind, temperature, quantities, commentary, new-recipe creation, prior revision comparison, and nested document inspection. Added `/demo/coffee/:roastBatchSlug` with a selected-brew graph and clickable record panels, including the lot, roaster, vendor, brewer, roast profile, recipe, pinned revision, and tasting/love relationships. The graph uses actual relationship directions and associates the pinned revision with the brew rather than the recipe's mutable current link.

Notes link owners and subjects for Person, Brew, RoastBatch, Recipe, and GameSession. API visibility includes public notes and the selected persona's private notes; private note text is excluded from searchText. The UI preserves the note subject during persona changes while clearing drafts, private data, and query details. It cancels stale reads and ignores late mutation responses from prior route/persona contexts; note retry keys also rotate when context changes. This remains a local demo persona model, not production authentication.

Added the four requested Obsidian slides and `docs/recipe-documents.md`. Verification on 2026-09-14: document integration passed unchanged historical snapshots, four concurrent identical retries, competing publications with exactly one success and one conflict, nested validation errors, notes on all five subject types, private-body leak checks, and the original brew's pinned revision 2 after current revision 3. All 23 Angular tests passed, including persona-navigation races. Real Chromium rehearsal passed all four slides, nested editing/comparison, recipe creation/reload, same-subject note privacy, escaped plain text, graph selection, mobile layout, and query inspection. Scoped code review has no remaining findings. Phase 3 graph integration and Phase 2 seed/checkpoint regressions also passed. Final .NET solution and production Angular builds passed without warnings. Story data was restored and Aspire stopped. Search ranking and embedding updates remain Phase 5 work.

## Phase 5: Full-Text, Vector, and Graph-Aware Discovery

Status: Complete

- [x] Build canonical searchable text for roast batches and recipes and generate embeddings locally during seeding and publishing.
- [x] Implement full-text search with relevance, phrase, fuzzy, stemming, autocomplete, and “more like this” examples.
- [x] Implement vector-neighbor search with selected dimensions, similarity, filtering, and visible distance scores.
- [x] Implement hybrid ranking with explicit full-text, vector, and graph-personalization contributions.
- [x] Implement `/demo/discover` modes that reuse the same query while showing how each retrieval strategy changes the ranking.
- [x] Add recommendation explanations based on actual paths, similarities, and availability instead of generated claims.
- [x] Add a local-model natural-language entry point only after deterministic retrieval and explanations work without an LLM.
- [x] Add slides for “keyword search,” “semantic search,” “hybrid ranking,” and “find my next coffee.”
- [x] Use a stable query on every discovery slide and state the exact result that moves or appears when the mode changes.

### Verification Plan

- Run a golden-query suite; expect the keyword-only, semantic-only, and graph-personalized records to appear in their intended modes.
- Inspect query plans; expect full-text and vector indexes rather than collection scans.
- Recreate embeddings from a clean seed; expect deterministic dimensions and stable top results within defined ranking tolerances.
- Present the discovery slides in sequence; expect the same query to illustrate each retrieval contribution visibly.

### Phase Summary

Implemented `/demo/discover` with URL-preserved query, persona, type, availability, and four retrieval modes. Lucene supports relevance, phrase, fuzzy spelling, English stemming, autocomplete, and native more-like-this queries. ArcadeDB's LSM_VECTOR cosine index supplies real 768-dimensional neighbors; aliases are grouped by subject before the HTTP row limit, filters apply before the final top 20, and stable slug ordering breaks ties. The API exposes exact keyword, vector, and community contributions, actual vendor availability and acquaintance/favorite paths, executed SQL/Cypher, and index plans. Hybrid ranking uses 0.35 × normalized BM25 + 0.65 × cosine similarity; personalized ranking adds 0.50 once for a qualifying available acquaintance favorite.

Replaced compatibility hashes with pinned local EmbeddingGemma through an Aspire-managed Ollama executable. Required embedding readiness precedes the optional Qwen download, so all four retrieval modes work without a language-model answer. The optional natural-language control returns a visible editable query and uses the same database retrieval/explanations. Models cache in ignored `.aspire/models`; first startup downloads approximately 1.15 GB. Setup, digests, inference checks, and offline reuse are documented in `docs/local-models.md`.

Both indexes consume the same public canonical text, bounded to 1,500 UTF-8 bytes without splitting Unicode runes; complete immutable recipe documents remain unchanged. Seed and in-place upgrade paths refresh real vectors, preserve existing revisions/notes, and mark the index version only when complete. Publication updates current-revision links, canonical text, and type-qualified embedding records in the same transaction. Migration retries retain the original roast description. Scale aliases retain their count and canonical subject vectors.

Added four Obsidian slides and `docs/discovery.md`. The stable query `blueberry` yields Ethiopia Blueberry Bloom first in keyword mode; Summer Orchard appears at semantic rank 4; Ethiopia moves from semantic rank 3 to hybrid rank 1; Priya's Honey Stonefruit moves from hybrid rank 5 to personalized rank 1. The slides explicitly acknowledge that the dark roast ranks first for the single-word semantic query. The inspector labels API ranking and filtering rather than attributing those calculations to a native database query.

Verification on 2026-09-14: the discovery suite passed clean story reseeding with identical rank order and distance tolerance 1e-5, all full-text examples, actual index plans, filtering, validation, and create/publish index updates. The scale profile passed with 20,045 embedding records and 2,000,000 telemetry samples, preserving the authored mode outcomes. Migration verification passed two forced in-place rebuilds with identical vectors and preserved user revisions/source text; a late embedding uniqueness failure rolled back both the recipe and revision. Unavailable vendors correctly removed coffee results from the availability filter and removed the graph boost. Long Unicode documents and recipe slugs matching legacy embedding IDs passed.

Real model checks passed digest pins, finite normalized 768-dimensional vectors, repeatability, batch inference, semantic contrast, bounded request validation, and optional Qwen interpretation. All 28 Angular tests passed, including query persistence, failed-search recovery, and stale-helper-response protection. Real Chromium rehearsal passed all four slides, exact ranking movements, query plans/evidence, helper use, reload, type filtering, error recovery, and mobile layout without console errors. Scoped review has no remaining substantive findings. Phase 4 document, Phase 3 graph, and Phase 2 seed/checkpoint regressions passed. Final .NET solution and production Angular builds passed without warnings. Story data was restored and Aspire stopped. Phase 6 remains untouched.

## Phase 6: Key/Value, Time-Series, and Geospatial Features

Status: Complete

- [x] Implement stable badge and short-code lookup using indexed persistent records.
- [x] Implement intentionally transient event counters through ArcadeDB's Redis support and label their restart behavior in the UI.
- [x] Implement simulated brew telemetry ingestion and a target-versus-actual chart on `/demo/brews/:brewSlug`.
- [x] Implement time-bucketed activity, percentile, rate, retention, and downsampling examples on `/demo/pulse`.
- [x] Implement venue coordinates and nearby/contained-in-area queries on `/demo/map`.
- [x] Correlate time-series tags and key/value results back to stable graph records in API responses.
- [x] Add slides for “badge lookup,” “live counter,” “brew curve,” “event pulse,” and “nearby coffee.”
- [x] Include the expected counter delta, named telemetry anomaly, or nearest location on each specialized-model slide.

### Verification Plan

- Resolve known badge codes; expect constant stable targets and clear not-found behavior.
- Increment a transient counter and restart the container; expect the UI and slide explanation to agree about reset behavior.
- Replay the authored brew series; expect the specified pour-rate spike and target deviation at fixed timestamps.
- Run time-window and venue-distance tests; expect exact buckets and ordering for seeded inputs.
- Activate all specialized-model slides; expect every stated counter, chart feature, and nearby result.

### Phase Summary

Implemented persistent code lookup, transient live counters, native time-series views and replay, and geospatial discovery. `/demo/lookup` resolves `badge-0001` to Priya and `short-blueberry` to Ethiopia Blueberry Bloom through the unique `BadgeLookup.slug` index and stored target links. `/demo/map` uses indexed `geo.within` containment plus native `geo.distance` in meters, then follows `SELLS` links to coffees. Default results put Great Lakes Coffee Table at 0.0 m and Vendor Table 1 at approximately 8.2 m. The map labels its approximate drawing projection and the shared convention boundary used by all three seed areas.

The event counter uses native Redis `GET`/`INCR` via ArcadeDB's authenticated HTTP command executor. The pinned TCP plugin was observed holding separate maps per connection; HTTP execution provides the verified shared server-memory map. Counter writes inherit the database client's no-retry behavior to avoid duplicate increments after ambiguous responses. The counter survives API restart, resets on ArcadeDB restart, and remains separate from durable historical activity. Database reset alone does not clear the server's Redis map. No additional Redis server, client package, or AppHost wiring was required.

`/demo/brews/:brewSlug` shows measured water/flow against the pinned immutable recipe. Water targets interpolate its steps in the API; flow target is stored on the brew. At second 30, measured flow is 12 versus 4 g/s; water is 120 versus target 140 g, deviation −20 g. Actual stored samples determine anomaly explanations. A replay queues a durable uniquely indexed `TelemetryRun`; the Aspire worker posts ten deterministic samples per half-second chunk through native line-protocol ingestion. The API validates bounded partial batches, reconciles existing timestamps under the shared reset gate, and marks completion only after all 60 points exist. Each explicit UI replay creates a new run; retries retain the pending identity. The chart polls only unfinished runs and waits for the selected sample instead of labeling an earlier measurement with a later timestamp.

`/demo/pulse` uses native time buckets, percentile, cumulative-water rate, and query-time downsampling. Golden results are 120 minute samples, 360 events, twelve ten-minute buckets of 30, percentile 5, event rate 3/minute (API division of native SUM), native water rate 4 g/s, and four 30-minute totals of 90 with means of 3. An isolated `DemoRetention` type uses one-day retention and one-minute compaction boundaries; native maintenance removes its two-day-old sample while preserving the recent sample and all authored telemetry. Retention works at sealed-block granularity, which is documented. SQL reads over native time-series storage avoid an observed 26.9.1 raw HTTP projection/tag bug; native HTTP ingestion remains in use. Inspectors expose executed statements, parameters and verified native time-series/index plans.

Added five Obsidian slides with exact actions and outcomes, `docs/telemetry.md`, `docs/keys-and-locations.md`, and README checkpoints. Verification on 2026-09-14: key/location integration passed indexed stable lookup, invalid/missing codes, finite coordinate/radius validation, containment/distance plans, linked offers, four concurrent increments, API-restart preservation, and database-restart counter loss with persistent lookup survival. Telemetry integration passed golden samples and aggregates, partial and concurrent replay retries without duplicates, null-sample rejection, native tag/range execution plans, and native retention 2 → 1 without deleting story data. Real Chromium passed all five slide flows, two fresh progressive replays and reload, editable sample inspection, bucket draft isolation and URL persistence, lookup/map error recovery, marker selection and radius filtering, and all four pages at mobile width. Review findings were corrected and scoped re-review found no remaining backend issues; the final selected-sample display correction has a regression test.

All 35 Angular tests and the production Angular build passed. Phase 4 document, Phase 3 graph, Phase 5 discovery, and Phase 2 seed/checkpoint regressions passed. Final .NET solution build passed with zero warnings/errors, and `git diff --check` passed. Story data was restored and Aspire stopped. Phase 6 changes remain uncommitted; Phase 7 is untouched.

## Phase 7: Transactions, Polyglot Queries, and Demo Lab

Status: Not started

- [ ] Implement `/demo/transactions` with a successful tasting transaction and a controlled halfway failure that rolls back all durable changes.
- [ ] Implement `/demo/lab` with curated, read-only SQL, Cypher, Redis, vector, full-text, time-series, and geospatial examples.
- [ ] Display the selected query language, parameter values, returned records, execution time, and safe query-plan output.
- [ ] Demonstrate that records created through one query language are immediately visible through another supported language.
- [ ] Prevent arbitrary mutating statements from being submitted through the demo lab.
- [ ] Add slides for “one ACID transaction,” “rollback,” “same data through multiple languages,” and “inspect the query plan.”

### Verification Plan

- Capture counts before and after the controlled failure; expect all durable counts and relationships to remain unchanged.
- Execute the curated lab catalog against a clean story seed; expect each example to return its documented result.
- Run security tests against the lab endpoint; expect unlisted or mutating input to be rejected.
- Activate each lab slide; expect the language and result displayed on screen to match the slide.

### Phase Summary

_(write when phase completes)_

## Phase 8: Slide-Link Automation and Rehearsal

Status: Not started

- [ ] Finalize `docs/arcadedb-coffee-demo.md` in the exact order of the 15-20 minute walkthrough.
- [ ] Keep each feature slide focused on one audience takeaway and one activation action.
- [ ] Add a closing slide covering operational simplification, avoided synchronization, ACID behavior, and appropriate limitations.
- [ ] Add a checked-in validator that parses every `Activate demo` URL from the deck and verifies it against the Angular demo-route manifest.
- [ ] Add a browser smoke test that opens each slide link from a clean story seed, performs the documented action, and asserts the expected visible result.
- [ ] Add deck checks for missing `ArcadeDB`, `Activate demo`, `Action`, or `Look for` fields on feature slides.
- [ ] Add a presenter runbook with startup, reset, fallback, timing, and recovery instructions.
- [ ] Add a slide-to-feature traceability table to the runbook, including route, seed dependency, API endpoint, ArcadeDB capability, and automated test.
- [ ] Rehearse the complete deck twice from a clean machine state and record actual duration and any manual recovery steps.

### Verification Plan

- Run the deck validator; expect zero malformed separators, missing slide fields, unknown routes, or duplicate activation identifiers.
- Run the slide-link browser suite; expect every route, action, and deterministic assertion to pass.
- Open the file in Obsidian and start the Slides presentation; expect readable Markdown, one intended slide per separator, working navigation, and usable HTTP links.
- Execute the presenter runbook from stopped services; expect the full walkthrough to complete within 20 minutes without editing data by hand.

### Phase Summary

_(write when phase completes)_

## Final Recap

_(write when all phases complete: summary of the entire piece of work)_

## Deployment Plan

_(write when all phases complete: step-by-step local startup, seeding, presentation, reset, and teardown instructions)_
