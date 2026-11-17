/// The document-tree store for classic v3 trees: a v3 tree is written to the files of the parent's v3 tree
/// specification and read back. Scenarios of the model-document-trees spec, requirements "The v3 version label
/// follows the tree specification", "Tree files follow the parent document-tree specifications" (the v3 kit cases),
/// "Modules are read and written in sorted logical-path order" and "Trees are read and written through virtual file
/// trees", and the round-trip law of the store.
///
/// A classic tree read from files keeps its attributes as JsonElement values, which have no structural equality, so
/// a tree is compared by the bytes of the single file it joins to: the classic writer's output, with the attributes
/// as they are. The mother's trees hold numbered attribute slots, so a store that moves, swaps or drops one attribute
/// gives other bytes.
module Morphir.IR.Tests.Trees.ClassicStoreTests

open System
open System.IO
open System.Text
open System.Text.Json
open Expecto
open FsCheck
open Morphir.Files
open Morphir.IR
open Morphir.IR.Trees
open Morphir.Json.Classic
open Morphir.Models.TestSupport
open Morphir.Models.TestSupport.DistributionBuilder
open Morphir.TestSupport.Fixtures
open Morphir.IR.Tests.Trees.StoreTestKit

module Driver = DocumentTreeStoreDriver
module Mother = DocumentTreeMother

let private config =
    { FsCheckConfig.defaultConfig with
        maxTest = 100
    }

let private written tree =
    match Driver.write tree with
    | Ok files -> files
    | Result.Error errors -> failtestf "Expected the tree to be written, got %A" errors

let private readBack files =
    match Driver.read files with
    | Ok(tree, report) -> tree, report
    | Result.Error errors -> failtestf "Expected the files to be read, got %A" errors

let private classicOf tree =
    match tree with
    | ClassicTree classic -> classic
    | V4Tree _ -> failtest "Expected a classic tree"

/// The profile of the single file a tree joins to, as its manifest records it.
let private profileOf label =
    match label with
    | Integer3 -> ClassicWireProfile.v3
    | SemVer310 -> ClassicWireProfile.v3_1_0

let private bytesOf profile (distribution: Mother.JsonClassicModel) =
    match ClassicJsonWriter.writeJsonAttributes profile false distribution with
    | Ok bytes -> bytes
    | Result.Error error ->
        failtestf
            "Expected a classic write, got %s at %s: %s"
            error.Rule
            error.Location
            error.Message

/// The single file that a classic tree joins to, written with the label its manifest records.
let private singleFile (tree: StoredTree) =
    let classic = classicOf tree

    match ClassicDocumentTree.join classic with
    | Ok distribution -> bytesOf (profileOf classic.Manifest.SingleFileVersion) distribution
    | Result.Error errors -> failtestf "Expected the tree to join, got %A" errors

let private formatVersionOf (text: string) =
    use document = JsonDocument.Parse text
    let version = document.RootElement.GetProperty "formatVersion"
    version.ValueKind, version.GetRawText()

let private expectRoundTrip tree =
    let read, report = readBack (written tree)

    Expect.sequenceEqual
        (singleFile read)
        (singleFile tree)
        "The tree read back joins to the single file of the tree written"

    Expect.isEmpty report.Notices "A written tree has nothing to report"
    Expect.isEmpty report.CompactedFiles "A plain tree has no compacted file"

let private expectEveryFileSays310 files =
    for KeyValue(location, text) in Driver.texts files do
        Expect.equal
            (formatVersionOf text)
            (JsonValueKind.String, "\"3.1.0\"")
            $"{location} says \"3.1.0\""

let private addressesOf tree =
    Manifest
    :: ((classicOf tree).Documents
        |> Map.toList
        |> List.map fst)

let private writtenWithBudget budget tree =
    match
        DocumentTreeStore.write
            { WriteOptions.defaults with
                PathBudget = budget
            }
            tree
    with
    | Ok files -> files
    | Result.Error errors -> failtestf "Expected the tree to be written, got %A" errors

/// The v3 mother cases. nonAsciiName is left out: its name has no canonical spelling, which a tree file needs.
let private v3Cases = [
    "v3Library", Mother.v3Library
    "v3Specs", Mother.v3Specs
    "v3PrivateAndDocumentedMembers", Mother.v3PrivateAndDocumentedMembers
]

[<Tests>]
let tests =
    testList "Store" [
        testList "Classic document tree store" [
            testPropertyWithConfig config "read (write t) = t, by the single file each joins to"
            <| Prop.forAll
                (Arb.fromGen Generators.v3AnyDistribution)
                (fun distribution ->
                    let tree = Mother.classicTreeOf (Mother.withNumberedSlots distribution)

                    match Driver.roundTrip tree with
                    | Ok(read, report) ->
                        singleFile read = singleFile tree
                        && report.Notices.IsEmpty
                    | Result.Error _ -> false
                )

            testList "every v3 mother case round-trips with numbered attribute slots" [
                for caseName, tree in v3Cases do
                    test caseName { expectRoundTrip tree }
            ]

            test "A v3 Library returns its original label" {
                let original =
                    Mother.withNumberedSlots DistributionMother.v3PrivateAndDocumentedMembers

                let originalBytes = bytesOf ClassicWireProfile.v3 original

                let fromSingleFile =
                    use document = JsonDocument.Parse(ReadOnlyMemory originalBytes)

                    match ClassicJson.readDistribution document.RootElement with
                    | Ok read -> read
                    | Result.Error error ->
                        failtestf "Expected the single file to read, got %A" error

                Expect.equal fromSingleFile.Version 3 "The single file says formatVersion 3"

                let files = written (Mother.classicTreeOf fromSingleFile.Distribution)
                expectEveryFileSays310 files

                let read, _ = readBack files
                let joined = singleFile read

                Expect.equal
                    (formatVersionOf (Encoding.UTF8.GetString joined))
                    (JsonValueKind.Number, "3")
                    "The joined file says formatVersion 3"

                Expect.sequenceEqual
                    joined
                    originalBytes
                    "The joined file is the original, every attribute slot included"
            }

            test "A v3 Specs tree keeps 3.1.0" {
                let files = written Mother.v3Specs
                expectEveryFileSays310 files

                let read, _ = readBack files
                let joined = singleFile read

                Expect.equal
                    (formatVersionOf (Encoding.UTF8.GetString joined))
                    (JsonValueKind.String, "\"3.1.0\"")
                    "The joined file says formatVersion \"3.1.0\""

                Expect.sequenceEqual
                    joined
                    (singleFile Mother.v3Specs)
                    "The joined file is the original"
            }

            test "A v3 tree has the layout of a v4 tree" {
                let tree = Mother.v3PrivateAndDocumentedMembers
                let files = written tree
                let own = (classicOf tree).Manifest.Package

                for address in addressesOf tree do
                    Expect.equal
                        (Driver.fileAt address files)
                        (Some(
                            match address with
                            | Manifest -> "manifest.json"
                            | _ ->
                                DocumentAddress.logicalPath own address
                                + ".json"
                        ))
                        $"{address} is at its logical path"

                Expect.equal
                    (Driver.fileAt (Dependency(Sdk.package, Module(modulePath "basics"))) files)
                    (Some "deps/morphir/_sdk/@/basics/module.json")
                    "A dependency is below a bare @ segment"

                Expect.equal
                    (VFileTree.fileCount files)
                    (addressesOf tree).Length
                    "One file per document"
            }

            test "A private v3 module says Private, and a public one says no access" {
                let files = written Mother.v3PrivateAndDocumentedMembers

                let hasAccess location =
                    (Driver.jsonAt location files).ContainsKey "access"

                Expect.equal
                    (string (
                        nonNull
                            (Driver.jsonAt "pkg/my-org/finance/accounts/internal/module.json" files).["access"]
                    ))
                    "Private"
                    "The private module"

                Expect.isFalse
                    (hasAccess "pkg/my-org/finance/accounts/module.json")
                    "The public module has no access member"

                Expect.isFalse
                    (hasAccess "deps/morphir/_sdk/@/basics/module.json")
                    "A dependency module has no access member"
            }

            test "Directory order does not change a v3 model" {
                for _, tree in v3Cases do
                    let files = written tree
                    let forward, _ = readBack files
                    let backward, _ = readBack (Driver.reversed files)

                    Expect.sequenceEqual
                        (singleFile backward)
                        (singleFile forward)
                        "The files in reverse order read as the same model"
            }

            test "The same tree from two sources" {
                let kitCase = FixtureCatalog.Kit.Case "document-tree-0015"
                let memory = kitFiles kitCase
                use sandbox = Morphir.TestSupport.TestSandbox.Create("morphir-v3-tree-sources-")
                let root = sandbox.PathOf "tree"

                for KeyValue(location, text) in Driver.texts memory do
                    let path =
                        Path.Combine(root, location.Replace('/', Path.DirectorySeparatorChar))

                    Directory.CreateDirectory(nonNull (Path.GetDirectoryName path))
                    |> ignore

                    File.WriteAllText(path, text)

                let disk =
                    match VFileTree.loadFromDisk root with
                    | Ok files -> files
                    | Result.Error problem ->
                        failtestf "The directory does not load: %s" (TreeLoadError.describe problem)

                let provided = VFileProviders.ofFileProvider (VFileProviders.asFileProvider memory)

                let model files =
                    match Driver.read files with
                    | Ok(tree, report) -> Ok(singleFile tree, report)
                    | Result.Error errors -> Result.Error errors

                Expect.equal
                    (model provided)
                    (model disk)
                    "Both reads give the same model and the same diagnostics"

                Expect.isOk (model disk) "The tree is read"

                let limited files =
                    DocumentTreeStore.read
                        { ReadOptions.defaults with
                            MaxFileBytes = 64L
                        }
                        files

                let refusedFromDisk = limited disk
                Expect.isError refusedFromDisk "A file over the limit is refused"

                Expect.equal
                    (limited provided)
                    refusedFromDisk
                    "Both refusals name the same files and rules"
            }
            |> testLabel "PlatformSpecific"

            testList "Kit tree cases are met" [
                test "the v3 tree cases are the pinned ones" {
                    let inCatalog =
                        FixtureCatalog.Kit.DocumentTree
                        |> Seq.filter (
                            isV4Case
                            >> not
                        )
                        |> Seq.map (fun kitCase -> kitCase.Id)
                        |> List.ofSeq

                    Expect.sequenceEqual
                        inCatalog
                        v3KitTreeIds
                        "The kit's v3 tree cases are 0011 to 0017"
                }

                for id in v3KitTreeIds do
                    test id {
                        let kitCase = FixtureCatalog.Kit.Case id

                        match kitCase.Diagnostic with
                        | null ->
                            let read, _ = readBack (kitFiles kitCase)

                            Expect.equal
                                (compact (Encoding.UTF8.GetString(singleFile read)))
                                (compact (nonNull kitCase.CanonicalSingleFile))
                                "The tree reads back as the canonical form"

                            if not kitCase.ReadOnly then
                                Expect.sequenceEqual
                                    (writtenAsKitFiles (writtenWithBudget (kitBudget kitCase) read))
                                    (kitFilesCompacted kitCase)
                                    "The tree is written back to the kit's files"
                        | diagnostic ->
                            // The kit decodes the rejected file alone, having no grammar for a rejected tree. Here it is
                            // the type file of document-tree-0015's tree, which a v3 tree reader checks the same way.
                            let typeFile = "pkg/my-org/my-project/domain/user.type.json"

                            let files =
                                kitFiles (FixtureCatalog.Kit.Case "document-tree-0015")
                                |> Driver.withFile typeFile (nonNull kitCase.RejectedDocument)

                            Expect.equal
                                (codesAndFiles (refusal (Driver.read files)))
                                [ diagnostic, Some typeFile ]
                                "The tree is refused with the kit's diagnostic, at the rejected file"
                    }
            ]

            testList "Kit file spellings are met" [
                test "the v3 file spellings are the pinned ones" {
                    let inCatalog =
                        FixtureCatalog.Kit.FileSpellings
                        |> Seq.filter (fun spelling -> not (isV4Json spelling.CanonicalJson))
                        |> Seq.map (fun spelling -> spelling.Id)
                        |> List.ofSeq

                    Expect.sequenceEqual
                        inCatalog
                        v3KitSpellingIds
                        "The kit's v3 file spelling is 0010"
                }

                for id in v3KitSpellingIds do
                    test id {
                        let spelling = kitSpelling id
                        Expect.equal spelling.LogicalPath "manifest" "The spelling is of a manifest"

                        let manifestOf (text: string) =
                            let read, _ = readBack (Driver.ofTexts [ "manifest.json", text ])
                            (Driver.texts (written read))["manifest.json"]

                        Expect.equal
                            (compact (manifestOf spelling.CanonicalJson))
                            (compact spelling.CanonicalJson)
                            "The manifest is written in its canonical spelling"

                        for accepted in spelling.AcceptedSpellings do
                            Expect.equal
                                (compact (manifestOf accepted))
                                (compact spelling.CanonicalJson)
                                "An accepted spelling reads as the same manifest"
                    }
            ]
        ]
    ]
