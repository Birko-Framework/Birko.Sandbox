# Birko.Framework — compile-validation gate

Headless aggregator that imports **all** Birko shared projects (via `.projitems`) so a single
`dotnet build` proves the whole framework compiles together. `OutputType=Library` — there is no
runnable demo here.

> **Looking for a runnable example?** This gate lives inside the **Birko.Sandbox** consumer repo;
> its sibling `Birko.Sandbox.csproj` is the runnable smoke harness that wires up a representative
> slice of every layer. This project (`Birko.Framework.csproj`) is purely the compile gate.

## What it does

- Imports every Birko shared project (via `.projitems`) for compile-time validation across the whole framework.
- Carries the NuGet packages those shared projects need (Npgsql, NEST, Microsoft.Data.Sqlite, StackExchange.Redis, MQTTnet, OpenTelemetry, RazorLight, MessagePack, protobuf-net, …) plus the `Microsoft.AspNetCore.App` framework reference.

## Building (the gate)

```bash
dotnet build Birko.Framework/Birko.Framework.csproj
```

A green build is the signal that all aggregated projects compile together. It is not runnable
(`dotnet run` does nothing useful — it's a Library).

## License

Part of the Birko Framework.
