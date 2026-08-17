# Birko.Sandbox

## Overview
Runnable **integration smoke harness** consumer for the Birko Framework (.NET 10.0, `Exe`). Wires up
a representative slice of every framework layer and does a tiny round-trip per layer, asserting
success. Complements (does not replace) the `*.Tests` unit/integration projects. This repo **also hosts the
`Birko.Framework` compile gate** (relocated from the framework repo in TASK-037, see Structure) —
a separate `Library` project. Created in TASK-037 by extracting the former in-repo TUI demo.

## Location
`C:\Source\Birko\Consumers\Birko.Sandbox` (a Birko consumer, under the `Birko\Consumers` bucket).

## How it consumes the framework
- `Directory.Build.props` resolves `$(BirkoSrc)` (`/p:BirkoSrc` → `BIRKO_SRC` env → default `..\..\Framework`).
- `Birko.Sandbox.csproj` is both the app and its own lean aggregator: it `<Import>`s only the
  `$(BirkoSrc)\Birko.X\*.projitems` the harness actually uses (NOT the full all-projects list — that's
  `Birko.Framework`'s job), plus the NuGet packages those shared projects require.

## Structure
This repo hosts **two** projects (both inherit `$(BirkoSrc)` from `Directory.Build.props`):
- `Birko.Sandbox.csproj` + `Program.cs` — the runnable **smoke harness** (lean projitems slice; one `Try*()` per layer, prints `OK`/`FAIL`, non-zero exit on failure).
- `Birko.Framework/Birko.Framework.csproj` — the **compile-validation gate** (`Library`), relocated from the framework repo in TASK-037: imports *all* `.projitems` via `$(BirkoSrc)` so `dotnet build` proves the whole framework compiles together. Not runnable.
- `_scratch/` — throwaway dir for file-based checks (JSON store, job state); git-ignored.

## Conventions
- Add a new layer check as its own `Try*()` method that returns bool and prints a single OK/FAIL line; register it in the runner list in `Program.cs`.
- Keep it a *smoke* harness — one tiny round-trip per layer, no live network calls (AI provider is wired but not invoked). Deep per-provider coverage lives in the `*.Tests` projects.
- When adding a layer that needs a new shared project, add its `.projitems` import (and any required NuGet package) to `Birko.Sandbox.csproj`.

## Building / running
```bash
dotnet build      # resolves $(BirkoSrc), imports succeed
dotnet run        # prints per-layer OK lines, exits 0; non-zero if any check fails
```
