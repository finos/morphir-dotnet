/// The document-tree store for IR v4: a v4 tree is written to the files of the parent's v4 tree specification
/// and read back. Scenarios of the model-document-trees spec, requirements "Tree files follow the parent
/// document-tree specifications", "Modules are read and written in sorted logical-path order", "Content without a
/// single-file place is reported, never dropped silently", "The reserved meta member is ignored" and "Trees are
/// read and written through virtual file trees", and the laws of the store.
module Morphir.IR.Tests.Trees.V4StoreTests

open System
open System.IO
open System.Text
open System.Text.Json
open Expecto
open FsCheck
open Morphir.Files
open Morphir.IR
open Morphir.IR.Trees
open Morphir.IR.V4
open Morphir.Json.V4
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

let private expectRoundTrip tree =
    let read, report = readBack (written tree)
    Expect.equal read tree "The tree read back equals the tree written"
    Expect.isEmpty report.Notices "A written tree has nothing to report"
    Expect.isEmpty report.CompactedFiles "A plain tree has no compacted file"

let private v4TreeOf tree =
    match tree with
    | V4Tree v4 -> v4
    | ClassicTree _ -> failtest "Expected a v4 tree"

let private joined tree =
    match V4DocumentTree.join (v4TreeOf tree) with
    | Ok distribution -> distribution
    | Result.Error errors -> failtestf "Expected the tree to join, got %A" errors

let private addressesOf tree =
    Manifest
    :: ((v4TreeOf tree).Documents
        |> Map.toList
        |> List.map fst)

let private v4ModuleOf (tree: StoredTree) address =
    match Map.tryFind address (v4TreeOf tree).Documents with
    | Some(ModuleDoc m) -> m
    | other -> failtestf "Expected a module document at %O, got %A" address other

let private moduleOf address =
    match address with
    | Own(ValueDef(path, _))
    | Own(TypeDef(path, _)) -> Own(Module path)
    | Dependency(package, ValueDef(path, _))
    | Dependency(package, TypeDef(path, _)) -> Dependency(package, Module path)
    | other -> other

let private nameOf address =
    match address with
    | Own(ValueDef(_, n))
    | Own(TypeDef(_, n))
    | Dependency(_, ValueDef(_, n))
    | Dependency(_, TypeDef(_, n)) -> n
    | other -> failtestf "%O has no name" other

let private canonicalDistribution (kitCase: KitTreeCase) =
    use document = JsonDocument.Parse(nonNull kitCase.CanonicalSingleFile)

    match V4Json.readDistribution document.RootElement with
    | Ok read -> read.Distribution
    | Result.Error error -> failtestf "The canonical file of %s does not read: %A" kitCase.Id error

let private decoded (read: JsonElement -> Result<'a, V4JsonError>) (text: string) =
    use document = JsonDocument.Parse text

    match read document.RootElement with
    | Ok value -> value
    | Result.Error error -> failtestf "Expected %s to read, got %A" text error

let private bytesText (result: Result<byte array, V4WriteError>) =
    match result with
    | Ok bytes -> Encoding.UTF8.GetString bytes
    | Result.Error error -> failtestf "Expected the file to be written, got %A" error

[<Tests>]
let tests =
    testList "Store" [
        testList "V4 document tree store" [
            testPropertyWithConfig config "read (write t) = t"
            <| Prop.forAll
                (Arb.fromGen Generators.v4DistributionWithoutModuleAnnotations)
                (fun distribution ->
                    let tree = Mother.treeOf distribution

                    match Driver.roundTrip tree with
                    | Ok(read, report) ->
                        read = tree
                        && report.Notices.IsEmpty
                    | Result.Error _ -> false
                )

            testPropertyWithConfig
                config
                "distinct addresses have distinct physical paths, case-insensitively"
            <| Prop.forAll
                (Arb.fromGen Generators.v4DistributionWithoutModuleAnnotations)
                (fun distribution ->
                    let tree = Mother.treeOf distribution
                    let files = written tree

                    let paths =
                        addressesOf tree
                        |> List.choose (fun address -> Driver.fileAt address files)

                    paths.Length = (addressesOf tree).Length
                    && (paths
                        |> List.map (fun path -> path.ToUpperInvariant())
                        |> List.distinct
                        |> List.length) = paths.Length
                    && VFileTree.fileCount files = paths.Length
                )

            test "A v4 tree is written as the design's worked example" {
                let files = written Mother.acmeDemo
                let at address = Driver.fileAt address files
                let billing = modulePath "billing"

                Expect.equal (at Manifest) (Some "manifest.json") "The manifest"

                Expect.equal
                    (at (Own(Module billing)))
                    (Some "pkg/acme/demo/billing/module.json")
                    "The module"

                Expect.equal
                    (at (Own(TypeDef(billing, name "invoice"))))
                    (Some "pkg/acme/demo/billing/invoice.type.json")
                    "A type"

                Expect.equal
                    (at (Own(ValueDef(billing, name "total"))))
                    (Some "pkg/acme/demo/billing/total.value.json")
                    "A value"
            }

            test "A dependency is written under a bare version segment" {
                let files = written Mother.acmeDemo
                let sdk = packageName "morphir/sdk"

                Expect.equal
                    (Driver.fileAt (Dependency(sdk, Module(modulePath "basics"))) files)
                    (Some "deps/morphir/sdk/@/basics/module.json")
                    "The dependency's module is below deps/morphir/sdk/@/"

                for address in addressesOf Mother.acmeDemo do
                    match address with
                    | Dependency _ ->
                        let path = (Driver.fileAt address files).Value

                        Expect.stringStarts
                            path
                            "deps/morphir/sdk/@/"
                            $"{address} is below the bare @ segment"
                    | Manifest
                    | Own _ -> ()

                expectRoundTrip Mother.acmeDemo
            }

            test "A long name is truncated and recorded" {
                let case = Mother.pathOverTheBudget
                let files = written case.Tree

                for address in [ case.OwnValue; case.DependencyValue ] do
                    let path = (Driver.fileAt address files).Value

                    let fileName =
                        path.Substring(
                            path.LastIndexOf '/'
                            + 1
                        )

                    let stem =
                        fileName.Substring(
                            0,
                            fileName.Length
                            - ".value.json".Length
                        )

                    Expect.isLessThanOrEqual
                        path.Length
                        Mother.defaultBudget
                        $"The path of {address} fits the budget"

                    match TreeStems.parseStem stem with
                    | Ok(CutStem(prefix, digest)) ->
                        Expect.isTrue
                            (TreeStems.matchesCut prefix digest (nameOf address))
                            $"The cut stem of {address} belongs to its name"
                    | other -> failtestf "Expected a cut stem for %O, got %A" address other

                    let moduleFile = (Driver.fileAt (moduleOf address) files).Value

                    let fileNames =
                        (nonNull ((Driver.jsonAt moduleFile files)["fileNames"])).AsObject()

                    Expect.equal
                        (string fileNames[CanonicalNames.nameToString (nameOf address)])
                        stem
                        $"The module file of {address} maps the name to its cut stem"

                expectRoundTrip case.Tree
            }

            test "A path exactly at the budget is not truncated" {
                let case = Mother.pathAtTheBudget
                let files = written case.Tree
                let tree = v4TreeOf case.Tree

                for address in [ case.OwnValue; case.DependencyValue ] do
                    let path = (Driver.fileAt address files).Value

                    Expect.equal
                        path.Length
                        Mother.defaultBudget
                        $"The path of {address} is exactly the budget"

                    Expect.equal
                        path
                        (DocumentAddress.logicalPath tree.Manifest.Package address
                         + ".json")
                        $"The stem of {address} is whole"

                    let moduleFile = (Driver.fileAt (moduleOf address) files).Value

                    Expect.isFalse
                        ((Driver.jsonAt moduleFile files).ContainsKey "fileNames")
                        $"The module file of {address} records no cut"

                expectRoundTrip case.Tree
            }

            test "Directory order does not change the model" {
                for tree in
                    [ Mother.acmeDemo; Mother.privateAndDocumentedMembers; Mother.v4Application ] do
                    let files = written tree
                    let forward, _ = readBack files
                    let backward, _ = readBack (Driver.reversed files)

                    Expect.equal forward tree "The files in their written order read as the tree"
                    Expect.equal backward forward "The files in reverse order read as the same tree"

                    match joined forward, joined backward with
                    | Library(_, _, first), Library(_, _, second)
                    | Application(_, _, first, _), Application(_, _, second, _) ->
                        Expect.sequenceEqual
                            (OrderedMap.keys second.Modules)
                            (OrderedMap.keys first.Modules)
                            "The modules are in the same order"
                    | other -> failtestf "Expected two models of one kind, got %A" other
            }

            test "Version and created are reported" {
                let files =
                    written Mother.acmeDemo
                    |> Driver.withMember "manifest.json" "version" "\"1.2.0\""
                    |> Driver.withMember "manifest.json" "created" "\"2026-01-15T12:00:00Z\""
                    |> Driver.withMember "manifest.json" "layout" "\"VfsMode\""

                let read, report = readBack files

                Expect.equal read Mother.acmeDemo "The model is unchanged"

                Expect.equal
                    report.Notices
                    [ DroppedManifestMember "version"; DroppedManifestMember "created" ]
                    "Version and created are reported, and layout and pathBudget are not"
            }

            test "A meta member does not change the model" {
                let files = written Mother.acmeDemo
                let moduleFile = "pkg/acme/demo/billing/module.json"

                let withMeta =
                    files
                    |> Driver.withMember moduleFile "$meta" """{ "generator": "example" }"""
                    |> Driver.withMember "manifest.json" "$meta" """{ "generator": "example" }"""
                    |> Driver.withMember "pkg/acme/demo/billing/invoice.type.json" "$meta" "{}"

                let read, _ = readBack withMeta
                let plain, _ = readBack files

                Expect.equal read plain "The model equals the one read without the member"

                for KeyValue(location, text) in Driver.texts (written read) do
                    Expect.isFalse (text.Contains "$meta") $"{location} holds no $meta"
            }

            test "Colliding stems are refused before encoding" {
                let errors = refusal (Driver.write Mother.collidingStems)

                match errors with
                | [ error ] ->
                    Expect.equal error.Code "tree.stem_collision" "The code"

                    Expect.equal
                        error.File
                        (Some "pkg/my-org/finance/pricing/models/invoice-line.type.json")
                        "The file of the two names"

                    Expect.stringContains error.Message "['invoice', 'line']" "The first name"
                    Expect.stringContains error.Message "['invoice-line']" "The second name"
                | other -> failtestf "Expected one collision, got %A" other
            }

            test "Stems that differ only in case are refused" {
                let errors = refusal (Driver.write Mother.caseOnlyCollidingStems)

                match errors with
                | [ error ] ->
                    Expect.equal error.Code "tree.stem_collision" "The code"

                    Expect.equal
                        error.File
                        (Some "pkg/my-org/finance/billing/_invoice.type.json")
                        "The file"

                    Expect.stringContains error.Message "['Invoice']" "The capitalized name"

                    Expect.stringContains
                        error.Message
                        "['i', 'n', 'v', 'o', 'i', 'c', 'e']"
                        "The initialism"
                | other -> failtestf "Expected one collision, got %A" other
            }

            test "Windows reserved stems round-trip" {
                let files = written Mother.windowsReservedStems
                let devices = modulePath "aux"

                Expect.equal
                    (Driver.fileAt (Own(Module devices)) files)
                    (Some "pkg/my-org/devices/aux_/module.json")
                    "The module directory takes the suffix"

                for device in DistributionMother.Names.windowsDevices do
                    let stem = TreeStems.escapeName device

                    Expect.equal
                        (Driver.fileAt (Own(TypeDef(devices, device))) files)
                        (Some $"pkg/my-org/devices/aux_/{stem}.type.json")
                        $"The type {stem} takes the suffix"

                    Expect.stringEnds stem "_" $"{stem} is not a device name"

                expectRoundTrip Mother.windowsReservedStems
            }

            test "An empty module round-trips" {
                let files = written Mother.emptyModule
                let moduleFile = Driver.jsonAt "pkg/my-org/finance/empty/module.json" files

                Expect.equal
                    ((nonNull moduleFile["types"]).AsArray().Count)
                    0
                    "The module lists no types"

                Expect.equal
                    ((nonNull moduleFile["values"]).AsArray().Count)
                    0
                    "The module lists no values"

                expectRoundTrip Mother.emptyModule
            }

            test "A package with no modules round-trips" {
                let files = written Mother.noModules
                Expect.equal (VFileTree.fileCount files) 1 "The tree is its manifest"
                expectRoundTrip Mother.noModules
            }

            test "A dependency without modules round-trips" {
                let files = written Mother.dependencyWithoutModules

                let dependencies =
                    (nonNull ((Driver.jsonAt "manifest.json" files)["dependencies"])).AsArray()

                Expect.sequenceEqual
                    [ for dependency in dependencies -> string dependency ]
                    [ "morphir/SDK"; "acme/ledger" ]
                    "The manifest lists both dependencies in model order"

                expectRoundTrip Mother.dependencyWithoutModules
            }

            test "A private module and private members round-trip" {
                expectRoundTrip Mother.privateAndDocumentedMembers
                expectRoundTrip Mother.v4Application
            }

            test "Module annotations are refused" {
                let errors = refusal (Driver.write Mother.v4SpecsWithModuleAnnotations)
                Expect.isNonEmpty errors "Every annotated module is refused"

                for error in errors do
                    Expect.equal error.Code "invalid_distribution_shape" "The code"

                    Expect.stringContains
                        error.Message
                        "Module annotations cannot be written"
                        "The reason"
            }

            test "A module doc spelled as lines is read as one doc" {
                let moduleFile = "pkg/acme/demo/billing/module.json"

                let files =
                    written Mother.acmeDemo
                    |> Driver.withModuleDocLines moduleFile [ "Invoices"; "and their totals." ]

                let read, _ = readBack files
                let billing = Own(Module(modulePath "billing"))

                Expect.equal
                    (v4ModuleOf read billing).Doc
                    (Some "Invoices\nand their totals.")
                    "The lines are joined with line breaks"

                Expect.equal
                    (string (nonNull (Driver.jsonAt moduleFile (written read)).["doc"]))
                    "Invoices\nand their totals."
                    "The doc is written back as a string"
            }

            test "A package metadata file is ignored with a notice" {
                let files =
                    written Mother.acmeDemo
                    |> Driver.withPackageMetadata "pkg/acme/demo"
                    |> Driver.withPackageMetadata "deps/morphir/sdk/@"

                let read, report = readBack files

                Expect.equal read Mother.acmeDemo "The model is unchanged"

                Expect.equal
                    report.Notices
                    [
                        IgnoredFile "deps/morphir/sdk/@/package.json"
                        IgnoredFile "pkg/acme/demo/package.json"
                    ]
                    "Each ignored file is named"

                for KeyValue(location, _) in Driver.texts (written read) do
                    Expect.isFalse
                        (location.EndsWith "package.json")
                        $"{location} is not a package metadata file"
            }

            test "Module metadata is refused as an unknown member" {
                let moduleFile = "pkg/acme/demo/billing/module.json"

                let errors =
                    written Mother.acmeDemo
                    |> Driver.withModuleMetadata moduleFile
                    |> Driver.read
                    |> refusal

                Expect.equal
                    (errors
                     |> List.map (fun e -> e.Code, e.File))
                    [ "unknown_member", Some moduleFile ]
                    "The module file is refused for its metadata member"
            }

            test "A long file outside the tree does not count against the budget" {
                let notes =
                    "notes/"
                    + String('n', Mother.defaultBudget)
                    + ".json"

                let read, report =
                    written Mother.acmeDemo
                    |> Driver.withUnrelatedFile notes
                    |> readBack

                Expect.equal read Mother.acmeDemo "The model is unchanged"
                Expect.isEmpty report.Notices "A file outside the tree is not part of it"
            }

            test "The same tree from two sources" {
                use sandbox = Morphir.TestSupport.TestSandbox.Create("morphir-tree-sources-")
                let root = sandbox.PathOf "tree"

                for entry in FixtureCatalog.Trees.AcmeDemo do
                    let location =
                        entry.RelativePath.Substring FixtureCatalog.Trees.AcmeDemoDirectory.Length

                    let path =
                        Path.Combine(root, location.Replace('/', Path.DirectorySeparatorChar))

                    Directory.CreateDirectory(nonNull (Path.GetDirectoryName path))
                    |> ignore

                    File.WriteAllBytes(path, FixtureCatalog.ReadBytes entry)

                let disk =
                    match VFileTree.loadFromDisk root with
                    | Ok files -> files
                    | Result.Error problem ->
                        failtestf "The directory does not load: %s" (TreeLoadError.describe problem)

                let embedded =
                    Microsoft.Extensions.FileProviders.ManifestEmbeddedFileProvider(
                        Reflection.Assembly.GetExecutingAssembly(),
                        "AcmeDemoTree"
                    )
                    |> VFileProviders.ofFileProvider

                Expect.equal
                    (VFileTree.fileCount embedded)
                    FixtureCatalog.Trees.AcmeDemo.Count
                    "The embedded provider holds every file of the tree"

                let fromDisk = Driver.read disk

                Expect.equal
                    (Driver.read embedded)
                    fromDisk
                    "Both reads give the same model and the same diagnostics"

                match fromDisk with
                | Ok(tree, report) ->
                    Expect.equal
                        (v4TreeOf tree).Documents.Count
                        7
                        "Four documents of acme/demo and three of morphir/sdk"

                    Expect.equal
                        report.Notices
                        [ DroppedManifestMember "version"; DroppedManifestMember "created" ]
                        "Both report the dropped members"
                | Result.Error errors -> failtestf "Expected the tree to be read, got %A" errors

                let limited files =
                    DocumentTreeStore.read
                        { ReadOptions.defaults with
                            MaxFileBytes = 64L
                        }
                        files

                let refusedFromDisk = limited disk
                Expect.isError refusedFromDisk "A file over the limit is refused"

                Expect.equal
                    (limited embedded)
                    refusedFromDisk
                    "Both refusals name the same files and rules"
            }
            |> testLabel "PlatformSpecific"

            testList "Kit tree cases are met" [
                test "the v4 tree cases are the pinned ones" {
                    let inCatalog =
                        FixtureCatalog.Kit.DocumentTree
                        |> Seq.filter isV4Case
                        |> Seq.map (fun kitCase -> kitCase.Id)
                        |> List.ofSeq

                    Expect.sequenceEqual
                        inCatalog
                        v4KitTreeIds
                        "The kit's v4 tree cases are 0003 to 0009"
                }

                for id in v4KitTreeIds do
                    test id {
                        let kitCase = FixtureCatalog.Kit.Case id
                        let read, _ = readBack (kitFiles kitCase)

                        Expect.equal
                            (joined read)
                            (canonicalDistribution kitCase)
                            "The tree reads back as the canonical form"

                        if not kitCase.ReadOnly then
                            let files =
                                match
                                    DocumentTreeStore.write
                                        { WriteOptions.defaults with
                                            PathBudget = kitBudget kitCase
                                        }
                                        read
                                with
                                | Ok files -> files
                                | Result.Error errors ->
                                    failtestf "Expected the tree to be written, got %A" errors

                            Expect.sequenceEqual
                                (writtenAsKitFiles files)
                                (kitFilesCompacted kitCase)
                                "The tree is written back to the kit's files"
                    }
            ]

            testList "Kit file spellings are met" [
                test "the v4 file spellings are the pinned ones" {
                    let inCatalog =
                        FixtureCatalog.Kit.FileSpellings
                        |> Seq.filter (fun spelling -> isV4Json spelling.CanonicalJson)
                        |> Seq.map (fun spelling -> spelling.Id)
                        |> List.ofSeq

                    Expect.sequenceEqual
                        inCatalog
                        v4KitSpellingIds
                        "The kit's v4 file spellings are 0001 and 0002"
                }

                for id in v4KitSpellingIds do
                    test id {
                        let spelling = kitSpelling id

                        match spelling.LogicalPath with
                        | "manifest" ->
                            let manifest = decoded V4TreeFiles.readManifest spelling.CanonicalJson

                            Expect.equal
                                (compact (
                                    bytesText (
                                        V4TreeFiles.writeManifest
                                            manifest.PathBudget
                                            manifest.Manifest
                                    )
                                ))
                                (compact spelling.CanonicalJson)
                                "The manifest is written in its canonical spelling"

                            for accepted in spelling.AcceptedSpellings do
                                Expect.equal
                                    (decoded V4TreeFiles.readManifest accepted)
                                    manifest
                                    "An accepted spelling reads as the same manifest"
                        | "module" ->
                            let moduleFile =
                                decoded (V4TreeFiles.readModule true) spelling.CanonicalJson

                            Expect.equal
                                (compact (
                                    bytesText (
                                        V4TreeFiles.writeModule
                                            moduleFile.Path
                                            moduleFile.Document
                                            moduleFile.FileNames
                                    )
                                ))
                                (compact spelling.CanonicalJson)
                                "The module is written in its canonical spelling"

                            for accepted in spelling.AcceptedSpellings do
                                Expect.equal
                                    (decoded (V4TreeFiles.readModule true) accepted)
                                    moduleFile
                                    "An accepted spelling reads as the same module"
                        | other -> failtestf "The kit names no tree file '%s'" other
                    }
            ]
        ]
    ]
