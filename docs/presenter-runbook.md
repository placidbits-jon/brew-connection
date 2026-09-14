# Presenter runbook

This runbook presents the 26 feature slides in `arcadedb-coffee-demo.md` in 15–20 minutes. The deck validator is the source of truth for browser automation:

```bash
python3 scripts/validate-demo-deck.py --json
```

## Prepare the stage

From the repository root, use Node 24.15.0, then start the AppHost and wait for Angular:

```bash
nvm use
aspire start
aspire wait api
aspire wait web
```

If DCP rejects the local developer certificate, stop the failed start and use:

```bash
ASPIRE_DCP_USE_DEVELOPER_CERTIFICATE=false aspire start
aspire wait api
aspire wait web
```

Open `http://localhost:4200/demo/story`, reveal the resource state, and wait for all four indicators to turn green. Keep the tokenized Aspire dashboard URL printed by `aspire start` available for logs and resource restarts.

Reset immediately before presenting. This replaces only the `coffee_demo` database and restores the deterministic story profile:

```bash
curl --fail-with-body -X POST \
  -H 'Content-Type: application/json' \
  -d '{}' \
  'http://localhost:4200/api/demo/reset?profile=story'
python3 scripts/validate-demo-deck.py
```

Install the browser once, then run the slide smoke suite while Aspire is healthy:

```bash
cd src/coffee-community-web
npm ci
npx playwright install chromium
cd ../..
npm --prefix src/coffee-community-web run test:slides
```

Run `npm ci` when dependencies are absent or the lockfile changed. The suite resets the story profile once, runs the slides in deck order so intentional writes remain available to later slides, and leaves the final state available for inspection. Override `DEMO_WEB_URL` and `DEMO_API_URL` when the proxies are exposed elsewhere, `DEMO_REPORT_DIR` to change the default `/tmp/brew-demo-slides` evidence directory, or `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` to use an existing Chromium binary. The JSON report records timestamps and per-slide durations. Set `DEMO_CAPTURE_SLIDES=1` to save a screenshot of each demonstrated result beside the report; failures always save a screenshot and rendered HTML.

### Reset again before presenting

The smoke suite deliberately leaves its writes in place. After inspecting its report, restore the clean story before opening the presentation:

```bash
curl --fail-with-body -X POST \
  -H 'Content-Type: application/json' \
  -d '{}' \
  'http://localhost:4200/api/demo/reset?profile=story'
```

Do not use the scale profile for the walkthrough. The story reset also removes prior meeting, love, note, recipe-publication, replay, and transaction mutations. The transient Redis tasting count is server memory and is not cleared by a database reset; describe it as a relative `+1`, as the slide does.

## Open the Obsidian presentation

Open `docs` as a vault (or copy the deck into a presentation vault). Enable the **Slides** core plugin. Copy [`obsidian-snippets/coffee-demo.css`](obsidian-snippets/coffee-demo.css) into the vault's `.obsidian/snippets/` directory, then enable **coffee-demo** under **Settings → Appearance → CSS snippets**. This keeps the long title and feature fields within the slide canvas; the default theme's large headings can clip the deck. The snippet affects presentations in that vault.

Open `arcadedb-coffee-demo.md` and run **Slides: Start presentation** from the command palette. Use the arrow controls or focus the presentation and press Left/Right. Each `---` starts one slide; the HTML activation comments are hidden. Activate a feature's HTTP link to open the live app, perform its action, then return to Slides. Keep the runbook out of the presentation.

## Timing and flow

Budget 1 minute for the opening and architecture, 3 minutes for foundation and graph, 3 minutes for documents, 3 minutes for discovery, 3 minutes for lookup/telemetry/location, 3 minutes for transactions and the query lab, and 1 minute for the close. This totals 17 minutes and leaves up to 3 minutes for transitions or one recovery. Keep each feature slide to its single **Action** and narrated **Look for** outcome.

The deck intentionally orders mutations before their dependent reads: recipe revision 3 precedes the pinned-revision proof, and the successful tasting precedes the SQL/Cypher record comparison. If slides are skipped or presented out of order, reset and resume from the start of the affected section.

## Recovery and fallback

- If a page is stale or an action times out, reload its activation URL once. If readiness is no longer green, use the Aspire dashboard to inspect the exact failing resource: `arcadedb`, `local-models`, `embedding`, `api`, `web`, or `telemetry-simulator`. Restart that resource and wait for `api` and `web` again.
- If authored data or ranking differs, run the story reset and restart at **A repeatable community story**. Never repair records by hand during the presentation.
- If a mutation reports an unknown transaction outcome, reload the transaction page or repeat the same action. Its fixed operation identity reconciles the durable result.
- If telemetry replay stalls, check the `telemetry-simulator` resource in Aspire, restart that resource, reload the brew route, and replay once.
- If the live application cannot be recovered within 60 seconds, return to the deck, narrate the exact **Look for** text, and use the architecture/closing slides to explain the verified boundary. Do not claim an interaction succeeded when it was not visible.
- Stop all resources after the session with `aspire stop --non-interactive`.

## Slide traceability

`story` means the clean deterministic story profile. `story + prior action` identifies an intentional dependency on an earlier deck mutation. `scripts/verify-demo-slides.cjs` identifies report entries by the activation ID below and dispatches actions by exact slide title.

| Activation ID | Route | Seed dependency | API endpoint(s) | ArcadeDB capability | Automated test |
|---|---|---|---|---|---|
| `repeatable-local-stage` | `/demo/story` | story | `GET /api/demo/story`; `GET /api/demo/status` | HTTP readiness, durable migration/seed markers | `verify-demo-checkpoints.py`; `verify-demo-slides.cjs` |
| `community-record-types` | `/demo/lab?view=schema` | story | `GET /api/demo/schema` | vertex, edge, document, time-series types and indexes | `verify-demo-checkpoints.py`; `verify-demo-slides.cjs` |
| `repeatable-community-story` | `/demo/story?persona=maya` | story | `GET /api/demo/story` | deterministic graph/document seed | `verify-community-seed.py`; `verify-demo-slides.cjs` |
| `record-meeting` | `/demo/meet/maya-chen` | story | `GET /api/demo/passport/{personSlug}`; `POST /api/demo/graph/meet` | indexed badge lookup, idempotent `MET` edge | `verify-community-graph.py`; `verify-demo-slides.cjs` |
| `coffee-passport` | `/demo/passport/maya-chen` | story | `GET /api/demo/passport/{personSlug}`; `POST /api/demo/graph/loves` | relationship traversal and attributed reaction | `verify-community-graph.py`; `verify-demo-slides.cjs` |
| `rematch-candidates` | `/demo/network/maya-chen` | story | `GET /api/demo/network/{personSlug}` | scored game edge traversal | `verify-community-graph.py`; `verify-demo-slides.cjs` |
| `shortest-social-path` | `/demo/network/maya-chen` | story | `GET /api/demo/network/{personSlug}` | breadth-first traversal over `MET` edges | `verify-community-graph.py`; `verify-demo-slides.cjs` |
| `reconnect-targets` | `/demo/network/maya-chen` | story | `GET /api/demo/network/{personSlug}` | attributed reconnect edges and shared interests | `verify-community-graph.py`; `verify-demo-slides.cjs` |
| `structured-recipe` | `/demo/recipes/blueberry-v60` | story | `GET /api/demo/recipes/{slug}` | linked nested revision document | `verify-community-documents.py`; `verify-demo-slides.cjs` |
| `private-note` | `/demo/coffee/ethiopia-blueberry-bloom` | story | `GET /api/demo/coffee/{slug}`; `GET /api/demo/notes`; `POST /api/demo/notes` | linked plain-text document and visibility filter | `verify-community-documents.py`; `verify-demo-slides.cjs` |
| `recipe-revision` | `/demo/recipes/blueberry-v60` | story | `GET /api/demo/recipes/{slug}`; `POST /api/demo/recipes/{slug}/revisions` | immutable document and atomic current link | `verify-community-documents.py`; `verify-demo-slides.cjs` |
| `bean-to-cup-provenance` | `/demo/coffee/ethiopia-blueberry-bloom` | story + revision action | `GET /api/demo/coffee/{slug}` | multi-hop provenance and pinned revision | `verify-community-documents.py`; `verify-demo-slides.cjs` |
| `keyword-search` | `/demo/discover?mode=keyword` | story | `GET /api/demo/discover` | Lucene full-text/BM25 | `verify-discovery.py`; `verify-demo-slides.cjs` |
| `semantic-search` | `/demo/discover?mode=semantic` | story | `GET /api/demo/discover` | indexed 768-d cosine neighbors | `verify-discovery.py`; `verify-demo-slides.cjs` |
| `hybrid-ranking` | `/demo/discover?mode=hybrid` | story | `GET /api/demo/discover` | explicit BM25/vector score combination | `verify-discovery.py`; `verify-demo-slides.cjs` |
| `personalized-discovery` | `/demo/discover?mode=personalized` | story | `GET /api/demo/discover` | search candidates plus graph path score | `verify-discovery.py`; `verify-demo-slides.cjs` |
| `indexed-code-lookup` | `/demo/lookup` | story | `GET /api/demo/lookup` | unique persistent key lookup | `verify-keygeo.py`; `verify-demo-slides.cjs` |
| `transient-tasting-counter` | `/demo/pulse` | running ArcadeDB process | `GET /api/demo/pulse`; `GET /api/demo/counter`; `POST /api/demo/counter` | transient Redis `GET`/`INCR` | `verify-keygeo.py`; `verify-demo-slides.cjs` |
| `pour-telemetry` | `/demo/brews/blueberry-bloom-v60` | story | `GET /api/demo/brews/{slug}`; `POST /api/demo/brews/{slug}/replay`; simulator: `GET /api/demo/telemetry/pending`, `POST /api/demo/telemetry/runs/{runId}/ingest` | native time-series ingestion and tagged range query | `verify-telemetry.py`; `verify-demo-slides.cjs` |
| `event-time-buckets` | `/demo/pulse?bucketMinutes=10` | story | `GET /api/demo/pulse`; `GET /api/demo/counter` | native bucket, percentile, rate, downsampling | `verify-telemetry.py`; `verify-demo-slides.cjs` |
| `nearby-coffee` | `/demo/map` | story | `GET /api/demo/map` | indexed containment, distance, graph-linked offers | `verify-keygeo.py`; `verify-demo-slides.cjs` |
| `acid-tasting-transaction` | `/demo/transactions?scenario=commit` | story | `GET /api/demo/transactions`; `POST /api/demo/transactions/run` | HTTP transaction across vertices, document, edges | `verify-transactions.py`; `verify-demo-slides.cjs` |
| `transaction-rollback` | `/demo/transactions?scenario=rollback` | story | `GET /api/demo/transactions`; `POST /api/demo/transactions/run` | staged writes and rollback | `verify-transactions.py`; `verify-demo-slides.cjs` |
| `polyglot-record` | `/demo/lab?example=sql-transaction-brew` | story + commit action | `GET /api/demo/lab`; `POST /api/demo/lab/run` | SQL/Cypher identity over one vertex | `verify-query-lab.py`; `verify-demo-slides.cjs` |
| `query-plan` | `/demo/lab?example=fulltext-coffee` | story | `GET /api/demo/lab`; `POST /api/demo/lab/run` | fixed full-text query and native SQL EXPLAIN | `verify-query-lab.py`; `verify-demo-slides.cjs` |
| `compatibility-checkpoint` | `/demo/lab?view=compatibility` | story | `GET /api/demo/lab`; `POST /api/demo/lab/run` | SQL, Cypher, Redis, full-text, vector, time-series, geospatial reads | `verify-query-lab.py`; `verify-demo-slides.cjs` |

## Rehearsal record

Automated runs on 2026-09-14 started with all Aspire services stopped, started the AppHost, waited for API and web readiness, reset to the story profile, and performed all 26 browser actions in deck order. Each used a new Chromium context. Installed dependencies, container images, persistent volume, and downloaded model caches were retained; these were fresh-service rehearsals, not first-time installation or OS reboot tests. No hand-edited data or recovery was needed in either recorded pass. The process-local DCP certificate workaround above was used for startup.

| Check | Status | Actual elapsed time | Evidence |
|---|---|---:|---|
| Automated rehearsal 1 | 26/26 passed | 12.106 s browser; 23.2 s startup through finish | `/tmp/phase8-rehearsal-final-1/report.json`, 26 screenshots; browser 17:40:24.865–17:40:36.971 UTC |
| Automated rehearsal 2 | 26/26 passed | 11.980 s browser; 22.4 s startup through finish | `/tmp/phase8-rehearsal-final-2/report.json`, 26 screenshots; browser 17:41:28.153–17:41:40.133 UTC |
| Obsidian visual check | 31/31 slides fit; navigation passed | 2026-09-14 | Portable Obsidian 1.13.7 in a temporary profile/vault, 1024 × 800 CSS viewport; `coffee-demo` snippet enabled. All 26 HTTP hrefs match the browser-tested deck. `/tmp/phase8-obsidian/render-report.json` and `slide-01.png` through `slide-31.png`. |
| Spoken presentation timing | Pending presenter rehearsal | — | The 17-minute budget is a proposed schedule. Automation does not measure narration or audience transitions; rehearse twice before presenting. |

The report/screenshot paths are local verification artifacts, not checked-in fixtures. To reproduce them, set `DEMO_REPORT_DIR` and `DEMO_CAPTURE_SLIDES=1` when running the browser suite. Initial test-authoring passes needed selector corrections (the About select's accessible name, transaction status/row headers, and query-plan container); these were automation fixes, not live-demo data repairs.

Native rendering initially exposed title clipping with the default Slides theme; the checked-in CSS snippet resolved it across all 31 slides. The portable application came from the [official Obsidian download page](https://obsidian.md/download); the temporary profile avoids changing a presenter's existing vault or settings. External URLs were inspected in Slides and exercised by Chromium automation; the operating system's default-browser integration was not separately asserted.
