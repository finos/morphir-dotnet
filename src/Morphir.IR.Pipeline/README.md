# Morphir.IR.Pipeline

The processor pipeline that morphir-dotnet frontends and backends share. The F# backend runs on it: see `Transpiler` in `Morphir.CodeGeneration.FSharp` for a complete pipeline.

- `MorphirProcessor<'T>` turns input into a tree of output files through typed stages.

Its input and output are the `VFile` and `VFileTree` of [Morphir.Files](../Morphir.Files/README.md), which covers file content, typed data, trees, paths and publishing to disk.

The design is in [Unified File Architecture](../../docs/design/unified-file-architecture.md).

## Run a pipeline

A run parses its input into one model of type `'T`, passes it through plugins, and compiles it into any number of files.

```fsharp
open Morphir.Files
open Morphir.IR.Pipeline

let parseCsv: Parser<string list> =
    Parser.forFile (fun file ->
        match VFile.readText file with
        | Ok text when text.Contains ',' -> Ok (List.ofArray (text.Split ','))
        | Ok _ -> Result.Error (ParseError.invalidInput "expected comma-separated values")
        | Result.Error (ContentError.ReadFailed (path, cause)) -> Result.Error (ParseError.Failed (path, cause)))

let sortRows = Plugin.create "sort" (fun (rows: string list) file -> (Some (List.sort rows), file))

let writeOneFilePerRow: Compiler<string list> =
    fun rows _ ->
        rows
        |> List.fold (fun tree row -> tree |> VFileTree.addFile (VFile.create (row + ".txt") row)) VFileTree.empty

let processor = pipeline {
    parse parseCsv
    uses sortRows
    stringify writeOneFilePerRow
}

let tree = MorphirProcessor.processFile (VFile.create "rows.csv" "b,a") processor
// tree holds a.txt and b.txt
```

The model type is inferred from the stages, and stages of different types do not compile together.

| Function | Input | Result |
| --- | --- | --- |
| `run` | A `File` or a `Directory` | One model, then the compilers' files |
| `processFile` | A `VFile` | Same as `run (File file)` |
| `processPath` | A path on disk | Same as `processFile (VFile.fromDisk path)` |
| `runEachFile` | A `VFileTree` | One run per file, with output staying in that file's directory |

A parser for a whole directory takes the `TreeNode` directly. A parser made with `Parser.forFile` reports unsupported input for a directory, so the next parser is tried. `Parser.ofParseable ()` builds a parser for any type with a static `Parse: string -> T` member.

### What happens when something fails

A failed run returns a tree with no files. Its `Messages` say why, and each message can name a `Path`, `Location`, `Position`, `Source` and `RuleId`. `VFileTree.hasErrors` and `VMessage.describe` read them.

| Stage | Behaviour |
| --- | --- |
| Parsers | Tried in order until one succeeds. Failures are warnings if a later parser succeeds. If all fail, `InvalidInput` and `Failed` become errors keeping their details, and a generic "No parser succeeded" error is added only when every parser reported `UnsupportedInput`. |
| Plugins | Keep running after an `Error` message so one run can report several problems. Returning `None` or adding a `Fatal` message stops the rest. |
| Compilers | Run only when there are no `Error` or `Fatal` messages. Warnings do not block them. |

Messages from the input file move to the output tree. So does data a plugin stored on the file: it becomes the tree's metadata, with compiler values winning. In `runEachFile`, a run's metadata goes onto that run's output files instead, and each file's own values win.

`Plugin.focus` runs a plugin on one part of a larger model, such as a package-level plugin on a model that also holds a version:

```fsharp
let onFirst = Plugin.focus fst (fun value (_, rest: string) -> (value, rest)) (Plugin.create "double" (fun (n: int) file -> (Some (n * 2), file)))
```
