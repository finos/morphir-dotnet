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

Both typed paths now lower transparent aliases, generic closed records, tuples,
function and unit payloads, and closed custom cases. Generated payload properties
are get-only. Classic integer constants are the current executable expression
slice. The remaining expression scope is tracked in the active OpenSpec tasks.
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
type corpus has compiled and executed under C# 14.0 with stable SDK 10.0.403,
compiler 5.9.0-1.26523.112, and C# 15.0 with SDK 11.0.100-rc.2.26504.105,
compiler 5.12.0-1.26504.105. Both consumers target net10.0 and run on 10.0.14.
The RC compiler is isolated under an owned test directory. Root global.json
stays on the stable SDK. Both independently constructed classic/v4 models produce
identical type-source bytes. The remaining expression corpus is pending.

`tests/Morphir.CodeGeneration.CSharp.Tests/verify-host.ps1` packs the real core
and Models into a local proof feed and restores a fresh consumer with a private
cache. The first attempt exposed the F# SDK's implicit FSharp.Core 10.1.403
reference downgrading the required 11.0.100 assembly, causing runtime load
failure. The corrected F# test host disables that implicit reference and uses
the actual transitive closure. No model library was retargeted. Both attempts
are retained in the owned evidence inventory.

## Type mappings and equality

| Morphir type | C# representation |
| --- | --- |
| Basics Int, Float, Bool | `int`, `double`, `bool` |
| String, Char, Decimal | `string`, `char`, exact admitted `decimal` |
| Unit, Order | `Morphir.SDK.CSharp.Unit`, `Order` |
| List, Maybe, Result | SDK immutable wrappers, `Result<E,V>` with error first |
| Dict, Set | SDK immutable wrappers with Morphir comparers |
| Tuple with at least two items | C# value tuple |
| Function | Nested `System.Func<Input,Output>` |
| Closed record | Sealed class with constructor and get-only payloads |
| Custom type | Closed abstract base with nested sealed case classes |

Transparent aliases expand before emission. Structurally equivalent records use
one deterministic representation, including aliases with substituted generic
arguments. Unused alias parameters do not change the underlying representation.
Anonymous record payloads receive deterministic named record classes. Private
constructors remain inaccessible from a separate assembly. A public payload
exposing a private nominal type fails with `private-type-exposure`.

Every generated record and case implements `IMorphirValue`. Equality checks the
exact generated type or case and calls `Basics.Equal` for every payload. Hashing
uses `MorphirValue.Hash` for the same payloads. Typed, boxed and operator equality
route through that protocol. Consumers verify nested collections, NaN, signed
zero and decimal scale against the SDK contract. No CLR `IEquatable` fallback or
reflection establishes Morphir equality. The protocol does not establish the
physical immutability of arbitrary caller-supplied generic payloads.
