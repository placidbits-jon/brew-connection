# ArcadeDB Coffee Community Demo

A local demo of a tech-conference coffee community, built with Angular, ASP.NET Core, Aspire, and ArcadeDB. The walkthrough grows from a social graph into documents, search, vectors, key/value access, time-series telemetry, geospatial queries, and transactions.

## Prerequisites

- Docker Desktop
- .NET SDK 10
- Aspire CLI 13.5.3 or newer
- Node.js 24.15.0 (the repository includes `.nvmrc`)

## Start the application

```bash
nvm use
aspire start
aspire wait web
```

Open [http://localhost:4200/demo/story](http://localhost:4200/demo/story). The Aspire command prints a tokenized dashboard URL for resource logs, traces, and health.

ArcadeDB data persists in the `arcadedb-demo-data` Docker volume. The demo uses the fixed local-only root password `CoffeeDemo_Local_2026!`; it is intentionally scoped to this disposable developer environment.

## Demo controls

- **Reveal Aspire resource state** shows live application, database, schema, and embedding readiness.
- **Reset demo state** drops and recreates only the `coffee_demo` database, then reapplies migrations and deterministic seed data.
- The walkthrough slides are in [`docs/arcadedb-coffee-demo.md`](docs/arcadedb-coffee-demo.md) and use Obsidian's `---` slide separators.

## Compatibility verification

```bash
./scripts/verify-arcadedb-compatibility.sh
```

The disposable compatibility database proves the ArcadeDB features used throughout the planned demo without touching the persistent application database.
