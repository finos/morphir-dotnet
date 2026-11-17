// Prints the target framework of the Morphir.Files assembly it loaded, then the location and content of each file of a
// tree built from two in-memory files (text and bytes), a property of one file, a file of the tree through its
// IFileProvider and the file count of the tree built back from that provider, then what publishing the tree under the folder given as the first argument did, three times: the second
// time one file has new content, and the third time that file has new content again while another handle holds it open for a
// short time, as a virus scanner does. Publishing runs the members that Polyfill.fs adds for netstandard2.0; on Windows the
// third publication waits for the handle around Polyfill's File.Replace.
module Morphir.Files.NetStandardHost.Program

open System
open System.Reflection
open System.IO
open System.Runtime.Versioning
open System.Threading
open Morphir.Files

let private origin = Property.create<string> "netstandard-host.origin"

let private treeOf (schema: string) =
    VFileTree.empty
    |> VFileTree.addFile (VFile.ofText "docs/readme.txt" "two files" |> VFile.set origin "text")
    |> VFileTree.addFile (VFile.ofBytes "data/schema.bin" (ReadOnlyMemory(Text.Encoding.UTF8.GetBytes schema)))

let private publish (root: string) (tree: VFileTree) =
    match VFileTree.publish { OutputRoot = root; Manifest = ManifestMode.Manifest "manifest.json" } tree with
    | Result.Ok result ->
        printfn "written %s" (String.Join(",", result.Written))
        printfn "unchanged %s" (String.Join(",", result.Unchanged))
        0
    | Result.Error error ->
        eprintfn "%s" (TreeWriteError.describe error)
        1

[<EntryPoint>]
let main args =
    let framework =
        typeof<VFile>.Assembly.GetCustomAttribute<TargetFrameworkAttribute>()
        |> Option.ofObj
        |> Option.map _.FrameworkName

    printfn "framework %s" (Option.defaultValue "unknown" framework)

    let tree = treeOf "v1"

    for KeyValue(location, file) in VFileTree.toFileMap tree do
        let content =
            match VFile.readText file with
            | Result.Ok text -> text
            | Result.Error error -> ContentError.describe error

        printfn "location %s %s" location content

    printfn "origin %s" (VFileTree.findFile "docs/readme.txt" tree |> Option.bind (VFile.tryGet origin) |> Option.defaultValue "none")

    let provider = VFileProviders.asFileProvider tree
    let readme = provider.GetFileInfo "docs/readme.txt"
    printfn "provider %s %d, %d files back" readme.Name readme.Length (VFileTree.fileCount (VFileProviders.ofFileProvider provider))

    match args with
    | [| root |] ->
        let first = publish root tree
        let second = if first <> 0 then first else publish root (treeOf "v2")

        if second <> 0 then
            second
        else
            // Reads, and shares reading and writing but not deletion; released from a thread of its own.
            let scanner = new FileStream(Path.Combine(root, "data", "schema.bin"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite)

            let release =
                Thread(
                    (fun () ->
                        Thread.Sleep 150
                        scanner.Dispose()),
                    IsBackground = true
                )

            release.Start()
            let third = publish root (treeOf "v3")
            release.Join()
            third
    | _ ->
        eprintfn "usage: NetStandardHost <output root>"
        2
