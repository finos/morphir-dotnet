/// Whole-tree YAML and multi-profile reading on <see cref="DocumentTreeStore"/>: reads v3.1 and v4 JSON and YAML
/// document trees (plain and contextual), enforces file/byte/depth/node/cancellation budgets, refuses mixed or
/// ambiguous manifests, and provides single-file YAML decoding via <see cref="TreeYaml"/>.
module Morphir.IR.Tests.Trees.YamlTreeStoreTests

open System
open System.IO
open System.Text
open System.Text.Json
open System.Threading
open Expecto
open Morphir.Files
open Morphir.IR
open Morphir.IR.Model
open Morphir.IR.Trees
open Morphir.IR.V4
open Morphir.TestSupport.Fixtures
open Morphir.IR.Tests.Trees.StoreTestKit

module Orders = Morphir.Models.TestSupport.DocumentTreeMother.BoundedOrders
module YamlDocs = Morphir.Models.TestSupport.DocumentTreeMother.YamlDocuments
module Driver = Morphir.Models.TestSupport.DocumentTreeStoreDriver

let private catalogTree version profile : VFileTree =
    Orders.catalogDocuments version profile
    |> Map.map (fun location fixture -> VFile.ofBytes location (ReadOnlyMemory(FixtureCatalog.ReadBytes fixture)))
    |> VFileTree.fromFileMap

let private readOk options files =
    match DocumentTreeStore.readAnyProfile options files with
    | Ok result -> result
    | Result.Error errors -> failtestf "Expected tree to read, got %A" errors

let private tryReadModelOk options files =
    let mutable loaded = Unchecked.defaultof<LoadedTreeModel>
    let mutable errors = Unchecked.defaultof<System.Collections.Generic.IReadOnlyList<TreeDiagnostic>>

    if DocumentTreeStore.tryReadModel (options, files, CancellationToken.None, &loaded, &errors) then
        loaded
    else
        failtestf "Expected tryReadModel to succeed, got %A" (List.ofSeq errors)

let private tryReadModelErrors options files =
    let mutable loaded = Unchecked.defaultof<LoadedTreeModel>
    let mutable errors = Unchecked.defaultof<System.Collections.Generic.IReadOnlyList<TreeDiagnostic>>

    if DocumentTreeStore.tryReadModel (options, files, CancellationToken.None, &loaded, &errors) then
        failtest "Expected tryReadModel to fail"
    else
        List.ofSeq errors

let private diagCodesAndFiles (errors: TreeDiagnostic list) =
    errors |> List.map (fun e -> e.Code, e.File)

[<Tests>]
let tests =
    testList "Store" [
        testList "Multi-profile and YAML trees" [
            test "Reads v4 plain and contextual YAML trees equivalently to plain JSON" {
                let jsonTree, jsonReport =
                    readOk TreeReadOptions.Defaults (catalogTree "v4" "plain-json")

                Expect.isEmpty jsonReport.CompactedFiles "Plain JSON has no compacted files"

                for profile, expectCompacted in [ "plain-yaml", false; "contextual-yaml", true; "contextual-json", true ] do
                    let tree, report = readOk TreeReadOptions.Defaults (catalogTree "v4" profile)
                    Expect.equal tree jsonTree $"v4 {profile} matches v4 plain-json"

                    if expectCompacted then
                        Expect.isNonEmpty report.CompactedFiles $"v4 {profile} reports compacted files"
                    else
                        Expect.isEmpty report.CompactedFiles $"v4 {profile} has no compacted files"
            }

            test "Reads v3.1 plain and contextual YAML trees equivalently to plain JSON" {
                let expected =
                    match readOk TreeReadOptions.Defaults (catalogTree "v3.1" "plain-json") with
                    | ClassicTree classic, _ ->
                        match ClassicDocumentTree.join classic with
                        | Ok dist -> dist
                        | Result.Error errors -> failtestf "Expected v3.1 join to succeed: %A" errors
                    | _ -> failtest "Expected ClassicTree"

                for profile, expectCompacted in [ "plain-yaml", false; "contextual-yaml", true; "contextual-json", true ] do
                    let tree, report = readOk TreeReadOptions.Defaults (catalogTree "v3.1" profile)

                    match tree with
                    | ClassicTree classic ->
                        match ClassicDocumentTree.join classic with
                        | Ok actual ->
                            Expect.equal
                                (Morphir.Json.Classic.ClassicJsonWriter.writeJsonAttributes
                                    Morphir.Json.Classic.ClassicWireProfile.v3
                                    false
                                    actual)
                                (Morphir.Json.Classic.ClassicJsonWriter.writeJsonAttributes
                                    Morphir.Json.Classic.ClassicWireProfile.v3
                                    false
                                    expected)
                                $"v3.1 {profile} joins identically to v3.1 plain-json"
                        | Result.Error errors -> failtestf "Expected join for v3.1 %s: %A" profile errors
                    | _ -> failtest $"Expected ClassicTree for v3.1 {profile}"

                    if expectCompacted then
                        Expect.isNonEmpty report.CompactedFiles $"v3.1 {profile} reports compacted files"
                    else
                        Expect.isEmpty report.CompactedFiles $"v3.1 {profile} has no compacted files"
            }

            test "TryReadModel returns VersionedModel with exact declared formatVersion and profile" {
                let v4Yaml = tryReadModelOk TreeReadOptions.Defaults (catalogTree "v4" "plain-yaml")
                Expect.equal v4Yaml.Profile TreeProfile.Yaml "YAML profile"
                Expect.equal v4Yaml.Model.Family ModelFamily.V4 "v4 family"
                Expect.equal v4Yaml.Model.Kind DistributionKind.Library "Library kind"
                Expect.equal v4Yaml.Model.SourceVersion.Text "4" "Exact v4 declared formatVersion"

                let v3Yaml = tryReadModelOk TreeReadOptions.Defaults (catalogTree "v3.1" "contextual-yaml")
                Expect.equal v3Yaml.Profile TreeProfile.Yaml "YAML profile"
                Expect.equal v3Yaml.Model.Family ModelFamily.Classic "Classic family"
                Expect.equal v3Yaml.Model.SourceVersion.Text "3.1.0" "Exact v3.1.0 declared formatVersion"

                let v4Json = tryReadModelOk TreeReadOptions.Defaults (catalogTree "v4" "plain-json")
                Expect.equal v4Json.Profile TreeProfile.Json "JSON profile"
                Expect.equal v4Json.Model.SourceVersion.Text "4" "Exact v4 JSON declared formatVersion"

                // Explicit string "4.0.0" in manifest is preserved verbatim.
                let files400 =
                    catalogTree "v4" "plain-yaml"
                    |> Driver.withText "manifest.yaml" (fun text -> text.Replace("formatVersion: 4", "formatVersion: \"4.0.0\""))

                let v400 = tryReadModelOk TreeReadOptions.Defaults files400
                Expect.equal v400.Model.SourceVersion.Text "4.0.0" "Preserves exact 4.0.0 string"
            }

            test "DocumentTreeStore.read keeps refusing YAML trees and preserves ignoring unrelated manifest.ion" {
                let errors = DocumentTreeStore.read ReadOptions.defaults (catalogTree "v4" "plain-yaml") |> refusal
                Expect.isNonEmpty errors "Legacy read refuses YAML"

                for err in errors do
                    Expect.equal err.Code "tree.unsupported_profile" "Legacy read refuses YAML files with tree.unsupported_profile"

                // Legacy DocumentTreeStore.read ignores non-JSON/YAML files including manifest.ion beside a JSON tree.
                let jsonWithIon =
                    catalogTree "v4" "plain-json"
                    |> Driver.withFile "manifest.ion" YamlDocs.minimalIonManifest

                match DocumentTreeStore.read ReadOptions.defaults jsonWithIon with
                | Ok _ -> ()
                | Result.Error legacyErrors ->
                    failtestf "Expected legacy DocumentTreeStore.read to ignore manifest.ion, got %A" legacyErrors

                // Multi-profile TryReadModel / readAnyProfile refuses manifest.ion beside a JSON tree as ambiguous.
                let multiErrors = tryReadModelErrors TreeReadOptions.Defaults jsonWithIon
                Expect.equal (diagCodesAndFiles multiErrors) [ "tree.ambiguous_profile", None ] "Multi-profile rejects json + ion"

                // Legacy DocumentTreeStore.read checks MaxFileBytes against stored bytes only, accepting a compacted
                // member whose stored bytes fit while its expanded bytes exceed MaxFileBytes.
                let expandingFiles, expandingRawBytes = Orders.contextualOrderWithExpandingAliases ()
                let legacyLimit = int64 expandingRawBytes + 32L

                match DocumentTreeStore.read { ReadOptions.defaults with MaxFileBytes = legacyLimit } expandingFiles with
                | Ok _ -> ()
                | Result.Error legacyErrors ->
                    failtestf "Expected legacy DocumentTreeStore.read to accept compacted member fitting stored MaxFileBytes, got %A" legacyErrors

                // Multi-profile TryReadModel / readAnyProfile enforces the expansion byte cap on the same tree.
                let multiExpansionErrors =
                    tryReadModelErrors (TreeReadOptions.Defaults.WithMaxFileBytes legacyLimit) expandingFiles

                Expect.equal
                    (diagCodesAndFiles multiExpansionErrors)
                    [ "tree.file_too_large", Some Orders.orderLocation ]
                    "Multi-profile reader caps expanded bytes against MaxFileBytes"
            }

            test "Accepts .yml files in YAML trees and ignores package.yaml with a notice" {
                let renamed =
                    catalogTree "v4" "plain-yaml"
                    |> Driver.texts
                    |> Map.toList
                    |> List.map (fun (path, text) ->
                        if path.EndsWith(".yaml", StringComparison.Ordinal) then
                            path.Substring(0, path.Length - 5) + ".yml", text
                        else
                            path, text
                    )
                    |> Driver.ofTexts
                    |> Driver.withFile "pkg/acme/orders/package.yaml" "formatVersion: 4\nversion: 1.0.0\n"

                let _, report = readOk TreeReadOptions.Defaults renamed

                Expect.contains
                    report.Notices
                    (TreeNotice.IgnoredFile "pkg/acme/orders/package.yaml")
                    "package.yaml is ignored with a notice"
            }

            test "Refuses .yaml and .yml physical ambiguity for the same member before opening either file" {
                let mutable opened = false
                let yamlTree = catalogTree "v4" "plain-yaml"
                let yamlPath = "pkg/acme/orders/domain/order.value.yaml"
                let ymlPath = "pkg/acme/orders/domain/order.value.yml"

                let guardedFile path =
                    VFile.ofStream
                        path
                        (StreamSource(fun () ->
                            opened <- true
                            failwith $"Should not open {path} when ambiguous siblings exist."
                        ))

                let ambiguousTree =
                    VFileTree.toFileMap yamlTree
                    |> Map.add yamlPath (guardedFile yamlPath)
                    |> Map.add ymlPath (guardedFile ymlPath)
                    |> VFileTree.fromFileMap

                let errors = tryReadModelErrors TreeReadOptions.Defaults ambiguousTree
                Expect.isFalse opened "Neither ambiguous sibling file was opened"

                let err = List.exactlyOne errors
                Expect.equal err.Code "tree.ambiguous_profile" "Physical ambiguity code"
                Expect.stringContains err.Message yamlPath "Names .yaml sibling"
                Expect.stringContains err.Message ymlPath "Names .yml sibling"
            }

            test "Refuses ambiguous manifests across JSON, YAML, and Ion" {
                let baseYaml = catalogTree "v4" "plain-yaml"
                let baseJson = catalogTree "v4" "plain-json"

                for name, files in
                    [
                        "json + yaml", baseJson |> Driver.withFile "manifest.yaml" "formatVersion: 4\n"
                        "json + ion", baseJson |> Driver.withFile "manifest.ion" YamlDocs.minimalIonManifest
                        "yaml + ion", baseYaml |> Driver.withFile "manifest.ion" YamlDocs.minimalIonManifest
                        "yaml + yml", baseYaml |> Driver.withFile "manifest.yml" "formatVersion: 4\n"
                    ] do
                    let errors =
                        DocumentTreeStore.readAnyProfile TreeReadOptions.Defaults files |> refusal

                    Expect.equal
                        (codesAndFiles errors)
                        [ "tree.ambiguous_profile", None ]
                        $"Ambiguous manifests ({name}) fail with tree.ambiguous_profile"
            }

            test "Refuses profile selection mismatches and mixed profile files" {
                let yamlTree = catalogTree "v4" "plain-yaml"
                let jsonTree = catalogTree "v4" "plain-json"

                let yamlAsJson =
                    DocumentTreeStore.readAnyProfile (TreeReadOptions.Defaults.WithProfiles TreeProfileSelection.JsonOnly) yamlTree
                    |> refusal

                Expect.isNonEmpty yamlAsJson "JsonOnly refuses YAML tree"
                Expect.all yamlAsJson (fun e -> e.Code = "tree.unsupported_profile") "All errors are tree.unsupported_profile"

                let jsonAsYaml =
                    DocumentTreeStore.readAnyProfile (TreeReadOptions.Defaults.WithProfiles TreeProfileSelection.YamlOnly) jsonTree
                    |> refusal

                Expect.isNonEmpty jsonAsYaml "YamlOnly refuses JSON tree"
                Expect.all jsonAsYaml (fun e -> e.Code = "tree.unsupported_profile") "All errors are tree.unsupported_profile"

                let mixedInYaml =
                    yamlTree
                    |> Driver.withFile "pkg/acme/orders/domain/extra.type.json" "{}"

                let mixedErrors =
                    DocumentTreeStore.readAnyProfile TreeReadOptions.Defaults mixedInYaml |> refusal

                Expect.equal
                    (codesAndFiles mixedErrors)
                    [ "tree.unsupported_profile", Some "pkg/acme/orders/domain/extra.type.json" ]
                    "JSON file inside YAML tree is refused"
            }

            test "Enforces MaxFiles, MaxFileBytes, MaxDepth, MaxNodes, and CancellationToken on YAML trees" {
                let yamlTree = catalogTree "v4" "plain-yaml"

                let tooMany =
                    DocumentTreeStore.readAnyProfile (TreeReadOptions.Defaults.WithMaxFiles 2) yamlTree |> refusal

                Expect.equal (codesAndFiles tooMany) [ "tree.too_many_files", None ] "MaxFiles enforced"

                let tooLarge =
                    DocumentTreeStore.readAnyProfile (TreeReadOptions.Defaults.WithMaxFileBytes 10L) yamlTree |> refusal

                Expect.equal (codesAndFiles tooLarge) [ "tree.file_too_large", Some "manifest.yaml" ] "MaxFileBytes enforced"

                let tooDeep =
                    DocumentTreeStore.readAnyProfile (TreeReadOptions.Defaults.WithMaxDepth 2) yamlTree |> refusal

                Expect.isNonEmpty tooDeep "MaxDepth enforced"
                Expect.exists tooDeep (fun e -> e.Code = "depth_limit_exceeded") "Depth limit error code"

                let tooManyNodes =
                    DocumentTreeStore.readAnyProfile (TreeReadOptions.Defaults.WithMaxNodes 5) yamlTree |> refusal

                Expect.isNonEmpty tooManyNodes "MaxNodes enforced"
                Expect.exists tooManyNodes (fun e -> e.Code = "tree.document_node_limit") "Node limit error code"

                // Mid-decode cancellation: cancel the token when a module file stream opens.
                use cts = new CancellationTokenSource()
                let moduleLocation = "pkg/acme/orders/domain/module.yaml"
                let moduleBytes = Encoding.UTF8.GetBytes((Driver.texts yamlTree)[moduleLocation])

                let cancellingTree =
                    VFileTree.toFileMap yamlTree
                    |> Map.add
                        moduleLocation
                        (VFile.ofStream
                            moduleLocation
                            (StreamSource(fun () ->
                                cts.Cancel()
                                new MemoryStream(moduleBytes, writable = false) :> Stream
                            )))
                    |> VFileTree.fromFileMap

                Expect.throwsT<OperationCanceledException>
                    (fun () ->
                        let mutable loaded = Unchecked.defaultof<LoadedTreeModel>
                        let mutable errors = Unchecked.defaultof<System.Collections.Generic.IReadOnlyList<TreeDiagnostic>>
                        DocumentTreeStore.tryReadModel (TreeReadOptions.Defaults, cancellingTree, cts.Token, &loaded, &errors)
                        |> ignore
                    )
                    "Cancellation triggered during tree decoding throws OperationCanceledException"
            }

            test "Propagates CancellationToken into V4 and Classic tree join loops and aborts mid-join" {
                // 1. Mid-loop cancellation inside TreeJoin.joinPackage after the first module begins joining.
                use pkgCts = new CancellationTokenSource()
                let mutable modulesJoined = 0
                let m1 = ModulePath(Path [ Name.fromList [ "alpha" ] ])
                let m2 = ModulePath(Path [ Name.fromList [ "beta" ] ])
                let emptyMod : V4ModuleDocument = {
                    Access = Some Morphir.IR.V4.Public
                    Doc = None
                    Annotations = []
                    TypeNames = []
                    ValueNames = []
                }

                let family : TreeJoin.TreeFamily<V4ModuleDocument, V4TypeDocument, V4ValueDocument> = {
                    TypeNames = fun m -> m.TypeNames
                    ValueNames = fun m -> m.ValueNames
                    IsDefinedType = function DefinedType _ -> true | SpecifiedType _ -> false
                    IsDefinedValue = function DefinedValue _ -> true | SpecifiedValue _ -> false
                    ModuleFault = fun _ _ -> None
                }

                let shape : TreeJoin.PackageShape<V4ModuleDocument, V4TypeDocument, V4ValueDocument, _, _, _, _, _> = {
                    TypeOf = function DefinedType t -> Some t | SpecifiedType _ -> None
                    ValueOf = function DefinedValue v -> Some v | SpecifiedValue _ -> None
                    TypeMap = OrderedMap.ofList
                    ValueMap = OrderedMap.ofList
                    ModuleOf =
                        fun _ ->
                            modulesJoined <- modulesJoined + 1
                            if modulesJoined = 1 then pkgCts.Cancel()
                            Some(fun _ _ -> ())
                }

                Expect.throwsT<OperationCanceledException>
                    (fun () ->
                        TreeJoin.joinPackage
                            pkgCts.Token
                            family
                            shape
                            OrderedMap.ofList
                            Own
                            (DocumentAddress.logicalPath (PackageName(Path [ Name.fromList [ "acme" ] ])))
                            [ Module m1, ModuleDoc emptyMod; Module m2, ModuleDoc emptyMod ]
                        |> ignore
                    )
                    "Cancellation triggered after first module joins aborts TreeJoin.joinPackage before second module"

                Expect.equal modulesJoined 1 "Only the first module ran before cancellation stopped the join loop"

                // 2. Mid-loop cancellation inside TreeJoin.joinDependencies after the first dependency joins.
                use depCts = new CancellationTokenSource()
                let mutable depsJoined = 0
                let dep1 = PackageName(Path [ Name.fromList [ "dep"; "one" ] ])
                let dep2 = PackageName(Path [ Name.fromList [ "dep"; "two" ] ])

                Expect.throwsT<OperationCanceledException>
                    (fun () ->
                        TreeJoin.joinDependencies
                            depCts.Token
                            (fun _ ->
                                depsJoined <- depsJoined + 1
                                if depsJoined = 1 then depCts.Cancel()
                                Ok())
                            OrderedMap.ofList
                            [ dep1; dep2 ]
                        |> ignore
                    )
                    "Cancellation triggered after first dependency joins aborts TreeJoin.joinDependencies"

                Expect.equal depsJoined 1 "Only the first dependency ran before cancellation stopped dependency join"

                // 3. Direct cancellation propagation into V4DocumentTree.joinCancellable and ClassicDocumentTree.joinCancellable
                //    when cancellation fires right after tree construction (inside TreeReader.readTreeWith's join handoff).
                let v4Tree =
                    match readOk TreeReadOptions.Defaults (catalogTree "v4" "plain-json") with
                    | V4Tree t, _ -> t
                    | _ -> failtest "Expected V4Tree"

                let classicTree =
                    match readOk TreeReadOptions.Defaults (catalogTree "v3.1" "plain-json") with
                    | ClassicTree t, _ -> t
                    | _ -> failtest "Expected ClassicTree"

                use cancelled = new CancellationTokenSource()
                cancelled.Cancel()

                Expect.throwsT<OperationCanceledException>
                    (fun () -> V4DocumentTree.joinCancellable cancelled.Token v4Tree |> ignore)
                    "V4DocumentTree.joinCancellable observes CancellationToken"

                Expect.throwsT<OperationCanceledException>
                    (fun () -> ClassicDocumentTree.joinCancellable cancelled.Token classicTree |> ignore)
                    "ClassicDocumentTree.joinCancellable observes CancellationToken"

                // Verify TreeReader.readTreeWith passes ctx.Cancellation into join rather than only checking around it.
                use joinHandoffCts = new CancellationTokenSource()
                let mutable joinEntered = false
                let mutable joinReturned = false
                let jsonFiles =
                    VFileTree.toFileMap (catalogTree "v4" "plain-json")
                    |> Map.remove "manifest.json"
                    |> Map.toList

                let ctx =
                    TreeFileSource.createDecodeContext
                        TreeProfile.Json
                        (256L * 1024L * 1024L)
                        None
                        64
                        None
                        joinHandoffCts.Token

                let readers : TreeFileReaders<_, _, _> = {
                    Module = V4TreeFiles.readModule
                    Type = V4TreeFiles.readType
                    Value = V4TreeFiles.readValue
                    Listed = fun (m: V4ModuleDocument) -> m.TypeNames, m.ValueNames
                }

                Expect.throwsT<OperationCanceledException>
                    (fun () ->
                        TreeReader.readTreeWith
                            ctx
                            "manifest.json"
                            false
                            4000
                            v4Tree.Manifest.Package
                            (V4DocumentTree.holdsDefinitions v4Tree.Manifest.Kind)
                            readers
                            (fun docs ->
                                joinHandoffCts.Cancel()
                                ({ Manifest = v4Tree.Manifest; Documents = docs } : V4DocumentTree))
                            (fun ct tree ->
                                joinEntered <- true
                                let res = V4DocumentTree.joinCancellable ct tree
                                joinReturned <- true
                                res)
                            V4Tree
                            []
                            jsonFiles
                        |> ignore
                    )
                    "TreeReader.readTreeWith forwards CancellationToken into joinCancellable"

                Expect.isTrue joinEntered "Join callback was entered after build"
                Expect.isFalse joinReturned "Join threw OperationCanceledException instead of returning"

                // 4. Cancellation triggered inside the logical-path sort comparator must propagate OperationCanceledException
                //    unwrapped (not System.InvalidOperationException from Array.Sort). Use 4 modules so Array.Sort invokes
                //    the comparator multiple times and enters the exception-wrapping path when cancellation throws mid-sort.
                use sortCts = new CancellationTokenSource()
                let mutable compared = 0
                let mutable modulesEntered = 0
                let m3 = ModulePath(Path [ Name.fromList [ "gamma" ] ])
                let m4 = ModulePath(Path [ Name.fromList [ "delta" ] ])

                Expect.throwsT<OperationCanceledException>
                    (fun () ->
                        TreeJoin.joinPackage
                            sortCts.Token
                            family
                            { shape with
                                ModuleOf =
                                    fun _ ->
                                        modulesEntered <- modulesEntered + 1
                                        Some(fun _ _ -> ())
                            }
                            OrderedMap.ofList
                            Own
                            (fun address ->
                                compared <- compared + 1
                                if compared = 1 then sortCts.Cancel()
                                DocumentAddress.logicalPath (PackageName(Path [ Name.fromList [ "acme" ] ])) address)
                            [
                                Module m4, ModuleDoc emptyMod
                                Module m2, ModuleDoc emptyMod
                                Module m3, ModuleDoc emptyMod
                                Module m1, ModuleDoc emptyMod
                            ]
                        |> ignore
                    )
                    "Cancellation during List.sortWith comparison propagates OperationCanceledException without InvalidOperationException wrapping"

                Expect.isGreaterThanOrEqual compared 1 "Comparator ran at least once"
                Expect.equal modulesEntered 0 "Cancellation fired inside Array.Sort before module joining began"
            }

            test "Rejects contextual YAML whose transcoded envelope exceeds remaining byte budget even when expanded document fits" {
                let yamlText = YamlDocs.contextualYamlWithEscapedEnvelopeOverhead 300
                let rawBytes = Encoding.UTF8.GetBytes yamlText

                // Measure the exact transcoded JSON envelope length and expanded document length.
                let jsonEnvelopeBytes =
                    match TreeYaml.readCountedDetailed true 64 10_000 None CancellationToken.None "manifest.yaml" rawBytes with
                    | Ok(_, _, encodedBytes) -> encodedBytes
                    | Result.Error errs -> failtestf "Expected unbounded YAML parse to succeed: %A" errs

                // Choose a budget strictly above rawBytes (and well above the ~90-byte expanded plain document),
                // yet strictly below the transcoded JSON envelope length (~600+ bytes from escaped quotes).
                let tightBudget = int64 rawBytes.Length + 32L
                Expect.isLessThan tightBudget jsonEnvelopeBytes "Transcoded JSON envelope must exceed tightBudget"

                let mutable doc = Unchecked.defaultof<JsonElement>
                let mutable errors = Unchecked.defaultof<System.Collections.Generic.IReadOnlyList<TreeDiagnostic>>

                let ok =
                    DocumentTreeStore.tryReadYamlDocument (
                        "manifest.yaml",
                        ReadOnlyMemory rawBytes,
                        tightBudget,
                        64,
                        10_000,
                        CancellationToken.None,
                        &doc,
                        &errors
                    )

                Expect.isFalse ok "Transcoded YAML envelope exceeding remaining budget must be rejected"
                Expect.equal (errors[0].Code) "tree.file_too_large" "Exceeding transcoded byte budget reports tree.file_too_large"

                // Whole-tree read with MaxTotalBytes = tightBudget must also reject without clamping overrun to zero.
                let manifestOnlyTree = Driver.ofTexts [ "manifest.yaml", yamlText ]
                let treeErrors =
                    tryReadModelErrors (TreeReadOptions.Defaults.WithMaxTotalBytes tightBudget) manifestOnlyTree

                Expect.isNonEmpty treeErrors "Whole-tree read rejects transcoded YAML envelope overrun"
                Expect.equal (treeErrors[0].Code) "tree.file_too_large" "Whole-tree overrun reports tree.file_too_large"

                // With sufficient budget for the transcoded envelope, it succeeds cleanly.
                let generousBudget = jsonEnvelopeBytes + 64L
                let okGenerous =
                    DocumentTreeStore.tryReadYamlDocument (
                        "manifest.yaml",
                        ReadOnlyMemory rawBytes,
                        generousBudget,
                        64,
                        10_000,
                        CancellationToken.None,
                        &doc,
                        &errors
                    )

                Expect.isTrue okGenerous "Succeeds when budget covers transcoded YAML envelope"
            }

            test "Accepts MaxDepth = Int32.MaxValue without integer overflow on JSON and YAML trees" {
                let maxDepthOptions = TreeReadOptions.Defaults.WithMaxDepth Int32.MaxValue

                for profile in [ "plain-json"; "contextual-json"; "plain-yaml"; "contextual-yaml" ] do
                    let loaded = tryReadModelOk maxDepthOptions (catalogTree "v4" profile)
                    Expect.equal loaded.Model.Family ModelFamily.V4 $"v4 {profile} loads with MaxDepth = Int32.MaxValue"

                let mutable doc = Unchecked.defaultof<JsonElement>
                let mutable errors = Unchecked.defaultof<System.Collections.Generic.IReadOnlyList<TreeDiagnostic>>
                let ok =
                    DocumentTreeStore.tryReadYamlDocument (
                        "manifest.yaml",
                        ReadOnlyMemory(Encoding.UTF8.GetBytes YamlDocs.contextualManifestYaml),
                        65536L,
                        Int32.MaxValue,
                        10_000,
                        CancellationToken.None,
                        &doc,
                        &errors
                    )

                Expect.isTrue ok "TryReadYamlDocument succeeds with MaxDepth = Int32.MaxValue"

                // Deeply nested YAML (3000 levels) with MaxDepth = Int32.MaxValue must succeed iteratively
                // without exhausting the process call stack.
                let deepBytes = Encoding.UTF8.GetBytes(YamlDocs.deepNestedYaml 3000)
                let deepOk =
                    DocumentTreeStore.tryReadYamlDocument (
                        "deep.yaml",
                        ReadOnlyMemory deepBytes,
                        1024L * 1024L,
                        Int32.MaxValue,
                        100_000,
                        CancellationToken.None,
                        &doc,
                        &errors
                    )

                Expect.isTrue deepOk "Deep YAML (3000 levels) decodes iteratively at MaxDepth = Int32.MaxValue without stack overflow"
            }

            test "Enforces MaxDepth = 1 identically on JSON and YAML trees with scalar manifest members" {
                let jsonManifestOnly = YamlDocs.manifestOnlyV4JsonTree ()
                let yamlManifestOnly = YamlDocs.manifestOnlyV4YamlTree ()

                // At MaxDepth = 1, the root object is depth 1 and its scalar children are depth 2 -> both refuse.
                let jsonErrors = tryReadModelErrors (TreeReadOptions.Defaults.WithMaxDepth 1) jsonManifestOnly
                let yamlErrors = tryReadModelErrors (TreeReadOptions.Defaults.WithMaxDepth 1) yamlManifestOnly

                Expect.equal (diagCodesAndFiles jsonErrors) [ "depth_limit_exceeded", Some "manifest.json" ] "JSON manifest with scalars refuses at MaxDepth = 1"
                Expect.equal (diagCodesAndFiles yamlErrors) [ "depth_limit_exceeded", Some "manifest.yaml" ] "YAML manifest with scalars refuses at MaxDepth = 1"

                // At MaxDepth = 2, both manifests with scalar children succeed identically.
                let jsonLoaded = tryReadModelOk (TreeReadOptions.Defaults.WithMaxDepth 2) jsonManifestOnly
                let yamlLoaded = tryReadModelOk (TreeReadOptions.Defaults.WithMaxDepth 2) yamlManifestOnly
                Expect.equal jsonLoaded.Model.SourceVersion.Text "4" "JSON manifest loads at MaxDepth = 2"
                Expect.equal yamlLoaded.Model.SourceVersion.Text "4" "YAML manifest loads at MaxDepth = 2"
            }

            test "TryReadYamlDocument returns tree.invalid_limit instead of throwing for non-positive limits" {
                let validBytes = ReadOnlyMemory(Encoding.UTF8.GetBytes YamlDocs.minimalV4PlainYaml)

                for label, maxBytes, maxDepth, maxNodes in
                    [
                        "maxDepth = -1", 65536L, -1, 1000
                        "maxDepth = 0", 65536L, 0, 1000
                        "maxBytes = 0", 0L, 64, 1000
                        "maxBytes = -1", -1L, 64, 1000
                        "maxNodes = 0", 65536L, 64, 0
                        "maxNodes = -1", 65536L, 64, -1
                    ] do
                    let mutable doc = Unchecked.defaultof<JsonElement>
                    let mutable errors = Unchecked.defaultof<System.Collections.Generic.IReadOnlyList<TreeDiagnostic>>

                    let ok =
                        DocumentTreeStore.tryReadYamlDocument (
                            "model.yaml",
                            validBytes,
                            maxBytes,
                            maxDepth,
                            maxNodes,
                            CancellationToken.None,
                            &doc,
                            &errors
                        )

                    Expect.isFalse ok $"Expected TryReadYamlDocument to fail cleanly for {label}"
                    Expect.equal (diagCodesAndFiles (List.ofSeq errors)) [ "tree.invalid_limit", Some "model.yaml" ] $"Expected tree.invalid_limit for {label}"
            }

            test "Preserves structured JSONPath location for nested context validation errors and keeps legacy StoreError message" {
                let invalidBytes = ReadOnlyMemory(Encoding.UTF8.GetBytes YamlDocs.contextualYamlWithInvalidAliasEntry)
                let mutable doc = Unchecked.defaultof<JsonElement>
                let mutable errors = Unchecked.defaultof<System.Collections.Generic.IReadOnlyList<TreeDiagnostic>>

                let ok =
                    DocumentTreeStore.tryReadYamlDocument (
                        "manifest.yaml",
                        invalidBytes,
                        65536L,
                        64,
                        10_000,
                        CancellationToken.None,
                        &doc,
                        &errors
                    )

                Expect.isFalse ok "Invalid context alias entry is refused"
                Expect.equal (errors[0].Code) "context.invalid_entry" "Context invalid entry code"
                Expect.equal (nonNull errors[0].LocationOrNull) "$.context.names.bad" "Structured JSONPath location preserved"
                Expect.stringContains errors[0].Message "context.names.bad" "Message describes entry"

                // Converting to legacy StoreError preserves the exact legacy message without duplicating a prefix.
                let legacy = errors[0].ToStoreError()
                Expect.isFalse (legacy.Message.StartsWith("$.context.names.bad:")) "Legacy StoreError message keeps original format"
                Expect.stringStarts legacy.Message "context.names.bad is" "Legacy message starts with context.names.bad"
            }

            test "Enforces shared aggregate expansion byte budget across tree members" {
                let filesMap = YamlDocs.compactedTwoMemberExpandingTree 400
                let vfileTree = Driver.ofTexts (Map.toList filesMap)
                let rawTotalBytes =
                    filesMap
                    |> Map.values
                    |> Seq.sumBy (fun text -> int64 (Encoding.UTF8.GetByteCount text))

                // Each member expands to ~1,500 bytes; both together expand to ~3,000+ bytes while rawTotalBytes is ~900.
                let aggregateBudget = 2200L
                Expect.isLessThan rawTotalBytes aggregateBudget "Raw tree fits easily inside aggregateBudget"

                let errors =
                    tryReadModelErrors
                        (TreeReadOptions.Defaults.WithMaxFileBytes(aggregateBudget).WithMaxTotalBytes(aggregateBudget))
                        vfileTree

                Expect.isNonEmpty errors "Aggregate expanded bytes across two members exceed shared allowance"
                Expect.equal (errors[0].Code) "tree.file_too_large" "Aggregate expansion limit code"
                Expect.equal (errors[0].File) (Some "pkg/acme/orders/domain/beta.value.json") "Second member hits shared allowance"

                // With sufficient aggregate budget, the same tree loads cleanly.
                let loaded =
                    tryReadModelOk
                        (TreeReadOptions.Defaults.WithMaxFileBytes(aggregateBudget).WithMaxTotalBytes(8192L))
                        vfileTree

                Expect.equal loaded.Profile TreeProfile.Json "Tree succeeds when aggregate budget suffices"
            }

            test "Enforces expansion byte and node limits incrementally on compacted documents" {
                let compacted = YamlDocs.compactedExpandingManifestYaml 20 12
                let rawBytes = Encoding.UTF8.GetBytes compacted
                let rawLimit = int64 rawBytes.Length + 32L

                // Fits raw byte limit, but expanded text (~20 * 10 * 12 > 2400 bytes) exceeds rawLimit.
                let mutable doc = Unchecked.defaultof<JsonElement>
                let mutable errors = Unchecked.defaultof<System.Collections.Generic.IReadOnlyList<TreeDiagnostic>>

                let byteRefused =
                    DocumentTreeStore.tryReadYamlDocument (
                        "manifest.yaml",
                        ReadOnlyMemory rawBytes,
                        rawLimit,
                        64,
                        10_000,
                        CancellationToken.None,
                        &doc,
                        &errors
                    )

                Expect.isFalse byteRefused "Expanded bytes exceeding byte limit are refused"
                Expect.equal (errors[0].Code) "tree.file_too_large" "Expansion byte limit diagnostic"

                // Single classic v3.1.0 contextual YAML document whose raw node count fits the allowance
                // while its expanded classic word arrays exceed that exact allowance.
                let classicBytes = Encoding.UTF8.GetBytes YamlDocs.compactedClassicExpandingTypeYaml
                let rawNodes =
                    match TreeYaml.readCounted true 64 Int32.MaxValue "sample.type.yaml" classicBytes with
                    | Ok(_, nodes) -> nodes
                    | Result.Error errs -> failtestf "Expected raw classic YAML parse to succeed: %A" errs

                let allowance = rawNodes + 5

                // Prove raw YAML parsing succeeds under the exact allowance.
                match TreeYaml.readCounted true 64 allowance "sample.type.yaml" classicBytes with
                | Ok _ -> ()
                | Result.Error errs -> failtestf "Expected raw YAML to fit allowance %d, got %A" allowance errs

                // Prove expansion triggers the node limit under that exact allowance.
                let nodeRefused =
                    DocumentTreeStore.tryReadYamlDocument (
                        "sample.type.yaml",
                        ReadOnlyMemory classicBytes,
                        65536L,
                        64,
                        allowance,
                        CancellationToken.None,
                        &doc,
                        &errors
                    )

                Expect.isFalse nodeRefused "Expanded classic word arrays exceeding allowance are refused"
                Expect.equal (errors[0].Code) "tree.document_node_limit" "Expansion node limit diagnostic"

                // Prove the same document succeeds when given enough room for the expanded classic word arrays.
                let expandedOk =
                    DocumentTreeStore.tryReadYamlDocument (
                        "sample.type.yaml",
                        ReadOnlyMemory classicBytes,
                        65536L,
                        64,
                        1000,
                        CancellationToken.None,
                        &doc,
                        &errors
                    )

                Expect.isTrue expandedOk "Classic contextual document succeeds when expanded nodes fit maxNodes"
            }

            test "Refuses malformed YAML, anchors, keys with ': ', and missing YAML node files with structured locations" {
                let yamlTree = catalogTree "v4" "plain-yaml"

                let malformed = yamlTree |> Driver.withFile "manifest.yaml" YamlDocs.malformedFlowYaml
                let malformedErrors = tryReadModelErrors TreeReadOptions.Defaults malformed
                Expect.equal (diagCodesAndFiles malformedErrors) [ "invalid_yaml", Some "manifest.yaml" ] "Malformed YAML"
                Expect.isNotNull malformedErrors[0].LocationOrNull "YAML error carries pointer location"

                let anchored =
                    yamlTree
                    |> Driver.withText "manifest.yaml" (fun text -> text.Replace("4", "&v 4"))

                let anchoredErrors = tryReadModelErrors TreeReadOptions.Defaults anchored
                Expect.equal (diagCodesAndFiles anchoredErrors) [ "unsupported_yaml_feature", Some "manifest.yaml" ] "Anchors refused"
                Expect.equal (nonNull anchoredErrors[0].LocationOrNull) "/formatVersion" "Exact structured YAML pointer location"
                Expect.isFalse (anchoredErrors[0].Message.StartsWith("/formatVersion")) "Location is separate from message body"

                // Nested YAML key containing ": " must not be truncated by string splitting.
                let colonKeyBytes = ReadOnlyMemory(Encoding.UTF8.GetBytes YamlDocs.nestedYamlKeyWithColonSpace)
                let mutable doc = Unchecked.defaultof<JsonElement>
                let mutable errors = Unchecked.defaultof<System.Collections.Generic.IReadOnlyList<TreeDiagnostic>>
                let colonOk =
                    DocumentTreeStore.tryReadYamlDocument (
                        "manifest.yaml",
                        colonKeyBytes,
                        65536L,
                        64,
                        1000,
                        CancellationToken.None,
                        &doc,
                        &errors
                    )

                Expect.isFalse colonOk "Anchor under nested key containing ': ' is refused"
                Expect.equal (errors[0].Code) "unsupported_yaml_feature" "Anchor error code"
                Expect.equal (nonNull errors[0].LocationOrNull) "/outer/note: detail" "Structured pointer preserves ': ' in key"
                Expect.isFalse (errors[0].Message.StartsWith("/outer/note")) "Message body does not repeat pointer"

                let missingFile = "pkg/acme/orders/domain/order.value.yaml"
                let withoutOrder = yamlTree |> Driver.withoutFile missingFile
                let missingErrors = tryReadModelErrors TreeReadOptions.Defaults withoutOrder
                Expect.equal (diagCodesAndFiles missingErrors) [ "missing_member", Some missingFile ] "Missing member names .yaml path"
            }

            test "TryReadYamlDocument decodes plain and contextual YAML documents under limits and cancellation" {
                let plainBytes = ReadOnlyMemory(Encoding.UTF8.GetBytes YamlDocs.minimalV4PlainYaml)
                let mutable doc = Unchecked.defaultof<JsonElement>
                let mutable errors = Unchecked.defaultof<System.Collections.Generic.IReadOnlyList<TreeDiagnostic>>

                let ok =
                    DocumentTreeStore.tryReadYamlDocument ("model.yaml", plainBytes, 65536L, 64, 1000, CancellationToken.None, &doc, &errors)

                Expect.isTrue ok "Plain YAML document parses"
                Expect.equal (doc.GetProperty("formatVersion").GetInt32()) 4 "Parsed formatVersion"

                let limited =
                    DocumentTreeStore.tryReadYamlDocument ("model.yaml", plainBytes, 65536L, 64, 1, CancellationToken.None, &doc, &errors)

                Expect.isFalse limited "Node limit enforced"
                Expect.equal (errors[0].Code) "tree.document_node_limit" "Node limit code"

                let ctxOk =
                    DocumentTreeStore.tryReadYamlDocument (
                        "manifest.yaml",
                        ReadOnlyMemory(Encoding.UTF8.GetBytes YamlDocs.contextualManifestYaml),
                        65536L,
                        64,
                        1000,
                        CancellationToken.None,
                        &doc,
                        &errors
                    )

                Expect.isTrue ctxOk "Contextual YAML document expands"
                Expect.equal (doc.GetProperty("package").GetString()) "acme/orders" "Expanded alias"

                use cts = new CancellationTokenSource()
                cts.Cancel()

                Expect.throwsT<OperationCanceledException>
                    (fun () ->
                        DocumentTreeStore.tryReadYamlDocument ("model.yaml", plainBytes, 65536L, 64, 1000, cts.Token, &doc, &errors)
                        |> ignore
                    )
                    "Cancelled token throws OperationCanceledException"
            }
        ]
    ]
