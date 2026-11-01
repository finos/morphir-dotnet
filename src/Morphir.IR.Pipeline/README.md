# Morphir.IR.Pipeline

The file abstractions and processor pipeline that morphir-dotnet frontends and backends share.

- `VFile` is one virtual file: content, path, path history, diagnostics and plugin data.
- `VFileTree` is a hierarchy of `VFile`s with project-level configuration and metadata.

Use `VFileTree` for any multi-file work. The design is in [Unified File Architecture](../../docs/design/unified-file-architecture.md).

## Write a parser

A `Parser` returns the parsed content, or a `ParseError` that says why it could not.

```fsharp
open Morphir.IR.Pipeline

let jsonParser: Parser =
    fun file ->
        match file.Content with
        | Some (:? string as text) when text.TrimStart().StartsWith "{" -> Result.Ok (box text)
        | Some _ -> Result.Error (ParseError.InvalidInput ("expected a JSON object", None))
        | None -> Result.Error (ParseError.UnsupportedInput "the file has no content")
```

| `ParseError` | Use it when |
| --- | --- |
| `UnsupportedInput reason` | The file is not in a format this parser handles. |
| `InvalidInput (message, position)` | The format is right but the content is malformed. Pass a `SourceRange` if you know where. |
| `Failed cause` | The parser could not finish, for example because reading the file threw an exception. |

`MorphirProcessor` tries parsers in order and stops at the first success. Each failure adds a warning to the file, whichever case it is, and the position from `InvalidInput` becomes the warning's `Position`. If every parser fails, the file also gets a "No parser succeeded" error.

## Build a tree

```fsharp
open Morphir.IR.Pipeline

let domain =
    VFileTree.create "Domain" TreeConfig.empty
    |> VFileTree.addFile (VFile.create "Orders.fs" "module Orders")

let tree =
    VFileTree.create "generated" (TreeConfig.create "MyProject" "1.0.0")
    |> VFileTree.addFile (VFile.create "Types.fs" "module Types")
    |> VFileTree.addDirectory domain

VFileTree.fileCount tree      // 2
VFileTree.directoryCount tree // 1
```

## How paths work

A `VFile` path inside a tree is relative to the directory that holds the file. A file's location is the directory paths above it joined to its own path. The root tree's `Path` is not part of the location, because it says where the tree came from and may name a project file.

In the tree above, `Orders.fs` has the location `Domain/Orders.fs`. Locations use forward slashes on every platform, so map keys match on Windows and Linux.

## Find and update files

`findFile` and `updateFile` take a location first. If no file has that location, they use the first file whose own `VFile` path matches.

```fsharp
VFileTree.findFile "Domain/Orders.fs" tree // the file, by location
VFileTree.findFile "Orders.fs" tree        // the same file, by its own path

let reviewed = tree |> VFileTree.updateFile "Types.fs" (VFile.warn "Check this" None)

VFileTree.hasErrors reviewed               // false
(VFileTree.statistics reviewed).WarningCount // 1
```

`updateFile` changes the file that `findFile` returns for the same path, so a same-named file in another directory stays as it was.

`map` and `filter` apply to every file in the tree. `filter` keeps directories even when it removes all of their files.

## Write to disk

```fsharp
match VFileTree.writeToDisk "out" tree with
| Ok () -> ()                      // out/Types.fs and out/Domain/Orders.fs
| Result.Error error -> eprintfn "%s" (TreeWriteError.describe error)
```

Inside code that opens this namespace, `Error` is the `MessageSeverity` case, so write `Result.Error` when matching a result.

`writeToDisk` checks the whole tree first. If any file fails the check, it writes nothing and returns `TreeWriteError.InvalidTree` with every problem it found:

| `TreeWriteProblem` | Meaning |
| --- | --- |
| `FileWithoutPath file` | The file has no path, so it has nowhere to go. |
| `PathOutsideOutputDirectory location` | The location is rooted or contains `..`. |
| `DuplicateLocation location` | Two files have this location. |

A failure during the write itself, such as a full disk, returns `TreeWriteError.WriteFailed` with the output directory and the original exception. Earlier files can remain on disk in that case. `writeToDisk` creates directories only for the files it writes, so an empty directory in the tree does not appear on disk.

Match the cases to act on a specific failure, or use `describe` to show the error to a person:

```fsharp
match VFileTree.writeToDisk "out" tree with
| Ok () -> ()
| Result.Error (TreeWriteError.WriteFailed (_, (:? System.UnauthorizedAccessException))) ->
    eprintfn "No permission to write to out"
| Result.Error error -> eprintfn "%s" (TreeWriteError.describe error)
```

## Load from disk

```fsharp
match VFileTree.loadFromDisk "out" with
| Ok loaded -> VFileTree.toStringMap loaded |> ignore // the same keys and text that were written
| Result.Error error -> eprintfn "%s" (TreeLoadError.describe error)
```

`loadFromDisk` reads every file as text. It returns `TreeLoadError.DirectoryNotFound` if the path is missing or is not a directory, and `TreeLoadError.LoadFailed` with the original exception if anything in the directory cannot be read. A few things to check before you point it at a directory:

- It includes hidden files and directories, so loading a repository root also reads `.git`.
- It does not preserve binary files, because it assumes UTF-8 text unless a file has a byte order mark.
- It skips symbolic links to directories, because they can form cycles. It reads symbolic links to files.

Use `filter` to drop files after loading. The whole directory is still read first, so load a narrower directory when the tree is large.

Writing a tree and loading it back gives the same locations and text. The exception is an empty directory, which `loadFromDisk` keeps but `writeToDisk` does not create. The loaded tree's `Path` is the directory you loaded, and its `Config` and `Metadata` are empty.

## Migrate from morphir-elm

morphir-elm backends return a flat map of file path to file content. `VFileTree` keeps that structure explicit and carries diagnostics with each file. The conversions below connect the two.

| morphir-elm | morphir-dotnet |
| --- | --- |
| Build a flat map of path to content | Build a `VFileTree`, or a `Map<string, VFile>` and call `fromFileMap` |
| Read the flat map of strings | `toStringMap` |
| Read a flat map with diagnostics kept | `toFileMap` |

To port a backend in two steps, first keep its flat map and wrap each value in a `VFile`:

```fsharp
let fileMap =
    Map.ofList [
        "Types.fs", VFile.create "Types.fs" "module Types"
        "Domain/Orders.fs", VFile.create "Domain/Orders.fs" "module Orders"
    ]

let inferred = VFileTree.fromFileMap fileMap

VFileTree.directoryCount inferred // 1
VFileTree.toStringMap inferred
// map [("Domain/Orders.fs", "module Orders"); ("Types.fs", "module Types")]
```

Then, once callers accept a tree, build the `VFileTree` directly and drop the map.

`fromFileMap` reads each file's location from its key and rewrites the file's path to match, so `Domain/Orders.fs` becomes `Orders.fs` inside a `Domain` directory. The old path stays in `VFile.History`.

The conversions lose a few things:

- `toFileMap` and `toStringMap` leave out files that have no path.
- If two files share a location, the map keeps the last one. `writeToDisk` returns `InvalidTree` for the same tree.
- `toStringMap` calls `ToString()` on non-string content and gives an empty string for missing content.
- The maps do not carry the tree's `Path`, `Metadata` or `Config`, or the metadata of its directories.
