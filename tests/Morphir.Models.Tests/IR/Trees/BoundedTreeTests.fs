module Morphir.IR.Tests.Trees.BoundedTreeTests

open System.IO
open System.Text.Json
open Expecto
open Morphir.Files
open Morphir.IR
open Morphir.IR.Trees
open Morphir.TestSupport.Fixtures

module Orders = Morphir.Models.TestSupport.DocumentTreeMother.BoundedOrders
module Mother = Morphir.Models.TestSupport.DocumentTreeMother
module Driver = Morphir.Models.TestSupport.DocumentTreeStoreDriver

let private domain = ModulePath(Path [ Name [ "domain" ] ])
let private manifestLocation = Orders.manifestLocation
let private moduleLocation = Orders.domainLocation
let private orderLocation = Orders.orderLocation
let private totalLocation = Orders.totalLocation

let private jsonVersions = [ "v3.1", "\"3.1.0\""; "v4", "4" ]

// The source refuses any unexpected lookup so a split body cannot be read by discovery.
let private ordersSource files =
    let files = VFileTree.toFileMap files
    let mutable revision = "orders-1"
    let lookedUp = ResizeArray<string>()

    let source: TreeFileSource.SnapshotSource = {
        Revision = fun () -> revision
        ContainsFile = fun location -> Map.containsKey location files
        NextModule = fun _ _ _ _ -> failtest "Member reads do not enumerate modules"
        FindFile =
            fun location ->
                lookedUp.Add location

                match Map.tryFind location files with
                | Some file -> Some file
                | None -> failtestf "Unexpected file lookup during discovery: %s" location
    }

    source, lookedUp, (fun () -> revision <- "orders-2")

let private read =
    function
    | Ok result -> result
    | Result.Error errors -> failtestf "Expected a successful read, got %A" errors

let private orderAddress = Own(ValueDef(domain, Name [ "order" ]))

let private expectReadError code location result =
    Expect.equal
        (result
         |> StoreTestKit.refusal
         |> StoreTestKit.codesAndFiles)
        [ code, Some location ]
        "The selected file error"

let private verifyOrdersBodies finalBody orderBody =
    match finalBody, orderBody with
    | BoundedTreeBody.ClassicValue(ClassicValueDocument.DefinedValue final),
      BoundedTreeBody.ClassicValue(ClassicValueDocument.DefinedValue order) ->
        let final = Classic.Documented.value final.Value
        let order = Classic.Documented.value order.Value

        match final.Body, order.Body with
        | Classic.Reference(attributes, target), Classic.Literal(_, Classic.WholeNumberLiteral 73L) ->
            Expect.equal
                (CanonicalNames.fqNameToString target)
                "acme/orders:domain#order"
                "Exact value reference"

            Expect.equal (attributes[0].GetString()) "Reference" "Classic inferred type is retained"

            Expect.equal
                attributes[1].ValueKind
                JsonValueKind.Object
                "Classic type attributes are retained"
        | _ -> failtest "Expected a reference and an integer literal in the classic bodies"
    | BoundedTreeBody.V4Value(DefinedValue final), BoundedTreeBody.V4Value(DefinedValue order) ->
        match final.Value.Value.Body, order.Value.Value.Body with
        | V4.ExpressionBody(_, V4.Value.Reference(_, target)),
          V4.ExpressionBody(_, V4.Value.Literal(_, V4.IntegerLiteral literal)) ->
            Expect.equal
                (CanonicalNames.fqNameToString target)
                "acme/orders:domain#order"
                "Exact value reference"

            Expect.equal literal (bigint 73) "Integer literal"
        | _ -> failtest "Expected a reference and an integer literal in the v4 bodies"
    | _ -> failtest "Expected two value definitions of the selected IR version"


let private verifyOrdersTypeAndSpecifications orderType sdkType sdkValue =
    let expectedType = "morphir/SDK:basics#int"

    let classicReference (expression: Classic.Type<JsonElement>) =
        match expression with
        | Classic.Type.Reference(attributes, target, []) ->
            Expect.equal
                (CanonicalNames.fqNameToString target)
                expectedType
                "The SDK type reference"

            Expect.equal (attributes.GetRawText()) "{}" "The frozen classic type attribute"
        | _ -> failtest "Expected a classic reference with no arguments"

    let v4Reference expression =
        match expression with
        | V4.Type.Reference(_, target, []) ->
            Expect.equal
                (CanonicalNames.fqNameToString target)
                expectedType
                "The SDK type reference"
        | _ -> failtest "Expected a v4 reference with no arguments"

    let expectedInputs = [ Name [ "a" ]; Name [ "b" ] ]

    match orderType, sdkType, sdkValue with
    | BoundedTreeBody.ClassicType(ClassicTypeDocument.DefinedType order),
      BoundedTreeBody.ClassicType(ClassicTypeDocument.SpecifiedType intSpec),
      BoundedTreeBody.ClassicValue(ClassicValueDocument.SpecifiedValue add) ->
        match Classic.Documented.value order.Value, Classic.Documented.value intSpec with
        | Classic.Type.TypeAliasDefinition([], expression), Classic.Type.OpaqueTypeSpecification [] ->
            classicReference expression
        | _ -> failtest "Expected the classic order alias and opaque SDK type"

        let signature = Classic.Documented.value add
        Expect.equal (List.map fst signature.Inputs) expectedInputs "Ordered SDK parameters"

        List.iter
            (snd
             >> classicReference)
            signature.Inputs

        classicReference signature.Output
    | BoundedTreeBody.V4Type(DefinedType order),
      BoundedTreeBody.V4Type(SpecifiedType intSpec),
      BoundedTreeBody.V4Value(SpecifiedValue add) ->
        match order.Value.Value, intSpec.Value with
        | V4.TypeAliasDefinition([], expression), V4.OpaqueTypeSpecification([], []) ->
            v4Reference expression
        | _ -> failtest "Expected the v4 order alias and opaque SDK type"

        Expect.equal (List.map fst add.Value.Inputs) expectedInputs "Ordered SDK parameters"

        List.iter
            (snd
             >> v4Reference)
            add.Value.Inputs

        v4Reference add.Value.Output
    | _ -> failtest "Expected a type definition and two same-version SDK specifications"

[<Tests>]
let tests =
    testList "Bounded document-tree inventory" [
        for version, formatVersion in jsonVersions do
            test $"Metadata pages and cursor binding for IR {formatVersion}" {
                let source, lookedUp, advance = ordersSource (Orders.metadata version)
                let snapshot = read (BoundedTreeReader.openTree 1024L source)
                let selection = Own(Module domain)

                let first = read (BoundedTreeReader.members 2 None selection snapshot)

                Expect.equal
                    (first.Items
                     |> List.map (fun item -> item.DocumentId))
                    [ "pkg/acme/orders/domain/order.type"; "pkg/acme/orders/domain/order.value" ]
                    "A type and a value can share a name"

                let cursor = Expect.wantSome first.Next "A third member is listed"
                let last = read (BoundedTreeReader.members 2 (Some cursor) selection snapshot)

                Expect.equal
                    (last.Items
                     |> List.map (fun item -> item.DocumentId))
                    [ "pkg/acme/orders/domain/total-in-_usd.value" ]
                    "The final page includes the escaped stem"

                Expect.isNone last.Next "There is no extra page"

                Expect.sequenceEqual
                    lookedUp
                    [ manifestLocation; moduleLocation; moduleLocation ]
                    "Only metadata was looked up"

                let foreign = read (BoundedTreeReader.openTree 1024L source)

                let foreignError =
                    BoundedTreeReader.members 2 (Some cursor) selection foreign
                    |> StoreTestKit.refusal

                Expect.equal
                    (foreignError
                     |> List.map (fun error -> error.Code))
                    [ "tree.invalid_cursor" ]
                    "A cursor cannot cross snapshots"

                advance ()

                let stale =
                    BoundedTreeReader.members 2 (Some cursor) selection snapshot
                    |> StoreTestKit.refusal

                Expect.equal
                    (stale
                     |> List.map (fun error -> error.Code))
                    [ "tree.revision_changed" ]
                    "A changed revision cannot resume the page"
            }

            test $"Metadata rejects a duplicate after the page boundary for IR {formatVersion}" {
                let source, lookedUp, _ = ordersSource (Orders.duplicateListedValue version)
                let snapshot = read (BoundedTreeReader.openTree 1024L source)

                let errors =
                    BoundedTreeReader.members 1 None (Own(Module domain)) snapshot
                    |> StoreTestKit.refusal

                Expect.equal
                    (errors
                     |> List.map (fun error -> error.Code))
                    [ "duplicate_member" ]
                    "The first page validates the complete listing"

                Expect.sequenceEqual
                    lookedUp
                    [ manifestLocation; moduleLocation ]
                    "No definition file is consulted"
            }

            test $"Metadata does not decode inline bodies for IR {formatVersion}" {
                let source, lookedUp, _ = ordersSource (Orders.malformedInlineValue version)
                let snapshot = read (BoundedTreeReader.openTree 1024L source)
                let page = read (BoundedTreeReader.members 2 None (Own(Module domain)) snapshot)

                Expect.equal
                    (page.Items
                     |> List.map (fun item -> item.DocumentId))
                    [ "pkg/acme/orders/domain/order.type"; "pkg/acme/orders/domain/module" ]
                    "An inline value refers to the module document despite its malformed payload"

                Expect.sequenceEqual
                    lookedUp
                    [ manifestLocation; moduleLocation ]
                    "Only metadata is consulted"
            }
    ]

let private moduleSource version =
    let source, lookedUp, advance = ordersSource (Orders.moduleMetadata version)
    let audit = "pkg/acme/orders/audit/module"
    let orders = "pkg/acme/orders/domain/module"
    let sdk = "deps/morphir/_sdk/@/basics/module"
    let enumerated = ResizeArray<string * string option>()

    let recording =
        { source with
            NextModule =
                fun _ _ prefix after ->
                    enumerated.Add(prefix, after)

                    [ audit; orders; sdk ]
                    |> List.tryFind (fun logical ->
                        logical.StartsWith(prefix, System.StringComparison.Ordinal)
                        && (after
                            |> Option.forall (fun previous ->
                                System.String.CompareOrdinal(logical, previous) > 0
                            ))
                    )
                    |> Ok
        }

    recording, lookedUp, enumerated, advance

[<Tests>]
let modulePages =
    testList "Bounded module inventory" [
        test "A noncanonical direct module selection never reaches storage" {
            let source, lookedUp, _, _ = moduleSource "v4"
            let snapshot = read (BoundedTreeReader.openTree 1024L source)
            let badModule = ModulePath(Path [ Name [ ".." ]; Name [ "domain" ] ])

            let errors =
                BoundedTreeReader.members 1 None (Own(Module badModule)) snapshot
                |> StoreTestKit.refusal

            Expect.equal
                (errors
                 |> List.map (fun error -> error.Code))
                [ "tree.invalid_selection" ]
                "Only canonical typed selections are admitted"

            Expect.sequenceEqual
                lookedUp
                [ manifestLocation ]
                "A bad selection never reaches the file source"
        }

        for version, formatVersion in jsonVersions do
            test $"Pages private and public modules with a package-scoped cursor in {formatVersion}" {
                let source, lookedUp, enumerated, advance = moduleSource version
                let snapshot = read (BoundedTreeReader.openTree 1024L source)
                let package = PackageName(Path [ Name [ "acme" ]; Name [ "orders" ] ])
                let sdk = PackageName(Path [ Name [ "morphir" ]; Name [ "s"; "d"; "k" ] ])
                let first = read (BoundedTreeReader.modules 1 None package snapshot)
                Expect.hasLength first.Items 1 "The first page admits one module"

                let audit =
                    first.Items
                    |> List.exactlyOne

                Expect.equal audit.DocumentId "pkg/acme/orders/audit/module" "Ordinal first module"
                Expect.equal audit.Access (Some V4.Private) "Private visibility is retained"

                Expect.equal
                    audit.Role
                    BoundedTreeRole.Definition
                    "The own package holds definitions"

                let cursor = Expect.wantSome first.Next "Another module follows"

                Expect.sequenceEqual
                    lookedUp
                    [ manifestLocation; "pkg/acme/orders/audit/module.json" ]
                    "Lookahead does not open another module"

                let foreign =
                    BoundedTreeReader.modules 1 (Some cursor) sdk snapshot
                    |> StoreTestKit.refusal

                Expect.equal
                    (foreign
                     |> List.map (fun error -> error.Code))
                    [ "tree.invalid_cursor" ]
                    "A cursor cannot cross packages"

                let final = read (BoundedTreeReader.modules 1 (Some cursor) package snapshot)

                Expect.equal
                    (final.Items
                     |> List.map (fun item -> item.DocumentId))
                    [ "pkg/acme/orders/domain/module" ]
                    "The next page advances"

                Expect.isNone final.Next "No more modules"

                let dependencies = read (BoundedTreeReader.modules 1 None sdk snapshot)

                let basics =
                    dependencies.Items
                    |> List.exactlyOne

                Expect.equal
                    basics.DocumentId
                    "deps/morphir/_sdk/@/basics/module"
                    "The dependency path preserves the escaped SDK name"

                Expect.equal basics.Access None "A specification has no definition visibility"

                Expect.equal
                    basics.Role
                    BoundedTreeRole.Specification
                    "A library dependency holds specifications"

                Expect.sequenceEqual
                    lookedUp
                    [
                        manifestLocation
                        "pkg/acme/orders/audit/module.json"
                        moduleLocation
                        "deps/morphir/_sdk/@/basics/module.json"
                    ]
                    "Module pages do not open any definition file"

                Expect.equal enumerated.Count 6 "Each page keeps only one lookahead"

                advance ()

                let stale =
                    BoundedTreeReader.modules 1 (Some cursor) package snapshot
                    |> StoreTestKit.refusal

                Expect.equal
                    (stale
                     |> List.map (fun error -> error.Code))
                    [ "tree.revision_changed" ]
                    "A stale snapshot cannot resume"
            }

        test "Refuses escaped scope and unordered metadata before opening an extra file" {
            let package = PackageName(Path [ Name [ "acme" ]; Name [ "orders" ] ])

            for path in [ "pkg/acme/orders/../domain/module"; "deps/morphir/_sdk/@/basics/module" ] do
                let source, lookedUp, _, _ = moduleSource "v4"

                let unsafe =
                    { source with
                        NextModule = fun _ _ _ _ -> Ok(Some path)
                    }

                let snapshot = read (BoundedTreeReader.openTree 1024L unsafe)

                BoundedTreeReader.modules 1 None package snapshot
                |> expectReadError "tree.invalid_module_path" path

                Expect.sequenceEqual
                    lookedUp
                    [ manifestLocation ]
                    "A misplaced path never reaches file lookup"

            let source, lookedUp, _, _ = moduleSource "v4"
            let repeated = "pkg/acme/orders/audit/module"

            let unordered =
                { source with
                    NextModule = fun _ _ _ _ -> Ok(Some repeated)
                }

            let snapshot = read (BoundedTreeReader.openTree 1024L unordered)

            BoundedTreeReader.modules 1 None package snapshot
            |> expectReadError "tree.invalid_module_path" repeated

            Expect.sequenceEqual
                lookedUp
                [
                    manifestLocation
                    repeated
                    + ".json"
                ]
                "A repeated lookahead does not open a second file"
        }
    ]

[<Tests>]
let selectedReads =
    testList "Bounded selected document bodies" [
        for version in [ "v3.1"; "v4" ] do
            test $"Reads the final-page reference and its selected value in {version}" {
                let source, lookedUp, _ = ordersSource (Orders.selectedValues version)
                let snapshot = read (BoundedTreeReader.openTree 1024L source)
                let selection = Own(Module domain)
                let first = read (BoundedTreeReader.members 2 None selection snapshot)
                let cursor = Expect.wantSome first.Next "The generator has another page to inspect"
                let finalPage = read (BoundedTreeReader.members 2 (Some cursor) selection snapshot)

                let final =
                    finalPage.Items
                    |> List.exactlyOne

                let order = first.Items[1]

                let finalBody = read (BoundedTreeReader.readMember final.Address snapshot)
                let orderBody = read (BoundedTreeReader.readMember order.Address snapshot)
                verifyOrdersBodies finalBody orderBody

                Expect.sequenceEqual
                    lookedUp
                    [
                        manifestLocation
                        moduleLocation
                        moduleLocation
                        moduleLocation
                        totalLocation
                        moduleLocation
                        orderLocation
                    ]
                    "The source opens only metadata and the two selected values"
            }

            test $"Checks selected membership and file identity in {version}" {
                let source, lookedUp, _ = ordersSource (Orders.selectedValueWithWrongName version)
                let snapshot = read (BoundedTreeReader.openTree 1024L source)
                let unlisted = Own(ValueDef(domain, Name [ "unlisted" ]))

                BoundedTreeReader.readMember unlisted snapshot
                |> expectReadError "tree.missing_member" moduleLocation

                BoundedTreeReader.readMember orderAddress snapshot
                |> expectReadError "tree.name_mismatch" orderLocation

                Expect.sequenceEqual
                    lookedUp
                    [ manifestLocation; moduleLocation; moduleLocation; orderLocation ]
                    "An unlisted member does not trigger a file lookup"
            }

            test
                $"Reads an alternately spelled inline name without decoding its malformed sibling in {version}" {
                let source, lookedUp, _ =
                    ordersSource (Orders.alternateInlineValueWithMalformedSibling version)

                let snapshot = read (BoundedTreeReader.openTree 1024L source)
                let selected = Own(ValueDef(domain, Name [ "a"; "b" ]))
                let body = read (BoundedTreeReader.readMember selected snapshot)

                match body with
                | BoundedTreeBody.ClassicValue(ClassicValueDocument.DefinedValue value) ->
                    let definition = Classic.Documented.value value.Value

                    match definition.Body with
                    | Classic.Literal(attributes, Classic.WholeNumberLiteral 73L) ->
                        Expect.equal
                            (attributes[0].GetString())
                            "Reference"
                            "The selected classic attributes survive"
                    | _ -> failtest "Expected an inline classic integer"
                | BoundedTreeBody.V4Value(DefinedValue value) ->
                    match value.Value.Value.Body with
                    | V4.ExpressionBody(_, V4.Value.Literal(_, V4.IntegerLiteral integer)) ->
                        Expect.equal integer (bigint 73) "The selected inline integer"
                    | _ -> failtest "Expected an inline v4 integer"
                | _ -> failtest "Expected a value definition"

                Expect.sequenceEqual
                    lookedUp
                    [ manifestLocation; moduleLocation ]
                    "An inline read opens only its containing module"
            }

            test $"Enforces the byte cap on the selected body in {version}" {
                let source, _, _ = ordersSource (Orders.oversizedSelectedValue version 1025)
                let snapshot = read (BoundedTreeReader.openTree 1024L source)

                BoundedTreeReader.readMember orderAddress snapshot
                |> expectReadError "tree.file_too_large" orderLocation
            }
    ]

let private frozenOrdersFiles version profile =
    let opened = ResizeArray<string>()
    let documents = Orders.catalogDocuments version profile

    let files =
        documents
        |> Map.toSeq
        |> Seq.map fst
        |> Set.ofSeq


    let modules =
        files
        |> Seq.filter (fun location ->
            location.EndsWith("/module.json")
            || location.EndsWith("/module.yaml")
        )
        |> Seq.map (fun location -> location.Substring(0, location.LastIndexOf('.')))
        |> Seq.sortWith (fun left right -> System.String.CompareOrdinal(left, right))
        |> List.ofSeq

    let source: TreeFileSource.SnapshotSource = {
        Revision = fun () -> "3f3157318"
        ContainsFile = fun location -> Set.contains location files
        NextModule =
            fun _ _ prefix after ->
                modules
                |> List.tryFind (fun path ->
                    path.StartsWith(prefix, System.StringComparison.Ordinal)
                    && (after
                        |> Option.forall (fun previous ->
                            System.String.CompareOrdinal(path, previous) > 0
                        ))
                )
                |> Ok
        FindFile =
            fun location ->
                if Set.contains location files then
                    let stream =
                        StreamSource(fun () ->
                            opened.Add location

                            new MemoryStream(FixtureCatalog.ReadBytes(documents[location]), false)
                            :> Stream
                        )

                    Some(VFile.ofStream location stream)
                else
                    None
    }

    source, opened, files

let private frozenOrders version profile =
    let source, opened, _ = frozenOrdersFiles version profile
    source, opened

// Native observations are compared with the independently authored parent oracle.
// This is an in-process unit test, with embedded files only and no kit runner.
[<Tests>]
let observationTests =
    testList "Bounded reader observations" [
        for version in [ "v3.1"; "v4" ] do
            for profile in [ "plain-json"; "plain-yaml"; "contextual-json"; "contextual-yaml" ] do
                test $"Observes parent facts for {version} {profile}" {
                    use expectedFile = JsonDocument.Parse(FixtureCatalog.ReadText FixtureCatalog.BoundedOrders.Expected)
                    let expected = expectedFile.RootElement
                    let source, _ = frozenOrders version profile
                    let encoding =
                        if profile.StartsWith "contextual-" then BoundedTreeEncoding.ContextualDraft1
                        else BoundedTreeEncoding.Plain
                    let snapshot = read (BoundedTreeReader.openTreeWithEncoding encoding 8192L source)
                    let inventory = expected.GetProperty "domainInventory"
                    let selection = "pkg/acme/orders/domain/module"
                    let actual =
                        read (Morphir.BoundedGeneration.ReaderObservation.observe selection (inventory.GetProperty("pageSize").GetInt32()) 8192L snapshot)
                    let equalJson actual expected message =
                        Expect.isTrue (JsonElement.DeepEquals(actual, expected)) message
                    for field in [ "distribution"; "package"; "pathBudget"; "dependencies"; "modules" ] do
                        equalJson (actual.GetProperty field) (expected.GetProperty field) field
                    equalJson (actual.GetProperty "pages") (inventory.GetProperty "pages") "Production cursor order"
                    equalJson (actual.GetProperty "finalPageBody") (inventory.GetProperty "finalPageBody") "Typed selected reference and observed target literal"
                    Expect.equal (actual.GetProperty("scope").GetString()) "native-reader" "Explicit native scope"
                    Expect.equal (actual.GetProperty("revision").GetString()) snapshot.RevisionId "Caller snapshot"
                    if version = "v3.1" then
                        let documents = actual.GetProperty "classicDocuments"
                        for attribute in expected.GetProperty("classicAttributes").EnumerateArray() do
                            let pointer = attribute.GetProperty("pointer").GetString() |> nonNull
                            let mutable value = documents.GetProperty(attribute.GetProperty("document").GetString() |> nonNull)
                            for token in pointer.Substring(1).Split('/') do
                                value <-
                                    if value.ValueKind = JsonValueKind.Array then value[int token]
                                    else value.GetProperty(token.Replace("~1", "/").Replace("~0", "~"))
                            equalJson value (attribute.GetProperty "value") pointer
                }
        test "Observation retains a stale snapshot refusal" {
            let source, _, advance = ordersSource (Orders.selectedValues "v4")
            let snapshot = read (BoundedTreeReader.openTree 8192L source)
            advance ()
            let errors =
                Morphir.BoundedGeneration.ReaderObservation.observe "pkg/acme/orders/domain/module" 2 8192L snapshot
                |> StoreTestKit.refusal
            Expect.equal (errors |> List.map _.Code) [ "tree.revision_changed" ] "No synthetic observations"
        }
        test "Observation uses the caller selection" {
            let source, _ = frozenOrders "v4" "plain-json"
            let snapshot = read (BoundedTreeReader.openTree 8192L source)
            let errors =
                Morphir.BoundedGeneration.ReaderObservation.observe "deps/morphir/_sdk/@/basics/module" 2 8192L snapshot
                |> StoreTestKit.refusal
            Expect.equal (errors |> List.map _.Code) [ "observation.unsupported_selection" ] "A specification is not a value definition"
        }
    ]

[<Tests>]
let profileTests =
    testList "Frozen orders profiles" [
        for version in [ "v3.1"; "v4" ] do
            for profile in [ "plain-json"; "plain-yaml"; "contextual-json"; "contextual-yaml" ] do
                test $"Pages and reads the frozen {version} {profile} tree" {
                    let source, opened = frozenOrders version profile

                    let encoding =
                        if profile.StartsWith "contextual-" then
                            BoundedTreeEncoding.ContextualDraft1
                        else
                            BoundedTreeEncoding.Plain

                    if encoding = BoundedTreeEncoding.ContextualDraft1 then
                        let manifest =
                            if profile.EndsWith "-yaml" then
                                "manifest.yaml"
                            else
                                "manifest.json"

                        BoundedTreeReader.openTree 1024L source
                        |> expectReadError "tree.unsupported_context" manifest

                    let snapshot =
                        read (BoundedTreeReader.openTreeWithEncoding encoding 1024L source)

                    let own =
                        read (CanonicalNames.parsePath "acme/orders")
                        |> PackageName

                    let sdk =
                        read (CanonicalNames.parsePath "morphir/SDK")
                        |> PackageName

                    let modules = read (BoundedTreeReader.modules 2 None own snapshot)

                    Expect.sequenceEqual
                        (modules.Items
                         |> Seq.map (fun item -> item.DocumentId))
                        [ "pkg/acme/orders/audit/module"; "pkg/acme/orders/domain/module" ]
                        "Own modules in order"

                    Expect.equal
                        modules.Items.Head.Access
                        (Some V4.Private)
                        "The audit module is private"

                    let basics =
                        (read (BoundedTreeReader.modules 2 None sdk snapshot)).Items
                        |> List.exactlyOne

                    Expect.equal
                        basics.Role
                        BoundedTreeRole.Specification
                        "The SDK module holds specifications"

                    let first =
                        read (BoundedTreeReader.members 2 None (Own(Module domain)) snapshot)

                    let cursor = Expect.wantSome first.Next "The final page is present"

                    let finalPage =
                        read (
                            BoundedTreeReader.members 2 (Some cursor) (Own(Module domain)) snapshot
                        )

                    let final =
                        finalPage.Items
                        |> List.exactlyOne

                    Expect.equal
                        final.DocumentId
                        "pkg/acme/orders/domain/total-in-_usd.value"
                        "Final escaped document"

                    Expect.isTrue
                        (opened
                         |> Seq.forall (fun path ->
                             not (
                                 path.Contains(".type.")
                                 || path.Contains(".value.")
                             )
                         ))
                        "Discovery did not open any split definition"

                    if encoding = BoundedTreeEncoding.ContextualDraft1 then
                        let path, expected =
                            if version = "v3.1" then
                                [| "value"; "value"; "body"; "2" |],
                                """[[["acme"],["orders"]],[["domain"]],["order"]]"""
                            else
                                [| "Public"; "ExpressionBody"; "body"; "Reference" |],
                                "\"acme/orders:domain#order\""

                        let alias =
                            read (DocumentTreeStore.resolveMemberAlias final.Address path snapshot)

                        Expect.equal
                            (alias.GetRawText())
                            expected
                            "One selected FQName keeps its version-specific spelling"

                    let selected = read (BoundedTreeReader.readMember final.Address snapshot)
                    let order = read (BoundedTreeReader.readMember orderAddress snapshot)
                    verifyOrdersBodies selected order

                    Expect.isFalse
                        (opened
                         |> Seq.exists (fun path -> path.Contains("order.type.")))
                        "The same-named type stays unopened"
                }

        test "Context-last JSON is accepted in general mode and refused in streaming mode" {
            let source, _, _ = ordersSource (Orders.manifestWithContextLast ())

            let result =
                read (
                    BoundedTreeReader.openTreeWithEncoding
                        BoundedTreeEncoding.ContextualDraft1
                        1024L
                        source
                )

            Expect.equal
                result.Package
                (PackageName(read (CanonicalNames.parsePath "acme/orders")))
                "Bounded context-last manifest"

            BoundedTreeReader.openTreeWithEncoding
                BoundedTreeEncoding.ContextualDraft1Streaming
                1024L
                source
            |> expectReadError "context.after_document" manifestLocation
        }

        test "A YAML context duplicate is refused even when the schema marker follows it" {
            let source, _, _ = ordersSource (Orders.yamlManifestWithDuplicateContextEntry ())

            BoundedTreeReader.openTreeWithEncoding BoundedTreeEncoding.ContextualDraft1 1024L source
            |> expectReadError "context.duplicate_entry" "manifest.yaml"
        }

        test "Contextual metadata does not resolve an unselected inline alias" {
            let source, _, _ = ordersSource (Orders.contextualInlineWithUnknownSiblingAlias ())

            let snapshot =
                read (
                    BoundedTreeReader.openTreeWithEncoding
                        BoundedTreeEncoding.ContextualDraft1
                        1024L
                        source
                )

            let listed = read (BoundedTreeReader.members 2 None (Own(Module domain)) snapshot)
            Expect.equal listed.Items.Length 2 "The incomplete payload was not visited"

            BoundedTreeReader.readMember orderAddress snapshot
            |> read
            |> ignore

            let broken = Own(ValueDef(domain, Name [ "broken" ]))

            let output =
                read (
                    DocumentTreeStore.resolveMemberAlias
                        broken
                        [| "Public"; "ExpressionBody"; "outputType" |]
                        snapshot
                )

            Expect.equal
                (output.GetString())
                "morphir/SDK:basics#int"
                "Resolving a slot ignores another typed alias in that body"

            BoundedTreeReader.readMember broken snapshot
            |> expectReadError "context.unknown_alias" moduleLocation
        }

        test "Selected contextual originals and literal lookalikes are preserved" {
            let files, original = Orders.contextualOrderWithLiteralAndOptionalPrefixes ()

            let openTree files =
                let provider = VFileProviders.asFileProvider files

                read (
                    DocumentTreeStore.openSnapshotWithEncoding
                        ReadOptions.defaults
                        BoundedTreeEncoding.ContextualDraft1
                        (fun () -> "immutable")
                        provider
                )

            let selection =
                read (DocumentTreeStore.readMemberWithArtifact orderAddress (openTree files))

            Expect.equal selection.File orderLocation "The physical source is identified"

            Expect.equal
                (System.Text.Encoding.UTF8.GetString(selection.Encoded.Span))
                original
                "Unknown optional data remains in the original bytes"

            match selection.Body with
            | BoundedTreeBody.V4Value(V4ValueDocument.DefinedValue value) ->
                match value.Value.Value.Body with
                | V4.ExpressionBody(_,
                                    V4.Value.Literal(_,
                                                     V4.DocumentLiteral(V4.DocumentObject members))) ->
                    Expect.equal
                        (V4.OrderedMap.toList members)
                        [
                            "ratio", V4.DocumentNumber "1.0"
                            "$name", V4.DocumentString "i"
                            "id", V4.DocumentNumber "9007199254740993"
                        ]
                        "Document literals keep their order, numbers and alias lookalikes"
                | _ -> failtest "Expected a DocumentLiteral expression"
            | _ -> failtest "Expected a v4 value definition"

            let critical = openTree (Orders.contextualOrderWithUnknownCriticalPrefixes ())

            DocumentTreeStore.readMemberWithArtifact orderAddress critical
            |> expectReadError "context.unknown_critical" orderLocation

            DocumentTreeStore.resolveMemberAlias
                orderAddress
                [| "Public"; "ExpressionBody"; "body"; "Literal"; "DocumentLiteral"; "$name" |]
                (openTree files)
            |> expectReadError "context.not_an_alias" orderLocation
        }

        test "Expanded names cannot exceed the document byte budget" {
            let files, selectedBytes = Orders.contextualOrderWithExpandingAliases ()
            Expect.isLessThan selectedBytes 1024 "The encoded file fits the byte cap"
            let source, _, _ = ordersSource files

            let snapshot =
                read (
                    BoundedTreeReader.openTreeWithEncoding
                        BoundedTreeEncoding.ContextualDraft1
                        1024L
                        source
                )

            BoundedTreeReader.readMember orderAddress snapshot
            |> expectReadError "tree.file_too_large" orderLocation
        }

        test "A type cannot resolve aliases using the value grammar" {
            let source, _, _ = ordersSource (Orders.contextualTypeFileWithValuePayload ())

            let snapshot =
                read (
                    BoundedTreeReader.openTreeWithEncoding
                        BoundedTreeEncoding.ContextualDraft1
                        1024L
                        source
                )

            let address = Own(TypeDef(domain, Name [ "order" ]))

            DocumentTreeStore.resolveMemberAlias
                address
                [| "Public"; "ExpressionBody"; "outputType" |]
                snapshot
            |> expectReadError "context.not_an_alias" Orders.orderTypeLocation
        }

        test "A context refuses invalid Unicode at the table entry or key" {
            let cases = [
                Orders.manifestWithInvalidContextSpelling (), "context.names.n"
                Orders.manifestWithInvalidContextKey (), "context.names"
            ]

            for files, location in cases do
                let source, _, _ = ordersSource files

                let error =
                    BoundedTreeReader.openTreeWithEncoding
                        BoundedTreeEncoding.ContextualDraft1
                        1024L
                        source
                    |> StoreTestKit.refusal
                    |> List.exactlyOne

                Expect.equal
                    error.Code
                    "context.invalid_entry"
                    "Malformed Unicode is a context refusal"

                Expect.stringContains
                    error.Message
                    location
                    "The refusal names the malformed context location"
        }

        test "JSON and YAML both refuse tables above their entry and spelling limits" {
            let verify expected (files, location) =
                let source, _, _ = ordersSource files

                BoundedTreeReader.openTreeWithEncoding
                    BoundedTreeEncoding.ContextualDraft1
                    524288L
                    source
                |> expectReadError expected location

            Orders.manifestsWithOversizedContextSpelling ()
            |> List.iter (verify "context.name_too_long")

            Orders.manifestsWithTooManyContextEntries ()
            |> List.iter (verify "context.too_many_entries")
        }

        test "Refuses ambiguous manifests before opening either file" {
            let source, opened = frozenOrders "v4" "plain-yaml"

            let ambiguous =
                { source with
                    ContainsFile =
                        fun path ->
                            path = "manifest.json"
                            || source.ContainsFile path
                }

            BoundedTreeReader.openTree 1024L ambiguous
            |> expectReadError "tree.ambiguous_profile" "manifest"

            Expect.isEmpty opened "Neither manifest is opened"
        }

        test "Refuses ambiguous selected YAML paths before opening a body" {
            let source, opened = frozenOrders "v4" "plain-yaml"
            let alias = "pkg/acme/orders/domain/order.value.yml"

            let ambiguous =
                { source with
                    ContainsFile =
                        fun path ->
                            path = alias
                            || source.ContainsFile path
                }

            let snapshot = read (BoundedTreeReader.openTree 1024L ambiguous)

            BoundedTreeReader.readMember orderAddress snapshot
            |> expectReadError "tree.ambiguous_profile" "pkg/acme/orders/domain/order.value"

            Expect.sequenceEqual
                opened
                [ "manifest.yaml"; "pkg/acme/orders/domain/module.yaml" ]
                "Neither body is opened"
        }

        test "Refuses a selected file in a different profile before opening it" {
            let source, opened = frozenOrders "v4" "plain-yaml"
            let yaml = "pkg/acme/orders/domain/order.value.yaml"

            let mixed =
                { source with
                    ContainsFile =
                        fun path ->
                            if path = orderLocation then true
                            elif path = yaml then false
                            else source.ContainsFile path
                }

            let snapshot = read (BoundedTreeReader.openTree 1024L mixed)

            BoundedTreeReader.readMember orderAddress snapshot
            |> expectReadError "tree.unsupported_profile" orderLocation

            Expect.sequenceEqual
                opened
                [ "manifest.yaml"; "pkg/acme/orders/domain/module.yaml" ]
                "The mixed-profile body is not opened"
        }
    ]

[<Tests>]
let logicalDocumentTests =
    testList "Bounded logical document delivery" [
        for version in [ "v3.1"; "v4" ] do
            for profile in [ "plain-json"; "plain-yaml"; "contextual-json"; "contextual-yaml" ] do
                test
                    $"Delivers original manifest, module and selected body bytes in {version} {profile}" {
                    let source, opened = frozenOrders version profile
                    let files = Orders.catalogDocuments version profile
                    let extension = if profile.EndsWith("-yaml") then ".yaml" else ".json"

                    let encoding =
                        if profile.StartsWith("contextual-") then
                            BoundedTreeEncoding.ContextualDraft1
                        else
                            BoundedTreeEncoding.Plain

                    let snapshot =
                        read (BoundedTreeReader.openTreeWithEncoding encoding 8192L source)

                    opened.Clear()

                    let expectDocument id =
                        let original =
                            FixtureCatalog.ReadBytes(
                                files[id
                                      + extension]
                            )

                        let bytes = read (DocumentTreeStore.readDocument id snapshot)

                        Expect.sequenceEqual
                            (bytes.ToArray())
                            original
                            "Return the unchanged encoded document"

                    expectDocument "manifest"
                    expectDocument "pkg/acme/orders/domain/module"
                    expectDocument "pkg/acme/orders/domain/total-in-_usd.value"
                    expectDocument "deps/morphir/_sdk/@/basics/int.type"

                    Expect.sequenceEqual
                        opened
                        ([
                            "manifest"
                            "pkg/acme/orders/domain/module"
                            "pkg/acme/orders/domain/module"
                            "pkg/acme/orders/domain/total-in-_usd.value"
                            "deps/morphir/_sdk/@/basics/module"
                            "deps/morphir/_sdk/@/basics/int.type"
                         ]
                         |> List.map (fun id ->
                             id
                             + extension
                         ))
                        "No unrelated definition opens"
                }

        test "Rejects physical and out-of-scope IDs before looking up files" {
            let source, opened = frozenOrders "v4" "plain-json"
            let snapshot = read (BoundedTreeReader.openTree 8192L source)
            opened.Clear()

            for invalid in
                [
                    "../manifest"
                    "manifest.json"
                    "/pkg/acme/orders/domain/order.value"
                    "pkg/acme/orders/domain/order.value.json"
                    "pkg/acme/orders/../domain/order.value"
                    "pkg/acme/orders/domain/unknown.spec"
                    "deps/foreign/sdk/@/basics/int.type"
                    "deps/morphir/_sdk/@v4/basics/int.type"
                ] do
                let errors =
                    DocumentTreeStore.readDocument invalid snapshot
                    |> StoreTestKit.refusal

                Expect.equal
                    (StoreTestKit.codesAndFiles errors
                     |> List.map fst)
                    [ "tree.invalid_selection" ]
                    invalid

            Expect.isEmpty opened "Forged logical IDs never open a file"
        }

        test "An inline member can only be fetched through its advertised module ID" {
            let source, lookedUp, _ =
                ordersSource (Orders.contextualInlineWithUnknownSiblingAlias ())

            let snapshot =
                read (
                    BoundedTreeReader.openTreeWithEncoding
                        BoundedTreeEncoding.ContextualDraft1
                        8192L
                        source
                )

            lookedUp.Clear()

            let error =
                DocumentTreeStore.readDocument "pkg/acme/orders/domain/order.value" snapshot
                |> StoreTestKit.refusal
                |> StoreTestKit.codesAndFiles

            Expect.equal
                error
                [ "tree.invalid_selection", None ]
                "No synthetic member document is delivered"

            let bytes =
                read (DocumentTreeStore.readDocument "pkg/acme/orders/domain/module" snapshot)

            Expect.isGreaterThan bytes.Length 0 "Deliver the containing module's original bytes"
            Expect.sequenceEqual lookedUp [ moduleLocation; moduleLocation ] "No sidecar is opened"
        }

        test "A selected split document must agree with its requested member" {
            let source, _, _ = ordersSource (Orders.selectedValueWithWrongName "v4")
            let snapshot = read (BoundedTreeReader.openTree 8192L source)

            DocumentTreeStore.readDocument "pkg/acme/orders/domain/order.value" snapshot
            |> expectReadError "tree.name_mismatch" orderLocation
        }

        test "Rejects a logical read after the admitted revision changes" {
            let source, lookedUp, advance = ordersSource (Orders.selectedValues "v4")
            let snapshot = read (BoundedTreeReader.openTree 8192L source)
            lookedUp.Clear()
            advance ()

            let result =
                DocumentTreeStore.readDocument "pkg/acme/orders/domain/order.value" snapshot

            let errors = StoreTestKit.refusal result

            Expect.equal
                (StoreTestKit.codesAndFiles errors
                 |> List.map fst)
                [ "tree.revision_changed" ]
                "No read on stale revision"

            Expect.isEmpty lookedUp "The stale snapshot did not reach storage"
        }
    ]

[<Tests>]
let logicalInventoryTests =
    let moduleIds (page: BoundedTreeInventoryPage) =
        page.Entries
        |> List.map (
            function
            | BoundedTreeInventoryEntry.ModuleEntry item -> item.DocumentId, item.Role
            | _ -> failtest "Root must contain modules"
        )

    let memberIds (page: BoundedTreeInventoryPage) =
        page.Entries
        |> List.map (
            function
            | BoundedTreeInventoryEntry.MemberEntry(item, role) -> item.DocumentId, role
            | _ -> failtest "A member page cannot contain modules"
        )

    testList "Bounded logical inventory" [
        test "Pages own modules and dependency modules without reading bodies" {
            let source, opened = frozenOrders "v4" "contextual-yaml"

            let snapshot =
                read (
                    BoundedTreeReader.openTreeWithEncoding
                        BoundedTreeEncoding.ContextualDraft1
                        8192L
                        source
                )

            opened.Clear()

            let first = read (DocumentTreeStore.pageInventory "root" 1 None snapshot)

            Expect.equal
                (moduleIds first)
                [ "pkg/acme/orders/audit/module", BoundedTreeRole.Definition ]
                "First module"

            Expect.sequenceEqual
                opened
                [ "pkg/acme/orders/audit/module.yaml" ]
                "The first page reads only its module"

            let rest =
                read (DocumentTreeStore.pageInventory "root" 2 first.Continuation snapshot)

            Expect.equal
                (moduleIds rest)
                [
                    "pkg/acme/orders/domain/module", BoundedTreeRole.Definition
                    "deps/morphir/_sdk/@/basics/module", BoundedTreeRole.Specification
                ]
                "Continue through declared packages"

            Expect.isNone rest.Continuation "The root listing is exhausted"

            let domain = "pkg/acme/orders/domain/module"
            let members = read (DocumentTreeStore.pageInventory domain 2 None snapshot)

            Expect.equal
                (memberIds members)
                [
                    "pkg/acme/orders/domain/order.type", BoundedTreeRole.Definition
                    "pkg/acme/orders/domain/order.value", BoundedTreeRole.Definition
                ]
                "Type and value have distinct identities"

            let tail =
                read (DocumentTreeStore.pageInventory domain 2 members.Continuation snapshot)

            Expect.equal
                (memberIds tail)
                [ "pkg/acme/orders/domain/total-in-_usd.value", BoundedTreeRole.Definition ]
                "The final page preserves the escaped logical ID"

            let sdk =
                read (
                    DocumentTreeStore.pageInventory
                        "deps/morphir/_sdk/@/basics/module"
                        2
                        None
                        snapshot
                )

            Expect.equal
                (memberIds sdk)
                [
                    "deps/morphir/_sdk/@/basics/int.type", BoundedTreeRole.Specification
                    "deps/morphir/_sdk/@/basics/add.value", BoundedTreeRole.Specification
                ]
                "The dependency role reaches its members"

            Expect.isTrue
                (Seq.forall (fun (f: string) -> f.EndsWith("/module.yaml")) opened)
                "Inventory opens no definition file"
        }

        test "A continuation is bound to the originating snapshot and selection" {
            let source, opened = frozenOrders "v4" "plain-json"
            let snapshot = read (BoundedTreeReader.openTree 8192L source)
            let other = read (BoundedTreeReader.openTree 8192L source)
            let id = "pkg/acme/orders/domain/module"
            let first = read (DocumentTreeStore.pageInventory id 1 None snapshot)

            let token =
                first.Continuation
                |> Option.defaultWith (fun () -> failtest "Expected a continuation")

            opened.Clear()

            let expectInvalid selection view cursor =
                let result = DocumentTreeStore.pageInventory selection 1 (Some cursor) view

                let errors =
                    result
                    |> StoreTestKit.refusal
                    |> StoreTestKit.codesAndFiles

                Expect.equal
                    errors
                    [ "tree.invalid_cursor", None ]
                    "Refuse forged or foreign continuations"

            expectInvalid "root" snapshot token
            expectInvalid id other token

            expectInvalid
                id
                snapshot
                ("x"
                 + token)

            Expect.isEmpty opened "Invalid continuations never open a module"

            let resumed = read (DocumentTreeStore.pageInventory id 1 (Some token) snapshot)

            Expect.equal
                (memberIds resumed)
                [ "pkg/acme/orders/domain/order.value", BoundedTreeRole.Definition ]
                "The correct selection can resume"
        }
    ]

[<Tests>]
let providerTests =
    testList "Bounded file provider entry point" [
        for version in [ "v3.1"; "v4" ] do
            for profile in [ "plain-json"; "plain-yaml" ] do
                test $"Pages {version} {profile} through a provider" {
                    let source, opened, files = frozenOrdersFiles version profile

                    let provider =
                        files
                        |> Seq.map (fun path ->
                            path,
                            source.FindFile path
                            |> Option.get
                        )
                        |> Map.ofSeq
                        |> VFileTree.fromFileMap
                        |> VFileProviders.asFileProvider

                    let suffix = if profile = "plain-json" then ".json" else ".yaml"
                    let mutable revision = "first"

                    let snapshot =
                        DocumentTreeStore.openSnapshot
                            ReadOptions.defaults
                            (fun () -> revision)
                            provider
                        |> read

                    Expect.sequenceEqual
                        opened
                        [
                            "manifest"
                            + suffix
                        ]
                        "Only the manifest opens at admission"

                    let own = PackageName(read (CanonicalNames.parsePath "acme/orders"))
                    let sdk = PackageName(read (CanonicalNames.parsePath "morphir/SDK"))
                    Expect.equal snapshot.Package own "Manifest package"
                    Expect.equal snapshot.Dependencies [ sdk ] "Manifest dependencies"
                    let first = read (DocumentTreeStore.pageModules 1 None own snapshot)

                    Expect.equal
                        first.Items.Head.DocumentId
                        "pkg/acme/orders/audit/module"
                        "Ordinal order"

                    Expect.equal
                        first.Items.Head.Access
                        (Some V4.Private)
                        "Private modules are visible"

                    let second = read (DocumentTreeStore.pageModules 1 first.Next own snapshot)

                    Expect.equal
                        second.Items.Head.DocumentId
                        "pkg/acme/orders/domain/module"
                        "The next module"

                    Expect.isNone second.Next "No further modules"

                    let dependency = read (DocumentTreeStore.pageModules 1 None sdk snapshot)

                    Expect.equal
                        dependency.Items.Head.Role
                        BoundedTreeRole.Specification
                        "SDK metadata is a spec"

                    let address = second.Items.Head.Address
                    let members = read (DocumentTreeStore.pageMembers 2 None address snapshot)
                    let last = read (DocumentTreeStore.pageMembers 2 members.Next address snapshot)
                    let selected = last.Items.Head

                    Expect.equal
                        selected.DocumentId
                        "pkg/acme/orders/domain/total-in-_usd.value"
                        "The final member"

                    Expect.isFalse
                        (opened
                         |> Seq.exists (fun path ->
                             path.Contains(".type.")
                             || path.Contains(".value.")
                         ))
                        "Discovery never opens split bodies"

                    let body = read (DocumentTreeStore.readMember selected.Address snapshot)

                    match version, body with
                    | "v3.1", BoundedTreeBody.ClassicValue _
                    | "v4", BoundedTreeBody.V4Value _ -> ()
                    | _ -> failtest "Expected the version-specific value"

                    Expect.equal
                        (Seq.last opened)
                        (selected.DocumentId
                         + suffix)
                        "Only the selected body opens"


                    let orderType =
                        read (DocumentTreeStore.readMember members.Items.Head.Address snapshot)

                    let sdkMembers =
                        read (
                            DocumentTreeStore.pageMembers
                                2
                                None
                                dependency.Items.Head.Address
                                snapshot
                        )

                    match sdkMembers.Items with
                    | [ intSpec; addSpec ] ->
                        let sdkType = read (DocumentTreeStore.readMember intSpec.Address snapshot)
                        let sdkValue = read (DocumentTreeStore.readMember addSpec.Address snapshot)
                        verifyOrdersTypeAndSpecifications orderType sdkType sdkValue
                    | _ -> failtest "Expected the two frozen SDK entries"

                    revision <- "second"

                    DocumentTreeStore.pageModules 1 first.Next own snapshot
                    |> StoreTestKit.refusal
                    |> StoreTestKit.codesAndFiles
                    |> fun errors ->
                        Expect.equal errors [ "tree.revision_changed", None ] "The cursor is stale"
                }

        test "Refuses a module scan over the metadata limit before opening a module" {
            let source, opened, files = frozenOrdersFiles "v4" "plain-json"

            let provider =
                files
                |> Seq.map (fun path ->
                    path,
                    source.FindFile path
                    |> Option.get
                )
                |> Map.ofSeq
                |> VFileTree.fromFileMap
                |> VFileProviders.asFileProvider

            let limits =
                { ReadOptions.defaults with
                    MaxFiles = 1
                }

            let snapshot = read (DocumentTreeStore.openSnapshot limits source.Revision provider)

            DocumentTreeStore.pageModules 1 None snapshot.Package snapshot
            |> expectReadError "tree.too_many_files" "pkg/acme/orders/"

            Expect.sequenceEqual opened [ "manifest.json" ] "Directory scans open no file content"
        }

        test "Uses the physical YAML extension for module path admission" {
            let moduleName = String.replicate 42 "a"

            let stem =
                "pkg/acme/orders/"
                + moduleName
                + "/module"

            let budget =
                (stem
                 + ".yml")
                    .Length

            let manifest =
                """{"formatVersion":4,"distribution":"Library","package":"acme/orders","pathBudget":BUDGET}"""
                    .Replace("BUDGET", string budget)

            let moduleText =
                """{"formatVersion":4,"path":"MODULE"}""".Replace("MODULE", moduleName)

            for suffix in [ ".yml"; ".yaml" ] do
                let physicalPath =
                    stem
                    + suffix

                let provider =
                    Map.ofList [
                        "manifest.yaml", VFile.ofText "manifest.yaml" manifest
                        physicalPath, VFile.ofText physicalPath moduleText
                    ]
                    |> VFileTree.fromFileMap
                    |> VFileProviders.asFileProvider

                let snapshot =
                    read (
                        DocumentTreeStore.openSnapshot
                            ReadOptions.defaults
                            (fun () -> "stable")
                            provider
                    )

                let page = DocumentTreeStore.pageModules 1 None snapshot.Package snapshot

                if suffix = ".yml" then
                    let selected = (read page).Items.Head
                    Expect.equal selected.DocumentId stem "The exact-budget module is listed"

                    read (DocumentTreeStore.pageMembers 1 None selected.Address snapshot)
                    |> ignore
                else
                    page
                    |> expectReadError "tree.over_budget" physicalPath
        }

        test "Refuses duplicate JSON object members in reserved metadata" {
            let manifest =
                """{"formatVersion":4,"distribution":"Library","package":"acme/orders","$meta":{"x":1,"x":2}}"""

            let provider =
                Map.ofList [ manifestLocation, VFile.ofText manifestLocation manifest ]
                |> VFileTree.fromFileMap
                |> VFileProviders.asFileProvider

            DocumentTreeStore.openSnapshot ReadOptions.defaults (fun () -> "stable") provider
            |> expectReadError "duplicate_member" manifestLocation
        }

        test "Refuses a known oversize source before opening the stream" {
            let mutable opens = 0

            let source =
                StreamSource(
                    (fun () ->
                        opens <- opens + 1
                        new MemoryStream(Array.zeroCreate<byte> 33) :> Stream
                    ),
                    length = 33L
                )

            let limits =
                { ReadOptions.defaults with
                    MaxFileBytes = 32L
                }

            let file = VFile.ofStream manifestLocation source

            let provider =
                Map.ofList [ manifestLocation, file ]
                |> VFileTree.fromFileMap
                |> VFileProviders.asFileProvider

            DocumentTreeStore.openSnapshot limits (fun () -> "stable") provider
            |> expectReadError "tree.file_too_large" manifestLocation

            Expect.equal opens 0 "The provider does not open known oversized content"

            let direct: TreeFileSource.SnapshotSource = {
                Revision = fun () -> "stable"
                ContainsFile = fun path -> path = manifestLocation
                FindFile = fun _ -> Some file
                NextModule = fun _ _ _ _ -> Ok None
            }

            BoundedTreeReader.openTree limits.MaxFileBytes direct
            |> expectReadError "tree.file_too_large" manifestLocation

            Expect.equal opens 0 "The virtual file does not open known oversized content"
        }

        test "Refuses entry points in a Library and absent entry points in an Application" {
            let manifests = [
                """{"formatVersion":4,"distribution":"Application","package":"acme/orders","pathBudget":256}""",
                "tree.missing_entry_points"
                """{"formatVersion":4,"distribution":"Library","package":"acme/orders","pathBudget":256,"entryPoints":{"startup":{"target":"acme/orders:domain#order","kind":"main"}}}""",
                "tree.misplaced_entry_points"
            ]

            for manifest, code in manifests do
                let provider =
                    Map.ofList [ manifestLocation, VFile.ofText manifestLocation manifest ]
                    |> VFileTree.fromFileMap
                    |> VFileProviders.asFileProvider

                DocumentTreeStore.openSnapshot ReadOptions.defaults (fun () -> "stable") provider
                |> expectReadError code manifestLocation
        }

        test "Refuses a selected inline member which has a split sidecar" {
            let source, lookedUp, _ = ordersSource (Orders.inlineAndSplitValue "v4")

            let snapshot = read (BoundedTreeReader.openTree 1024L source)

            BoundedTreeReader.readMember orderAddress snapshot
            |> expectReadError "tree.duplicate_document" orderLocation

            Expect.sequenceEqual
                lookedUp
                [ manifestLocation; moduleLocation ]
                "The sidecar body stays unopened"
        }
    ]


[<Tests>]
let boundedExportTests =
    let totalId = "pkg/acme/orders/domain/total-in-_usd.value"

    let openContextual version profile =
        let source, opened = frozenOrders version profile

        let snapshot =
            read (
                BoundedTreeReader.openTreeWithEncoding
                    BoundedTreeEncoding.ContextualDraft1
                    8192L
                    source
            )

        opened.Clear()
        snapshot, opened

    let expectPlain version name (bytes: System.ReadOnlyMemory<byte>) =
        let catalog = Orders.catalogDocuments version "plain-json"

        use expected =
            JsonDocument.Parse(System.ReadOnlyMemory(FixtureCatalog.ReadBytes(catalog[name])))

        use actual = JsonDocument.Parse bytes
        Expect.isTrue (JsonElement.DeepEquals(actual.RootElement, expected.RootElement)) name

    testList "Bounded expanded export" [
        test "Original delivery uses the source cap independently of expanded inventory" {
            let files, original = Orders.contextualModuleWithSharedInventoryAliases ()
            let source, _, _ = ordersSource files

            let snapshot =
                read (
                    BoundedTreeReader.openTreeWithEncoding
                        BoundedTreeEncoding.ContextualDraft1
                        8192L
                        source
                )

            let moduleId = "pkg/acme/orders/domain/module"
            let originalSize = int64 (System.Text.Encoding.UTF8.GetByteCount original)
            let expanded = read (DocumentTreeStore.readExpandedDocument moduleId 8192L snapshot)

            Expect.isGreaterThan
                (int64 expanded.Bytes.Length)
                originalSize
                "The aliases expand past the delivery budget"

            let delivered = read (DocumentTreeStore.readDocumentWithin moduleId originalSize snapshot)

            Expect.equal
                (System.Text.Encoding.UTF8.GetString delivered.Span)
                original
                "Deliver the original envelope"

            DocumentTreeStore.readDocumentWithin moduleId (originalSize - 1L) snapshot
            |> expectReadError "tree.file_too_large" moduleLocation
        }

        test "Exports inventory beyond the source cap and preserves parameter order" {
            let files, original = Orders.contextualModuleWithSharedInventoryAliases ()
            let sourceSize = int64 (System.Text.Encoding.UTF8.GetByteCount original)

            let options =
                { ReadOptions.defaults with
                    MaxFileBytes = sourceSize
                }

            let snapshot =
                DocumentTreeStore.openSnapshotWithEncoding
                    options
                    BoundedTreeEncoding.ContextualDraft1
                    (fun () -> "stable")
                    (VFileProviders.asFileProvider files)
                |> read
            let staged = ResizeArray<BoundedTreeExportDocument>()

            let stage file =
                staged.Add file
                Ok()

            let limits = {
                MaxDocumentBytes = 8192L
                MaxTotalBytes = 65536L
                PageSize = 1
            }

            read (DocumentTreeStore.writeExpandedTree limits stage snapshot)
            Expect.equal staged.Count 8 "Manifest, module and six split bodies"
            let moduleFile = staged |> Seq.find (fun file -> file.File = moduleLocation)

            Expect.isGreaterThan
                (int64 moduleFile.Bytes.Length)
                sourceSize
                "The exported module exceeds the source cap"

            let value = staged |> Seq.find (fun file -> file.File.EndsWith ".value.json")
            use document = JsonDocument.Parse value.Bytes

            let inputs =
                document.RootElement
                    .GetProperty("def")
                    .GetProperty("Public")
                    .GetProperty("ExpressionBody")
                    .GetProperty("inputTypes")

            let names = inputs.EnumerateObject() |> Seq.map _.Name |> List.ofSeq
            Expect.equal names [ "rate"; "amount" ] "Keep source parameter order through expansion"
        }

        for version in [ "v3.1"; "v4" ] do
            for profile in [ "contextual-json"; "contextual-yaml" ] do
                test $"Exports one selected {version} {profile} definition" {
                    let snapshot, opened = openContextual version profile

                    let exported =
                        read (DocumentTreeStore.readExpandedDocument totalId 8192L snapshot)

                    Expect.equal
                        exported.File
                        (totalId
                         + ".json")
                        "The logical identity selects plain JSON output"

                    expectPlain version exported.File exported.Bytes

                    let extension = if profile.EndsWith("-yaml") then ".yaml" else ".json"

                    let bodyReads =
                        opened
                        |> Seq.filter (fun name ->
                            name.EndsWith(
                                ".value"
                                + extension
                            )
                        )

                    Expect.sequenceEqual
                        bodyReads
                        [
                            totalId
                            + extension
                        ]
                        "Open only the selected body"
                }

        for version, tree in
            [ "v3.1", Mother.v3SharedNameOverTheBudget; "v4", Mother.sharedNameOverTheBudget ] do
            test $"Preserves {version} physical stems recorded in fileNames" {
                let files = read (Driver.write tree.Tree)
                let provider = VFileProviders.asFileProvider files

                let snapshot =
                    read (
                        DocumentTreeStore.openSnapshot
                            ReadOptions.defaults
                            (fun () -> "stable")
                            provider
                    )

                let staged = ResizeArray<string * VFile>()

                let stage (file: BoundedTreeExportDocument) =
                    staged.Add(file.File, VFile.ofBytes file.File file.Bytes)
                    Ok()

                let limits = {
                    MaxDocumentBytes = 65536L
                    MaxTotalBytes = 524288L
                    PageSize = 1
                }

                read (DocumentTreeStore.writeExpandedTree limits stage snapshot)

                let expanded =
                    Map.ofSeq staged
                    |> VFileTree.fromFileMap

                let paths tree =
                    VFileTree.toFileMap tree
                    |> Map.keys
                    |> Set.ofSeq

                Expect.equal (paths expanded) (paths files) "Keep the admitted shortened sidecars"
                let reopened, _ = read (Driver.read expanded)
                let roundTrip = read (Driver.write reopened)

                Expect.equal
                    (Driver.texts roundTrip)
                    (Driver.texts files)
                    "The preserved fileNames resolve both members"
            }

        test "Uses the export cap when validating the expanded inline module" {
            let files, sourceBytes = Orders.contextualInlineOrderWithExpandingAliases ()

            let options =
                { ReadOptions.defaults with
                    MaxFileBytes = int64 sourceBytes
                }

            let provider = VFileProviders.asFileProvider files

            let snapshot =
                DocumentTreeStore.openSnapshotWithEncoding
                    options
                    BoundedTreeEncoding.ContextualDraft1
                    (fun () -> "stable")
                    provider
                |> read

            let moduleId = "pkg/acme/orders/domain/module"
            let expanded = read (DocumentTreeStore.readExpandedDocument moduleId 8192L snapshot)

            Expect.isGreaterThan
                expanded.Bytes.Length
                sourceBytes
                "The expanded module exceeds the source cap"

            let staged = ResizeArray<BoundedTreeExportDocument>()

            let stage file =
                staged.Add file
                Ok()

            let limits = {
                MaxDocumentBytes = 8192L
                MaxTotalBytes = 65536L
                PageSize = 1
            }

            read (DocumentTreeStore.writeExpandedTree limits stage snapshot)

            let emitted =
                staged
                |> Seq.find (fun file -> file.File = moduleLocation)

            Expect.sequenceEqual
                (emitted.Bytes.ToArray())
                (expanded.Bytes.ToArray())
                "Stage the validated inline module once"
        }

        test "Selected module export refuses a malformed inline body" {
            let source, _, _ = ordersSource (Orders.malformedInlineValue "v4")
            let snapshot = read (BoundedTreeReader.openTree 8192L source)

            let result =
                DocumentTreeStore.readExpandedDocument
                    "pkg/acme/orders/domain/module"
                    8192L
                    snapshot

            Expect.isError result "The inline value must decode before the module is returned"
        }

        test "Rejects the first byte outside the exact expanded limit" {
            let snapshot, _ = openContextual "v4" "contextual-json"
            let exported = read (DocumentTreeStore.readExpandedDocument totalId 8192L snapshot)
            let size = int64 exported.Bytes.Length
            let exact = read (DocumentTreeStore.readExpandedDocument totalId size snapshot)

            Expect.sequenceEqual
                (exact.Bytes.ToArray())
                (exported.Bytes.ToArray())
                "Exactly at the output limit"

            DocumentTreeStore.readExpandedDocument totalId (size - 1L) snapshot
            |> expectReadError "tree.file_too_large" totalLocation
        }

        test
            "Expansion preserves literal order and exact numbers while original bytes retain optional context" {
            let files, original = Orders.contextualOrderWithLiteralAndOptionalPrefixes ()
            let source, _, _ = ordersSource files

            let snapshot =
                read (
                    BoundedTreeReader.openTreeWithEncoding
                        BoundedTreeEncoding.ContextualDraft1
                        8192L
                        source
                )

            let id = "pkg/acme/orders/domain/order.value"
            let expanded = read (DocumentTreeStore.readExpandedDocument id 8192L snapshot)
            use parsed = JsonDocument.Parse expanded.Bytes

            let expression =
                parsed.RootElement
                    .GetProperty("def")
                    .GetProperty("Public")
                    .GetProperty("ExpressionBody")

            let literal =
                expression
                    .GetProperty("body")
                    .GetProperty("Literal")
                    .GetProperty("DocumentLiteral")

            let fields =
                literal.EnumerateObject()
                |> Seq.map (fun entry -> entry.Name, entry.Value.GetRawText())
                |> List.ofSeq

            Expect.equal
                fields
                [ "ratio", "1.0"; "$name", "\"i\""; "id", "9007199254740993" ]
                "Opaque literal"

            Expect.equal
                (expression.GetProperty("outputType").GetString())
                "morphir/SDK:basics#int"
                "Only the typed alias is expanded"

            let bytes = read (DocumentTreeStore.readDocument id snapshot)

            Expect.equal
                (System.Text.Encoding.UTF8.GetString bytes.Span)
                original
                "Exact opaque pass-through"
        }

        test "Expanded output has a separate limit from admitted source bytes" {
            let files, selectedBytes = Orders.contextualOrderWithExpandingAliases ()
            let source, _, _ = ordersSource files

            let snapshot =
                read (
                    BoundedTreeReader.openTreeWithEncoding
                        BoundedTreeEncoding.ContextualDraft1
                        (int64 selectedBytes)
                        source
                )

            let id = "pkg/acme/orders/domain/order.value"
            let expanded = read (DocumentTreeStore.readExpandedDocument id 8192L snapshot)

            Expect.isGreaterThan
                expanded.Bytes.Length
                selectedBytes
                "Alias expansion can exceed the input length"

            DocumentTreeStore.readExpandedDocument id (int64 selectedBytes) snapshot
            |> expectReadError "tree.file_too_large" orderLocation
        }

        test "A revision change during staging prevents a successful export" {
            let source, _, advance = ordersSource (Orders.selectedValues "v4")
            let snapshot = read (BoundedTreeReader.openTree 8192L source)
            let staged = ResizeArray<string>()

            let stage (file: BoundedTreeExportDocument) =
                staged.Add file.File
                advance ()
                Ok()

            let limits = {
                MaxDocumentBytes = 8192L
                MaxTotalBytes = 65536L
                PageSize = 1
            }

            let result = DocumentTreeStore.writeExpandedTree limits stage snapshot

            let codes =
                result
                |> StoreTestKit.refusal
                |> StoreTestKit.codesAndFiles
                |> List.map fst

            Expect.equal codes [ "tree.revision_changed" ] "A changed snapshot is refused"
            Expect.sequenceEqual staged [ "manifest.json" ] "No further file reaches the stage"
        }

        test "Pages a complete tree to a private stage without buffering unrelated bodies" {
            let snapshot, opened = openContextual "v4" "contextual-yaml"
            let expected = Orders.catalogDocuments "v4" "plain-json"
            let seen = System.Collections.Generic.HashSet<string>()

            let stage (file: BoundedTreeExportDocument) =
                if seen.Count = 0 then
                    Expect.equal file.File "manifest.json" "Manifest is first"

                    Expect.sequenceEqual
                        opened
                        [ "manifest.yaml" ]
                        "No definition read before the first stage write"

                Expect.isTrue (seen.Add file.File) "An artifact is staged once"
                expectPlain "v4" file.File file.Bytes
                Ok()

            let limits = {
                MaxDocumentBytes = 8192L
                MaxTotalBytes = 65536L
                PageSize = 1
            }

            read (DocumentTreeStore.writeExpandedTree limits stage snapshot)

            Expect.equal
                (Set.ofSeq seen)
                (expected
                 |> Map.keys
                 |> Set.ofSeq)
                "Stage the admitted tree and dependencies"
        }

        test "Does not emit the next file after exhausting the total export budget" {
            let snapshot, opened = openContextual "v4" "contextual-json"

            let manifest =
                read (DocumentTreeStore.readExpandedDocument "manifest" 8192L snapshot)

            opened.Clear()
            let names = ResizeArray<string>()

            let stage (file: BoundedTreeExportDocument) =
                names.Add file.File
                Ok()

            let limits = {
                MaxDocumentBytes = 8192L
                MaxTotalBytes = int64 manifest.Bytes.Length
                PageSize = 1
            }

            let result = DocumentTreeStore.writeExpandedTree limits stage snapshot

            let codes =
                result
                |> StoreTestKit.refusal
                |> StoreTestKit.codesAndFiles
                |> List.map fst

            Expect.equal codes [ "tree.file_too_large" ] "Cumulative budget"

            Expect.sequenceEqual
                names
                [ "manifest.json" ]
                "Only a complete admitted artifact reached the stage"

            Expect.isTrue
                (Seq.forall (fun (name: string) -> not (name.EndsWith ".value.json")) opened)
                "No value is opened"
        }
    ]
