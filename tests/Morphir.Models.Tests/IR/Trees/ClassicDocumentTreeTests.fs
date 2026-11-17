/// Classic v3 document trees: a classic model splits into one document per module, type and value plus the
/// manifest, and the tree joins back to the model. Scenarios of the model-document-trees spec, requirement "A model
/// splits into a tree and joins back unchanged", and the refusals of join.
///
/// A classic model read from JSON keeps its attributes as JsonElement values, which have no structural equality,
/// so a round trip is checked by the bytes the classic writer gives for the original and for the joined model.
module Morphir.IR.Tests.Trees.ClassicDocumentTreeTests

open System.Text.Json
open Expecto
open FsCheck
open Morphir.IR
open Morphir.IR.Trees
open Morphir.Json.Classic
open Morphir.Models.TestSupport
open Morphir.Models.TestSupport.DistributionBuilder

type private TypedModel = Classic.Distribution.Distribution<unit, Classic.Type<unit>>

type private JsonModel = Classic.Distribution.Distribution<JsonElement, JsonElement>

let private v3Cases: (string * TypedModel) list = [
    "v3Library", DistributionMother.v3Library
    "v3Specs", DistributionMother.v3Specs
    "nonAsciiName", DistributionMother.nonAsciiName
    "v3PrivateAndDocumentedMembers", DistributionMother.v3PrivateAndDocumentedMembers
]

let private config =
    { FsCheckConfig.defaultConfig with
        maxTest = 200
    }

/// The profile of the single file a tree joins to, as its manifest records it.
let private profileOf label =
    match label with
    | Integer3 -> ClassicWireProfile.v3
    | SemVer310 -> ClassicWireProfile.v3_1_0

let private profileFor (distribution: Classic.Distribution.Distribution<_, _>) =
    match distribution with
    | Classic.Distribution.Library _ -> ClassicWireProfile.v3
    | Classic.Distribution.Specs _ -> ClassicWireProfile.v3_1_0

let private bytesOf profile (distribution: JsonModel) =
    match ClassicJsonWriter.writeJsonAttributes profile false distribution with
    | Ok bytes -> bytes
    | Error error -> failtestf "Expected a classic write, got %s at %s: %s" error.Rule error.Location error.Message

/// The model as the classic reader gives it: written with the typed writer and read back with JSON attributes.
let private readFromJson (distribution: TypedModel) : JsonModel =
    match ClassicJsonWriter.writeTyped (profileFor distribution) false distribution with
    | Error error -> failtestf "Expected a classic write, got %s: %s" error.Rule error.Message
    | Ok bytes ->
        use document = JsonDocument.Parse(System.ReadOnlyMemory bytes)

        match ClassicJson.readDistribution document.RootElement with
        | Ok read -> read.Distribution
        | Error error -> failtestf "Expected a classic read, got %s at %s: %s" error.Rule error.Location error.Message

/// The model with each attribute slot holding its own JSON object, {"slot": n}, so a join that moves, swaps or
/// drops one attribute gives other bytes.
let private withNumberedSlots (distribution: TypedModel) : JsonModel =
    let counter = ref 0

    let next () =
        counter.Value <- counter.Value + 1
        use document = JsonDocument.Parse($"{{\"slot\":{counter.Value}}}")
        document.RootElement.Clone()

    Classic.Attributes.mapDistribution (fun () -> next ()) (fun _ -> next ()) distribution

/// The bytes of the joined model, written with the profile its manifest records, or the errors of the join.
let private roundTripBytes (distribution: JsonModel) =
    let tree = ClassicDocumentTree.split distribution

    ClassicDocumentTree.join tree
    |> Result.map (bytesOf (profileOf tree.Manifest.SingleFileVersion))

let private expectRoundTrip (distribution: JsonModel) =
    match roundTripBytes distribution with
    | Ok joined ->
        Expect.sequenceEqual joined (bytesOf (profileFor distribution) distribution) "The joined model equals the original"
    | Error errors -> failtestf "Expected the tree to join, got %A" errors

/// The codes of a refused join, or a failure if the join succeeds.
let private refusalCodes (tree: ClassicDocumentTree<unit, Classic.Type<unit>>) =
    match ClassicDocumentTree.join tree with
    | Ok joined -> failtestf "Expected the join to be refused, got %A" joined
    | Error errors ->
        errors
        |> List.map (fun e -> e.Code)

let private pricing = modulePath "pricing/models"

let private invoice = Own(TypeDef(pricing, name "invoice"))

let private withDocuments change (tree: ClassicDocumentTree<_, _>) =
    { tree with
        Documents = change tree.Documents
    }

let private moduleDocumentOf address (tree: ClassicDocumentTree<_, _>) =
    match Map.tryFind address tree.Documents with
    | Some(ModuleDoc m) -> m
    | other -> failtestf "Expected a module document at %O, got %A" address other

let private kindOf (distribution: Classic.Distribution.Distribution<_, _>) =
    match distribution with
    | Classic.Distribution.Library _ -> ClassicDistributionKind.LibraryKind
    | Classic.Distribution.Specs _ -> ClassicDistributionKind.SpecsKind

/// Whether a document holds a definition: a defined type or value, or a module document with an access.
let private isDefinition document =
    match document with
    | ModuleDoc(m: ClassicModuleDocument) -> m.Access.IsSome
    | TypeDoc(ClassicTypeDocument.DefinedType _)
    | ValueDoc(ClassicValueDocument.DefinedValue _) -> true
    | TypeDoc(ClassicTypeDocument.SpecifiedType _)
    | ValueDoc(ClassicValueDocument.SpecifiedValue _) -> false

[<Tests>]
let tests =
    testList "Trees" [
        testList "Classic document trees" [
            test "A v3 model round-trips" {
                let original = withNumberedSlots DistributionMother.v3PrivateAndDocumentedMembers

                Expect.stringContains
                    (System.Text.Encoding.UTF8.GetString(bytesOf ClassicWireProfile.v3 original))
                    "{\"slot\":"
                    "Every attribute slot holds its own value"

                expectRoundTrip original
            }

            testList "every v3 mother case round-trips with numbered attribute slots" [
                for caseName, distribution in v3Cases do
                    test caseName { expectRoundTrip (withNumberedSlots distribution) }
            ]

            // nonAsciiName is left out: the classic reader refuses a word outside a-z and 0-9.
            testList "every v3 mother case round-trips as the classic reader gives it" [
                for caseName, distribution in v3Cases do
                    if caseName <> "nonAsciiName" then
                        test caseName { expectRoundTrip (readFromJson distribution) }
            ]

            testPropertyWithConfig config "generated v3 models round-trip with every attribute slot"
            <| Prop.forAll
                (Arb.fromGen Generators.v3AnyDistribution)
                (fun distribution ->
                    let original = withNumberedSlots distribution

                    match roundTripBytes original with
                    | Ok joined -> joined = bytesOf (profileFor original) original
                    | Error _ -> false
                )

            test "A v3 Library records the integer label, and a v3 Specs the 3.1.0 label" {
                let library = ClassicDocumentTree.split DistributionMother.v3Library
                let specs = ClassicDocumentTree.split DistributionMother.v3Specs

                Expect.equal
                    library.Manifest
                    {
                        Kind = ClassicDistributionKind.LibraryKind
                        Package = packageName "my-org/finance"
                        Dependencies = []
                        SingleFileVersion = Integer3
                    }
                    "The Library manifest"

                Expect.equal
                    specs.Manifest
                    {
                        Kind = ClassicDistributionKind.SpecsKind
                        Package = packageName "my-org/finance"
                        Dependencies = [ Sdk.package ]
                        SingleFileVersion = SemVer310
                    }
                    "The Specs manifest"
            }

            test "A split has one document per module, type and value" {
                let tree = ClassicDocumentTree.split DistributionMother.v3Specs

                Expect.equal
                    (tree.Documents
                     |> Map.keys
                     |> Set.ofSeq)
                    (set [
                        Own(Module pricing)
                        invoice
                        Own(TypeDef(pricing, name "status"))
                        Own(ValueDef(pricing, name "default-currency"))
                        Own(ValueDef(pricing, name "same-invoice"))
                        Dependency(Sdk.package, Module dependencyModule)
                        Dependency(Sdk.package, TypeDef(dependencyModule, name "money"))
                        Dependency(Sdk.package, ValueDef(dependencyModule, name "zero"))
                    ])
                    "The addresses"
            }

            test "A module document holds the module's access and documentation" {
                let tree = ClassicDocumentTree.split DistributionMother.v3PrivateAndDocumentedMembers
                let accounts = moduleDocumentOf (Own(Module(modulePath "accounts"))) tree
                let internalModule = moduleDocumentOf (Own(Module(modulePath "accounts/internal"))) tree

                let basics =
                    moduleDocumentOf (Dependency(Sdk.package, Module(modulePath "basics"))) tree

                Expect.equal accounts.Access (Some Classic.AccessControlled.Public) "A public module definition"
                Expect.equal accounts.Doc (Some "Accounts and their balances.") "Its documentation"

                Expect.equal
                    internalModule.Access
                    (Some Classic.AccessControlled.Private)
                    "A private module definition"

                Expect.equal
                    internalModule.Doc
                    (Some "Helpers that the package keeps to itself.")
                    "Its documentation"

                Expect.equal basics.Access None "A dependency module is a specification, with no access"
            }

            testList "holdsDefinitions" [
                for kind, own in [ ClassicDistributionKind.LibraryKind, true; ClassicDistributionKind.SpecsKind, false ] do
                    test $"{kind}" {
                        let basics = Module(modulePath "basics")

                        Expect.equal (ClassicDocumentTree.holdsDefinitions kind (Own basics)) own "The own package"

                        Expect.isFalse
                            (ClassicDocumentTree.holdsDefinitions kind (Dependency(Sdk.package, basics)))
                            "A dependency holds specifications"

                        Expect.isFalse
                            (ClassicDocumentTree.holdsDefinitions kind Manifest)
                            "The manifest holds neither"
                    }

                testPropertyWithConfig
                    config
                    "every document of a split holds what holdsDefinitions says"
                <| Prop.forAll
                    (Arb.fromGen Generators.v3AnyDistribution)
                    (fun distribution ->
                        let kind = kindOf distribution

                        (ClassicDocumentTree.split distribution).Documents
                        |> Map.forall (fun address document ->
                            isDefinition document = ClassicDocumentTree.holdsDefinitions kind address
                        )
                    )
            ]

            testList "join refuses an inconsistent tree" [
                test "A module that lists a type with no document is refused" {
                    let tree =
                        ClassicDocumentTree.split DistributionMother.v3Library
                        |> withDocuments (Map.remove invoice)

                    match ClassicDocumentTree.join tree with
                    | Error [ error ] ->
                        Expect.equal error.Code "tree.missing_definition" "The code"
                        Expect.equal error.Address (Some invoice) "The address of the missing definition"
                        Expect.stringContains error.Message "invoice" "The message names the type"
                    | other -> failtestf "Expected one refusal, got %A" other
                }

                test "A definition no module lists is refused" {
                    let tree =
                        ClassicDocumentTree.split DistributionMother.v3Library
                        |> withDocuments (Map.remove (Own(Module pricing)))

                    Expect.contains (refusalCodes tree) "tree.unlisted_definition" "The code"
                }

                test "A document of the wrong kind for its address is refused" {
                    let tree = ClassicDocumentTree.split DistributionMother.v3Library
                    let moduleDocument = tree.Documents[Own(Module pricing)]

                    Expect.equal
                        (refusalCodes (tree |> withDocuments (Map.add invoice moduleDocument)))
                        [ "tree.mismatched_document" ]
                        "The code"
                }

                test "A specification in a definition tree is refused" {
                    let library = ClassicDocumentTree.split DistributionMother.v3Library
                    let specified = (ClassicDocumentTree.split DistributionMother.v3Specs).Documents[invoice]

                    Expect.equal
                        (refusalCodes (library |> withDocuments (Map.add invoice specified)))
                        [ "tree.mismatched_document" ]
                        "The code"
                }

                test "A module specification with an access is refused" {
                    let specs = ClassicDocumentTree.split DistributionMother.v3Specs

                    let withAccess =
                        ModuleDoc
                            { moduleDocumentOf (Own(Module pricing)) specs with
                                Access = Some Classic.AccessControlled.Public
                            }

                    Expect.equal
                        (refusalCodes (specs |> withDocuments (Map.add (Own(Module pricing)) withAccess)))
                        [ "tree.mismatched_document" ]
                        "The code"
                }

                test "A module definition with no access is refused" {
                    let library = ClassicDocumentTree.split DistributionMother.v3Library

                    let withoutAccess =
                        ModuleDoc
                            { moduleDocumentOf (Own(Module pricing)) library with
                                Access = None
                            }

                    Expect.equal
                        (refusalCodes (library |> withDocuments (Map.add (Own(Module pricing)) withoutAccess)))
                        [ "tree.mismatched_document" ]
                        "The code"
                }

                test "A module that lists a name twice is refused" {
                    let library = ClassicDocumentTree.split DistributionMother.v3Library
                    let pricingModule = moduleDocumentOf (Own(Module pricing)) library

                    let repeated =
                        ModuleDoc
                            { pricingModule with
                                TypeNames = pricingModule.TypeNames @ [ name "invoice" ]
                            }

                    Expect.equal
                        (refusalCodes (library |> withDocuments (Map.add (Own(Module pricing)) repeated)))
                        [ "tree.duplicate_name" ]
                        "The code"
                }

                test "A dependency the manifest does not list is refused" {
                    let tree = ClassicDocumentTree.split DistributionMother.v3Specs

                    Expect.equal
                        (refusalCodes { tree with Manifest.Dependencies = [] })
                        [ "tree.undeclared_dependency" ]
                        "The code"
                }

                test "A dependency the manifest lists twice is refused" {
                    let tree = ClassicDocumentTree.split DistributionMother.v3Specs

                    Expect.equal
                        (refusalCodes
                            { tree with
                                Manifest.Dependencies = tree.Manifest.Dependencies @ tree.Manifest.Dependencies
                            })
                        [ "tree.duplicate_dependency" ]
                        "The code"
                }

                test "A label that does not fit the kind is refused" {
                    let library = ClassicDocumentTree.split DistributionMother.v3Library
                    let specs = ClassicDocumentTree.split DistributionMother.v3Specs

                    Expect.equal
                        (refusalCodes { library with Manifest.SingleFileVersion = SemVer310 })
                        [ "tree.mismatched_label" ]
                        "A Library joins to 3"

                    match ClassicDocumentTree.join { specs with Manifest.SingleFileVersion = Integer3 } with
                    | Error [ error ] ->
                        Expect.equal error.Code "tree.mismatched_label" "A Specs joins to 3.1.0"
                        Expect.equal error.Address (Some Manifest) "The manifest is at fault"
                        Expect.stringContains error.Message "\"3.1.0\"" "The message names the label a Specs needs"
                    | other -> failtestf "Expected one refusal, got %A" other
                }

                test "A manifest held as a document is refused" {
                    let library = ClassicDocumentTree.split DistributionMother.v3Library
                    let moduleDocument = library.Documents[Own(Module pricing)]

                    Expect.equal
                        (refusalCodes (library |> withDocuments (Map.add Manifest moduleDocument)))
                        [ "tree.mismatched_document" ]
                        "The code"
                }
            ]
        ]
    ]
