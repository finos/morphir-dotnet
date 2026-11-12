// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Fixed parent-plan expectations and a recording test peer. No worker or real guest is started here.
module Morphir.Conformance.Tests.GuestKitPlanFixture

open System
open System.Collections.Generic
open System.Text
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.Mep
open Morphir.Conformance.GuestKit
open Morphir.Conformance.WasmHost
open Morphir.Conformance.Tests.GuestKitFakeGuest

let parse text =
    match Json.parse text with
    | Ok value -> value
    | Error message -> failtest message

// Authored from TIER-1, not from ModelTierPlan or a recorded runner result.
let bothCases = [
    "describe", "morphir.extension.describe"
    "model-initialize", "morphir.initialize"
    "model-initialized-silent", "morphir.initialized"
    "unknown-notification-silent", "morphir.mck.unknown"
    "compile-model-vthree", "morphir.frontend.compileModel"
    "generate-model-vthree", "morphir.backend.generateModel"
    "roundtrip-model-vthree", "morphir.frontend.compileModel"
    "generate-model-independent-vthree", "morphir.backend.generateModel"
    "roundtrip-model-independent-vthree", "morphir.frontend.compileModel"
    "compile-model-vfour", "morphir.frontend.compileModel"
    "generate-model-vfour", "morphir.backend.generateModel"
    "roundtrip-model-vfour", "morphir.frontend.compileModel"
    "generate-model-independent-vfour", "morphir.backend.generateModel"
    "roundtrip-model-independent-vfour", "morphir.frontend.compileModel"
    "bad-digest", "morphir.backend.generateModel"
    "malformed-source", "morphir.frontend.compileModel"
    "unknown-method", "morphir.mck.unknown"
    "describe-immutable", "morphir.extension.describe"
    "model-shutdown", "morphir.shutdown"
    "after-shutdown", "morphir.ping"
    "model-exit", "morphir.exit"
]

let threeCases = [
    "describe", "morphir.extension.describe"
    "model-initialize", "morphir.initialize"
    "model-initialized-silent", "morphir.initialized"
    "unknown-notification-silent", "morphir.mck.unknown"
    "compile-model-vthree", "morphir.frontend.compileModel"
    "generate-model-vthree", "morphir.backend.generateModel"
    "roundtrip-model-vthree", "morphir.frontend.compileModel"
    "generate-model-independent-vthree", "morphir.backend.generateModel"
    "roundtrip-model-independent-vthree", "morphir.frontend.compileModel"
    "bad-digest", "morphir.backend.generateModel"
    "malformed-source", "morphir.frontend.compileModel"
    "unknown-method", "morphir.mck.unknown"
    "describe-immutable", "morphir.extension.describe"
    "model-shutdown", "morphir.shutdown"
    "after-shutdown", "morphir.ping"
    "model-exit", "morphir.exit"
]

let fourCases = [
    "describe", "morphir.extension.describe"
    "model-initialize", "morphir.initialize"
    "model-initialized-silent", "morphir.initialized"
    "unknown-notification-silent", "morphir.mck.unknown"
    "compile-model-vfour", "morphir.frontend.compileModel"
    "generate-model-vfour", "morphir.backend.generateModel"
    "roundtrip-model-vfour", "morphir.frontend.compileModel"
    "generate-model-independent-vfour", "morphir.backend.generateModel"
    "roundtrip-model-independent-vfour", "morphir.frontend.compileModel"
    "bad-digest", "morphir.backend.generateModel"
    "malformed-source", "morphir.frontend.compileModel"
    "unknown-method", "morphir.mck.unknown"
    "describe-immutable", "morphir.extension.describe"
    "model-shutdown", "morphir.shutdown"
    "after-shutdown", "morphir.ping"
    "model-exit", "morphir.exit"
]

let tuple operation version =
    obj [
        "operation", str operation
        "profile", str "morphir-ir"
        "codec", str "morphir-json"
        "envelopeVersion", str "0.1.0-draft.1"
        "irVersion", str version
    ]

let threeTuples = [
    tuple "morphir.frontend.compileModel" "3.0.0"
    tuple "morphir.backend.generateModel" "3.0.0"
]

let fourTuples = [
    tuple "morphir.frontend.compileModel" "4.0.0"
    tuple "morphir.backend.generateModel" "4.0.0"
]

let limits =
    parse
        """{"maxEncodedBytes":1000000,"maxDecodedBytes":750000,"maxDepth":128,"maxNodes":100000}"""

let claimBoth =
    parse
        """{"claimsVersion":"0.1.0-draft.2","protocolVersions":["0.2.0-draft.1"],"extension":{"id":"morphir-plan-fixture","name":"Plan fixture","version":"0.1.0","types":["frontend","backend"]},"capabilities":{"frontend":{"languages":[{"id":"python","fileExtensions":[".py"]}],"irVersions":["3","4"],"compile":true,"incremental":false,"fragments":false},"backend":{"targets":["python"],"irVersions":["3","4"],"generate":true},"modelTransport":{"tuples":[{"operation":"morphir.frontend.compileModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"3.0.0"},{"operation":"morphir.backend.generateModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"3.0.0"},{"operation":"morphir.frontend.compileModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"4.0.0"},{"operation":"morphir.backend.generateModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"4.0.0"}],"limits":{"maxEncodedBytes":1000000,"maxDecodedBytes":750000,"maxDepth":128,"maxNodes":100000},"guarantees":[]}}}"""

let private versions side values claim =
    replaceAt [ "capabilities"; side; "irVersions" ] (arr (List.map str values)) claim

let private tuples values claim =
    replaceAt [ "capabilities"; "modelTransport"; "tuples" ] (arr values) claim

let claimThree =
    claimBoth
    |> versions "frontend" [ "3" ]
    |> versions "backend" [ "3" ]
    |> tuples threeTuples

let claimFour =
    claimBoth
    |> versions "frontend" [ "4.0.0" ]
    |> versions "backend" [ "4.0.0" ]
    |> tuples fourTuples

let claimPairOnlyFour =
    claimBoth
    |> tuples fourTuples

let claimDualProtocol =
    claimBoth
    |> replaceAt [ "protocolVersions" ] (arr [ str "0.1"; str "0.2.0-draft.1" ])

type SelectionVector = {
    Name: string
    Claim: JsonValue
    Versions: string list
    Cases: (string * string) list
    DescribeSucceeds: bool
}

let selectionVectors = [
    {
        Name = "Both versions select the fixed 21-case plan"
        Claim = claimBoth
        Versions = [ "3.0.0"; "4.0.0" ]
        Cases = bothCases
        DescribeSucceeds = true
    }
    {
        Name = "IR 3 alone selects the fixed 16-case plan"
        Claim = claimThree
        Versions = [ "3.0.0" ]
        Cases = threeCases
        DescribeSucceeds = true
    }
    {
        Name = "IR 4 alone selects the fixed 16-case plan"
        Claim = claimFour
        Versions = [ "4.0.0" ]
        Cases = fourCases
        DescribeSucceeds = true
    }
    {
        Name = "Backend claim excludes IR 3 despite both tuple pairs"
        Claim =
            claimBoth
            |> versions "backend" [ "4" ]
        Versions = [ "4.0.0" ]
        Cases = fourCases
        DescribeSucceeds = true
    }
    {
        Name = "Frontend claim excludes IR 4 despite both tuple pairs"
        Claim =
            claimBoth
            |> versions "frontend" [ "3" ]
        Versions = [ "3.0.0" ]
        Cases = threeCases
        DescribeSucceeds = true
    }
    {
        Name = "Only the IR 4 tuple pair selects IR 4"
        Claim = claimPairOnlyFour
        Versions = [ "4.0.0" ]
        Cases = fourCases
        DescribeSucceeds = true
    }
    {
        Name = "Missing IR 3 compile tuple excludes IR 3"
        Claim =
            claimBoth
            |> tuples (
                tuple "morphir.backend.generateModel" "3.0.0"
                :: fourTuples
            )
        Versions = [ "4.0.0" ]
        Cases = fourCases
        DescribeSucceeds = true
    }
    {
        Name = "Missing IR 3 generate tuple excludes IR 3"
        Claim =
            claimBoth
            |> tuples (
                tuple "morphir.frontend.compileModel" "3.0.0"
                :: fourTuples
            )
        Versions = [ "4.0.0" ]
        Cases = fourCases
        DescribeSucceeds = true
    }
    {
        Name = "Unpaired tuples fail describe"
        Claim =
            claimBoth
            |> tuples [
                tuple "morphir.frontend.compileModel" "3.0.0"
                tuple "morphir.backend.generateModel" "4.0.0"
            ]
        Versions = []
        Cases = [ "describe", "morphir.extension.describe" ]
        DescribeSucceeds = false
    }
    {
        Name = "Legacy-only protocol fails describe"
        Claim =
            claimBoth
            |> replaceAt [ "protocolVersions" ] (arr [ str "0.1" ])
        Versions = []
        Cases = [ "describe", "morphir.extension.describe" ]
        DescribeSucceeds = false
    }
    {
        Name = "A dual-protocol claim keeps the required 21-case plan"
        Claim = claimDualProtocol
        Versions = [ "3.0.0"; "4.0.0" ]
        Cases = bothCases
        DescribeSucceeds = true
    }
    {
        Name = "False legacy flags do not exclude model tuples"
        Claim =
            claimBoth
            |> replaceAt [ "capabilities"; "frontend"; "compile" ] (JsonValue.Bool false)
            |> replaceAt [ "capabilities"; "backend"; "generate" ] (JsonValue.Bool false)
        Versions = [ "3.0.0"; "4.0.0" ]
        Cases = bothCases
        DescribeSucceeds = true
    }
]

// Canonical encodings of the FIXED kit JSON data, prepared statically, not from a runner or compiler.
let private payloadThree =
    parse
        """{"codec":"morphir-json","layout":"single-document","decodedByteLength":712,"sha256":"2152c9c42b03006e2c0d0e708e48ec0b24ebe07277a99a5ca8368c2e8ee96528","content":"eyJkaXN0cmlidXRpb24iOlsiTGlicmFyeSIsW1sibWNrIl0sWyJnZW5lcmljIl1dLFtdLHsibW9kdWxlcyI6W1tbWyJtYWluIl1dLHsiYWNjZXNzIjoiUHVibGljIiwidmFsdWUiOnsiZG9jIjpudWxsLCJ0eXBlcyI6W10sInZhbHVlcyI6W1tbImJsb2NrZWQiXSx7ImFjY2VzcyI6IlB1YmxpYyIsInZhbHVlIjp7ImRvYyI6IiIsInZhbHVlIjp7ImJvZHkiOlsiTGl0ZXJhbCIsWyJSZWZlcmVuY2UiLHt9LFtbWyJtb3JwaGlyIl0sWyJzIiwiZCIsImsiXV0sW1siYmFzaWNzIl1dLFsiYm9vbCJdXSxbXV0sWyJCb29sTGl0ZXJhbCIsZmFsc2VdXSwiaW5wdXRUeXBlcyI6W10sIm91dHB1dFR5cGUiOlsiUmVmZXJlbmNlIix7fSxbW1sibW9ycGhpciJdLFsicyIsImQiLCJrIl1dLFtbImJhc2ljcyJdXSxbImJvb2wiXV0sW11dfX19XSxbWyJyZWFkeSJdLHsiYWNjZXNzIjoiUHVibGljIiwidmFsdWUiOnsiZG9jIjoiIiwidmFsdWUiOnsiYm9keSI6WyJMaXRlcmFsIixbIlJlZmVyZW5jZSIse30sW1tbIm1vcnBoaXIiXSxbInMiLCJkIiwiayJdXSxbWyJiYXNpY3MiXV0sWyJib29sIl1dLFtdXSxbIkJvb2xMaXRlcmFsIix0cnVlXV0sImlucHV0VHlwZXMiOltdLCJvdXRwdXRUeXBlIjpbIlJlZmVyZW5jZSIse30sW1tbIm1vcnBoaXIiXSxbInMiLCJkIiwiayJdXSxbWyJiYXNpY3MiXV0sWyJib29sIl1dLFtdXX19fV1dfX1dXX1dLCJmb3JtYXRWZXJzaW9uIjozfQ=="}"""

let private payloadFour =
    parse
        """{"codec":"morphir-json","layout":"single-document","decodedByteLength":429,"sha256":"e4e8fdf89588221a413e8b1f73b07a6e1a576fd7ccdcc8e5ea3cfdf0d31cf8ae","content":"eyJkaXN0cmlidXRpb24iOnsiTGlicmFyeSI6eyJkZWYiOnsibW9kdWxlcyI6eyJtYWluIjp7IlB1YmxpYyI6eyJ0eXBlcyI6e30sInZhbHVlcyI6eyJibG9ja2VkIjp7IlB1YmxpYyI6eyJFeHByZXNzaW9uQm9keSI6eyJib2R5Ijp7IkxpdGVyYWwiOnsiQm9vbExpdGVyYWwiOmZhbHNlfX0sImlucHV0VHlwZXMiOnt9LCJvdXRwdXRUeXBlIjoibW9ycGhpci9TREs6YmFzaWNzI2Jvb2wifX19LCJyZWFkeSI6eyJQdWJsaWMiOnsiRXhwcmVzc2lvbkJvZHkiOnsiYm9keSI6eyJMaXRlcmFsIjp7IkJvb2xMaXRlcmFsIjp0cnVlfX0sImlucHV0VHlwZXMiOnt9LCJvdXRwdXRUeXBlIjoibW9ycGhpci9TREs6YmFzaWNzI2Jvb2wifX19fX19fX0sImRlcGVuZGVuY2llcyI6e30sInBhY2thZ2VOYW1lIjoibWNrL2dlbmVyaWMifX0sImZvcm1hdFZlcnNpb24iOjR9"}"""

let model version independent =
    obj [
        "envelopeVersion", str "0.1.0-draft.1"
        "modelId",
        str (
            if independent then
                "urn:morphir:mck:generic"
            else
                "urn:plan-fixture:compiled"
        )
        "revision", str (if independent then "independent-1" else "fixed-1")
        "irVersion", str version
        "profile", str "morphir-ir"
        "payload", (if version = "3.0.0" then payloadThree else payloadFour)
    ]

let private malformedSource () =
    kitJson ()
    |> at [ "languages"; "python"; "malformed" ]

let sourceDocuments malformed =
    let documents =
        if malformed then
            malformedSource ()
        else
            kitJson ()
            |> at [ "languages"; "python"; "documents" ]

    match documents with
    | JsonValue.Array documents ->
        arr (
            documents
            |> List.map (fun document ->
                obj [
                    "uri", at [ "uri" ] document
                    "languageId", str "python"
                    "version", num 1
                    "text", at [ "text" ] document
                ]
            )
        )
    | _ -> failtest "fixed source documents are not an array"

let compileRequest version malformed =
    obj [
        "languageId", str "python"
        "sources", obj [ "documents", sourceDocuments malformed ]
        "package", parse """{"name":"mck/generic","exposedModules":["main"]}"""
        "options", obj [ "typesOnly", JsonValue.Bool false; "irVersion", str version ]
        "contextDigest", str (String('c', 64))
        "output", tuple "morphir.frontend.compileModel" version
    ]

let private generateRequest version independent badDigest =
    let value = model version independent

    let value =
        if badDigest then
            value
            |> replaceAt [ "payload"; "sha256" ] (str (String('0', 64)))
        else
            value

    obj [
        "model", value
        "input", tuple "morphir.backend.generateModel" version
        "target", str "python"
        "contextDigest", str (String('c', 64))
    ]

let private protocols = obj [ "protocolVersions", arr [ str "0.2.0-draft.1" ] ]

// Explicit params:{} is this fixture's canonical sender choice, retained from the parent runner.
// It is not a protocol requirement or permission to change ordinary HostSession defaults.
// Observations retain the actual bytes, including absent params, for consumer reconciliation.
let expectedWire (cases: (string * string) list) offeredTuples firstVersion =
    let mutable requestId = 0

    cases
    |> List.map (fun (id, method) ->
        let notification =
            List.contains id [
                "model-initialized-silent"
                "unknown-notification-silent"
                "model-exit"
            ]

        let version =
            if id.EndsWith("-vthree", StringComparison.Ordinal) then
                "3.0.0"
            else
                "4.0.0"

        let parameters =
            match id with
            | "describe"
            | "describe-immutable" -> protocols
            | "model-initialize" ->
                obj [
                    "protocolVersions", arr [ str "0.2.0-draft.1" ]
                    "host",
                    parse """{"kind":"cli","name":"morphir-conformance-tests","version":"0.0.0"}"""
                    "modelTransport",
                    obj [ "tuples", arr offeredTuples; "limits", limits; "guarantees", arr [] ]
                ]
            | "malformed-source" -> compileRequest firstVersion true
            | "bad-digest" -> generateRequest firstVersion true true
            | _ when method = "morphir.frontend.compileModel" -> compileRequest version false
            | _ when method = "morphir.backend.generateModel" ->
                generateRequest
                    version
                    (id.StartsWith("generate-model-independent", StringComparison.Ordinal))
                    false
            | _ -> obj []

        let fields = [ "jsonrpc", str "2.0"; "method", str method; "params", parameters ]

        if notification then
            obj fields
        else
            requestId <-
                requestId
                + 1

            obj (
                ("id", num requestId)
                :: fields
            )
    )

type WireExchange = {
    Session: int
    Notification: bool
    RequestBytes: byte array
    ReplyBytes: byte array option
}

/// This records delivery to a SCRIPTED TEST PEER only. It cannot prove worker-to-WASM delivery.
type private RecordingPeer
    (
        claim: JsonValue,
        session: int,
        exchanges: List<WireExchange>,
        edit: JsonValue -> JsonValue -> JsonValue
    ) =
    let mutable shutDown = false

    let answer method parameters =
        if shutDown then
            Error(-32014, "The scripted peer is shut down.")
        else
            match method with
            | "morphir.extension.describe" -> Ok claim
            | "morphir.initialize" ->
                let capabilities =
                    claim
                    |> at [ "capabilities" ]
                    |> replaceAt
                        [ "modelTransport" ]
                        (parameters
                         |> at [ "modelTransport" ])

                Ok(
                    obj [
                        "protocolVersion", str "0.2.0-draft.1"
                        "extension",
                        claim
                        |> at [ "extension" ]
                        "capabilities", capabilities
                    ]
                )
            | "morphir.frontend.compileModel" ->
                let documents =
                    parameters
                    |> at [ "sources"; "documents" ]

                if documents = sourceDocuments true then
                    Ok(
                        parse
                            """{"success":false,"diagnostics":[{"severity":"error","message":"Malformed fixed source"}]}"""
                    )
                else
                    let version =
                        match
                            parameters
                            |> at [ "options"; "irVersion" ]
                        with
                        | JsonValue.String value -> value
                        | _ -> failtest "missing request version"

                    Ok(
                        obj [
                            "success", JsonValue.Bool true
                            "diagnostics", arr []
                            "model", model version false
                        ]
                    )
            | "morphir.backend.generateModel" ->
                let digest =
                    parameters
                    |> at [ "model"; "payload"; "sha256" ]

                if digest = str (String('0', 64)) then
                    Error(-32602, "The payload digest does not match.")
                else
                    Ok(
                        obj [
                            "success", JsonValue.Bool true
                            "diagnostics", arr []
                            "artifacts",
                            arr [ obj [ "path", str "main.py"; "content", pythonSource () ] ]
                        ]
                    )
            | "morphir.shutdown" ->
                shutDown <- true
                Ok(obj [])
            | "morphir.ping" -> Ok(parse """{"ok":true}""")
            | _ -> Error(-32601, "Unknown method at the scripted peer.")

    interface ISingleMessageChannel with
        member _.RequestAsync(method, message, _cancellation) =
            let bytes = message.ToArray()
            let parsed = parse (Encoding.UTF8.GetString bytes)

            let parameters =
                memberOf "params" parsed
                |> Option.defaultValue (obj [])

            let response =
                match answer method parameters with
                | Ok result ->
                    obj [ "jsonrpc", str "2.0"; "id", at [ "id" ] parsed; "result", result ]
                | Error(code, text) ->
                    obj [
                        "jsonrpc", str "2.0"
                        "id", at [ "id" ] parsed
                        "error", obj [ "code", num code; "message", str text ]
                    ]

            let reply = Encoding.UTF8.GetBytes(Json.compact (edit parsed response))

            exchanges.Add
                {
                    Session = session
                    Notification = false
                    RequestBytes = bytes
                    ReplyBytes = Some reply
                }

            Task.FromResult<byte array>(reply)

        member _.NotifyAsync(_method, message, _cancellation) =
            exchanges.Add
                {
                    Session = session
                    Notification = true
                    RequestBytes = message.ToArray()
                    ReplyBytes = None
                }

            Task.FromResult<byte array>(null)

type RunTrace = {
    Result: ModelTierRun
    Sessions: int
    Exchanges: WireExchange list
}

let runClaimPrepared (prepare: HostSession -> CancellationToken -> Task) edit claim =
    let exchanges = List<WireExchange>()
    let mutable sessions = 0

    let openSession =
        Func<KitSessionRequest, CancellationToken, Task<HostSession>>(fun request cancellation ->
            sessions <-
                sessions
                + 1

            let peer = RecordingPeer(claim, sessions, exchanges, edit)

            task {
                let session = request.CreateSession(peer :> ISingleMessageChannel, host)
                do! prepare session cancellation
                return session
            }
        )

    use cancellation = new CancellationTokenSource(TimeSpan.FromSeconds 5.0)

    let result =
        ModelTierRunner
            .RunAsync(kit.Value, openSession, cancellation.Token)
            .GetAwaiter()
            .GetResult()

    {
        Result = result
        Sessions = sessions
        Exchanges = List.ofSeq exchanges
    }

let runClaimWith edit claim =
    runClaimPrepared (fun _ _ -> Task.CompletedTask) edit claim

let runClaim claim =
    runClaimWith (fun _ response -> response) claim

let message exchange =
    parse (Encoding.UTF8.GetString exchange.RequestBytes)
