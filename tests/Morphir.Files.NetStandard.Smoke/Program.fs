// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Exercises the netstandard2.0 build of Morphir.Files from a net11.0 program, as a compiler host loads it: a tree of
/// two in-memory files (text and bytes), their locations and content, a property of one file, the tree as an
/// IFileProvider and the tree built back from that provider, and three publications of the tree under the folder that
/// the first argument names. The second publication has new content in one file; the third has new content in that
/// file again while another handle holds it open for a short time, as a virus scanner does. Publishing runs the members
/// that Polyfill.fs adds for netstandard2.0, and on Windows the third publication waits for the handle around
/// Polyfill's File.Replace. Prints "ok" or "FAIL" with each check; exit 0 means every check held.
module Morphir.Files.NetStandard.Smoke

open System
open System.IO
open System.Reflection
open System.Runtime.Versioning
open System.Threading
open Morphir.Files

let private origin = Property.create<string> "netstandard-smoke.origin"

let private treeOf (schema: string) =
    VFileTree.empty
    |> VFileTree.addFile (VFile.ofText "docs/readme.txt" "two files" |> VFile.set origin "text")
    |> VFileTree.addFile (VFile.ofBytes "data/schema.bin" (ReadOnlyMemory(Text.Encoding.UTF8.GetBytes schema)))

/// What a publication wrote and left unchanged, or its error.
let private publish (root: string) (tree: VFileTree) =
    match VFileTree.publish { OutputRoot = root; Manifest = ManifestMode.Manifest "manifest.json" } tree with
    | Result.Ok result -> $"written {String.Join(',', result.Written)}; unchanged {String.Join(',', result.Unchanged)}"
    | Result.Error error -> TreeWriteError.describe error

/// The third publication, while another handle reads the changed file and shares reading and writing but not
/// deletion; a thread of its own releases the handle.
let private publishWhileHeld (root: string) =
    let scanner =
        new FileStream(Path.Combine(root, "data", "schema.bin"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite)

    let release =
        Thread(
            (fun () ->
                Thread.Sleep 150
                scanner.Dispose()),
            IsBackground = true
        )

    release.Start()
    let published = publish root (treeOf "v3")
    release.Join()
    published

[<EntryPoint>]
let main args =
    match args with
    | [| root |] ->
        let tree = treeOf "v1"

        let framework =
            typeof<VFile>.Assembly.GetCustomAttribute<TargetFrameworkAttribute>()
            |> Option.ofObj
            |> Option.map _.FrameworkName
            |> Option.defaultValue "unknown"

        let locations =
            [ for KeyValue(location, file) in VFileTree.toFileMap tree ->
                  match VFile.readText file with
                  | Result.Ok text -> $"{location} {text}"
                  | Result.Error error -> $"{location} {ContentError.describe error}" ]

        let property =
            VFileTree.findFile "docs/readme.txt" tree
            |> Option.bind (VFile.tryGet origin)
            |> Option.defaultValue "none"

        let provider = VFileProviders.asFileProvider tree
        let readme = provider.GetFileInfo "docs/readme.txt"
        let fromProvider = $"{readme.Name} {readme.Length}, {VFileTree.fileCount (VFileProviders.ofFileProvider provider)} files back"

        let checks = [
            // The host runs the netstandard2.0 assembly, not a build for a newer framework.
            "the netstandard2.0 assembly", ".NETStandard,Version=v2.0", (fun () -> framework)
            "the location and content of each file", "data/schema.bin v1 | docs/readme.txt two files", (fun () -> String.Join(" | ", locations))
            "the property of a file", "text", (fun () -> property)
            // The length is the byte count, and a tree built from the provider has both files.
            "the tree as an IFileProvider", "readme.txt 9, 2 files back", (fun () -> fromProvider)
            "the first publication writes both files in tree order", "written docs/readme.txt,data/schema.bin; unchanged ", (fun () -> publish root tree)
            "the second publication replaces only the changed file", "written data/schema.bin; unchanged docs/readme.txt", (fun () -> publish root (treeOf "v2"))
            "the third publication waits for a short-lived handle, then replaces the file", "written data/schema.bin; unchanged docs/readme.txt", (fun () -> publishWhileHeld root)
        ]

        // In order: each publication starts from the files of the one before.
        let results =
            [ for name, expected, observe in checks ->
                  let observed = observe ()
                  let held = observed = expected

                  if held then
                      Console.WriteLine $"ok   {name}"
                  else
                      Console.WriteLine $"FAIL {name}: expected '{expected}', got '{observed}'"

                  held ]

        if List.forall id results then 0 else 1
    | _ ->
        eprintfn "usage: Morphir.Files.NetStandard.Smoke <output root>"
        2
