module Morphir.Ion.Tests.ModelDocumentTests

open System
open System.Numerics
open System.Text.Json
open Expecto
open Morphir.Ion
open Morphir.Ion.Tests
open Morphir.Json.V4

let private richBinary = "mep/fixtures/rich-model.ionb"
let private richText = "mep/fixtures/rich-model.ion"

let private limits = IonReadLimits.defaults

let private payload (data: byte array) =
    ModelDocument.readPayload limits (ReadOnlyMemory<byte> data)

let private file (data: byte array) =
    ModelDocument.readFile limits (ReadOnlyMemory<byte> data)

let private fileText (text: string) =
    ModelDocument.readFile limits (Fixtures.utf8 text)

let private succeeds what result =
    match result with
    | Ok value -> value
    | Error error -> failtestf "%s: expected success, got %s" what (ModelReadError.describe error)

let private failsWith code result =
    match result with
    | Ok _ -> failtestf "Expected %s, got success" code
    | Error error -> Expect.equal (ModelReadError.code error) code "error code"

let private rich () =
    payload (Fixtures.bytes richBinary)
    |> succeeds "rich model"

let private text value = IonSymbol.Text value

let private fields (value: IonValue) =
    match value.Data with
    | IonData.Struct fields -> fields
    | other -> failtestf "Expected a struct, got %A" other

let private field name value =
    fields value
    |> List.filter (fun (key, _) -> key = text name)
    |> List.map snd
    |> function
        | [ one ] -> one
        | other -> failtestf "Expected one field %s, got %d" name other.Length

/// A morphir_model text document with the given ir text.
let private withIr ir =
    $"morphir_model::{{ir: {ir}, enrichments: [], provenance: []}}"

let private irError (result: Result<IrDocument, IonIrError>) =
    match result with
    | Ok _ -> failtest "Expected an IR error"
    | Error error -> error

let private plainIr () =
    use document = JsonDocument.Parse(Fixtures.text "mep/fixtures/plain-ir.json")

    match V4Json.readDistribution document.RootElement with
    | Ok value -> value
    | Error error -> failtestf "plain-ir.json: %s" error.Message

let private caseLimits (id: string) =
    use document = JsonDocument.Parse(Fixtures.text "mep/model-transport.cases.json")

    let case =
        document.RootElement.GetProperty("cases").EnumerateArray()
        |> Seq.find (fun case -> case.GetProperty("id").GetString() = id)

    let limit =
        case
            .GetProperty("guestSupport")
            .GetProperty("modelTransport")
            .GetProperty("limits")

    let expected = case.GetProperty("expected").GetProperty("code").GetString()

    let content =
        case
            .GetProperty("request")
            .GetProperty("params")
            .GetProperty("model")
            .GetProperty("payload")
            .GetProperty("content")
            .GetString()

    {
        MaxDecodedBytes = limit.GetProperty("maxDecodedBytes").GetInt32()
        MaxDepth = limit.GetProperty("maxDepth").GetInt32()
        MaxNodes = limit.GetProperty("maxNodes").GetInt32()
    },
    expected,
    Convert.FromBase64String content

[<Tests>]
let payloads =
    testList "Payload reading requires binary Ion" [
        testCase "Reference payload"
        <| fun () ->
            let data = Fixtures.bytes richBinary
            Expect.equal data.Length 499 "fixture size"

            payload data
            |> succeeds "payload"
            |> ignore

        testCase "Text given as a payload"
        <| fun () ->
            payload (Fixtures.bytes richText)
            |> failsWith "invalid_model_payload"

        testCase "Text read as a local file"
        <| fun () ->
            let binary = rich ()

            let readable =
                file (Fixtures.bytes richText)
                |> succeeds "text file"

            Expect.equal readable.Ir binary.Ir "ir"
            Expect.equal readable.Enrichments binary.Enrichments "enrichments"
            Expect.equal readable.Provenance binary.Provenance "provenance"

        testCase "Two top-level values"
        <| fun () ->
            // 0x21 0x01 is the Ion int 1.
            let data = Array.append (Fixtures.bytes richBinary) [| 0x21uy; 0x01uy |]

            payload data
            |> failsWith "invalid_model_payload"

        testCase "A payload with no value"
        <| fun () ->
            payload [| 0xE0uy; 0x01uy; 0x00uy; 0xEAuy |]
            |> failsWith "invalid_model_payload"

        testCase "Malformed binary reports the Ion error"
        <| fun () ->
            payload [| 0xE0uy; 0x01uy; 0x00uy; 0xEAuy; 0xEEuy; 0xFFuy |]
            |> failsWith "malformed_ion"

        testCase "Another binary version marker is not a payload"
        <| fun () ->
            payload [| 0xE0uy; 0x01uy; 0x01uy; 0xEAuy |]
            |> failsWith "invalid_model_payload"
    ]

[<Tests>]
let shape =
    testList "The document shape is checked" [
        testCase "Missing annotation"
        <| fun () ->
            fileText "{ir: {}, enrichments: [], provenance: []}"
            |> failsWith "invalid_model_document"

        testCase "Another annotation"
        <| fun () ->
            fileText "other::{ir: {}, enrichments: [], provenance: []}"
            |> failsWith "invalid_model_document"

        testCase "Top-level value is not a struct"
        <| fun () ->
            fileText "morphir_model::[1]"
            |> failsWith "invalid_model_document"

        testCase "Repeated reserved field"
        <| fun () ->
            fileText "morphir_model::{ir: {}, ir: {}, enrichments: [], provenance: []}"
            |> failsWith "invalid_model_document"

        testCase "Missing reserved field"
        <| fun () ->
            fileText "morphir_model::{ir: {}, enrichments: []}"
            |> failsWith "invalid_model_document"

        testCase "Repeated names in unknown data"
        <| fun () ->
            let model =
                fileText
                    "morphir_model::{ir: {}, enrichments: [{owner: \"urn:x\", data: {a: 1, a: 2}}], provenance: []}"
                |> succeeds "repeated names"

            match model.Enrichments with
            | [ enrichment ] ->
                Expect.equal
                    (fields enrichment.Data
                     |> List.map fst)
                    [ text "a"; text "a" ]
                    "both fields kept"
            | other -> failtestf "Expected one enrichment, got %A" other

        testCase "Enrichment without an owner"
        <| fun () ->
            fileText "morphir_model::{ir: {}, enrichments: [{data: 1}], provenance: []}"
            |> failsWith "invalid_model_document"

        testCase "Enrichment with an empty owner"
        <| fun () ->
            fileText
                "morphir_model::{ir: {}, enrichments: [{owner: \"\", data: 1}], provenance: []}"
            |> failsWith "invalid_model_document"

        testCase "Enrichment owner that is not namespaced"
        <| fun () ->
            fileText
                "morphir_model::{ir: {}, enrichments: [{owner: \"plain\", data: 1}], provenance: []}"
            |> failsWith "invalid_model_document"

        testCase "Enrichment without data"
        <| fun () ->
            fileText "morphir_model::{ir: {}, enrichments: [{owner: \"urn:x\"}], provenance: []}"
            |> failsWith "invalid_model_document"

        testCase "Enrichments that are not a list"
        <| fun () ->
            fileText "morphir_model::{ir: {}, enrichments: {}, provenance: []}"
            |> failsWith "invalid_model_document"

        testCase "Unknown top-level members are kept in the bytes"
        <| fun () ->
            let data =
                Fixtures.utf8 "morphir_model::{ir: {}, enrichments: [], provenance: [], extra: 1}"

            let model =
                ModelDocument.readFile limits data
                |> succeeds "extra member"

            Expect.equal (model.Bytes.ToArray()) (data.ToArray()) "bytes"
    ]

[<Tests>]
let retention =
    testList "Reading retains the model exactly" [
        testCase "Original bytes"
        <| fun () ->
            let model = rich ()

            Expect.equal
                (Fixtures.sha256 (model.Bytes.ToArray()))
                "49f87687c033a8ec39a7f42b93bb1c54b4856ceab0b05246d81ea0a8b27e10b1"
                "sha256"

        testCase "Bytes are a copy, so later changes to the caller's array do not reach them"
        <| fun () ->
            let data = Fixtures.bytes richBinary
            let before = Array.copy data

            let model =
                payload data
                |> succeeds "payload"

            let fromFile =
                file data
                |> succeeds "file"

            for index in
                0 .. data.Length
                     - 1 do
                data[index] <- 0uy

            Expect.equal (model.Bytes.ToArray()) before "payload bytes"
            Expect.equal (fromFile.Bytes.ToArray()) before "file bytes"

            Expect.equal
                (Fixtures.sha256 (model.Bytes.ToArray()))
                "49f87687c033a8ec39a7f42b93bb1c54b4856ceab0b05246d81ea0a8b27e10b1"
                "digest"

        testCase "Unknown enrichment"
        <| fun () ->
            let model = rich ()

            match model.Enrichments with
            | [ enrichment ] ->
                Expect.equal enrichment.Owner "urn:morphir:mep:fixture:unknown-enrichment" "owner"

                Expect.equal
                    enrichment.Data.Annotations
                    [ text "source_name"; text "target_name" ]
                    "annotations in order"

                let precision = field "sourcePrecision" enrichment.Data

                Expect.equal
                    (field "exact" precision).Data
                    (IonData.Int(BigInteger.Parse "9007199254740993"))
                    "exact"

                Expect.equal
                    (field "amount" enrichment.Data).Data
                    (IonData.Decimal
                        {
                            Coefficient = BigInteger 1234500
                            Scale = 4
                            IsNegativeZero = false
                        })
                    "amount keeps its scale"

                Expect.equal (field "kind" precision).Data (IonData.Symbol(text "int64")) "kind"

                Expect.equal
                    (field "futureFact" enrichment.Data).Annotations
                    [ text "future_vocabulary" ]
                    "futureFact annotation"
            | other -> failtestf "Expected one enrichment, got %A" other

        testCase "Provenance"
        <| fun () ->
            match (rich ()).Provenance with
            | [ value ] -> Expect.equal value.Annotations [ text "fixture_origin" ] "annotation"
            | other -> failtestf "Expected one value, got %A" other
    ]

[<Tests>]
let limitCases =
    testList "Limits apply to the document" [
        for id in [ "model-0012"; "model-0013"; "model-0014" ] do
            testCase $"Corpus limit case {id}"
            <| fun () ->
                let caseLimits, expected, data = caseLimits id
                Expect.equal data (Fixtures.bytes richBinary) "case payload is the fixture"

                match ModelDocument.readPayload caseLimits (ReadOnlyMemory<byte> data) with
                | Ok _ -> failtestf "Expected %s" expected
                | Error error -> Expect.equal (ModelReadError.code error) expected "corpus code"

        testCase "Limits also apply to a local file"
        <| fun () ->
            match
                ModelDocument.readFile
                    { limits with MaxNodes = 1 }
                    (ReadOnlyMemory<byte>(Fixtures.bytes richText))
            with
            | Error(ModelReadError.Read(IonReadError.NodeLimitExceeded 1)) -> ()
            | other -> failtestf "Unexpected %A" other
    ]

[<Tests>]
let irView =
    testList "The IR has a typed view" [
        testCase "Reference IR"
        <| fun () ->
            match ModelDocument.decodeIr "4.0.0" (rich ()) with
            | Ok(V4Ir(version, distribution)) ->
                let expected = plainIr ()
                Expect.equal version "4.0.0" "version"
                Expect.equal distribution expected.Distribution "distribution equals plain-ir.json"
            | other -> failtestf "Unexpected %A" other

        testCase "Version mismatch"
        <| fun () ->
            let error =
                ModelDocument.decodeIr "3.0.0" (rich ())
                |> irError

            Expect.equal error.Code "ir_version_mismatch" "code"

        testCase "Missing format version"
        <| fun () ->
            let model =
                fileText (withIr "{distribution: {}}")
                |> succeeds "model"

            let error =
                ModelDocument.decodeIr "4.0.0" model
                |> irError

            Expect.equal error.Code "missing_format_version" "code"

        testCase "Integer format version 4 means 4.0.0"
        <| fun () ->
            let model =
                fileText (
                    withIr
                        "{formatVersion: 4, distribution: {Library: {packageName: \"a/b\", dependencies: {}, def: {modules: {}}}}}"
                )
                |> succeeds "model"

            match ModelDocument.decodeIr "4.0.0" model with
            | Ok(V4Ir("4.0.0", _)) -> ()
            | other -> failtestf "Unexpected %A" other

        testCase "Classic IR"
        <| fun () ->
            let model =
                fileText (
                    withIr
                        "{formatVersion: 3, distribution: [\"Library\", [[\"car\"]], [], {modules: []}]}"
                )
                |> succeeds "model"

            match ModelDocument.decodeIr "3.0.0" model with
            | Ok(ClassicIr("3.0.0", _)) -> ()
            | other -> failtestf "Unexpected %A" other

        testCase "A JSON reader error carries its rule and location"
        <| fun () ->
            let model =
                fileText (withIr "{formatVersion: \"4.0.0\", distribution: 5}")
                |> succeeds "model"

            let error =
                ModelDocument.decodeIr "4.0.0" model
                |> irError

            Expect.isNotEmpty error.Code "code"
            Expect.isSome error.Location "location"

        testCase "Ion-only data in the IR member"
        <| fun () ->
            let model =
                fileText (withIr "{formatVersion: \"4.0.0\", when: 2020-01-01T}")
                |> succeeds "model"

            let error =
                ModelDocument.decodeIr "4.0.0" model
                |> irError

            Expect.equal error.Code "ir_not_json_compatible" "code"
            Expect.isSome error.Path "names the location"

            Expect.equal
                (ModelDocument.readFile limits model.Bytes
                 |> Result.isOk)
                true
                "the model stays readable"

        testCase "Repeated JSON member in the IR"
        <| fun () ->
            let model =
                fileText (withIr "{formatVersion: \"4.0.0\", distribution: {a: 1, a: 2}}")
                |> succeeds "model"

            let error =
                ModelDocument.decodeIr "4.0.0" model
                |> irError

            Expect.equal error.Code "ir_not_json_compatible" "code"

        testCase "IR deeper than 64 levels decodes within the readers' limit"
        <| fun () ->
            // Classic attributes are arbitrary JSON, so they can nest as deeply as the reader allows.
            let attributes =
                String.replicate 100 "{a: "
                + "1"
                + String.replicate 100 "}"

            let model =
                fileText (
                    withIr
                        $"{{formatVersion: 3, distribution: [\"Library\", [[\"car\"]], [], {{modules: [[[[\"m\"]], {{access: \"Public\", value: {{types: [], values: [[[\"v\"], {{access: \"Public\", value: {{inputTypes: [], outputType: [\"Reference\", {{}}, [[[\"morphir\"], [\"s\", \"d\", \"k\"]], [[\"basics\"]], [\"int\"]], []], body: [\"Literal\", {attributes}, [\"WholeNumberLiteral\", 5000]]}}}}]]}}}}]]}}]}}"
                )
                |> succeeds "model"

            match ModelDocument.decodeIr "3.0.0" model with
            | Ok(ClassicIr("3.0.0", _)) -> ()
            | other -> failtestf "Unexpected %A" other

        testCase "IR that is too deep is refused with the depth code"
        <| fun () ->
            let nested =
                String.replicate 600 "["
                + String.replicate 600 "]"

            let model =
                ModelDocument.readFile
                    { limits with MaxDepth = 2000 }
                    (Fixtures.utf8 (withIr $"{{formatVersion: \"4.0.0\", x: {nested}}}"))
                |> succeeds "model"

            let error =
                ModelDocument.decodeIr "4.0.0" model
                |> irError

            Expect.equal error.Code "depth_limit_exceeded" "code"

        testCase "An annotated ir member is refused by decodeIr"
        <| fun () ->
            let model =
                fileText (withIr "tag::{formatVersion: \"4.0.0\"}")
                |> succeeds "model"

            let error =
                ModelDocument.decodeIr "4.0.0" model
                |> irError

            Expect.equal error.Code "ir_not_json_compatible" "code"

        testCase "Provenance that is not a list"
        <| fun () ->
            fileText "morphir_model::{ir: {}, enrichments: [], provenance: {}}"
            |> failsWith "invalid_model_document"

        testCase "Enrichment keeps the annotations of its own struct"
        <| fun () ->
            let model =
                fileText
                    "morphir_model::{ir: {}, enrichments: [a::b::{owner: \"urn:x\", data: 1}], provenance: []}"
                |> succeeds "model"

            match model.Enrichments with
            | [ enrichment ] ->
                Expect.equal enrichment.Annotations [ text "a"; text "b" ] "annotations"
            | other -> failtestf "Unexpected %A" other
    ]

let private project text =
    match IonReader.read limits (Fixtures.utf8 text) with
    | Ok [ value ] -> IrProjection.toJson value
    | other -> failtestf "Could not read %s: %A" text other

let private ion data : IonValue = { Annotations = []; Data = data }
let private ionText value = ion (IonData.String value)
let private ionList items = ion (IonData.List items)

let private ionStruct fields =
    ion (
        IonData.Struct(
            fields
            |> List.map (fun (name, value) -> text name, value)
        )
    )

/// A model whose v3 ir has one type alias that nests a function type this many levels. It is built with a
/// loop, so building it needs no deep stack.
let private deepV3Model levels : ModelDocument =
    let unit = ionList [ ionText "Unit"; ionStruct [] ]
    let mutable deep = unit

    for _ in 1..levels do
        deep <- ionList [ ionText "Function"; ionStruct []; deep; unit ]

    let alias =
        ionStruct [
            "access", ionText "Public"
            "value",
            ionStruct [
                "doc", ionText ""
                "value", ionList [ ionText "TypeAliasDefinition"; ionList []; deep ]
            ]
        ]

    let moduleValue =
        ionStruct [
            "access", ionText "Public"
            "value",
            ionStruct [
                "values", ionList []
                "types", ionList [ ionList [ ionList [ ionText "t" ]; alias ] ]
            ]
        ]

    let ir =
        ionStruct [
            "formatVersion", ion (IonData.Int(BigInteger 3))
            "distribution",
            ionList [
                ionText "Library"
                ionList [ ionList [ ionText "p" ] ]
                ionList []
                ionStruct [
                    "modules",
                    ionList [ ionList [ ionList [ ionList [ ionText "m" ] ]; moduleValue ] ]
                ]
            ]
        ]

    {
        Bytes = ReadOnlyMemory<byte>.Empty
        Ir = ir
        Enrichments = []
        Provenance = []
    }

[<Tests>]
let deepJson =
    testList "A deep v3 ir is refused or read, never fatal" [
        testCase "3,000 levels through decodeIr"
        <| fun () ->
            match ModelDocument.decodeIr "3.0.0" (deepV3Model 3000) with
            | Ok(ClassicIr _) -> ()
            | Error error -> Expect.equal error.Code "depth_limit_exceeded" error.Message
            | other -> failtestf "Unexpected %A" other

        testCase "3,000 levels through the classic reader"
        <| fun () ->
            let json =
                "{\"formatVersion\": 3, \"distribution\": [\"Library\", [[\"p\"]], [], {\"modules\": [[[[\"m\"]], {\"access\": \"Public\", \"value\": {\"values\": [], \"types\": [[[\"t\"], {\"access\": \"Public\", \"value\": {\"doc\": \"\", \"value\": [\"TypeAliasDefinition\", [], "
                + String.replicate 3000 "[\"Function\", {}, "
                + "[\"Unit\", {}]"
                + String.replicate 3000 ", [\"Unit\", {}]]"
                + "]}}]]}}]]}]}"

            use document = JsonDocument.Parse(json, JsonDocumentOptions(MaxDepth = 4096))

            match Morphir.Json.Classic.ClassicJson.readDistribution document.RootElement with
            | Ok _ -> ()
            | Error error -> Expect.equal error.Rule "depth_limit_exceeded" error.Message
    ]

[<Tests>]
let projection =
    testList "JSON projection" [
        testCase "Maps structs, lists, strings, bools, ints, decimals and null"
        <| fun () ->
            Expect.equal
                (project
                    "{a: [1, -2, true, \"x\", null, 123.4500, 5d-2, 12d3, -0.00, 9007199254740993]}")
                (Ok "{\"a\":[1,-2,true,\"x\",null,123.4500,0.05,12e3,-0.00,9007199254740993]}")
                "projection"

        testCase "Decimal text is exact"
        <| fun () ->
            for source, expected in
                [
                    "12.", "12e0"
                    "12d0", "12e0"
                    "0d0", "0e0"
                    "-0d0", "-0e0"
                    "0d3", "0e3"
                    "-0d3", "-0e3"
                    "0.0", "0.0"
                    "-0.0", "-0.0"
                    "-0.05", "-0.05"
                    "-12d3", "-12e3"
                    "123.4500", "123.4500"
                ] do
                Expect.equal (project $"{{a: {source}}}") (Ok $"{{\"a\":{expected}}}") source

        testCase "Padding up to 64 zeros stays positional"
        <| fun () ->
            let zeros = String.replicate 64 "0"
            Expect.equal (project "{a: 5d-65}") (Ok $"{{\"a\":0.{zeros}5}}") "64 zeros"
            Expect.equal (project "{a: 5d-66}") (Ok "{\"a\":5e-66}") "65 zeros use an exponent"

        testCase "A huge scale does not allocate or raise"
        <| fun () ->
            // The Ion text reader stops at scale 1000, so these values are built directly.
            let decimalIn coefficient scale = {
                Annotations = []
                Data =
                    IonData.Struct [
                        text "a",
                        {
                            Annotations = []
                            Data =
                                IonData.Decimal
                                    {
                                        Coefficient = BigInteger(coefficient: int)
                                        Scale = scale
                                        IsNegativeZero = false
                                    }
                        }
                    ]
            }

            Expect.equal
                (IrProjection.toJson (decimalIn 5 2000000000))
                (Ok "{\"a\":5e-2000000000}")
                "large positive scale"

            Expect.equal
                (IrProjection.toJson (decimalIn -5 -2000000000))
                (Ok "{\"a\":-5e2000000000}")
                "large negative scale"

            Expect.equal
                (IrProjection.toJson (decimalIn 5 Int32.MinValue))
                (Ok "{\"a\":5e2147483648}")
                "negating the smallest scale does not overflow"

        testCase "Refused kinds name their location"
        <| fun () ->
            for source in
                [
                    "{a: {b: x::1}}"
                    "{a: [sym]}"
                    "{a: (1 2)}"
                    "{a: 1e0}"
                    "{a: 2020-01-01T}"
                    "{a: {{aGk=}}}"
                    "{a: {{\"hi\"}}}"
                    "{a: null.int}"
                    "{a: $0}"
                    "{a: 1, a: 2}"
                    "x::{a: 1}"
                ] do
                match project source with
                | Error error ->
                    Expect.equal error.Code "ir_not_json_compatible" source
                    Expect.isSome error.Path source
                | Ok text -> failtestf "%s gave %s" source text
    ]

/// The sample in src/Morphir.Ion/README.md. Keep the two the same.
module private ReadmeSample =
    let describe (payload: ReadOnlyMemory<byte>) : string =
        match ModelDocument.readPayload IonReadLimits.defaults payload with
        | Error error -> $"{ModelReadError.code error}: {ModelReadError.describe error}"
        | Ok model ->
            // model.Bytes is the payload, unchanged. Enrichments and provenance are Ion values.
            let retained = $"{model.Bytes.Length} bytes, {model.Enrichments.Length} enrichments"

            match ModelDocument.decodeIr "4.0.0" model with
            | Ok(V4Ir(version, _)) -> $"{retained}, IR {version}"
            | Ok(ClassicIr(version, _)) -> $"{retained}, classic IR {version}"
            | Error error -> $"{retained}, IR not decoded ({error.Code})"

[<Tests>]
let readme =
    testList "README sample" [
        testCase "describes the reference payload"
        <| fun () ->
            Expect.equal
                (ReadmeSample.describe (ReadOnlyMemory<byte>(Fixtures.bytes richBinary)))
                "499 bytes, 1 enrichments, IR 4.0.0"
                "reference"

        testCase "reports a refused payload"
        <| fun () ->
            let message = ReadmeSample.describe (ReadOnlyMemory<byte>(Fixtures.bytes richText))
            Expect.stringStarts message "invalid_model_payload:" "text is not a payload"
    ]
