# F# generation APIs

The additive typed API accepts an existing semantic model. The context `VFile`
supplies paths and caller metadata; generation does not read its content or
serialize the model.

```fsharp
open Morphir.IR.Model
open Morphir.IR.Pipeline
open Morphir.CodeGeneration.FSharp

let generateLibrary (model: VersionedModel) =
    Transpiler.generateModel model (VFile.fromPath "retained/model.ion")

let generateWithoutFolding (model: VersionedModel) =
    Transpiler.generateModelWith
        { Optimize = false }
        model
        (VFile.fromPath "retained/model.ion")
```

`TypedGenerationTests` and `GenerationOptionsTests` compile these public calls
as consumers and execute their generated programs through the common runner.
The typed entry point supports classic Library versions 1, 2, 3, 3.0.0 and
3.1.0, and v4 Library versions 4 and 4.0.0. V4 generation consumes native v4
nodes. Specs and Application return `unsupported-distribution`.
No physical-format conversion or version migration happens in the typed API.

`Transpiler.generate : VFile -> VFileTree` retains its strict classic JSON-file
contract: integer formatVersion 1, 2 or 3 and Library implementations. Missing,
fractional, string or future versions fail. It does not accept Ion, trees or v4.
`generateWithOptions` applies the same JSON contract with explicit options.

`GenerationOptions.Default` enables exact constant folding. Disabling it does
not disable scope, type, naming, SDK or dependency checks. Both typed and file
APIs share the target profile and dependency ordering. Constructed typed models
cannot bypass generation's name rules or type-application arity checks.

Success returns one file per module. `Transpiler.manifest` and
`VFileTree.declaredOrder` carry dependency order independently of directories.
Errors return no successful files and carry the context path and available
logical location. The caller owns publication; generation writes no files.

Callers can set `Transpiler.requiredSemantics` in the context file's typed
metadata. This profile refuses nonempty requirements until a mapping is declared.
Opaque IR extension objects do not implicitly declare such requirements. Native,
external, incomplete and hole nodes are refused, including external fallbacks.
Nonempty type constraints are unsupported; optional opaque metadata is retained
without becoming executable behavior.

Native AOT hosts emit the generator's complete F# source without Fantomas,
whose formatter requires dynamic generic reflection. JIT hosts also apply
Fantomas. Whitespace can therefore differ between these runtimes; generated
behavior and declared artifact order use the same conformance checks.

Tests extend the [common conformance inventory](../Morphir.Testing/README.md).
One rental expectation covers classic and v4 typed models and optimization on/off.
The legacy file path uses the same expectation for classic JSON. Ion adapters
must use that compiler and expectation approach.

## Supported model profile

| Capability | Shared case inventory |
| --- | --- |
| Aliases, unions, generics, tuples, closed records, functions, unit and visibility | `typeStructure` |
| Constructors, lists, records, fields, applications, lambdas, conditionals and ordered patterns | `expressionCore`, `recordBilling` |
| Local definitions, recursion, destructuring, lexical scope and updates | `lexicalCapture`, `localRecursion`, `recordOperations`, `letRecOrder` |
| SDK Basics, List, Maybe and Result mappings | `sdkMappings`, `sdkRecordPayloads`, `sdkIdentityCapture`, pricing and arithmetic cases |
| Name, scope, type arity, inferred/declared consistency and dependency checks | `TypedValidationTests.failures`, inferred-type rows in `ModelConformanceTests` |
| Exact constant folding with the same observable behavior when disabled | `GenerationArithmetic.cases`, pricing rows |
| Scalar boundaries and representability | `GenerationScalars.cases` |
| Refused distribution/body/type forms | `ModelRefusalInputs.cases` |
| Unknown required semantics | `GenerationRequirementsTests` and backend-resolution tests |

An SDK name needs an explicit mapping. Arbitrary SDK or dependency references
do not become available simply because their name occurs in the input.

Integers map to .NET Int32. Floats map to finite IEEE doubles, preserving their
bits, including negative zero. Decimals must be exactly representable as a .NET
decimal with the declared scale, at most 28. Characters must be a single BMP
character. Overflow, decimal rounding, nonfinite floats and supplementary-plane
characters fail with `literal-range`. Document literals are refused with
`unsupported-document-literal`.
Decimal folding remains disabled when exactness and scale cannot be proved.
Float arithmetic folds only when the result equals the exact binary rational
calculation. Validation runs before any fold, including for unused expressions.

Open extensible records, holes, incomplete types or bodies, native bodies,
external bodies including fallbacks, and nonempty constraints are refused.
Constraint checks include dependency type and value signatures, even when a
dependency is unused by the generated modules.
Optional opaque extension objects do not request executable semantics. Callers
that require an extension must supply a capability requirement explicitly.

## Extending conformance

Add the behavior and its independent expected output to Morphir.Testing. Extend
the existing representation inventory for that case. Use `Supported`, `Refused`
with a diagnostic code, or `Unrepresentable` with a concrete reason for every
required row. `GenerationConformance.validate` fails missing and duplicate rows.
Run generation through `Transpiler.generateModelWith` and the existing compiler
runner. Do not add a version-specific compiler, assertion strategy or expected
result copied from another emitter. Source parity is an additional assertion.
