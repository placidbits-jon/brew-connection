# Coffee, Community, and One Multi-Model Database

ArcadeDB at a tech convention's community coffee space

**Story:** Remember the people, coffees, recipes, and moments worth finding again.

---

## One event, many connected questions

- Who did I meet, and why did I want to reconnect?
- Which coffee did I love, who roasted it, and who brewed it?
- What recipe produced that cup?
- Who beat me at a game?
- What should I try next?

---

## One application, seven access patterns

**ArcadeDB:** Graph, document, key/value, full-text, vector, time-series, and geospatial

- Relationships preserve the community story.
- Documents preserve flexible recipes, profiles, and notes.
- Specialized indexes answer search, similarity, telemetry, and proximity questions.
- One engine removes cross-database synchronization from the demo.

---

## Local architecture

```text
Angular -> ASP.NET Core -> ArcadeDB HTTP/JSON
                         -> SQL / Cypher / Redis
          Local embedding service -> 768-d vectors
          Telemetry simulator -> time-series ingestion

Aspire starts, connects, observes, and resets every resource.
```

---

## A repeatable local stage

**ArcadeDB:** Persistent container, HTTP API, migration marker, and deterministic seed

- Aspire starts ArcadeDB 26.9.1, the API, Angular, embeddings, and telemetry together.
- The API separates process, database, schema, seed, and embedding readiness.
- Reset rebuilds only the named demo database, so every presentation begins at the same point.

**Activate demo:** [Open the story screen](http://localhost:4200/demo/story)

**Action:** Select **Reveal Aspire resource state**.

**Look for:** Four green status indicators for the app, ArcadeDB, schema, and local embeddings.

---

## One community, many record types

**ArcadeDB:** Vertex, edge, document, time-series, and specialized indexes

- Stable slugs identify people and coffees across resets.
- Graph links connect the story; nested documents retain recipe details.
- Full-text, vector, ordered, hash, and geospatial indexes serve different questions.

**Activate demo:** [Open the schema explorer](http://localhost:4200/demo/lab?view=schema)

**Action:** Select **Person** in the type list.

**Look for:** The live `Person` vertex type, its `slug` property, and the unique `Person[slug]` hash index.

---

## A repeatable community story

**ArcadeDB:** Deterministic vertices, attributed edges, and linked documents

- Maya meets Priya and tastes her Blueberry Bloom V60.
- Luis beats Maya at Coffee Cards, creating a reason to reconnect.
- The story profile begins with 40 people, 8 vendors, 25 roast batches, 20 recipes, and 100 brews.

**Activate demo:** [Open Maya's story](http://localhost:4200/demo/story?persona=maya)

**Action:** Select **Maya Chen · attendee** in **Explore as** and read **A story you can follow**.

**Look for:** Priya Nair under **People met**, Priya's Blueberry Bloom V60 under **Cups tasted**, and Luis Ortega under **Rematch targets**.

---

## Record a meeting

**ArcadeDB:** Persistent badge lookup and an attributed `MET` edge

- A badge resolves to a person using a stable indexed key.
- The meeting records where and why two people connected.
- Repeating the same scan keeps one relationship.

**Activate demo:** [Scan a badge as Maya](http://localhost:4200/demo/meet/maya-chen)

**Action:** Leave badge code `badge-0003` selected and click **Scan badge**.

**Look for:** Maya meets Attendee 0003, with the meeting context and location saved. Scanning again does not add another meeting.

---

## Your coffee passport

**ArcadeDB:** Traversals over tasting, love, meeting, and game edges

- One timeline brings together cups, conversations, and games.
- Reactions belong to the relationship between a person and a brew.
- Query inspection exposes the statements behind the timeline.

**Activate demo:** [Open Maya's passport](http://localhost:4200/demo/passport/maya-chen)

**Action:** Click **Love this cup** for the default `blueberry-bloom-v60` brew, then **Inspect queries**.

**Look for:** Maya's love for Priya's Blueberry Bloom V60 appears in the timeline, alongside the executed query and parameters.

---

## Who beat me?

**ArcadeDB:** Game session vertices and scored `BEAT_IN_GAME` edges

- A session groups participants and an outcome.
- Scores remain attached to the game relationship.
- A loss becomes an explicit rematch candidate.

**Activate demo:** [Open Maya's community network](http://localhost:4200/demo/network/maya-chen?target=luis-ortega)

**Action:** Select **Show rematch candidates**.

**Look for:** Luis Ortega beat Maya 10–7 in `maya-luis-rematch`.

---

## The shortest social path

**ArcadeDB:** Multi-hop traversal through the `MET` graph

- Maya knows Priya, and Priya knows Luis.
- The path explains how to make an introduction.
- The result comes from the current relationship graph.

**Activate demo:** [Find Maya's path to Luis](http://localhost:4200/demo/network/maya-chen?target=luis-ortega)

**Action:** Click **Find shortest path** with Luis Ortega selected.

**Look for:** Maya Chen → Priya Nair → Luis Ortega, two meeting hops.

---

## People to reconnect with

**ArcadeDB:** Attributed `WANTS_TO_RECONNECT` edges and shared interests

- Explicit intent keeps useful conversations from getting lost.
- Shared interests provide a reason to reconnect.
- Saving the same target again does not duplicate that intent.

**Activate demo:** [Open Maya's reconnect targets](http://localhost:4200/demo/network/maya-chen?target=luis-ortega)

**Action:** Select **Show reconnect targets**.

**Look for:** Priya Nair remains Maya's reconnect target for the blueberry recipe.

---

## A structured recipe

**ArcadeDB:** A recipe vertex linked to a nested `RecipeRevision` document

- Steps retain timing, water amounts, and instructions together.
- Equipment, grind, temperature, and commentary remain structured fields.
- The vertex links the recipe into the community graph.

**Activate demo:** [Open the Blueberry Bloom recipe](http://localhost:4200/demo/recipes/blueberry-v60?persona=maya-chen)

**Action:** Select **Revision 1** in **Compare prior revision**, then inspect **Current nested recipe document**.

**Look for:** Revision 2 with three pour steps, 300 grams of water, 93°C, and a 20-click grind setting.

---

## A plain-text memory

**ArcadeDB:** A note document with owner and subject links

- Notes can refer to a person, brew, roast batch, recipe, or game session.
- Private memories belong to the selected demo persona.
- Public notes can be shared without exposing private text.

**Activate demo:** [Remember Maya's cup](http://localhost:4200/demo/coffee/ethiopia-blueberry-bloom?persona=maya-chen)

**Action:** Set **About** to **Brew** and **Subject slug** to `blueberry-bloom-v60`, then click **Load subject notes**. Enter `Ask Priya about the sweeter finish.` in **Note**, leave visibility private, and click **Save note**. Switch **Demo persona** to Priya Nair.

**Look for:** Maya sees the saved memory; Priya does not see Maya's private note for the same brew.

---

## Publish without losing history

**ArcadeDB:** Immutable revision documents and an atomic current-revision link

- Publication adds a document instead of overwriting the earlier recipe.
- Comparison keeps earlier settings available.
- Existing brews keep their original revision link.

**Activate demo:** [Publish the next recipe revision](http://localhost:4200/demo/recipes/blueberry-v60?persona=maya-chen)

**Action:** Change **Grind clicks** to `18` and click **Publish revision**. Select revision 2 in **Compare prior revision**.

**Look for:** Current revision 3 uses 18 clicks; revision 2 still has 20 clicks and its original nested steps.

---

## Bean-to-cup provenance

**ArcadeDB:** Connected vertices, relationship attributes, and linked documents

- The origin lot connects to the roast, vendor, brewer, recipe, and taster.
- Selecting a node reveals its stored properties.
- The brew's pinned revision preserves what actually produced the cup.

**Activate demo:** [Trace Blueberry Bloom](http://localhost:4200/demo/coffee/ethiopia-blueberry-bloom?persona=maya-chen)

**Action:** Select the **Pinned revision 2** node for Priya's Blueberry Bloom V60.

**Look for:** Ethiopia Guji Lot 17, Great Lakes Roasters, Great Lakes Coffee Table, Priya's brew, and Maya's reaction. The selected revision remains 2 even after publication advances the recipe to revision 3.

---

## Keyword search

**ArcadeDB:** Lucene full-text index with BM25 relevance

- Search the same canonical public text used to generate vectors.
- Inspect phrase, fuzzy spelling, stemming, autocomplete, and more-like-this examples.

**Activate demo:** [Match blueberry](http://localhost:4200/demo/discover?query=blueberry&mode=keyword&persona=maya-chen&availableOnly=true)

**Action:** Keep `blueberry` in the search box and select **Keyword**.

**Look for:** Ethiopia Blueberry Bloom ranks first. Blueberry Label Dark Roast also matches, despite its smoky profile. Summer Orchard is absent because its description uses berry nectar and blossom instead.

---

## Semantic search

**ArcadeDB:** Indexed cosine neighbors over 768-dimensional local EmbeddingGemma vectors

- Match related meanings, including descriptions without the literal query word.
- Each result shows its actual cosine distance; lower is closer.

**Activate demo:** [Find related meanings](http://localhost:4200/demo/discover?query=blueberry&mode=semantic&persona=maya-chen&availableOnly=true)

**Action:** Select **Semantic**, keeping the query `blueberry`.

**Look for:** Summer Orchard appears at rank 4 with zero keyword contribution. Blueberry Label Dark Roast is first: semantic retrieval adds related candidates, but this single-word query does not express a roast preference.

---

## Hybrid ranking

**ArcadeDB:** Full-text and vector indexes feeding an explicit combined score

- The API combines 0.35 × normalized BM25 and 0.65 × cosine similarity.
- Contributions are visible instead of hidden behind a generated answer.

**Activate demo:** [Combine words and meaning](http://localhost:4200/demo/discover?query=blueberry&mode=hybrid&persona=maya-chen&availableOnly=true)

**Action:** Select **Hybrid**, keeping `blueberry`, then inspect the first result's score contributions.

**Look for:** Ethiopia Blueberry Bloom moves from semantic rank 3 to hybrid rank 1. Summer Orchard remains in the results through its vector contribution.

---

## Find my next coffee

**ArcadeDB:** Hybrid candidates plus actual acquaintance → favorite brew → available coffee paths

- A qualifying community path adds 0.50 to the combined score.
- Recommendations show who loved the brew and where its coffee is available.
- The optional local language model only rewrites the query; database retrieval supplies every result and explanation.

**Activate demo:** [Ask Maya's community](http://localhost:4200/demo/discover?query=blueberry&mode=personalized&persona=maya-chen&availableOnly=true)

**Action:** Select **Personalized**, keeping `blueberry`. Read the community evidence on the first result.

**Look for:** Priya's Honey Stonefruit moves from hybrid rank 5 to personalized rank 1. Its explanation follows Maya — MET → Priya — LOVED → a brew — USED_BATCH → this coffee, with an available vendor.

---

## One code, one community record

**ArcadeDB:** Persistent uniquely indexed key/value documents

- A badge resolves directly to the attendee's graph record.
- A short code resolves to a coffee with provenance.
- Both mappings survive a database-container restart.

**Activate demo:** [Look up Priya's badge](http://localhost:4200/demo/lookup?code=badge-0001)

**Action:** Click **Look up code**, then follow **Priya Nair**.

**Look for:** `badge-0001` resolves to Priya's passport. The `short-blueberry` example resolves to Ethiopia Blueberry Bloom.

---

## One more tasting

**ArcadeDB:** Transient Redis `GET` and `INCR` commands through the HTTP executor

- The live counter is intentionally held in the database server's memory.
- The historical time-series totals below are separate durable records.
- Restarting the API preserves the counter; restarting ArcadeDB resets it.

**Activate demo:** [Add a live tasting](http://localhost:4200/demo/pulse)

**Action:** Note the live count, then click **Add one tasting** once.

**Look for:** The live value increases by exactly 1 and shows **Last increment: +1**. The page explicitly labels the database-restart reset behavior.

---

## Watch the pour

**ArcadeDB:** Native time-series ingestion and bounded timestamp/tag queries

- Simulated measurements retain their brew, brewer, device, and method tags.
- The graph supplies the brew's pinned recipe revision.
- Replay ingests a separate run, preserving the seeded measurements.

**Activate demo:** [Replay Blueberry Bloom](http://localhost:4200/demo/brews/blueberry-bloom-v60)

**Action:** Click **Replay simulation** and wait for 60 samples. Keep **Inspect second** at `30`.

**Look for:** The **30-second pour spike** reaches 12 g/s against a 4 g/s target. Measured water is 120 g; the interpolated pinned-recipe target is 140 g, a −20 g deviation.

---

## The event, in time buckets

**ArcadeDB:** Native time buckets, percentile, rate, and query-time downsampling

- Durable minute samples reveal patterns beyond the transient live count.
- Changing the bucket size changes the view while preserving the source samples.
- An isolated retention example demonstrates lifecycle behavior separately from the authored story.

**Activate demo:** [Read the event pulse](http://localhost:4200/demo/pulse?bucketMinutes=10)

**Action:** Keep **Bucket size** at **10 minutes**, click **Update buckets**, and select a bar.

**Look for:** 120 minute samples sum to 360 events. Each of the 12 bars contains 30 events; the 95th percentile is 5, the rate is 3 events/minute, and each 30-minute downsample totals 90.

---

## Nearby coffee, connected

**ArcadeDB:** Indexed polygon containment, native distance in meters, and graph-linked vendor offers

- Search from a coordinate within the convention boundary.
- Distance answers where; the graph answers which coffees are sold there.
- Selecting a marker highlights its vendor and linked coffees.

**Activate demo:** [Find the nearest coffee table](http://localhost:4200/demo/map?latitude=42.3314&longitude=-83.0458&radius=100&area=pour-over-bar)

**Action:** Click **Find nearby**, then select **Great Lakes Coffee Table** on the map or result list.

**Look for:** Great Lakes Coffee Table is first at 0.0 m, inside the selected boundary, with Ethiopia Blueberry Bloom linked beneath it. Vendor Table 1 is approximately 8.2 m away.

---

## One ACID tasting transaction

**ArcadeDB:** A single HTTP transaction session spanning SQL vertex, document, and edge writes

- The brew, public note, and four relationships commit together.
- The recipe link pins the immutable revision used for the tasting.
- Repeating the same operation does not duplicate the tasting.

**Activate demo:** [Commit the complete tasting](http://localhost:4200/demo/transactions?scenario=commit)

**Action:** Click **Run successful tasting**.

**Look for:** **Committed** and a +1 change for Brew, Note, BREWED, USED_BATCH, USED_RECIPE, and TASTED. SQL and Cypher show the same new record ID. A repeat reports **Already committed** with zero new changes.

---

## What if it fails halfway?

**ArcadeDB:** Rollback of staged vertex and relationship writes

- The failure scenario uses a separate tasting identity.
- Staged records are visible inside the transaction before rollback.
- Fresh reads verify the rollback after it is acknowledged.

**Activate demo:** [Trigger the controlled failure](http://localhost:4200/demo/transactions?scenario=rollback)

**Action:** Click **Trigger halfway failure**.

**Look for:** **Inside transaction** shows one extra Brew, BREWED edge, and USED_BATCH edge. **After** matches **Before** for every checked graph/document type, and the page reports **Rolled back**. No failed-tasting record survives.

---

## The same record, through another language

**ArcadeDB:** SQL and Cypher over the same stored graph vertex

- SQL creates the transaction's Brew.
- Cypher reads it immediately without a copy or synchronization job.
- SQL `@rid` and Cypher `elementId()` identify the same record.

**Activate demo:** [Read the committed tasting](http://localhost:4200/demo/lab?view=queries&example=sql-transaction-brew)

**Action:** Click **Run query** and note the RID. Select **Committed Brew through Cypher**, then click **Run query** again.

**Look for:** Both reads return **Transaction Blueberry V60** with exactly the same RID. If no tasting has been committed yet, the lab gives an explicit empty-state message.

---

## Inspect the query plan

**ArcadeDB:** Native SQL EXPLAIN for a fixed full-text query

- The catalog supplies bounded, read-only statements and fixed parameters.
- Each run shows actual records, database round-trip time, and plan availability.
- SQL, Cypher, Redis, vector, time-series, and geospatial examples share one lab.

**Activate demo:** [Inspect the full-text plan](http://localhost:4200/demo/lab?view=queries&example=fulltext-coffee)

**Action:** Click **Run query** and read **Execution plan**.

**Look for:** The blueberry query returns Ethiopia Blueberry Bloom and an actual **FETCH FROM INDEXED FUNCTION SEARCH_INDEX** plan. The lab shows the fixed `blueberry` parameter and measured execution time; it does not accept arbitrary statements.

---

## Compatibility checkpoint

**ArcadeDB:** Version 26.9.1 with native and plugin query engines

- Seven fixed runtime reads cover SQL, Cypher, Redis GET, full-text, vector, time-series, and geospatial queries.
- Each result includes its returned record count and available native plan.
- Transaction, mutation, and retention checks remain separate demonstrations.

**Activate demo:** [Open the query lab](http://localhost:4200/demo/lab?view=compatibility)

**Action:** Select **Run compatibility summary**.

**Look for:** Seven `read-completed` results with actual record counts. The displayed limitations explain that this read-only summary does not test transaction writes, rollback, Redis increments, or retention.

---

## Why this matters

- The same coffee can be traversed, searched, ranked, charted, and located.
- Full-text and vector indexes participate in the database's storage and transaction model.
- The backend selects the best query model without translating data into another store.
- The live demo is reproducible from a clean local seed.
