---
title: "C# generation"
---

`Morphir.CodeGeneration.CSharp` landed in
[PR #478](https://github.com/finos/morphir-dotnet/pull/478), tracked by
[issue #474](https://github.com/finos/morphir-dotnet/issues/474).
The core accepts typed `VersionedModel` values in memory, preserves
their source family/version, and returns immutable sources and diagnostics.
The entry point is `CSharpGenerator.Generate(model, options, cancellationToken)`.
`GenerationOptions.Default` selects numeric C# 14, independently of the SDK.
Numeric C# 15 uses the same public representation and runtime contract.

Both typed paths now lower transparent aliases, generic closed records, tuples,
function and unit payloads, and closed custom cases. Generated payload properties
are get-only. Both typed paths now emit literals, variables and references,
application, returned functions, lambdas, nonrecursive value/function lets,
conditionals, top-level self recursion, constructors, tuples, lists and closed
record operations. Ordered patterns include wildcard, alias, literal, tuple,
constructor, empty-list, head/tail and unit patterns.
The SDK binding table covers the frozen 67-operation inventory. Generated calls
execute the 207 required frozen observations in both typed families. The eight
representation exclusions remain exclusions. The
[main specification](../../../../../openspec/specs/morphir-csharp-transpilation/spec.md)
and [archived 13/13 tasks](../../../../../openspec/changes/archive/2026-11-03-add-csharp-code-generation/tasks.md)
record the delivered bounded core. Archive follow-up landing and coordinator
cleanup remain pending. Decoding, configuration, source publication
and compiler execution belong to callers. CLI registration and a production
Roslyn adapter are separate initiatives.

## Host compatibility

| Environment | Evidence |
| --- | --- |
| Windows arm64, .NET 10.0.14, SDK 10.0.403 Roslyn driver | Fresh package consumer restores, AddSource compiles, constant executes, caller cancellation returns no sources |
| Windows arm64, .NET 10.0.14, SDK 10.0.403 fresh in-memory consumer | Both typed families and numeric profiles generate nonempty type/function/pattern/record/SDK text; deterministic sources, cancellation and refusal checks pass |
| Self-contained win-arm64, net10.0, `TrimMode=link` | Published consumer executes with identical observations/source hashes to its untrimmed build; no IL warnings in this smoke |
| Legacy .NET Framework / netstandard IDE compiler hosts | Untested; Models and core require net10.0 |
| Other IDE/runtime/OS combinations | Untested |
| Native AOT | Untested |

The demonstrated production package closure is Models, FSharp.Core 11.0.100 and
FSharp.SystemTextJson 1.4.36. The test runner additionally brings Expecto and
Mono.Cecil. Roslyn references come from the selected SDK only in the test host.
Tooling, Ion, Pipeline and the CLI are absent from the core package closure.
Language syntax compatibility does not establish IDE-host compatibility.

Numeric language profiles and compiler release channels are separate. The same
shared type/function/pattern/record/SDK corpus has compiled and executed under
C# 14.0 with stable SDK 10.0.403,
compiler 5.9.0-1.26523.112, and C# 15.0 with SDK 11.0.100-rc.2.26504.105,
compiler 5.12.0-1.26504.105. Both consumers target net10.0 and run on 10.0.14.
The RC compiler is isolated under an owned test directory. Root global.json
stays on the stable SDK. Both independently constructed classic/v4 models produce
identical source bytes. Original classic car-rental versions 1/2/3, a labeled
synthesized v4 car input, direct daily-rate inputs and a compatible v4 ecosystem
type slice also compile and execute under both pinned compilers.

`tests/Morphir.CodeGeneration.CSharp.Tests/verify-host.ps1` packs the real core
and Models into a local proof feed and restores a fresh consumer with a private
cache. The first attempt exposed the F# SDK's implicit FSharp.Core 10.1.403
reference downgrading the required 11.0.100 assembly, causing runtime load
failure. The corrected F# test host disables that implicit reference and uses
the actual transitive closure. No model library was retargeted. Both attempts
are retained in the owned evidence inventory.

`verify-trimming.ps1` packs Models and the core into a fresh local feed, restores
a separate private cache, and runs a typed in-memory consumer before and after
self-contained trimming. That consumer's package closure contains only Models,
the core, FSharp.Core and FSharp.SystemTextJson. It loads no Roslyn, Expecto,
Mono.Cecil or SDK runtime. The generated text still declares the SDK runtime
requirement. This smoke establishes the listed consumer behavior on the tested
host; it does not establish arbitrary IDE integration or Native AOT support.

## Calling the core

Supply an already decoded `Morphir.IR.Model.VersionedModel`. Classic Library
versions `1`, `2`, `3`, `3.0.0` and `3.1.0`, and v4 Library versions `4` and
`4.0.0`, retain their exact source version and typed family. Empty libraries
succeed with an empty source list. Specs, Application and unsupported versions
produce located failures with no sources. No serialization bridge or implicit
migration runs.

```fsharp
open Morphir.CodeGeneration.CSharp

let result = CSharpGenerator.Generate(model, { LanguageVersion = 14 }, cancellationToken)
match result.Status with
| GenerationStatus.Success -> consumeSources result.Sources
| GenerationStatus.Failure -> reportDiagnostics result.Diagnostics
| GenerationStatus.Cancelled -> reportCancellation ()
```

The caller supplies `model`, the token and the three handling functions. Sources
contain portable relative `.g.cs` paths and deterministic LF text. Encode text as
UTF-8 without a BOM when publishing it. Decoding, package resolution, compilation,
configuration and file publication remain caller responsibilities. Numeric 15
uses the same API and case representation. See the [diagnostic contract](diagnostics.md).

## Business fixture evidence

The shared parameterized test matrix executes the original classic car-rental
1/2/3 files unchanged and preserves their raw source hashes and version spelling.
Independent observations require daily rate 5000, Available/Reserved/Rented/
Available lifecycle states, invoice 15000, seven typed rejections and persistence
of both the original inventory and prior billing obligation.

The equivalent v4 car library is labeled **SYNTHESIZED TEST INPUT**. Its typed
builder exists only in tests and supplies no expected result. It is neither an
upstream v4 fixture nor production migration support. The existing direct v4
daily-rate fixture executes independently and returns 5000.

Whole original ecosystem examples retain their source limitations. The regulatory
example references missing types; books-and-records has `open-deal` and
`close-deal` draft `IncompleteBody` declarations. Both originals fail with located
diagnostics and no partial sources. The separately labeled **COMPATIBLE TYPE
SLICE** retains every original books-and-records type, visibility and payload,
excluding exactly those two draft values. Its nonempty generated records/cases
execute payload, equality/hash, nested dictionary persistence and get-only
property checks. This is selected-input evidence, not full-original conformance.

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

## Names, literals and functions

Package paths become escaped namespaces. A module named `Example.Functions`
uses the static class `Example.FunctionsModule`; records and cases are nested
under that class. Source paths use safe prefixes, such as
`p_Example/m_FunctionsModule.g.cs`, including on Windows. Portable comparisons
ignore case. A collision diagnostic identifies both owners. Generated local
identities are fresh within each function and do not depend on source spelling.
Names that collide with a required structural protocol, generic parameter or
inherited CLR member fail with a located `name-collision`. Malformed typed names
fail with `invalid-name`. An empty library succeeds without allocating a package
namespace or file path.

Top-level definitions emit typed static methods. Function values and partial
applications use nested `Func` values that call those methods. Local bindings
evaluate before their name enters scope, and local functions capture their
original outer bindings. Delegate invocation uses `.Invoke`, and arguments
evaluate once in source order. The shared compiled consumer observes those
properties through a caller-supplied callback, generic identity, a captured let,
shadowing, a three-argument partial application and a returned lambda.

Literals use culture-independent numeric text and explicit UTF-16 escapes.
Supplementary strings are admitted. Char requires one non-surrogate BMP scalar.
Int literals must fit checked Int32. A v4 decimal lexeme must hold exactly in
Decimal96; rounded or out-of-range values fail with `representation-loss`.
Signed-zero Float bits are preserved, and classic nonfinite Float literals map
to the corresponding double constants. Invalid UTF-16 strings fail explicitly.

Type checking runs before a successful source set is returned. Unknown variables
and references, inconsistent applications, infinite types and unsupported numeric
constraints produce located diagnostics. Local recursive groups remain outside
the admitted slice. The business fixture scope and evidence are described above.

Cancellation checks cover typed traversal, recursive alias/record expansion,
name allocation, SDK comparer construction and text emission. The final result
boundary checks the token again before returning success. Cancellation returns
`Cancelled`, the original typed family/source version, and no sources. The
active-generation tests interrupt alias expansion and million-character strings
in both families. Their measured stop times describe that bounded test run,
not a general latency guarantee. Invalid later modules also return no partial
source set.

Matches evaluate their subject once and select the first matching branch.
Destructuring and pattern lambdas bind fresh lexical identities after their
pattern succeeds. An unmatched non-exhaustive match or destructuring raises a
pattern failure. This is the source operation's failure, with all branches
checked during generation. Unsupported source operations fail generation.

Record expressions evaluate fields in source order. Updates evaluate the target
once, then update values in source order, and construct a new immutable record.
Constructor argument order follows the selected structural record declaration.
The classic model's record map has key order; v4 retains its authored field order.
Field functions require a known closed record type. Missing fields, incompatible
updates, duplicate binders and inconsistent branch types produce located failures.

## SDK bindings

Generated code calls the public `Morphir.SDK.CSharp` API. The generator package
does not load that runtime to generate text. Its declared output requirement is
net10.0 plus `Morphir.SDK.CSharp`, independently of the compiler host's packages.

| Source module | Admitted runtime calls |
| --- | --- |
| Basics | Checked Int32/IEEE Float arithmetic, conversion/rounding, comparisons, min/max/clamp, Order cases and structural equality |
| List | singleton, cons, map, append, head, tail and length |
| Maybe | Just/Nothing constructors and patterns, map and andThen |
| Result | Ok/Err constructors and patterns, map and andThen; error type first, value type second |
| Dict | empty, singleton, fromList, get, insert, update, remove, toList and union |
| Set | empty, singleton, fromList, insert, remove, toList and union |
| Decimal | Exact add, sub, mul, div, compare and toString |

Map and andThen use the actual instance combinators. Nothing and Err bypass
callbacks, and callback exceptions propagate. SDK constructors also work as
function values. Dictionary/set factories select `MorphirComparers.Int`,
`Float`, `Char` or `String`, composing list and two/three-tuple comparers as
needed. No CLR default comparer or reflection fallback is generated.

Ordering requires concrete admitted comparable shapes, with finite Float values.
Bool, Decimal, Unit,
Order, records, variants and function shapes have no SDK ordering. A factory
whose key comparer cannot be resolved fails with `unsupported-comparable`.
Known function payloads fail equality admission with `unsupported-equality`.
Unmapped SDK references fail with `unmapped-sdk`; dependencies without an
implementation fail with `unresolved-reference` or `unresolved-type`.

Type/literal incompatibilities are generation diagnostics. Value-dependent SDK
failures, such as checked overflow or nonfinite Float-to-Int conversion, remain
the SDK's execution errors. Decimal operations preserve the exact Decimal96
subset and do not round unsupported fractions. Ordinary Decimal equality ignores
authored scale; Decimal arithmetic produces canonical values. The generated-call
corpus checks frozen values/errors, input persistence and equality/hash invariants.
