# Birko.Framework (compile-validation gate)

## Overview
Headless **compile-validation aggregator** (.NET 10.0, `OutputType=Library`). It `<Import>`s
virtually every Birko `.projitems` so a single `dotnet build` proves the whole framework
compiles together. It is **not** runnable and contains no demo code.

This gate lives **inside the Birko.Sandbox consumer repo** alongside the runnable smoke harness
(`Birko.Sandbox.csproj`), which wires up a representative slice of every layer and runs a tiny
round-trip (`dotnet run`). The framework repo (`Birko\Framework\Birko.Framework`) is docs/meta
only. (The former in-repo TUI demo was extracted and this gate relocated here in TASK-037.)

## Project Location
`C:\Source\Birko\Consumers\Birko.Sandbox\Birko.Framework\` — a project inside the Birko.Sandbox consumer repo.

## Structure
- `Birko.Framework.csproj` — the aggregator: ~150 `.projitems` imports + the NuGet packages they require.
- No `Program.cs` / `Examples/` / `Services/` / `Configuration/` — the TUI demo was removed in TASK-037 and re-homed in Birko.Sandbox.

## Dependencies
Imports virtually all Birko shared projects via `.projitems` (resolved through `$(BirkoSrc)`, inherited from the Birko.Sandbox `Directory.Build.props`), including:
- All Data layer projects (SQL, ElasticSearch, MongoDB, RavenDB, InfluxDB, TimescaleDB, JSON, InMemory, XML)
- All ViewModel + Views projects
- All Communication + Security projects
- Caching, Messaging, Telemetry, BackgroundJobs, MessageQueue, EventBus
- Storage, Health, Rules, CQRS, Workflow, Serialization
- Models, Validation, Structures, Helpers, AI, Localization

External packages: Npgsql, NEST, Microsoft.Data.Sqlite, StackExchange.Redis, MQTTnet, OpenTelemetry, RazorLight, Newtonsoft.Json, MessagePack, protobuf-net, etc.

## Key Notes
- `OutputType` is **Library** — a headless compile gate, not a runnable app. Build with
  `dotnet build Birko.Framework/Birko.Framework.csproj`; a green build means every imported project compiles together.
- Uses `$(MSBuildThisFileDirectory)` prefix for all shared-project imports so paths work from both CLI and Visual Studio.
- NuGet audit suppressions for known transitive vulnerabilities from Microsoft.Data.SqlClient.
- `PreserveCompilationContext` is enabled for RazorLight template compilation.
- For a runnable end-to-end example, see the **Birko.Sandbox** consumer.

## Maintenance

### README Updates
When making changes that affect the aggregator's role or imported set, update README.md.

### CLAUDE.md Updates
When making major changes, update this CLAUDE.md to reflect new or renamed files, changed architecture, or updated dependencies.
