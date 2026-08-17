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

## What it smoke-tests

For a representative slice of each layer it constructs settings → store/service → does a tiny
round-trip and asserts success:

- **Configuration** — load/round-trip a `Settings` object
- **Stores** — `InMemory` (and `JSON`) store CRUD round-trip
- **Serialization** — serializer round-trip (Newtonsoft)
- **Workflow** — build + run a tiny workflow
- **Background jobs** — enqueue + process a job
- **AI** — wire an `ILlmProvider` / agent via the factories (no live network call)

Each check prints an `OK` / `FAIL` line; the process exits **non-zero** on any failure, so it is
usable as a CI smoke gate.

## Running

```bash
dotnet run            # from the Birko.Sandbox directory
```

`$(BirkoSrc)` defaults to the sibling `Birko\Framework` bucket (`..\..\Framework`); override with
`/p:BirkoSrc=…` or the `BIRKO_SRC` env var for non-standard layouts.

## License

Part of the Birko Framework. See `License.md`.
