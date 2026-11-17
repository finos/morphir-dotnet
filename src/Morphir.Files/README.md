# Morphir.Files

The virtual files that morphir-dotnet frontends, backends and models share. `Morphir.IR.Pipeline` runs its processors over them.

- `VFile` is one virtual file: content, path, path history, diagnostics and plugin data.
- `VFileTree` is a hierarchy of `VFile`s with configuration, metadata and tree-level diagnostics.
- `PropertyBag` holds typed data attached to files, trees and tree configuration.
- `VFileProviders` turns a tree into a Microsoft `IFileProvider` and builds a tree from any `IFileProvider`.

The library targets `net11.0`, `net10.0` and `netstandard2.0`, so `Morphir.Models` and the assemblies that a compiler or MSBuild host loads can use it.

The design is in [Unified File Architecture](../../docs/design/unified-file-architecture.md).

```fsharp
open Morphir.Files
```

## File content

A file's content is absent, or one of three cases:

| `VFileContent` | Held in memory | Create with |
| --- | --- | --- |
| `Text` | Yes | `VFile.create path "text"` or `VFile.ofText` |
| `Bytes` | Yes | `VFile.create path bytes` or `VFile.ofBytes` |
| `Stream` | No | `VFile.fromDisk path`, `VFile.ofDiskFile path diskPath`, or `VFile.create path source` with a `StreamSource` |

`VFile.create` and `VFile.fromContent` check the content type at compile time. They are inline, so C# callers use `ofText`, `ofBytes` and `ofStream`.

A `StreamSource` opens a new stream each time it is read, so one parser reading a file does not use it up for the next. The caller that opens a stream owns it and disposes it.

```fsharp
let source = StreamSource(fun () -> System.IO.File.OpenRead "large.bin" :> System.IO.Stream)
let large = VFile.create "large.bin" source

match VFile.readText (VFile.fromPath "empty.txt") with
| Ok text -> printfn "%s" text // absent content reads as empty text
| Result.Error error -> eprintfn "%s" (ContentError.describe error)
```

`VFile.readText` and `VFileContent.openRead` return a `ContentError` with the original exception rather than throwing. Reading text honours a byte order mark. Text is written as UTF-8 without one, and bytes and streams reach disk byte for byte.

## Attach typed data

Files, trees and tree configuration can carry extra data in a `PropertyBag`, and so can the processors of `Morphir.IR.Pipeline`. A property is an object that knows its value's type, so values go in and come out without casts, and storing the wrong type is a compile error.

```fsharp
// Declare each property once, with a dotted name that starts with your library
let irVersion  = Property.create<int> "morphir.ir.version"
let sourcePath = Property.create<string> "morphir.source.path"

// Build a bag from typed values
let bag = PropertyBag.ofSeq [ irVersion.With 3; sourcePath.With "orders.json" ]

// The bag is always the last parameter, so calls chain and compose
let updated = bag |> PropertyBag.set irVersion 4 |> PropertyBag.remove sourcePath
let version = PropertyBag.tryGet irVersion <| updated   // Some 4
let stamp = PropertyBag.set irVersion 3 >> PropertyBag.set sourcePath "orders.json"

// Files, trees, configuration and processors have the same functions
let file = VFile.fromPath "orders.json" |> VFile.set irVersion 3
VFile.tryGet irVersion file                             // Some 3
```

Two properties refer to the same value only when both their name and value type match. A property with the same name and a different type sees nothing and raises no error.

`|>` and `<|` have the same precedence and group left to right, so `bag |> PropertyBag.set irVersion <| 3` is not what it looks like. Add parentheses if you mix them in one expression.

## Build a tree

```fsharp
let domain =
    VFileTree.create "Domain" TreeConfig.empty
    |> VFileTree.addFile (VFile.create "Orders.fs" "module Orders")

let generated =
    VFileTree.create "generated" (TreeConfig.create "MyProject" "1.0.0")
    |> VFileTree.addFile (VFile.create "Types.fs" "module Types")
    |> VFileTree.addDirectory domain

VFileTree.fileCount generated      // 2
VFileTree.directoryCount generated // 1
```

## How paths work

A `VFile` path inside a tree is relative to the directory that holds the file. A file's location is the directory paths above it joined to its own path. The root tree's `Path` is not part of the location, because it says where the tree came from and may name a project file.

In the tree above, `Orders.fs` has the location `Domain/Orders.fs`. Locations use forward slashes on every platform, so map keys match on Windows and Linux.

## Find and update files

`findFile` and `updateFile` take a location first. If no file has that location, they use the first file whose own `VFile` path matches.

```fsharp
VFileTree.findFile "Domain/Orders.fs" generated // the file, by location
VFileTree.findFile "Orders.fs" generated        // the same file, by its own path

let reviewed = generated |> VFileTree.updateFile "Types.fs" (VFile.warn "Check this" None)

VFileTree.hasErrors reviewed                    // false
(VFileTree.statistics reviewed).WarningCount    // 1
```

`updateFile` changes the file that `findFile` returns for the same path, so a same-named file in another directory stays as it was.

`map` and `filter` apply to every file in the tree. `filter` keeps directories even when it removes all of their files.

## Publish to disk

`publish` writes a tree under an output root so that files cannot escape it, cannot silently overwrite each other, and failed output never replaces good output. `writeToDisk` is `publish` with no manifest.

```fsharp
let options = { OutputRoot = "out"; Manifest = ManifestMode.Manifest "manifest.json" }

match VFileTree.publish options generated with
| Ok result -> printfn "wrote %d, unchanged %d, removed %d" result.Written.Length result.Unchanged.Length result.Removed.Length
| Result.Error error -> eprintfn "%s" (TreeWriteError.describe error)
```

Inside code that opens this namespace, `Error` is the `MessageSeverity` case, so write `Result.Error` when matching a result.

`publish` first checks the whole tree. If anything fails, it writes nothing and returns `TreeWriteError.InvalidTree` with every problem it found. `validatePublish` runs the same check alone, so you can fail before doing expensive work.

| `TreeWriteProblem` | Meaning |
| --- | --- |
| `FileWithoutPath file` | The file has no usable path, so it has nowhere to go. |
| `PathOutsideOutputDirectory location` | The location is rooted, contains `..` or resolves outside the root. |
| `DuplicateLocation location` | Two files have this location, compared case-insensitively on every platform, or a file is at the manifest's location. |
| `SymbolicLinkInPath location` | The root or a directory on the way to the location is a symbolic link. |
| `TreeHasErrors errors` | The tree reports errors, so it must not replace existing output. |
| `DeclaredOrderMismatch (unexpected, missing)` | The declared order does not list exactly the tree's locations. |
| `InvalidManifestEntry entry` | The previous manifest holds an unsafe entry. An entry behind a symbolic link is reported as `SymbolicLinkInPath`. |
| `InvalidManifestName name` | The manifest's name is not a plain file name directly under the root. |

`.` and empty path segments are normalized when a location is built, so `./src/a.fs` publishes at `src/a.fs`.

Content is then written to a private staging directory inside the root, and files move into place only after every file is staged, so a source that fails to read leaves existing output alone. Publishers running as the same user on one machine take turns through a lock file in the temp directory. Files whose bytes already match are left untouched, so their timestamps do not change. `TreeWriteError.WriteFailed` holds the output root and the original exception. An I/O failure during the moves can leave a mix of old and new files, which the next publication corrects. `publish` creates directories only for the files it writes, so an empty directory in the tree does not appear on disk.

With a manifest, `publish` writes the locations in the order given by the `VFileTree.declaredOrder` property, or sorted if it is absent, and deletes files the previous manifest listed that are no longer in the tree. It never deletes anything else. `readManifest` reads the list back and checks every entry.

## Load from disk

```fsharp
match VFileTree.loadFromDisk "out" with
| Ok loaded -> printfn "%d files, none read yet" (VFileTree.fileCount loaded)
| Result.Error error -> eprintfn "%s" (TreeLoadError.describe error)
```

`loadFromDisk` lists the directory without reading any file, and each file's content opens on demand. It returns `TreeLoadError.DirectoryNotFound` if the path is missing or is not a directory, and `TreeLoadError.LoadFailed` with the original exception if a directory cannot be listed. An unreadable file surfaces as a `ContentError` when its content is read.

- It includes hidden files and directories, so loading a repository root also lists `.git`. Use `filter` to drop them.
- It skips symbolic links to directories, because they can form cycles. It follows symbolic links to files.

Publishing a tree and loading it back gives the same locations and content. The exception is an empty directory, which `loadFromDisk` keeps but `publish` does not create. The loaded tree's `Path` is the directory you loaded, and its `Config` and `Metadata` are empty.

## Exchange files with IFileProvider

`VFileProviders` connects trees to `Microsoft.Extensions.FileProviders`, in both directions.

```fsharp
open Microsoft.Extensions.FileProviders

let tree =
    VFileTree.fromFileMap (Map.ofList [ "pkg/acme/demo/module.json", VFile.create "module.json" "{}" ])

let provider: IFileProvider = VFileProviders.asFileProvider tree
let info = provider.GetFileInfo "pkg/acme/demo/module.json"                         // info.Exists, info.Length = 2L
let names = [ for entry in provider.GetDirectoryContents "pkg/acme/demo" -> entry.Name ] // [ "module.json" ]

let back = VFileProviders.ofFileProvider provider                                   // the same locations, nothing read yet
```

`asFileProvider` gives a tree wherever an `IFileProvider` is accepted:

- A path is a file's location in the tree, with either separator and an optional leading one. Lookups are case-sensitive.
- A file's stream opens its content, and its length is its byte count. It has no physical path, and its modification time is `DateTimeOffset.MinValue`.
- A directory, a missing path and a path with `..` are not files: they do not exist, and opening one throws.
- Listing a directory gives its files and then its subdirectories, in tree order, and nothing deeper. The empty path lists the top level.
- `Watch` returns a change token that never fires, because a tree does not change.

`ofFileProvider` builds a tree from any provider: physical, embedded, composite. Each file keeps its location below the provider's root with forward slashes, and its content is a `Stream` that opens the provider's file when it is read, so building the tree reads no file. As with `loadFromDisk`, each directory lists its files and then its subdirectories, sorted by name, and empty directories are kept. The tree's `Path` is `.`.

C# calls them as `VFileProviders.AsFileProvider(tree)` and `VFileProviders.OfFileProvider(provider)`.

## Migrate from morphir-elm

morphir-elm backends return a flat map of file path to file content. `VFileTree` keeps that structure explicit and carries diagnostics with each file. The conversions below connect the two.

| morphir-elm | morphir-dotnet |
| --- | --- |
| Build a flat map of path to content | Build a `VFileTree`, or a `Map<string, VFile>` and call `fromFileMap` |
| Read the flat map of strings | `toStringMap`, which reads content and so returns a `Result` |
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
// Ok (map [("Domain/Orders.fs", "module Orders"); ("Types.fs", "module Types")])
```

Then, once callers accept a tree, build the `VFileTree` directly and drop the map.

`fromFileMap` reads each file's location from its key and rewrites the file's path to match, so `Domain/Orders.fs` becomes `Orders.fs` inside a `Domain` directory. The old path stays in `VFile.History`.

The conversions lose a few things:

- `toFileMap` and `toStringMap` leave out files that have no path.
- If two files share a location, the map keeps the last one. `publish` returns `InvalidTree` for the same tree.
- `toStringMap` gives an empty string for missing content and returns a `ContentError` if a file cannot be read.
- The maps do not carry the tree's `Path`, `Metadata`, `Messages` or `Config`, or the metadata of its directories.
