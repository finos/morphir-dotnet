---
title: "C# generation"
---

`Morphir.CodeGeneration.CSharp` is being implemented under
[issue #474](https://github.com/finos/morphir-dotnet/issues/474).
The current foundation accepts typed `VersionedModel` values in memory, preserves
their source family/version, and returns immutable sources and diagnostics.
The entry point is `CSharpGenerator.Generate(model, options, cancellationToken)`.
`GenerationOptions.Default` selects numeric C# 14, independently of the SDK.
Numeric C# 15 uses the same public representation and runtime contract.

This foundation checkpoint emits integer constants from classic libraries.
The remaining type/expression scope is tracked in the active OpenSpec tasks.
It is not yet a complete backend. Decoding, configuration, source publication
and compiler execution belong to callers. CLI registration and a production
Roslyn adapter are separate initiatives.

## Host compatibility

| Environment | Evidence |
| --- | --- |
| Windows arm64, .NET 10.0.14, SDK 10.0.403 Roslyn driver | Fresh package consumer restores, AddSource compiles, constant executes, caller cancellation returns no sources |
| Legacy .NET Framework / netstandard IDE compiler hosts | Untested; Models and core require net10.0 |
| Other IDE/runtime/OS combinations | Untested |

The demonstrated production package closure is Models, FSharp.Core 11.0.100 and
FSharp.SystemTextJson 1.4.36. The test runner additionally brings Expecto and
Mono.Cecil. Roslyn references come from the selected SDK only in the test host.
Tooling, Ion, Pipeline and the CLI are absent from the core package closure.
Language syntax compatibility does not establish IDE-host compatibility.

Numeric language profiles and compiler release channels are separate. The same
constant source has compiled and executed under C# 14.0 with stable SDK 10.0.403,
compiler 5.9.0-1.26523.112, and C# 15.0 with SDK 11.0.100-rc.2.26504.105,
compiler 5.12.0-1.26504.105. Both consumers target net10.0 and run on 10.0.14.
The RC compiler is isolated under an owned test directory. Root global.json
stays on the stable SDK. This initial profile proof does not cover the pending
type/expression corpus.

`tests/Morphir.CodeGeneration.CSharp.Tests/verify-host.ps1` packs the real core
and Models into a local proof feed and restores a fresh consumer with a private
cache. The first attempt exposed the F# SDK's implicit FSharp.Core 10.1.403
reference downgrading the required 11.0.100 assembly, causing runtime load
failure. The corrected F# test host disables that implicit reference and uses
the actual transitive closure. No model library was retargeted. Both attempts
are retained in the owned evidence inventory.
