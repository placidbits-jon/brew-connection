# Coffee discovery

Open `/demo/discover` or use the discovery links in [the Reveal.js presentation source](../src/coffee-community-slides/public/slides.html). The query, mode, persona, subject type, keyword example, source slug, and availability filter live in the URL, so refresh and slide links reconstruct the same search.

## Compare the four modes

Use the story profile, Maya Chen, available results, and the query `blueberry` throughout the sequence. These are measured outcomes from the pinned local model:

| Mode | First result | What changes |
| --- | --- | --- |
| Keyword | Ethiopia Blueberry Bloom | Blueberry Label Dark Roast also matches; Summer Orchard does not. |
| Semantic | Blueberry Label Dark Roast | Summer Orchard appears at rank 4; Ethiopia Blueberry Bloom is rank 3. |
| Hybrid | Ethiopia Blueberry Bloom | The full-text contribution returns Ethiopia to rank 1; Summer Orchard remains rank 4. |
| Personalized | Priya's Honey Stonefruit | Priya's favorite moves from hybrid rank 5 to rank 1 through a stored acquaintance path. |

The single word `blueberry` does not encode a light-roast preference. Semantic retrieval correctly adds related descriptions, but does not guarantee that the dark roast disappears. Try `bright fruity floral coffee` separately to explore that distinction. Changes to recipes, meetings, reactions, availability, seed profile, or query can change rankings; reset the story profile to reproduce the slides.

## Ranking and evidence

Keyword mode uses Lucene's BM25 score. Both RoastBatch and Recipe indexes use EnglishAnalyzer, which performs stemming. The keyword example selector provides these curated forms:

| Example | Query | Behavior |
| --- | --- | --- |
| Words | `blueberry` | Indexed keyword relevance |
| Exact phrase | `blueberry jasmine` | Adjacent terms in order |
| Fuzzy spelling | `bluebery` | Up to two edits per term |
| Word stems | `roasted` | EnglishAnalyzer normalization, also active in the other forms |
| Autocomplete | `blueb` | Prefix expansion of the last term |
| More like this | Any nonempty query; source `ethiopia-blueberry-bloom` | Lucene representative terms from the source record; excludes that record |

The API tokenizes input into words before constructing the Lucene expression. The selector determines syntax; this is not an arbitrary Lucene or SQL console. More-like-this supplies the keyword portion from the selected source, while semantic and hybrid vector retrieval still use the visible query.

Semantic mode uses ArcadeDB's `SearchEmbedding[embedding]` LSM_VECTOR index with 768 dimensions and cosine distance. Results show actual distance; lower is closer. The API requests the complete indexed neighborhood, deduplicates scale aliases by subject, applies type and availability filters, and returns the first 20 ranked subjects. This prioritizes reproducible filtered results in the local demo over large-corpus query latency. It is not a production approximate-neighbor performance benchmark.

Hybrid scoring is explicit:

```text
0.35 × (BM25 / maximum returned candidate BM25)
+ 0.65 × clamp(1 − cosine distance, −1, 1)
```

Personalized mode adds 0.50 when the selected person has a stored `MET` relationship with someone who `LOVED` a brew that `USED_BATCH` the candidate coffee and an available vendor `SELLS` it. The bonus is awarded once, independent of duplicate qualifying paths. Explanations show the actual person, acquaintance, brew, vendor, and relationships. Recipes are available to read; roast availability comes from current vendor offers. Ties use slug ordering.

## One-query cross-model proof

Select **One query** to load the curated `stonefruit honey` coffee example. A single parameterized ArcadeDB SQL retrieval starts from native vector-index neighbors, intersects them with a Lucene `SEARCH_INDEX` subquery, and intersects those candidates with a `MATCH` traversal from the selected persona through `MET → LOVED → USED_BATCH → SELLS`. The vendor vertex must be currently available. Surviving coffees are ordered by cosine distance.

This mode deliberately uses strict intersection semantics: a coffee must satisfy all four conditions. It demonstrates cross-model composition inside one database statement, while **Personalized** remains the more useful product behavior for broad discovery because its API-side weighted union can retain strong candidates that lack one signal. The query embedding is computed by the pinned local model before the statement runs. The query inspector shows the single retrieval and its `EXPLAIN` plan; the diagnostic `EXPLAIN` is separate from the retrieval itself.

The query inspector includes executed SQL and Cypher, parameters, and full-text/vector execution plans. Ranking and final filtering run in the API and are labeled accordingly. The route serializes database work with the same gate used for reset and community mutations.

## Indexing and optional language input

Roast descriptions and current recipe documents produce the same canonical public text for Lucene and local embeddings. Recipe publication computes a vector inside the transaction before database writes, then commits the immutable revision, current-revision link, searchable text, and SearchEmbedding update together. A failure rolls back database writes. Existing brews retain their pinned revision. Private notes never enter canonical text.

Startup migrates the previous compatibility embeddings in place, preserving user recipes and notes. An index-version marker advances after rebuilding every subject; interrupted rebuilds can resume. Scale aliases retain their index volume and point to the same canonical subject vectors. Full recipe documents remain available even when searchable text uses a rune-preserving excerpt of at most 1,500 UTF-8 bytes to fit the local model context.

See [local model setup](local-models.md) for pinned digests, first-run downloads, offline caching, context prefixes, and health checks. The optional **Describe your next cup in your own words** control uses local Qwen only to rewrite an editable query. The API obtains recommendations and evidence through the same personalized retrieval path. The rewrite appears on screen and in the query box. Direct search remains usable when the helper is unavailable; model output is not a factual answer and does not implement hard flavor exclusions.

## Verify

With Aspire running:

```sh
python3 scripts/verify-discovery.py --api-url http://localhost:5130 --reset
python3 scripts/verify-local-models.py --embedding-url http://localhost:5012 --interpret
npm --prefix src/coffee-community-web test -- --watch=false
```

The discovery suite checks the golden candidates, exact score decomposition, search examples, filters, input errors, index plans, recipe creation/publication, and repeatable dimensions and ranks across clean story resets. `--reset` replaces local demo data. The local-model script verifies actual inference, pins, normalization, repeatability, bounded inputs, and optional query interpretation.

For migration and late-write rollback coverage, run `scripts/verify-discovery-migration.py --api-url http://localhost:5130 --db-url <ArcadeDB HTTP endpoint from Aspire>` against the local story profile. It restarts the API twice, deliberately changes the index marker, verifies preserved revisions and identical vectors, forces an embedding uniqueness failure, and checks live availability. It leaves fixture data behind; reset the story profile afterward. This test requires the Aspire CLI and the database endpoint, which can change between starts.
