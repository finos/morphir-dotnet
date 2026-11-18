module Morphir.IR.Tests.Trees.BoundedTreeTests

open System.IO
open System.Text.Json
open Expecto
open Morphir.Files
open Morphir.IR
open Morphir.IR.Trees
open Morphir.TestSupport.Fixtures

module Orders = Morphir.Models.TestSupport.DocumentTreeMother.BoundedOrders

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
        NextModule = fun _ _ _ -> failtest "Member reads do not enumerate modules"
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
                fun _ prefix after ->
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
                        NextModule = fun _ _ _ -> Ok(Some path)
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
                    NextModule = fun _ _ _ -> Ok(Some repeated)
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
            fun _ prefix after ->
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
                NextModule = fun _ _ _ -> Ok None
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
