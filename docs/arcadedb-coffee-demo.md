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

## Compatibility checkpoint

**ArcadeDB:** Version 26.9.1 with native and plugin query engines

- Cypher graph writes and traversals
- Nested documents and indexed key lookup
- BM25 full-text and 768-dimensional vector search
- Redis-style counters, time-series aggregation, geospatial distance, and HTTP transactions

**Activate demo:** [Open the query lab](http://localhost:4200/demo/lab?view=compatibility)

**Action:** Select **Run compatibility summary**.

**Look for:** Every capability reports `Passed` and identifies ArcadeDB 26.9.1.

---

## Why this matters

- The same coffee can be traversed, searched, ranked, charted, and located.
- Full-text and vector indexes participate in the database's storage and transaction model.
- The backend selects the best query model without translating data into another store.
- The live demo is reproducible from a clean local seed.
