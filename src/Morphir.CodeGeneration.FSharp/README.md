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
At this stage the typed entry point supports classic Library versions 1, 2, 3,
3.0.0 and 3.1.0. V4 implementation is still required by the approved change and
is not advertised as available yet. Specs returns `unsupported-distribution`.
No physical-format conversion or version migration happens in the typed API.

`Transpiler.generate : VFile -> VFileTree` retains its strict classic JSON-file
contract: integer formatVersion 1, 2 or 3 and Library implementations. Missing,
fractional, string or future versions fail. It does not accept Ion, trees or v4.
`generateWithOptions` applies the same JSON contract with explicit options.

`GenerationOptions.Default` enables exact constant folding. Disabling it does
not disable scope, type, naming, SDK or dependency checks. Both typed and file
APIs use the same validation and classic emitter. Constructed typed models
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

Tests extend the [common conformance inventory](../Morphir.Testing/README.md).
One rental expectation covers classic versions, typed/file APIs and optimization
on/off. New v4 or Ion adapters must use that compiler and expectation approach.
