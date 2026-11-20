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

let private modelStepIds (versions: string list) =
    let cases =
        match versions with
        | [ "3.0.0" ] -> GuestKitPlanFixture.threeCases
        | [ "4.0.0" ] -> GuestKitPlanFixture.fourCases
        | _ -> GuestKitPlanFixture.bothCases
    cases |> List.tail |> List.map fst
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
                if List.contains step.Id [ "model-initialized-silent"; "unknown-notification-silent"; "model-exit" ] then
                    Expect.equal step.Outcome.WireValue "not-run" "scripted peer has no inner-delivery/process evidence"
                else Expect.equal (step.Outcome.ToString()) "passed" step.Id

            Expect.sequenceEqual result.IrVersions [ "3.0.0"; "4.0.0" ] "IR versions"
            Expect.sequenceEqual result.ModelIrVersions [ "3.0.0"; "4.0.0" ] "model IR versions"
            Expect.equal result.Language "python" "language"
            Expect.equal result.Guest "morphir-fake 0.1.0" "guest"
            Expect.isNull result.ModelTierReason "the model tier was planned"
        }

        test "One factory opening binds only the selected offer after describe" {
            let _, opened = run faithful
            Expect.equal opened.Length 1 "one factory opening"
            let request, channel = opened[0]
            Expect.equal request.Purpose KitSessionPurpose.ModelTier "one required session"
            Expect.isNull (request.OfferOrNull()) "the offer is not guessed before describe"
            let methods = channel.Requests |> List.map fst
            Expect.equal methods.Head "morphir.extension.describe" "describe first"
            let _, initialize = channel.Requests |> List.find (fun (method, _) -> method = "morphir.initialize")
            Expect.equal (initialize |> at [ "modelTransport"; "tuples" ])
                (arr (GuestKitPlanFixture.threeTuples @ GuestKitPlanFixture.fourTuples)) "actual selected offer"
            Expect.sequenceEqual channel.Notifications [ "morphir.initialized"; "morphir.mck.unknown"; "morphir.exit" ] "one sequence of owned notifications"
        }

        test "Each step says how many requests it sent, and the peer received exactly those" {
            let result, opened = run faithful
            let _, channel = opened |> Seq.exactlyOne
            Expect.equal (result.Steps |> Seq.sumBy _.Requests) channel.Requests.Length "actual requests"
            Expect.equal (result.Steps |> Seq.sumBy _.Notifications) channel.Notifications.Length "actual adapter notifications, not guest invocation claims"
        }
        test "The requests carry the kit's documents, package, context digest and output tuple" {
            let _, opened = run faithful
            let _, modelChannel = opened |> Seq.exactlyOne

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

            Expect.sequenceEqual result.IrVersions [ "4.0.0" ] "no unsupported version in the broad projection"
        }

        test "A guest that claims model tuples at one of two IR versions gets the model cases of that version" {
            let result, _ =
                run {
                    faithful with
                        ModelVersions = [ "4.0.0" ]
                }

            Expect.sequenceEqual result.IrVersions [ "4.0.0" ] "SEL-1 excludes the version without a JSON tuple pair"
            Expect.sequenceEqual result.ModelIrVersions [ "4.0.0" ] "model tier at 4.0.0 only"
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

            let failed = (step "compile-model-vfour" result).Outcome.ToString()
            Expect.stringStarts failed "failed: " "fails"
            Expect.stringContains failed "BoolLiteral" "names the member"
            Expect.stringContains failed "true" "quotes the expected value"
            Expect.equal ((step "compile-model-vthree" result).Outcome.ToString()) "passed" "v3 still passes"
            Expect.equal ((step "generate-model-vfour" result).Outcome.ToString()) "passed" "generation from the compiled model still runs"
        }

        test "A guest that accepts a wrong payload digest fails bad-digest" {
            let result, _ =
                run {
                    faithful with
                        AcceptBadDigest = true
                }

            Expect.stringStarts ((step "bad-digest" result).Outcome.ToString()) "failed: " "fails"
        }

        test "An initialize result whose extension differs from describe fails model-initialize" {
            let result, _ =
                run {
                    faithful with
                        InitializeVersion = "0.2.0"
                }

            Expect.stringStarts ((step "model-initialize" result).Outcome.ToString()) "failed: " "fails"
        }

        test "A guest that claims no model tuples fails describe and opens no model session" {
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
            Expect.stringContains ((step "describe" result).Outcome.ToString()) "compileModel" "describe names the missing tuple pair"
            Expect.isEmpty result.IrVersions "no usable broad version"
            Expect.isEmpty result.ModelIrVersions "no usable model version"
            Expect.equal opened.Length 1 "no model session"
        }

        test "A guest that claims only MEP 0.1 fails describe" {
            let result, _ =
                run {
                    faithful with
                        ProtocolVersions = [ "0.1" ]
                }

            Expect.isEmpty result.ModelIrVersions "no model tier"
            Expect.isEmpty result.IrVersions "no usable broad version"
            Expect.stringContains ((step "describe" result).Outcome.ToString()) "0.2.0-draft.1" "describe names the required protocol"
        }

        test "The adopted required plan retains moved cases and never calls legacy model methods" {
            let result, opened = run faithful

            match ModelTierPlan.checkDescribe kit.Value GuestKitPlanFixture.claimBoth with
            | Error reason -> failtest reason
            | Ok selection ->
                Expect.equal (ModelTierPlan.requiredCases selection) GuestKitPlanFixture.bothCases "all 21 adopted case ids and methods"

            // The data above does not imply execution. Public result migration and the internal runtime seam remain pending.
            Expect.isTrue result.ModelTierOnly "the transitional executor still cannot qualify the complete kit"

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

[<Tests>]
let textTests =
    testList "Generic guest kit result text (task 3.5)" [
        test "A step outcome's text starts with its wire value" {
            Expect.equal (StepOutcome.Passed.ToString()) "passed" "passed"
            Expect.equal ((StepOutcome.Failed "a difference").ToString()) "failed: a difference" "failed"
            Expect.equal ((StepOutcome.NotRun "a reason").ToString()) "not-run: a reason" "not run"

            for outcome in [ StepOutcome.Passed; StepOutcome.Failed "d"; StepOutcome.NotRun "r" ] do
                Expect.stringStarts (outcome.ToString()) outcome.WireValue outcome.WireValue
        }

        test "An issue of the whole kit file is written at /" {
            Expect.equal ({ Path = ""; Message = "No kit bytes were given." }.ToString()) "/: No kit bytes were given." "root"
            Expect.equal ({ Path = "/languages"; Message = "m" }.ToString()) "/languages: m" "member"

            match GuestKit.TryParse(null) with
            | false, _, failure -> Expect.equal failure "/: No kit bytes were given." "TryParse writes each issue's text"
            | true, _, _ -> failtest "null bytes parsed"
        }

        test "RequiredStep gives the step with its id and names a missing id" {
            let result, _ = run faithful

            Expect.isTrue (Object.ReferenceEquals((result.RequiredStep "describe"), (result.Step "describe"))) "the same step"

            try
                result.RequiredStep "no-such-step" |> ignore
                failtest "a missing step was returned"
            with :? ArgumentException as error ->
                Expect.stringContains error.Message "no-such-step" "the message names the id"
        }
    ]
