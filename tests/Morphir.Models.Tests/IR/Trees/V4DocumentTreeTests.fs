/// v4 document trees: a v4 model splits into one document per module, type and value plus the manifest, and the
/// tree joins back to the model. Scenarios of the model-document-trees spec, requirement "A model splits into a
/// tree and joins back unchanged", and the refusals of join.
module Morphir.IR.Tests.Trees.V4DocumentTreeTests

open Expecto
open FsCheck
open Morphir.IR
open Morphir.IR.Trees
open Morphir.IR.V4
open Morphir.Models.TestSupport
open Morphir.Models.TestSupport.DistributionBuilder

let private v4Cases = [
    "v4LibraryWithOneModule", DistributionMother.v4LibraryWithOneModule
    "v4LibraryWithDependency", DistributionMother.v4LibraryWithDependency
    "v4Specs", DistributionMother.v4Specs
    "v4SpecsWithModuleAnnotations", DistributionMother.v4SpecsWithModuleAnnotations
    "v4Application", DistributionMother.v4Application
    "acmeDemo", DistributionMother.acmeDemo
    "emptyModule", DistributionMother.emptyModule
    "noModules", DistributionMother.noModules
    "nameUsedOnce", DistributionMother.nameUsedOnce
    "nameUsedOften", DistributionMother.nameUsedOften
    "collidingStems", DistributionMother.collidingStems
    "windowsReservedStems", DistributionMother.windowsReservedStems
    "privateAndDocumentedMembers", DistributionMother.privateAndDocumentedMembers
]

let private config =
    { FsCheckConfig.defaultConfig with
        maxTest = 200
    }

let private roundTrip distribution =
    V4DocumentTree.join (V4DocumentTree.split distribution)

let private expectRoundTrip distribution =
    match roundTrip distribution with
    | Ok joined -> Expect.equal joined distribution "The joined model equals the original"
    | Error errors -> failtestf "Expected the tree to join, got %A" errors

/// The codes of a refused join, or a failure if the join succeeds.
let private refusalCodes (tree: V4DocumentTree) =
    match V4DocumentTree.join tree with
    | Ok joined -> failtestf "Expected the join to be refused, got %A" joined
    | Error errors ->
        errors
        |> List.map (fun e -> e.Code)

let private pricing = modulePath "pricing/models"

let private invoice = Own(TypeDef(pricing, name "invoice"))

let private withDocuments change (tree: V4DocumentTree) =
    { tree with
        Documents = change tree.Documents
    }

let private ownModulePaths distribution =
    match distribution with
    | Library(_, _, definition)
    | Application(_, _, definition, _) -> OrderedMap.keys definition.Modules
    | Specs(_, _, specification) -> OrderedMap.keys specification.Modules

let private sortedByLogicalPath package (paths: ModulePath list) =
    paths
    |> List.sortWith (fun a b ->
        System.String.CompareOrdinal(
            DocumentAddress.logicalPath package (Own(Module a)),
            DocumentAddress.logicalPath package (Own(Module b))
        )
    )

let private packageOf distribution =
    match distribution with
    | Library(package, _, _)
    | Specs(package, _, _)
    | Application(package, _, _, _) -> package

[<Tests>]
let tests =
    testList "Trees" [
        testList "V4 document trees" [
            test "A v4 model round-trips" {
                expectRoundTrip DistributionMother.privateAndDocumentedMembers
            }

            testList "every v4 mother case round-trips" [
                for caseName, distribution in v4Cases do
                    test caseName { expectRoundTrip distribution }
            ]

            testPropertyWithConfig config "generated v4 models round-trip"
            <| Prop.forAll
                (Arb.fromGen Generators.v4Distribution)
                (fun distribution -> roundTrip distribution = Ok distribution)

            test "An empty module round-trips, and its module document still exists" {
                expectRoundTrip DistributionMother.emptyModule

                let tree = V4DocumentTree.split DistributionMother.emptyModule

                match Map.tryFind (Own(Module(modulePath "empty"))) tree.Documents with
                | Some(ModuleDoc m) ->
                    Expect.isEmpty m.TypeNames "No types"
                    Expect.isEmpty m.ValueNames "No values"
                | other -> failtestf "Expected the module document, got %A" other
            }

            test "A package with no modules round-trips" {
                expectRoundTrip DistributionMother.noModules

                Expect.isEmpty
                    (V4DocumentTree.split DistributionMother.noModules).Documents
                    "A package with no modules has only the manifest"
            }

            test "A split has one document per module, type and value" {
                let tree = V4DocumentTree.split DistributionMother.acmeDemo

                let billing = modulePath "billing"
                let basics = modulePath "basics"

                Expect.equal
                    (tree.Documents
                     |> Map.keys
                     |> Set.ofSeq)
                    (set [
                        Own(Module billing)
                        Own(TypeDef(billing, name "invoice"))
                        Own(TypeDef(billing, DistributionMother.Names.invoiceLine))
                        Own(ValueDef(billing, name "total"))
                        Dependency(packageName "morphir/sdk", Module basics)
                        Dependency(packageName "morphir/sdk", TypeDef(basics, name "money"))
                        Dependency(packageName "morphir/sdk", ValueDef(basics, name "zero"))
                    ])
                    "The addresses"

                Expect.equal
                    tree.Manifest
                    {
                        Kind = LibraryKind
                        Package = packageName "acme/demo"
                        Dependencies = [ packageName "morphir/sdk" ]
                        EntryPoints = []
                    }
                    "The manifest"
            }

            testPropertyWithConfig config "joined modules are in logical-path order"
            <| Prop.forAll
                (Arb.fromGen Generators.v4Distribution)
                (fun distribution ->
                    match roundTrip distribution with
                    | Ok joined ->
                        let paths = ownModulePaths joined
                        paths = sortedByLogicalPath (packageOf joined) paths
                    | Error _ -> false
                )

            testList "join refuses an inconsistent tree" [
                test "A module that lists a type with no document is refused" {
                    let tree =
                        V4DocumentTree.split DistributionMother.v4LibraryWithOneModule
                        |> withDocuments (Map.remove invoice)

                    match V4DocumentTree.join tree with
                    | Error [ error ] ->
                        Expect.equal error.Code "tree.missing_definition" "The code"

                        Expect.equal
                            error.Address
                            (Some invoice)
                            "The address of the missing definition"

                        Expect.stringContains error.Message "invoice" "The message names the type"
                    | other -> failtestf "Expected one refusal, got %A" other
                }

                test "A definition no module lists is refused" {
                    let tree =
                        V4DocumentTree.split DistributionMother.v4LibraryWithOneModule
                        |> withDocuments (Map.remove (Own(Module pricing)))

                    Expect.contains (refusalCodes tree) "tree.unlisted_definition" "The code"
                }

                test "A document of the wrong kind for its address is refused" {
                    let tree = V4DocumentTree.split DistributionMother.v4LibraryWithOneModule

                    let moduleDocument = tree.Documents[Own(Module pricing)]

                    Expect.equal
                        (refusalCodes (
                            tree
                            |> withDocuments (Map.add invoice moduleDocument)
                        ))
                        [ "tree.mismatched_document" ]
                        "The code"
                }

                test "A specification in a definition tree is refused" {
                    let library = V4DocumentTree.split DistributionMother.v4LibraryWithOneModule
                    let specs = V4DocumentTree.split DistributionMother.v4Specs
                    let specified = specs.Documents[invoice]

                    Expect.equal
                        (refusalCodes (
                            library
                            |> withDocuments (Map.add invoice specified)
                        ))
                        [ "tree.mismatched_document" ]
                        "The code"
                }

                test "A module specification with an access is refused" {
                    let specs = V4DocumentTree.split DistributionMother.v4Specs

                    let withAccess =
                        match specs.Documents[Own(Module pricing)] with
                        | ModuleDoc m -> ModuleDoc { m with Access = Some Public }
                        | other -> failtestf "Expected a module document, got %A" other

                    Expect.equal
                        (refusalCodes (
                            specs
                            |> withDocuments (Map.add (Own(Module pricing)) withAccess)
                        ))
                        [ "tree.mismatched_document" ]
                        "The code"
                }

                test "A module definition with annotations is refused" {
                    let library = V4DocumentTree.split DistributionMother.v4LibraryWithOneModule

                    let annotations =
                        match
                            (V4DocumentTree.split DistributionMother.v4SpecsWithModuleAnnotations)
                                .Documents[Own(Module pricing)]
                        with
                        | ModuleDoc m -> m.Annotations
                        | other -> failtestf "Expected a module document, got %A" other

                    let annotated =
                        match library.Documents[Own(Module pricing)] with
                        | ModuleDoc m -> ModuleDoc { m with Annotations = annotations }
                        | other -> failtestf "Expected a module document, got %A" other

                    Expect.equal
                        (refusalCodes (
                            library
                            |> withDocuments (Map.add (Own(Module pricing)) annotated)
                        ))
                        [ "tree.mismatched_document" ]
                        "The code"
                }

                test "A module that lists a name twice is refused" {
                    let library = V4DocumentTree.split DistributionMother.v4LibraryWithOneModule

                    let repeated =
                        match library.Documents[Own(Module pricing)] with
                        | ModuleDoc m ->
                            ModuleDoc
                                { m with
                                    TypeNames =
                                        m.TypeNames
                                        @ [ name "invoice" ]
                                }
                        | other -> failtestf "Expected a module document, got %A" other

                    Expect.equal
                        (refusalCodes (
                            library
                            |> withDocuments (Map.add (Own(Module pricing)) repeated)
                        ))
                        [ "tree.duplicate_name" ]
                        "The code"
                }

                test "A dependency the manifest does not list is refused" {
                    let tree = V4DocumentTree.split DistributionMother.v4LibraryWithDependency

                    Expect.equal
                        (refusalCodes { tree with Manifest.Dependencies = [] })
                        [ "tree.undeclared_dependency" ]
                        "The code"
                }

                test "A dependency the manifest lists twice is refused" {
                    let tree = V4DocumentTree.split DistributionMother.v4LibraryWithDependency

                    Expect.equal
                        (refusalCodes
                            { tree with
                                Manifest.Dependencies =
                                    tree.Manifest.Dependencies
                                    @ tree.Manifest.Dependencies
                            })
                        [ "tree.duplicate_dependency" ]
                        "The code"
                }

                test "Entry points outside an application are refused" {
                    let application = V4DocumentTree.split DistributionMother.v4Application
                    let library = V4DocumentTree.split DistributionMother.v4LibraryWithOneModule

                    Expect.equal
                        (refusalCodes
                            { library with
                                Manifest.EntryPoints = application.Manifest.EntryPoints
                            })
                        [ "tree.misplaced_entry_points" ]
                        "The code"
                }

                test "A manifest held as a document is refused" {
                    let library = V4DocumentTree.split DistributionMother.v4LibraryWithOneModule
                    let moduleDocument = library.Documents[Own(Module pricing)]

                    Expect.equal
                        (refusalCodes (
                            library
                            |> withDocuments (Map.add Manifest moduleDocument)
                        ))
                        [ "tree.mismatched_document" ]
                        "The code"
                }
            ]
        ]
    ]
