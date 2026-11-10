// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Conformance.Tests.GuestKitTests

open System
open System.Text
open Expecto
open Morphir.Conformance.WasmHost
open Morphir.Conformance.GuestKit
open Morphir.Conformance.Tests.GuestKitFakeGuest

let private parse (text: string) =
    match Json.parse text with
    | Ok value -> value
    | Error message -> failtest message

let private differs version expected actual =
    match IrComparison.difference version expected actual with
    | Some difference -> difference
    | None -> failtest $"expected a difference between {Json.compact expected} and {Json.compact actual}"

let private same version expected actual =
    match IrComparison.difference version expected actual with
    | None -> ()
    | Some difference -> failtest $"expected no difference: {difference}"

/// The kit file with one change, read by the runner's reader.
let private kitWith (edit: JsonValue -> JsonValue) =
    let edited = edit (kitJson ())
    GuestKit.Parse(Encoding.UTF8.GetBytes(Json.compact edited))

let private issuesOf result =
    match result with
    | Ok _ -> failtest "the kit was accepted"
    | Error issues ->
        issues
        |> Seq.map string
        |> String.concat "\n"

let private addMember (name: string) (item: JsonValue) (value: JsonValue) =
    match value with
    | JsonValue.Object members -> JsonValue.Object(members @ [ name, item ])
    | _ -> failtest "not an object"

/// The v4 expected IR with the output type of ready replaced.
let private readyOutputType (replacement: JsonValue) =
    expectedIr "4.0.0"
    |> replaceAt
        [
            "distribution"
            "Library"
            "def"
            "modules"
            "main"
            "Public"
            "values"
            "ready"
            "Public"
            "ExpressionBody"
            "outputType"
        ]
        replacement

/// The values list of the v3 expected IR's main module, with an edit.
let private v3Values (edit: JsonValue list -> JsonValue list) =
    let rec walk (value: JsonValue) =
        match value with
        | JsonValue.Object members ->
            JsonValue.Object(
                members
                |> List.map (fun (key, item) ->
                    match key, item with
                    | "values", JsonValue.Array entries -> key, JsonValue.Array(edit entries)
                    | _ -> key, walk item
                )
            )
        | JsonValue.Array items -> JsonValue.Array(List.map walk items)
        | other -> other

    walk (expectedIr "3.0.0")

/// The v3 expected IR with the BoolLiteral of every value negated.
let rec private negateLiterals (value: JsonValue) =
    match value with
    | JsonValue.Array [ JsonValue.String "BoolLiteral"; JsonValue.Bool flag ] ->
        JsonValue.Array [ JsonValue.String "BoolLiteral"; JsonValue.Bool(not flag) ]
    | JsonValue.Array items -> JsonValue.Array(List.map negateLiterals items)
    | JsonValue.Object members ->
        JsonValue.Object(
            members
            |> List.map (fun (key, item) ->
                match key, item with
                | "BoolLiteral", JsonValue.Bool flag -> key, JsonValue.Bool(not flag)
                | _ -> key, negateLiterals item
            )
        )
    | other -> other

[<Tests>]
let readerTests =
    testList "Generic guest kit reader (task 3.5)" [
        test "The pinned kit loads with its three languages and two IR versions" {
            let loaded = kit.Value
            Expect.equal loaded.Profile "generic-json-library-v1" "profile"
            Expect.equal loaded.ContractVersion "0.1.0-draft.1" "contract version"
            Expect.sequenceEqual loaded.Languages [ "gleam"; "morphir-fixture-flags"; "python" ] "languages"
            Expect.sequenceEqual loaded.IrVersions [ "3.0.0"; "4.0.0" ] "IR versions"
            Expect.equal loaded.Sha256 "f16f2a4c577b507527a77a19a383c2c0083b4db1db131cd06d170e65a88598ae" "kit SHA-256"
        }

        test "An unknown member at the root is an error that names it" {
            let issues = issuesOf (kitWith (addMember "tiers" (arr [])))
            Expect.stringContains issues "/tiers" "names the member"
        }

        test "An unknown member in a language is an error that names it" {
            let issues =
                issuesOf (
                    kitWith (fun kit ->
                        kit
                        |> replaceAt [ "languages"; "python" ] (kit |> at [ "languages"; "python" ] |> addMember "steps" (arr []))
                    )
                )

            Expect.stringContains issues "/languages/python/steps" "names the member"
        }

        test "An unknown member in the package is an error that names it" {
            let issues =
                issuesOf (
                    kitWith (fun kit ->
                        kit
                        |> replaceAt [ "package" ] (kit |> at [ "package" ] |> addMember "typesOnly" (JsonValue.Bool true))
                    )
                )

            Expect.stringContains issues "/package/typesOnly" "names the member"
        }

        test "An IR version the runner does not know is an error that names it" {
            let issues =
                issuesOf (
                    kitWith (fun kit ->
                        kit
                        |> replaceAt
                            [ "expectedIrVersions" ]
                            (kit
                             |> at [ "expectedIrVersions" ]
                             |> addMember "5.0.0" (obj [ "formatVersion", num 5 ]))
                    )
                )

            Expect.stringContains issues "/expectedIrVersions/5.0.0" "names the version"
        }

        test "A comparison rule in the kit file is an unknown member, not a rule" {
            let issues =
                issuesOf (kitWith (addMember "comparison" (obj [ "ignoreArrayOrder", JsonValue.Bool true ])))

            Expect.stringContains issues "/comparison" "names the member"
        }

        test "Another profile is refused by name" {
            let issues =
                issuesOf (kitWith (replaceAt [ "profile" ] (str "python-json-library-v1")))

            Expect.stringContains issues "python-json-library-v1" "names the profile"
        }

        test "A document URI with a src/ prefix is refused" {
            let issues =
                issuesOf (
                    kitWith (
                        replaceAt
                            [ "languages"; "python"; "documents" ]
                            (arr [ obj [ "uri", str "src/main.py"; "text", str "x" ] ])
                    )
                )

            Expect.stringContains issues "src/main.py" "names the URI"
        }

        test "A repeated member is refused" {
            let text = Encoding.UTF8.GetString(kitBytes ())
            let repeated = text.Replace("\"profile\":", "\"profile\": \"generic-json-library-v1\", \"profile\":")
            Expect.isError (GuestKit.Parse(Encoding.UTF8.GetBytes repeated)) "repeated member"
        }
    ]

[<Tests>]
let comparisonTests =
    testList "The kit's three comparison rules and no other (task 3.5)" [
        test "Rule 1: object member order is ignored" {
            let expected = expectedIr "4.0.0"
            let actual = reverseMembers expected
            Expect.notEqual (Json.compact actual) (Json.compact expected) "the texts differ"
            same IrVersion.V4 expected actual
        }

        test "Rule 1 does not hide a changed member" {
            let expected = expectedIr "4.0.0"

            let actual =
                reverseMembers (
                    expected
                    |> replaceAt [ "distribution"; "Library"; "packageName" ] (str "mck/other")
                )

            let difference = differs IrVersion.V4 expected actual
            Expect.stringContains difference "packageName" "names the member"
            Expect.stringContains difference "\"mck/generic\"" "quotes the expected value"
            Expect.stringContains difference "\"mck/other\"" "quotes the actual value"
        }

        test "Rule 2: in IR 3 the order of the values list is ignored" {
            let expected = expectedIr "3.0.0"
            let actual = v3Values List.rev
            Expect.notEqual (Json.compact actual) (Json.compact expected) "the texts differ"
            same IrVersion.V3 expected actual
        }

        test "Rule 2 does not hide a changed value in a reordered list" {
            let expected = expectedIr "3.0.0"
            let actual = negateLiterals (v3Values List.rev)
            let difference = differs IrVersion.V3 expected actual
            Expect.stringContains difference "false" "quotes a literal"
        }

        test "Rule 2 does not apply to another list" {
            let expected =
                parse """{"modules":[],"other":[[["a"],1],[["b"],2]]}"""

            let actual =
                parse """{"modules":[],"other":[[["b"],2],[["a"],1]]}"""

            differs IrVersion.V3 expected actual
            |> ignore
        }

        test "Rule 2 does not apply in IR 4" {
            let expected = parse """{"values":[[["a"],1],[["b"],2]]}"""
            let actual = parse """{"values":[[["b"],2],[["a"],1]]}"""
            same IrVersion.V3 expected actual
            differs IrVersion.V4 expected actual |> ignore
        }

        test "Rule 3: in IR 4 a Reference with an fqname and no args equals the FQName at an outputType" {
            let expected = expectedIr "4.0.0"

            same
                IrVersion.V4
                expected
                (readyOutputType (obj [ "Reference", obj [ "fqname", str "morphir/SDK:basics#bool" ] ]))

            same
                IrVersion.V4
                expected
                (readyOutputType (
                    obj [
                        "Reference", obj [ "fqname", str "morphir/SDK:basics#bool"; "args", arr [] ]
                    ]
                ))
        }

        test "Rule 3 applies to a member of inputTypes" {
            let expected =
                parse """{"inputTypes":{"flag":"morphir/SDK:basics#bool"},"outputType":"morphir/SDK:basics#bool"}"""

            let actual =
                parse
                    """{"inputTypes":{"flag":{"Reference":{"fqname":"morphir/SDK:basics#bool"}}},"outputType":"morphir/SDK:basics#bool"}"""

            same IrVersion.V4 expected actual
        }

        test "Rule 3 does not apply outside a type position" {
            let expected = expectedIr "4.0.0"

            let actual =
                expected
                |> replaceAt
                    [ "distribution"; "Library"; "packageName" ]
                    (obj [ "Reference", obj [ "fqname", str "mck/generic" ] ])

            differs IrVersion.V4 expected actual |> ignore
        }

        test "Rule 3 does not apply to a Reference with arguments or another member" {
            let expected = expectedIr "4.0.0"

            differs
                IrVersion.V4
                expected
                (readyOutputType (
                    obj [
                        "Reference",
                        obj [ "fqname", str "morphir/SDK:basics#bool"; "args", arr [ str "morphir/SDK:basics#int" ] ]
                    ]
                ))
            |> ignore

            differs
                IrVersion.V4
                expected
                (readyOutputType (
                    obj [ "Reference", obj [ "fqname", str "morphir/SDK:basics#bool"; "attributes", obj [] ] ]
                ))
            |> ignore
        }

        test "Rule 3 does not apply in IR 3" {
            let expected = parse """{"outputType":"morphir/SDK:basics#bool"}"""

            let actual =
                parse """{"outputType":{"Reference":{"fqname":"morphir/SDK:basics#bool"}}}"""

            same IrVersion.V4 expected actual
            differs IrVersion.V3 expected actual |> ignore
        }

        test "A wrong output type differs" {
            let difference =
                differs IrVersion.V4 (expectedIr "4.0.0") (readyOutputType (str "Unit"))

            Expect.stringContains difference "outputType" "names the member"
            Expect.stringContains difference "\"Unit\"" "quotes the actual value"
        }

        test "Numbers compare by their literal text" {
            differs IrVersion.V4 (parse """{"formatVersion":4}""") (parse """{"formatVersion":4.0}""")
            |> ignore
        }

        test "Array order outside rule 2 is kept" {
            differs IrVersion.V4 (parse """{"a":[1,2]}""") (parse """{"a":[2,1]}""")
            |> ignore
        }
    ]

let private notRunReason (step: KitStep) =
    match step.Outcome with
    | StepOutcome.NotRun reason -> reason
    | _ -> failtest $"{step.Id} ran: {outcome step}"

let private modelStepIds (versions: string list) = [
    "model-initialize"
    "model-initialized-silent"
    for version in versions do
        let suffix = if version = "3.0.0" then "vthree" else "vfour"

        for name in [
            "compile-model"
            "generate-model"
            "roundtrip-model"
            "generate-model-independent"
            "roundtrip-model-independent"
        ] do
            $"{name}-{suffix}"
    "bad-digest"
    "model-shutdown"
    "model-exit"
]

[<Tests>]
let runnerTests =
    testList "Generic guest kit model-tier runner (task 3.5)" [
        test "A faithful guest passes every model-tier step at both IR versions" {
            let result, _ = run faithful

            let ids =
                result.Steps
                |> Seq.map _.Id
                |> List.ofSeq

            Expect.equal ids ("describe" :: modelStepIds [ "3.0.0"; "4.0.0" ]) "the plan"

            for step in result.Steps do
                Expect.equal (outcome step) "passed" step.Id

            Expect.sequenceEqual result.IrVersions [ "3.0.0"; "4.0.0" ] "IR versions"
            Expect.sequenceEqual result.ModelIrVersions [ "3.0.0"; "4.0.0" ] "model IR versions"
            Expect.equal result.Language "python" "language"
            Expect.equal result.Guest "morphir-fake 0.1.0" "guest"
            Expect.isNull result.ModelTierReason "the model tier was planned"
        }

        test "The runner opens a describe session with no offer and a model session with the kit's offer" {
            let _, opened = run faithful
            Expect.equal opened.Length 2 "two sessions"
            let describe, describeChannel = opened[0]
            let model, modelChannel = opened[1]
            Expect.equal describe.Purpose KitSessionPurpose.Describe "first purpose"
            Expect.isNull (describe.OfferOrNull()) "no offer for describe"
            Expect.equal model.Purpose KitSessionPurpose.ModelTier "second purpose"
            let offer = model.OfferOrNull()
            Expect.isNotNull offer "an offer for the model tier"

            Expect.sequenceEqual
                (offer.Tuples
                 |> Seq.map (fun tuple -> $"{tuple.Operation} {tuple.IrVersion}"))
                [
                    "morphir.frontend.compileModel 3.0.0"
                    "morphir.backend.generateModel 3.0.0"
                    "morphir.frontend.compileModel 4.0.0"
                    "morphir.backend.generateModel 4.0.0"
                ]
                "the kit's tuples, compile and generate for each version"

            Expect.equal offer.Limits.MaxEncodedBytes 1000000 "limits"
            Expect.sequenceEqual (describeChannel.Requests |> List.map fst) [ "morphir.extension.describe" ] "describe session"
            Expect.sequenceEqual describeChannel.Notifications [ "morphir.exit" ] "describe session ends with exit"
            Expect.equal (modelChannel.Requests |> List.head |> fst) "morphir.initialize" "model session starts"
            Expect.sequenceEqual modelChannel.Notifications [ "morphir.initialized"; "morphir.exit" ] "model notifications"
        }

        test "Each step says how many requests it sent, and the guest received exactly those" {
            let result, opened = run faithful
            let _, describeChannel = opened[0]
            let _, modelChannel = opened[1]

            let requests tier =
                result.Steps
                |> Seq.filter (fun step -> step.Tier = tier)
                |> Seq.sumBy _.Requests

            Expect.equal (requests KitTier.Describe) describeChannel.Requests.Length "describe session"
            Expect.equal (requests KitTier.Model) modelChannel.Requests.Length "model session"

            Expect.equal
                (result.Steps
                 |> Seq.sumBy _.Notifications)
                (describeChannel.Notifications.Length
                 + modelChannel.Notifications.Length)
                "notifications"
        }

        test "The requests carry the kit's documents, package, context digest and output tuple" {
            let _, opened = run faithful
            let _, modelChannel = opened[1]

            let _, compile =
                modelChannel.Requests
                |> List.find (fun (method, _) -> method = "morphir.frontend.compileModel")

            Expect.equal (compile |> at [ "languageId" ]) (str "python") "language"
            Expect.equal (compile |> at [ "package" ]) (kitJson () |> at [ "package" ]) "package"
            Expect.equal (compile |> at [ "contextDigest" ]) (str (String('c', 64))) "context digest"
            Expect.equal (compile |> at [ "options"; "irVersion" ]) (str "3.0.0") "IR version"
            Expect.equal (compile |> at [ "options"; "typesOnly" ]) (JsonValue.Bool false) "typesOnly"
            Expect.equal (compile |> at [ "output"; "codec" ]) (str "morphir-json") "output tuple"

            match compile |> at [ "sources"; "documents" ] with
            | JsonValue.Array [ document ] ->
                Expect.equal (document |> at [ "uri" ]) (str "main.py") "uri"
                Expect.equal (document |> at [ "text" ]) (pythonSource ()) "text"
                Expect.equal (document |> at [ "version" ]) (num 1) "version"
            | other -> failtest $"documents: {Json.compact other}"
        }

        test "A guest that claims one IR version gets the cases of that version only" {
            let result, _ =
                run {
                    faithful with
                        IrVersions = [ "4" ]
                        ModelVersions = [ "4.0.0" ]
                }

            Expect.equal
                (result.Steps
                 |> Seq.map _.Id
                 |> List.ofSeq)
                ("describe" :: modelStepIds [ "4.0.0" ])
                "only vfour"

            Expect.sequenceEqual result.ModelIrVersions [ "4.0.0" ] "model IR versions"

            Expect.isFalse
                (result.LegacyTier
                 |> Seq.exists (fun step -> step.Id.EndsWith "-vthree"))
                "no legacy vthree case"
        }

        test "A guest that claims model tuples at one of two IR versions gets the model cases of that version" {
            let result, _ =
                run {
                    faithful with
                        ModelVersions = [ "4.0.0" ]
                }

            Expect.sequenceEqual result.IrVersions [ "3.0.0"; "4.0.0" ] "both versions selected"
            Expect.sequenceEqual result.ModelIrVersions [ "4.0.0" ] "model tier at 4.0.0 only"

            Expect.isTrue
                (result.LegacyTier
                 |> Seq.exists (fun step -> step.Id = "compile-vthree"))
                "the legacy plan keeps vthree"
        }

        test "A wrong IR fails the compile case with the difference" {
            let result, _ =
                run {
                    faithful with
                        Compile =
                            fun version expected ->
                                if version = "4.0.0" then
                                    expected
                                    |> replaceAt
                                        [
                                            "distribution"
                                            "Library"
                                            "def"
                                            "modules"
                                            "main"
                                            "Public"
                                            "values"
                                            "ready"
                                            "Public"
                                            "ExpressionBody"
                                            "body"
                                            "Literal"
                                            "BoolLiteral"
                                        ]
                                        (JsonValue.Bool false)
                                else
                                    expected
                }

            let failed = outcome (step "compile-model-vfour" result)
            Expect.stringStarts failed "failed: " "fails"
            Expect.stringContains failed "BoolLiteral" "names the member"
            Expect.stringContains failed "true" "quotes the expected value"
            Expect.equal (outcome (step "compile-model-vthree" result)) "passed" "v3 still passes"
            Expect.equal (outcome (step "generate-model-vfour" result)) "passed" "generation from the compiled model still runs"
        }

        test "A guest that accepts a wrong payload digest fails bad-digest" {
            let result, _ =
                run {
                    faithful with
                        AcceptBadDigest = true
                }

            Expect.stringStarts (outcome (step "bad-digest" result)) "failed: " "fails"
        }

        test "An initialize result whose extension differs from describe fails model-initialize" {
            let result, _ =
                run {
                    faithful with
                        InitializeVersion = "0.2.0"
                }

            Expect.stringStarts (outcome (step "model-initialize" result)) "failed: " "fails"
        }

        test "A guest that claims no model tuples has no model tier, and the result says why" {
            let result, opened =
                run {
                    faithful with
                        ModelVersions = []
                }

            Expect.equal
                (result.Steps
                 |> Seq.map _.Id
                 |> List.ofSeq)
                [ "describe" ]
                "only describe"

            Expect.isNotNull result.ModelTierReason "a reason"
            Expect.stringContains result.ModelTierReason "compileModel" "says what is missing"
            Expect.equal opened.Length 1 "no model session"
        }

        test "A guest that claims only MEP 0.1 has no model tier" {
            let result, _ =
                run {
                    faithful with
                        ProtocolVersions = [ "0.1" ]
                }

            Expect.isEmpty result.ModelIrVersions "no model tier"
            Expect.stringContains result.ModelTierReason "0.2.0-draft.1" "says why"
        }

        test "The legacy tier is listed as not run because this host does not speak MEP 0.1" {
            let result, opened = run faithful

            let ids =
                result.LegacyTier
                |> Seq.map _.Id
                |> List.ofSeq

            Expect.equal
                ids
                [
                    "initialize"
                    "initialized-silent"
                    "unknown-notification-silent"
                    for suffix in [ "vthree"; "vfour" ] do
                        for name in [
                            "compile"
                            "generate"
                            "roundtrip"
                            "generate-independent"
                            "roundtrip-independent"
                        ] do
                            $"{name}-{suffix}"
                    "malformed-source"
                    "unknown-method"
                    "describe-immutable"
                    "shutdown"
                    "after-shutdown"
                    "exit"
                ]
                "the kit's legacy plan"

            for step in result.LegacyTier do
                Expect.equal step.Tier KitTier.Legacy step.Id
                Expect.stringContains (notRunReason step) "MEP 0.1" step.Id
                Expect.equal step.Requests 0 step.Id

            Expect.isTrue result.ModelTierOnly "model tier only"
            Expect.stringContains result.LegacyTierReason "MEP 0.1" "reason"

            let methods =
                opened
                |> List.collect (fun (_, channel) -> channel.Requests |> List.map fst)

            Expect.isFalse (List.contains "morphir.frontend.compile" methods) "no legacy compile was sent"
            Expect.isFalse (List.contains "morphir.backend.generate" methods) "no legacy generate was sent"
        }

        test "The result never reads as a passing run of the kit" {
            let result, _ = run faithful
            let text = result.ToString()
            Expect.stringContains text "model tier only" "says model tier only"
            Expect.stringContains text "not a complete run of the generic guest kit" "says not complete"

            for word in [ "qualified"; "kit passed"; "kit passes" ] do
                Expect.isFalse (text.Contains(word, StringComparison.OrdinalIgnoreCase)) word

            let report = result.Report()
            Expect.stringContains report "not a complete run of the generic guest kit" "the report says not complete"

            let verdicts =
                typeof<ModelTierRun>.GetProperties()
                |> Array.filter (fun property -> property.PropertyType = typeof<bool>)
                |> Array.map _.Name

            Expect.equal verdicts [| "ModelTierOnly" |] "no boolean verdict"
        }
    ]
