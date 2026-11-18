module Morphir.Files.Tests.FileProviderTests

open System
open System.Collections
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Text
open Expecto
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Primitives
open Morphir.Files
open Morphir.TestSupport

/// A stream that reads its bytes forward only, as a network or pipe stream does, and counts the reads of every
/// stream that one counter gives out.
type private ForwardOnlyStream(bytes: byte array, reads: int ref) =
    inherit Stream()
    let mutable position = 0

    override _.CanRead = true
    override _.CanSeek = false
    override _.CanWrite = false
    override _.Length = raise (NotSupportedException "a forward-only stream has no length")

    override _.Position
        with get () = raise (NotSupportedException "a forward-only stream has no position")
        and set _ = raise (NotSupportedException "a forward-only stream has no position")

    override _.Flush() = ()
    override _.Seek(_, _) = raise (NotSupportedException "a forward-only stream cannot seek")
    override _.SetLength _ = raise (NotSupportedException "a forward-only stream cannot change")
    override _.Write(_, _, _) = raise (NotSupportedException "a forward-only stream is read only")

    override _.Read(buffer: byte array, offset: int, count: int) =
        reads.Value <- reads.Value + 1
        let available = min count (bytes.Length - position)
        Array.blit bytes position buffer offset available
        position <- position + available
        available

/// The trees and files that the provider tests read, by intent.
module private ProviderTreeMother =
    /// Text whose UTF-8 form is longer than its characters, so a length in characters would be wrong.
    let moduleText = """{"module":"Demo","note":"café"}"""

    let moduleBytes = UTF8Encoding(false).GetBytes moduleText

    /// A tree with one file whose content is a forward-only stream of no declared length, and the opens and reads of
    /// its streams so far.
    let forwardOnlyStreamOfUnknownLength () =
        let opened = ref 0
        let reads = ref 0

        let source =
            StreamSource(fun () ->
                opened.Value <- opened.Value + 1
                new ForwardOnlyStream(moduleBytes, reads) :> Stream)

        VFileTree.fromFileMap (Map.ofList [ "pkg/acme/demo/module.json", VFile.ofStream "module.json" source ]),
        (fun () -> opened.Value, reads.Value)

    let typeBytes = [| 0x7Buy; 0x7Duy; 0x0Auy |]

    /// A package `acme/demo` with a module file, two subdirectories with files of their own, and a sibling package.
    let demoPackage =
        VFileTree.fromFileMap (
            Map.ofList [
                "pkg/acme/demo/module.json", VFile.ofText "module.json" moduleText
                "pkg/acme/demo/types/order.type.json", VFile.ofBytes "order.type.json" (ReadOnlyMemory typeBytes)
                "pkg/acme/demo/values/total.value.json", VFile.ofText "total.value.json" "{}"
                "pkg/acme/other/module.json", VFile.ofText "module.json" "{}"
            ]
        )

    /// The locations of the embedded document tree below EmbeddedTree/ (see Morphir.Files.Tests.fsproj).
    let embeddedLocations =
        [
            "deps/morphir/sdk/@/basics/module.json"
            "manifest.json"
            "pkg/acme/demo/billing/invoice.type.json"
            "pkg/acme/demo/billing/module.json"
        ]

/// All the bytes of a stream, which this disposes.
let private drain (stream: Stream) =
    use stream = stream
    use buffer = new MemoryStream()
    stream.CopyTo buffer
    buffer.ToArray()

let private bytesOf (file: VFile) =
    match file.Content with
    | None -> [||]
    | Some content ->
        match VFileContent.openRead content with
        | Ok stream -> drain stream
        | Result.Error error -> failtestf "could not read %A: %s" file.Path (ContentError.describe error)

/// An IFileProvider that counts the streams its files open, so a test can see when content is read.
type private CountingProvider(inner: IFileProvider) =
    let mutable opened = 0

    let rec wrap (info: IFileInfo) : IFileInfo =
        { new IFileInfo with
            member _.Exists = info.Exists
            member _.Length = info.Length
            member _.PhysicalPath = info.PhysicalPath
            member _.Name = info.Name
            member _.LastModified = info.LastModified
            member _.IsDirectory = info.IsDirectory

            member _.CreateReadStream() =
                opened <- opened + 1
                info.CreateReadStream() }

    member _.Opened = opened

    interface IFileProvider with
        member _.GetFileInfo subpath = wrap (inner.GetFileInfo subpath)

        member _.GetDirectoryContents subpath =
            let contents = inner.GetDirectoryContents subpath
            let entries = [ for entry in contents -> wrap entry ]

            { new IDirectoryContents with
                member _.Exists = contents.Exists
              interface IEnumerable<IFileInfo> with
                member _.GetEnumerator() = (entries :> IEnumerable<IFileInfo>).GetEnumerator()
              interface IEnumerable with
                member _.GetEnumerator() = (entries :> IEnumerable).GetEnumerator() }

        member _.Watch filter = inner.Watch filter

[<Tests>]
let treeAsProviderTests =
    testList "FileProviders" [
        test "A file is found by its location" {
            let provider = VFileProviders.asFileProvider ProviderTreeMother.demoPackage

            let info = provider.GetFileInfo "pkg/acme/demo/module.json"

            Expect.isTrue info.Exists "the file exists"
            Expect.isFalse info.IsDirectory "a file is not a directory"
            Expect.equal info.Name "module.json" "the name is the last segment of the location"
            Expect.equal info.Length (int64 ProviderTreeMother.moduleBytes.Length) "the length is the byte count"
            Expect.equal (drain (info.CreateReadStream())) ProviderTreeMother.moduleBytes "the stream yields the bytes"

            let fromBytes = provider.GetFileInfo "/pkg/acme/demo/types/order.type.json"
            Expect.isTrue fromBytes.Exists "a leading slash is the root, as for the providers of .NET"
            Expect.equal (drain (fromBytes.CreateReadStream())) ProviderTreeMother.typeBytes "byte content reads byte for byte"
        }

        test "A missing file is reported as not existing" {
            let provider = VFileProviders.asFileProvider ProviderTreeMother.demoPackage

            for path in [ "pkg/acme/demo/missing.json"; "pkg/acme/demo"; ""; "pkg/acme/demo/../demo/module.json" ] do
                let info = provider.GetFileInfo path
                Expect.isFalse info.Exists $"'%s{path}' is not a file of the tree"
                Expect.throws (fun () -> info.CreateReadStream() |> ignore) $"'%s{path}' cannot be opened"
        }

        test "A directory lists its entries" {
            let provider = VFileProviders.asFileProvider ProviderTreeMother.demoPackage

            let contents = provider.GetDirectoryContents "pkg/acme/demo"

            Expect.isTrue contents.Exists "the directory exists"

            let entries =
                [ for entry in contents -> entry.Name, entry.IsDirectory ] |> List.sortBy fst

            Expect.equal
                entries
                [ "module.json", false; "types", true; "values", true ]
                "each file and subdirectory directly under it, and nothing deeper"

            for entry in contents do
                Expect.isTrue entry.Exists $"%s{entry.Name} exists"

            Expect.isFalse (provider.GetDirectoryContents "pkg/acme/missing").Exists "a missing directory does not exist"
            Expect.isFalse (provider.GetDirectoryContents "pkg/acme/demo/module.json").Exists "a file is not a directory"
            Expect.equal [ for entry in provider.GetDirectoryContents "" -> entry.Name ] [ "pkg" ] "the root lists the top level"
        }

        test "Watching never signals a change" {
            let provider = VFileProviders.asFileProvider ProviderTreeMother.demoPackage
            let mutable invoked = false

            for filter in [ "**/*"; "pkg/acme/demo/module.json"; "" ] do
                let token: IChangeToken = provider.Watch filter
                use _registration = token.RegisterChangeCallback((fun _ -> invoked <- true), null)

                Expect.isFalse token.HasChanged $"'%s{filter}' reports no change"
                Expect.isFalse token.ActiveChangeCallbacks $"'%s{filter}' raises no callback"

            Expect.isFalse invoked "no callback was invoked"
        }

        test "Embedded fixtures become a tree" {
            let embedded =
                ManifestEmbeddedFileProvider(Assembly.GetExecutingAssembly(), "EmbeddedTree") :> IFileProvider

            let tree = VFileProviders.ofFileProvider embedded
            let files = VFileTree.toFileMap tree

            Expect.equal (files |> Map.toList |> List.map fst) ProviderTreeMother.embeddedLocations "one file per embedded file at its location"

            for KeyValue(location, file) in files do
                let expected = drain ((embedded.GetFileInfo location).CreateReadStream())
                Expect.isNonEmpty expected $"%s{location} is embedded"
                Expect.equal (bytesOf file) expected $"%s{location} reads the embedded bytes"
        }

        test "A tree built from a tree's provider reads no stream of unknown length" {
            let tree, opensAndReads = ProviderTreeMother.forwardOnlyStreamOfUnknownLength ()

            let back = VFileProviders.ofFileProvider (VFileProviders.asFileProvider tree)

            Expect.equal (opensAndReads ()) (0, 0) "building the tree opened and read no stream"

            let file = Expect.wantSome (VFileTree.findFile "pkg/acme/demo/module.json" back) "the file comes back"

            match file.Content with
            | Some(VFileContent.Stream source) -> Expect.isNone source.Length "an unknown length stays unknown"
            | other -> failtestf "expected stream content, got %A" other

            Expect.equal (bytesOf file) ProviderTreeMother.moduleBytes "the file reads the stream's bytes"
            Expect.equal (fst (opensAndReads ())) 1 "reading the file opened the stream once"
        }

        test "A tree built from a tree's provider keeps each length that is known" {
            let back = VFileProviders.ofFileProvider (VFileProviders.asFileProvider ProviderTreeMother.demoPackage)

            let lengthOf location =
                match (Expect.wantSome (VFileTree.findFile location back) location).Content with
                | Some(VFileContent.Stream source) -> source.Length
                | other -> failtestf "expected stream content at %s, got %A" location other

            Expect.equal (lengthOf "pkg/acme/demo/module.json") (Some(int64 ProviderTreeMother.moduleBytes.Length)) "text: its UTF-8 byte count"
            Expect.equal (lengthOf "pkg/acme/demo/types/order.type.json") (Some(int64 ProviderTreeMother.typeBytes.Length)) "bytes: their count"
        }

        test "Content is not read while building" {
            use sandbox = TestSandbox.Create("files-provider-")
            let root = sandbox.PathOf("large")
            let directories = [ for d in 1..10 -> Path.Combine(root, "pkg", $"module%02d{d}") ]

            for directory in directories do
                Directory.CreateDirectory directory |> ignore

                for f in 1..20 do
                    File.WriteAllText(Path.Combine(directory, $"type%02d{f}.json"), $"{{\"index\":%d{f}}}")

            use physical = new PhysicalFileProvider(root)
            let counting = CountingProvider(physical)

            let tree = VFileProviders.ofFileProvider counting

            Expect.equal (VFileTree.fileCount tree) 200 "the tree has every file"
            Expect.equal counting.Opened 0 "no file was opened while the tree was built"

            let file = VFileTree.findFile "pkg/module03/type07.json" tree |> Option.get
            Expect.equal (VFile.readText file) (Ok "{\"index\":7}") "the file reads from the provider"
            Expect.equal counting.Opened 1 "reading one file opens only that file"
        }
        |> testLabel "PlatformSpecific"
    ]

[<Tests>]
let readmeExampleTests =
    testList "FileProviders README" [
        test "The README example of the providers runs" {
            // The example of "Exchange files with IFileProvider" in src/Morphir.Files/README.md.
            let tree =
                VFileTree.fromFileMap (Map.ofList [ "pkg/acme/demo/module.json", VFile.create "module.json" "{}" ])

            let provider: IFileProvider = VFileProviders.asFileProvider tree
            let info = provider.GetFileInfo "pkg/acme/demo/module.json"
            let names = [ for entry in provider.GetDirectoryContents "pkg/acme/demo" -> entry.Name ]

            let back = VFileProviders.ofFileProvider provider

            Expect.isTrue info.Exists "info.Exists is true"
            Expect.equal info.Length 2L "info.Length is 2"
            Expect.equal names [ "module.json" ] "the directory lists module.json"
            Expect.equal (VFileTree.findFile "pkg/acme/demo/module.json" back |> Option.map VFile.readText) (Some (Ok "{}")) "the file comes back"
        }
    ]
