# Query lab

Open `/demo/lab` to select and execute fixed, read-only examples against the current ArcadeDB dataset. `/demo/lab?view=compatibility` selects the read-only runtime compatibility summary. Database errors remain errors; the lab does not fabricate successful results or query plans.

`GET /api/demo/lab` returns the catalog and limitations. `POST /api/demo/lab/run` accepts exactly one JSON string property, for example `{"id":"sql-brew"}`. Commands, languages, parameters, duplicate properties, extra properties, query-string overrides and bodies over 1024 bytes are rejected with HTTP 400. There is no arbitrary-query endpoint.

| Example id | What it reads |
| --- | --- |
| `sql-brew` | Seeded Blueberry Bloom Brew, including its persistent RID |
| `cypher-brew` | The same Brew and RID through Cypher |
| `sql-transaction-brew` | Optional SQL-created transaction Brew |
| `cypher-transaction-brew` | That same committed Brew as a graph vertex |
| `redis-counter` | Transient counter with GET through ArcadeDB's HTTP Redis executor |
| `fulltext-coffee` | Native Lucene search for blueberry, at most ten coffees |
| `vector-coffee` | Ten native COSINE neighbors using a local 768-dimensional query embedding |
| `timeseries-brew` | Ten native time-series samples from the authored Brew |
| `geo-vendors` | At most ten tables within the fixed venue polygon and 100-meter radius |
| `compatibility-summary` | The seven seeded reads above, excluding optional transaction reads |

Each run returns the actual language, command, parameters, records, record count, database round-trip execution time in milliseconds and plan status. SQL plans come from a separate native `EXPLAIN` request. Execution time excludes query embedding generation and the separate EXPLAIN request. Summary execution time includes all its child reads and their preparation/plans. Vector results include the actual 768-value parameter, not a placeholder; scale aliases may appear among neighbors. Cypher plans are explicitly not available in this lab. Redis GET has no SQL query plan.

The transaction examples return an explicit empty-state message until the successful transaction demo commits `transaction-blueberry-v60`. Resetting the story removes that record. Both examples resolve it independently through their respective query languages.

The compatibility summary reports completed reads and record counts from the running database. It does not run write tests or establish transaction atomicity, rollback, isolation, durability, Redis increment behavior, retention, or complete language compatibility. Use the transaction demo and dedicated verification scripts for those claims. The Redis key is transient; GET may return an unset value and never increments it. Reads share the demo graph gate so a reset or transaction cannot interleave within a lab run.

Run against a ready seeded API:

```sh
python3 scripts/verify-query-lab.py http://localhost:5130
```

The script runs every catalog entry, compares SQL/Cypher RIDs, checks actual SQL plans and vector dimensions, rejects injection/override payloads and verifies the counter and all schema record counts are unchanged. It performs no reset or write. Run it without concurrent demo mutations or reset actions.
