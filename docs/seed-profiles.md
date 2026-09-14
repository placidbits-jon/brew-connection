# Reset and verify seed profiles

Start the application as described in the [README](../README.md). You need Python 3 to run the integration verifier.

## Reset the story

Use **Reveal Aspire resource state → Reset demo state** on the story screen, or run:

```bash
curl --fail-with-body -X POST 'http://localhost:4200/api/demo/reset?profile=story'
```

Reset replaces the `coffee_demo` database. The default profile is `story`; unsupported profile names return HTTP 400 before any data changes. Only reset when you intend to replace the current demo data.

Open [Maya's story](http://localhost:4200/demo/story?persona=maya) and the [schema explorer](http://localhost:4200/demo/lab?view=schema). The persona query accepts `maya`, `priya`, `luis`, or a seeded person's full slug. The persona selector writes the full slug to the URL so reloading restores the selection.

## Load the scale profile

```bash
curl --fail-with-body -X POST 'http://localhost:4200/api/demo/reset?profile=scale'
```

Wait for the request to finish. Scale ingestion writes 2,040 people, 2,025 roast batches, 20,045 indexed embedding documents, and 2,000,000 telemetry samples. The authored story remains in the scale profile. API logs report ingestion progress. Use the story reset command to restore presentation-sized data afterward.

## Verify repeatability

Use `aspire describe --non-interactive` to find ArcadeDB's HTTP endpoint, then substitute that endpoint below:

```bash
python3 scripts/verify-community-seed.py \
  --db-url http://localhost:ARCADEDB_PORT \
  --api-url http://localhost:4200 --profile story --reset

python3 scripts/verify-community-seed.py \
  --db-url http://localhost:ARCADEDB_PORT \
  --api-url http://localhost:4200 --profile scale --reset
```

Check the slide APIs and invalid-input behavior without replacing valid data:

```bash
python3 scripts/verify-demo-checkpoints.py --api-url http://localhost:4200
```

Each `--reset` invocation replaces the demo database twice and checks the resulting counts, stable slugs, indexes, and authored scenarios. Omit `--reset` to inspect the existing selected profile without replacing data.

## Implementation notes

- [CommunitySchema](../src/CoffeeCommunity.Api/Infrastructure/CommunitySchema.cs) defines migration `002-community`. [CommunitySeeder](../src/CoffeeCommunity.Api/Infrastructure/CommunitySeeder.cs) generates both profiles using fixed sequences and timestamps.
- On startup, [DemoBootstrapper](../src/CoffeeCommunity.Api/Infrastructure/DemoBootstrapper.cs) retains a completed current seed. A foundation-version or interrupted seed is rebuilt. This migration strategy intentionally replaces demo data; it is not a production data migration.
- Telemetry uses the fixed epoch `1789401600000` (2026-09-14 16:00 UTC). Native time-series retention is omitted so future rehearsals keep the same authored samples. Phase 6 demonstrates retention on a separate sacrificial `DemoRetention` type so the historical series remain available. Replay uses additional device tags and durable `TelemetryRun` identities; reset removes these together.
- Embeddings use the pinned local `embeddinggemma:300m` model through Ollama, with 768 normalized dimensions and cosine distance. Roast batches and current recipe revisions share canonical public text across full-text and vector indexes. Scale embeddings retain repeated subject aliases for index volume; discovery deduplicates them before ranking. See [local model setup](local-models.md).
- Notes retain owner and subject links. Their owner/visibility hash index uses `ownerSlug` because the pinned database rejects linked-vertex values as hash keys.
- Database HTTP writes have automatic resilience retries disabled to prevent replay after an ambiguous response. Failed seed attempts remain incomplete and rebuild on the next startup or reset.
