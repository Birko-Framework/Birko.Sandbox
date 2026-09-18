# Birko.Sandbox

A runnable **integration smoke harness** for the Birko Framework — the "first test place" where a
framework change is sanity-checked end to end before touching real consumers (Symbio, etc.).

It consumes the framework the documented consumer way (its own app csproj + `Directory.Build.props`
resolving `$(BirkoSrc)`, importing only the `.projitems` it uses), **not** by importing every
shared project.

This repo **also hosts the all-projects compile gate** as a separate `Library` project
(`Birko.Framework/Birko.Framework.csproj`), relocated here from the framework repo in TASK-037 — it
imports *every* `.projitems` so `dotnet build` proves the whole framework compiles. The two
projects are independent: this one (the harness) runs a lean slice; that one only compiles.

## What it checks

25 checks across every layer a console consumer can reach. Each constructs settings → store or
service → does a small round-trip and asserts the result.

| Group | Checks |
|---|---|
| `core` | settings identity, date-time provider, Newtonsoft round-trip, `Money` value object |
| `data` | InMemory CRUD + bulk/filter delete, ordering + paging, JSON and XML file round-trips, SQLite CRUD, SQLite decimal precision, the whole-table-write refusal, a SQL migration, the decorator chain stamping from an injected clock, cache get/set/get-or-set |
| `data (server-backed)` | PostgreSQL, MySQL and SQL Server settings compose a connection string |
| `services` | workflow build + transition, background job enqueue/dequeue, health-check runner, AI provider factory |
| `communication` | REST client wiring, GraphQL request building |
| `security` | BCrypt hash + verify |

### The three outcomes

- **`OK`** — ran for real.
- **`CFG`** — configuration and wiring verified **without contacting anything**. This is the honest
  maximum for a backend that needs a server, and it is still the thing that actually breaks when the
  framework moves underneath a consumer. The framework's own live suites cover the rest.
- **`--`** — needs hardware or an external account; not checkable here at all.

Only `FAIL` counts against the run. A machine without PostgreSQL is not a broken framework, and
reporting it as one would train everybody to ignore the output. The process exits with the **number
of failures**, and `build-and-test.yml` runs it on every push.

### Adding a check

Write a method returning `Outcome` (or `Task<Outcome>`) and add one line to the `Checks` list in
`Program.cs`. `Harness.cs` owns the grouping, colour, timing and summary.

## Running

```bash
dotnet run            # from the Birko.Sandbox directory
```

`$(BirkoSrc)` defaults to the sibling `Birko\Framework` bucket (`..\..\Framework`); override with
`/p:BirkoSrc=…` or the `BIRKO_SRC` env var for non-standard layouts.

## License

Part of the Birko Framework. See `License.md`.
