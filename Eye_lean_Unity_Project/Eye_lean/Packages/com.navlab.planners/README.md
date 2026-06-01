# com.navlab.planners

C# port of the navlab path planners (A*, D* Lite).

## Install (Unity Package Manager)

```
git+https://github.com/<org>/navlab.git?path=/planner-engine/csharp
```

## Tests

Run via Unity Test Runner in Editor mode, or from CI via `unity -runTests`.

## Standalone build / test (no Unity)

For development without Unity:

```bash
cd planner-engine/csharp/Standalone
dotnet build
dotnet test
```

The `Standalone/` directory contains `.csproj` files that include the same
Runtime/*.cs sources and Tests/Editor/*.cs tests, with the asmdefs ignored.

## Contract

See `../SPEC.md`. Cross-language parity asserted by `../parity-corpus`.
