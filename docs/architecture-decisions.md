# Architecture Decisions

## ADR-001: Pin ArcadeDB 26.9.1

Status: Accepted on 2026-09-14

ArcadeDB `26.9.1` is the first pinned database version for the demo. A compatibility probe exercises the exact image instead of relying on `latest`.

The JVM distribution is used because the demo intentionally exposes the Redis, MongoDB, PostgreSQL, and Gremlin plugins alongside the built-in HTTP interface and Studio. The server is configured through `ARCADEDB_SETTINGS`, and database files are mounted at `/home/arcadedb/databases`.

The ASP.NET Core API will use ArcadeDB's HTTP/JSON API as its primary interface. It exposes every query language required by the demo, parameterized statements, transaction sessions, health checks, and the server's OpenAPI contract without requiring a .NET-specific ArcadeDB driver. The application may use another wire protocol only where showing that protocol is itself part of the lesson.

ArcadeDB's `/api/v1/health` returns HTTP 204 in the pinned image. Aspire's built-in HTTP health check requires HTTP 200, so AppHost treats the container process as its startup dependency while the API performs the authenticated ArcadeDB readiness check exposed at `/health` and `/api/demo/status`.

## ADR-002: Use model-native records for one connected story

Status: Accepted on 2026-09-14

People, coffees, brews, games, recipes, and locations become vertices when traversal is central. Occurrence-specific facts belong on edges. Nested, versioned content is stored in explicit documents and linked from graph records. Search, vector, geospatial, and time-series indexes operate on data connected by stable application slugs and ArcadeDB record IDs.

This lets the demo show different access patterns without copying the event into independent databases or maintaining synchronization pipelines.

## ADR-003: Use a local 768-dimensional embedding model

Status: Accepted on 2026-09-14

The target model is `embeddinggemma`, whose published model metadata declares 768-dimensional embeddings. The compatibility probe creates and queries an ArcadeDB `LSM_VECTOR` index with exactly 768 dimensions.

The application will access the model through an `IEmbeddingService` abstraction. A deterministic provider will support fast tests, while the live demo provider runs locally and never sends attendee or note content to a hosted service. The final hosting method will favor a compact local runtime over the standard Ollama container because the latter adds roughly 2.65 GB of image layers before the model itself.

## ADR-004: Treat live OpenAPI as the versioned HTTP contract

Status: Accepted on 2026-09-14

The compatibility spike found two documentation differences that matter to executable code:

- Hash index DDL accepted by 26.9.1 uses `UNIQUE_HASH` and `NOTUNIQUE_HASH`.
- The native time-series JSON query request uses numeric `from` and `to`, a `fields` array, and an `aggregation` object containing numeric `bucketInterval` and `requests`.

The API client and compatibility checks follow `/api/v1/openapi.json` from the pinned server. Prose documentation remains useful for concepts, but generated or hand-written request types must match the live contract.

## ADR-005: Keep slide activation deterministic

Status: Accepted on 2026-09-14

Feature slides link to stable Angular `/demo/...` routes using application slugs rather than ArcadeDB record IDs. Each slide declares one exact action and one expected visible result. A later verification script will compare deck links with the route manifest and run the interactions against a clean story seed.
