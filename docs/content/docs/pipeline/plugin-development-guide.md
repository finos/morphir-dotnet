---
title: "Plugin Development Guide"
description: "Develop typed plugins for the Morphir IR pipeline"
weight: 30
---

# Plugin Development Guide

A plugin is one typed step in a pipeline. It receives the model, can change it or report on it, and passes it on. This guide covers the plugin contract, how plugins fail, the plugins in `Morphir.IR.Pipeline.Plugins`, and how to test your own.

The [pipeline README](https://github.com/finos/morphir-dotnet/blob/main/src/Morphir.IR.Pipeline/README.md) covers parsers, compilers, file content and publishing.

## The plugin contract

```fsharp
type Plugin<'T> =
    { Name: string
      Configure: PropertyBag -> PropertyBag
      Transform: 'T -> VFile -> ('T option * VFile) }
```

`'T` is the model type, so a plugin receives the parsed model with no cast, and a plugin of the wrong type does not compile into a processor.

- **`Name`** identifies the plugin.
- **`Configure`** adds settings to the processor's data when the plugin is added. Most plugins use `id`, which is what `Plugin.create` does.
- **`Transform`** receives the model and the file. It returns the model to pass on, or `None` to reject it, along with the updated file.

The smallest plugin:

```fsharp
open Morphir.IR.Pipeline

let double = Plugin.create "double" (fun (value: int) file -> (Some (value * 2), file))
```

## Reporting diagnostics

Plugins report through the file. A message can name a logical location, a rule and its source, so people and tools can act on it:

```fsharp
let rejectNegative =
    Plugin.create "reject-negative" (fun (value: int) file ->
        if value < 0 then
            let message =
                { VMessage.create Error "The value must not be negative." with
                    Location = Some "Rules.total"
                    RuleId = Some "negative-value"
                    Source = Some "reject-negative"
                }

            Some value, file |> VFile.report message
        else
            Some value, file)
```

`VFile.info`, `VFile.warn`, `VFile.error` and `VFile.fail` add simpler messages. When the run ends, the file's messages move to the output tree.

## How a plugin stops a run

| The plugin | Later plugins | Compilers |
| --- | --- | --- |
| Adds an `Info` or `Warning` message | Run | Run |
| Adds an `Error` message and returns the model | Run | Do not run |
| Adds a `Fatal` message | Do not run | Do not run |
| Returns `None` | Do not run | Do not run |

Prefer an `Error` and the model when later plugins can still find useful problems, so one run reports as much as it can. Return `None` when later plugins would be working on something broken.

A run with an `Error` produces no files, and publishing refuses a tree that reports errors, so a failed run cannot replace good output.

## Sharing data between plugins

Declare a typed property once and share it. Values go in and come out without casts:

```fsharp
let rowCount = Property.create<int> "my-library.row-count"

let countRows =
    Plugin.create "count" (fun (rows: string list) file ->
        Some rows, file |> VFile.set rowCount rows.Length)

let reportRows =
    Plugin.create "report" (fun (rows: string list) file ->
        match VFile.tryGet rowCount file with
        | Some count -> Some rows, file |> VFile.info (sprintf "%d rows" count)
        | None -> Some rows, file |> VFile.warn "Rows were not counted." None)
```

Use a dotted name that starts with your library. Two properties refer to the same value only when both their name and value type match.

Data on the file reaches the output tree's metadata when the run ends, so callers read it with `VFileTree.tryGet`.

## Running on part of a model

`Plugin.focus` runs a plugin on one part of a larger model. It takes a function that reads the part, and one that puts an updated part back:

```fsharp
type Model = { Version: int; Rows: string list }

let sortRows = Plugin.create "sort" (fun (rows: string list) file -> (Some (List.sort rows), file))

let sortModelRows: Plugin<Model> =
    Plugin.focus (fun model -> model.Rows) (fun rows model -> { model with Rows = rows }) sortRows
```

If the inner plugin returns `None`, the whole model is rejected.

## The built-in plugins

`Morphir.IR.Pipeline.Plugins` works on classic IR. Each plugin is generic over the package's attribute types, so it runs on any `PackageDefinition<'ta, 'va>`.

### Optimizer

`Optimizer.create` takes `Optimizer.Options`:

| Options | Rewrites |
| --- | --- |
| `Optimizer.foldingOnly` | Folds SDK operations on literals. Never changes which models a later stage accepts. |
| `Optimizer.all` | Also rewrites boolean short-circuits, `if` on a literal and identity lambdas. These can remove code a later stage would have rejected. |

Folding only happens when the result matches what generated code computes at run time. `Optimizer.tryFold` returns `None` when:

- the function is not an SDK function, so a model's own `add` is never folded
- a whole number, whether an input or the result, does not fit the `int32` that Morphir `Int` maps to
- a division is by zero, or a float result is not finite
- the literal types differ
- the operation has no generated mapping, such as `Basics.and` or `String.append`

Each change adds an `Info` message naming the value.

### TypeValidator

`TypeValidator.create ()` reports an error with the rule `type-mismatch` only when both sides are SDK primitive types that cannot match, such as a string literal under a declared `Int`. Anything it cannot infer counts as unknown and is silent, including model types and aliases, so it never rejects a valid model. It always keeps the model, so later plugins can report more. It does not report unbound variables.

### PrettyPrinter

`PrettyPrinter.create ()` formats every value in a package into the typed property `PrettyPrinter.output`, leaving the model unchanged. `PrettyPrinter.createWithConfig` takes a configuration built from `defaultConfig`, `withColors`, `withIndent` and `withoutTypes`.

```fsharp
open Morphir.IR.Classic
open Morphir.IR.Pipeline.Plugins

let inspect (package: Package.PackageDefinition<unit, unit>) =
    let parser: Parser<Package.PackageDefinition<unit, unit>> = fun _ -> Ok package

    let processor = pipeline {
        parse parser
        uses (TypeValidator.create ())
        uses (Optimizer.create Optimizer.foldingOnly)
        uses (PrettyPrinter.create ())
    }

    let tree = MorphirProcessor.processFile VFile.empty processor
    VFileTree.tryGet PrettyPrinter.output tree
```

## Writing a safe transformation

A transformation must not change what a program does. For each rewrite, ask what the generated code would have computed, including at the edges:

```fsharp
// Safe only inside the range of the type Morphir Int maps to
let foldAdd (a: int64) (b: int64) =
    let inRange (value: int64) = value >= int64 System.Int32.MinValue && value <= int64 System.Int32.MaxValue

    if inRange a && inRange b && inRange (a + b) then Some (a + b) else None
```

Check the inputs as well as the result. A literal out of range is rejected by the backend today, so folding it into something in range would make a rejected model start generating.

Match functions by their fully qualified name. A model can define its own function called `add`.

Rewrite bottom-up, so `(1 + 2) + 3` collapses completely, and keep the attributes of the expression you replace, because later stages can read them.

## Testing plugins

Call `Transform` directly to test a plugin by itself, and assert on the model and the messages:

```fsharp
open Expecto

[<Tests>]
let tests =
    testList "double" [
        test "should double the value" {
            let result, file = double.Transform 21 VFile.empty

            Expect.equal result (Some 42) "should double"
            Expect.isEmpty file.Messages "should report nothing"
        }
    ]
```

Then test it in a processor, since that covers its effect on later stages:

```fsharp
let runWith plugin =
    let parser: Parser<int> = fun _ -> Ok -1

    MorphirProcessor.empty
    |> MorphirProcessor.parse parser
    |> MorphirProcessor.plugin plugin
    |> MorphirProcessor.processFile VFile.empty

test "should report a negative value on the tree" {
    Expect.isTrue (VFileTree.hasErrors (runWith rejectNegative)) "the error should reach the tree"
}
```

For a transformation that changes generated code, compile and run the output with the plugin on and off and compare the results. `optimizationEquivalenceTests` in `tests/Morphir.CodeGeneration.FSharp.Tests` does this for the optimizer.

## A complete example

`Transpiler` in `Morphir.CodeGeneration.FSharp` is a production pipeline. It parses a classic model, validates scopes and types, folds constants and emits one F# file per module, and the Myriad plugin and build task both call it.

## Next steps

- Read the [TypeValidator source](https://github.com/finos/morphir-dotnet/blob/main/src/Morphir.IR.Pipeline.Plugins/TypeValidator.fs)
- Read the [Optimizer source](https://github.com/finos/morphir-dotnet/blob/main/src/Morphir.IR.Pipeline.Plugins/Optimizer.fs)
- Read the [PrettyPrinter source](https://github.com/finos/morphir-dotnet/blob/main/src/Morphir.IR.Pipeline.Plugins/PrettyPrinter.fs)
- Read the [Transpiler source](https://github.com/finos/morphir-dotnet/blob/main/src/Morphir.CodeGeneration.FSharp/Transpiler.fs)
