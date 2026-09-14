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
