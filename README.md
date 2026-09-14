# ArcadeDB Coffee Community Demo

A local demo of a tech-conference coffee community, built with Angular, ASP.NET Core, Aspire, and ArcadeDB. The walkthrough grows from a social graph into documents, search, vectors, key/value access, time-series telemetry, geospatial queries, and transactions.

## Prerequisites

- Docker Desktop
- .NET SDK 10
- Aspire CLI 13.5.3 or newer
- Node.js 24.15.0 (the repository includes `.nvmrc`)
- [Ollama](https://ollama.com/download) on `PATH` for local embeddings and optional query interpretation

## Start the application

```bash
nvm use
aspire start
aspire wait web
```

Open [http://localhost:4200/demo/story](http://localhost:4200/demo/story). The Aspire command prints a tokenized dashboard URL for resource logs, traces, and health.

First startup downloads approximately 1.15 GB of pinned local models. Later starts reuse the ignored `.aspire/models` cache; see [local model setup](docs/local-models.md).

ArcadeDB data persists in the `arcadedb-demo-data` Docker volume. The demo uses the fixed local-only root password `CoffeeDemo_Local_2026!`; it is intentionally scoped to this disposable developer environment.

## Demo controls

- **Reveal Aspire resource state** shows live application, database, schema, and embedding readiness.
- **Reset demo state** drops and recreates only the `coffee_demo` database, then reapplies the community schema and the deterministic `story` seed. See [reset and verification commands](docs/seed-profiles.md) for the `scale` profile.
- The walkthrough slides are in [`docs/arcadedb-coffee-demo.md`](docs/arcadedb-coffee-demo.md) and use Obsidian's `---` slide separators.

## Phase 2 checkpoints

- [Maya's seeded story](http://localhost:4200/demo/story?persona=maya) shows community counts, meetings, tastings, and rematch targets.
- [Schema explorer](http://localhost:4200/demo/lab?view=schema) shows live types, properties, and logical indexes.
- [Seed profile guide](docs/seed-profiles.md) explains repeatability checks, scale ingestion, and local embedding indexing.

## Phase 3 checkpoints

- [Badge scan](http://localhost:4200/demo/meet/maya-chen) records a meeting for Maya.
- [Coffee passport](http://localhost:4200/demo/passport/maya-chen) combines cups, people, reactions, and games.
- [Community network](http://localhost:4200/demo/network/maya-chen?target=luis-ortega) explains introductions, shared interests, reconnects, and rematches.
- [Graph rehearsal guide](docs/community-graph.md) covers slide order, game actions, and integration tests.

## Phase 4 checkpoints

- [Structured recipe](http://localhost:4200/demo/recipes/blueberry-v60?persona=maya-chen) supports nested editing, revision publishing, and comparison.
- [Coffee provenance](http://localhost:4200/demo/coffee/ethiopia-blueberry-bloom?persona=maya-chen) connects the origin, people, and pinned recipe revision.
- [Document rehearsal guide](docs/recipe-documents.md) covers notes, persona visibility, and verification.

## Phase 5 checkpoints

- [Discovery](http://localhost:4200/demo/discover?query=blueberry&mode=keyword&persona=maya-chen&availableOnly=true) compares keyword, semantic, hybrid, and personalized results with visible scores and evidence.
- [Discovery rehearsal guide](docs/discovery.md) explains the four slide outcomes, full-text examples, ranking, and verification.
- [Local model setup](docs/local-models.md) covers model pins, downloads, readiness, and optional natural-language input.

## Phase 6 checkpoints

- [Code lookup](http://localhost:4200/demo/lookup?code=badge-0001) resolves persistent badges and coffee short codes.
- [Brew telemetry](http://localhost:4200/demo/brews/blueberry-bloom-v60) replays simulated measurements against pinned recipe targets.
- [Event pulse](http://localhost:4200/demo/pulse?bucketMinutes=10) shows the transient Redis counter, durable time buckets, percentile, rate, downsampling, and retention example.
- [Nearby coffee](http://localhost:4200/demo/map) combines indexed containment, distance, and vendor offers.
- [Telemetry guide](docs/telemetry.md) explains the authored anomaly, simulation, lifecycle, and verification.
- [Codes and locations guide](docs/keys-and-locations.md) covers durable lookup, counter restart behavior, and geospatial checks.

## Phase 7 checkpoints

- [Transactions](http://localhost:4200/demo/transactions?scenario=commit) demonstrate an atomic tasting, repeat-safe commit, and controlled rollback.
- [Query lab](http://localhost:4200/demo/lab?view=queries) runs fixed read-only queries with actual parameters, records, timing, and plans.
- [Transaction guide](docs/transactions.md) and [query lab guide](docs/query-lab.md) describe operation identities, proof boundaries, and verification.

If Aspire reports that DCP cannot load a self-signed root CA from the local developer certificate, start with the process-local workaround:

```bash
ASPIRE_DCP_USE_DEVELOPER_CERTIFICATE=false aspire start
```

## Compatibility verification

```bash
./scripts/verify-arcadedb-compatibility.sh
```

The disposable compatibility database proves the ArcadeDB features used throughout the planned demo without touching the persistent application database.

## Phase 8 presentation checks

The [presenter runbook](docs/presenter-runbook.md) includes startup, reset, recovery, a 17-minute speaking budget, and a slide-to-feature traceability table.

```bash
python3 scripts/validate-demo-deck.py
python3 scripts/test-demo-deck.py
npm --prefix src/coffee-community-web run test:slides
```

The browser suite requires running Aspire resources and Playwright Chromium (installation instructions are in the runbook). It resets the story once, follows all 26 slide actions in order, and writes a timestamped report. Reset again before presenting: the suite deliberately leaves its completed actions available for inspection.
