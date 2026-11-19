// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Mep.Tests.SessionTests

open System
open System.IO
open System.IO.Pipelines
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.Mep
open Morphir.Mep.Tests.Transcripts
open Morphir.TestSupport.Fixtures

module CF = Morphir.Mep.Tests.ConnectionTests.ConformanceFixture

[<Tests>]
let conformanceRawLifecycleTests =
    testSequenced <| testList "MEP conformance raw lifecycle refusals" [
        testCase "raw initialize cannot bypass selected offer binding or consume its attempt" <| fun _ ->
            use lifetime = new CF.Lifetime()
            let edge = CF.Edge()
            let session = CF.create edge lifetime
            let before = edge.Bodies
            Expect.isError
                (CF.wait (session.RequestAsync(Method.Initialize, Nullable<JsonElement>(), CancellationToken.None)))
                "Raw initialize must use the dedicated bound-offer operation"
            for method in [Method.Initialized; Method.Exit; Method.CancelRequest; Method.Progress] do
                Expect.isError
                    (CF.wait (session.RequestAsync(method, Nullable<JsonElement>(), CancellationToken.None)))
                    "A notification cannot be submitted as a raw request"
            Expect.equal edge.Bodies before "Refusals prepare no wire message and consume no ID"
            Expect.equal (CF.localCount session) 5 "Every rejected raw entry records a local refusal"
            CF.initialize session
            Expect.equal (CF.ids edge.Bodies) [1UL; 2UL] "Dedicated describe and initialize retain the original opportunity"
            Expect.equal (CF.methods edge.Bodies) ["morphir.extension.describe"; "morphir.initialize"; "morphir.initialized"] "Dedicated initialize owns its automatic notification"

        testCase "raw shutdown cannot bypass the dedicated state commit before a queued probe" <| fun _ ->
            use lifetime = new CF.Lifetime()
            let edge = CF.Edge()
            let session = CF.create edge lifetime
            CF.initialize session
            let before = edge.Bodies
            Expect.isError
                (CF.wait (session.RequestAsync(Method.Shutdown, Nullable<JsonElement>(), CancellationToken.None)))
                "Raw shutdown must not transmit without its state commit"
            Expect.equal edge.Bodies before "The rejected raw shutdown has no I/O"
            use barrier = new CF.Barrier()
            edge.After <- fun method token -> if method = "morphir.shutdown" then barrier.Pause token :> Task else Task.CompletedTask
            let shutdown = session.ShutdownAsync CancellationToken.None
            CF.wait barrier.Entered |> ignore
            let notification = (CF.control session).SendUnknownNotificationAsync CancellationToken.None
            barrier.Release()
            CF.wait shutdown |> CF.expectOk |> ignore
            CF.wait notification |> CF.expectNotSubmitted
            Expect.equal (CF.methods edge.Bodies |> List.last) "morphir.shutdown" "Queued notification observes the dedicated committed state"
            CF.wait ((CF.control session).ProbeAfterShutdownAsync CancellationToken.None) |> CF.expectSubmitted |> ignore
            Expect.equal (CF.ids edge.Bodies) [1UL; 2UL; 3UL; 4UL] "Raw refusal consumed no ID or probe opportunity"
    ]

let private decode<'T when 'T: not null> (info: Serialization.Metadata.JsonTypeInfo<'T>) (node: JsonNode) : 'T =
    match JsonSerializer.Deserialize(node.ToJsonString(), info :> Serialization.Metadata.JsonTypeInfo) with
    | :? 'T as value -> value
    | _ -> failtest "Decode failed"

/// A guest made from the initialize result of a transcript, with the claims built from it.
let private guestOf (result: JsonNode) =
    let initialized = decode MepJson.Default.InitializeResult result

    let claims =
        { ClaimsVersion = SemanticVersion.Create "0.1.0-draft.2"
          ProtocolVersions = Lifecycle.protocolVersions
          Extension = initialized.Extension
          Capabilities = readOnlyDict []
          Requires = None
          Critical = [||] }

    Guest(
        { Claims = claims
          Capabilities = initialized.Capabilities
          Methods = readOnlyDict [] }
    )

/// The legacy transcripts initialize with 0.1; morphir-dotnet speaks only 0.2.0-draft.1.
let private current (text: string) = text.Replace("\"0.1\"", "\"0.2.0-draft.1\"")

let private dispatch (guest: Guest) (text: string) =
    guest.Dispatcher.DispatchAsync(ReadOnlyMemory(Encoding.UTF8.GetBytes text), CancellationToken.None).Result
    |> Option.ofObj
    |> Option.map (fun bytes -> JsonNode.Parse(bytes) |> nonNull)

let private code (node: JsonNode) =
    node["error"] |> Option.ofObj |> Option.map (fun error -> (error["code"] |> nonNull).GetValue<int>())

let private modelAccessTranscript = readFixture FixtureCatalog.Mep.BoundedGenerationSession
let private boundedInitialize = modelAccessTranscript[0].Text
let private boundedGuestResult = modelAccessTranscript[1].Text
let private boundedStart = modelAccessTranscript[3].Text

let private boundedGuestWithMethods (reply: JsonNode) methods =
    let initialized = decode MepJson.Default.InitializeResult (reply["result"] |> nonNull)

    let claims =
        { ClaimsVersion = SemanticVersion.Create "0.1.0-draft.2"
          ProtocolVersions = [| ProtocolVersion.Create "0.2.0-draft.2"; ProtocolVersion.Create "0.2.0-draft.1" |]
          Extension = initialized.Extension
          Capabilities = readOnlyDict []
          Requires = None
          Critical = [||] }

    Guest(
        { Claims = claims
          Capabilities = initialized.Capabilities
          Methods = methods }
    )

let private boundedGuestFrom reply = boundedGuestWithMethods reply (readOnlyDict [])
let private invalidDocumentDeliveries (delivery: JsonNode) =
    let variant (key: string) (value: JsonNode) =
        let changed = delivery.DeepClone()
        changed[key] <- value
        changed
    let length = (delivery["decodedByteLength"] |> nonNull).GetValue<int>()
    let content = (delivery["content"] |> nonNull).GetValue<string>()
    [ "incorrect digest", variant "sha256" (JsonValue.Create(String.replicate 64 "0"))
      "incorrect length", variant "decodedByteLength" (JsonValue.Create(length + 1))
      "malformed base64", variant "content" (JsonValue.Create("!" + content.Substring(1))) ]

let private boundedGuest () = boundedGuestFrom (JsonNode.Parse boundedGuestResult |> nonNull)

let private boundedReply guest request =
    dispatch guest request |> Option.defaultWith (fun () -> failtest "Expected a JSON-RPC response")

let private boundedHostInfo () =
    let request = JsonNode.Parse boundedInitialize |> nonNull
    decode MepJson.Default.PeerInfo ((request["params"] |> nonNull)["host"] |> nonNull)

[<Tests>]
let boundedGenerationNegotiationTests =
    testList
        "MEP bounded generation negotiation"
        [
            testCase "the live initialize result agrees to the exact transcript tuple and limits" <| fun _ ->
                let guest = boundedGuest ()
                let actual = boundedReply guest boundedInitialize
                let expected = JsonNode.Parse boundedGuestResult |> nonNull
                let actualResult = actual["result"] |> nonNull
                let expectedResult = expected["result"] |> nonNull

                Expect.equal
                    ((actualResult["protocolVersion"] |> nonNull).GetValue<string>())
                    "0.2.0-draft.2"
                    "The bounded session uses the exact draft"

                Expect.isTrue
                    (JsonNode.DeepEquals(
                        (actualResult["capabilities"] |> nonNull)["modelAccess"],
                        (expectedResult["capabilities"] |> nonNull)["modelAccess"]
                    ))
                    "The live session agrees to the complete tuple and every limit"

            testCase "draft.1 never admits a bounded capability or start" <| fun _ ->
                let guest = boundedGuest ()
                let legacy = boundedInitialize.Replace(
                    """["0.2.0-draft.2","0.2.0-draft.1"]""",
                    """["0.2.0-draft.1"]"""
                )
                let actual = boundedReply guest legacy
                let result = actual["result"] |> nonNull

                Expect.equal
                    ((result["protocolVersion"] |> nonNull).GetValue<string>())
                    "0.2.0-draft.1"
                    "The guest chose the only offered version"

                Expect.isFalse
                    ((result["capabilities"] |> nonNull).AsObject().ContainsKey "modelAccess")
                    "The legacy initialize result omits bounded access"

                let request = """{"jsonrpc":"2.0","id":2,"method":"morphir.backend.generateModel.start","params":{}}"""
                Expect.equal (code (boundedReply guest request)) (Some -32013) "The session refuses the method"

            testCase "a different codec in the offer cannot authorize a bounded call" <| fun _ ->
                let guest = boundedGuest ()
                let differentCodec = boundedInitialize.Replace("\"morphir-json\"", "\"morphir-yaml\"")
                let initialized = boundedReply guest differentCodec
                let result = initialized["result"] |> nonNull
                let capabilities = result["capabilities"] |> nonNull
                let access = capabilities["modelAccess"] |> Option.ofObj

                let tupleCount = access |> Option.map (fun node -> (node["tuples"] |> nonNull).AsArray().Count)
                Expect.isTrue (tupleCount.IsNone || tupleCount = Some 0) "No tuple is synthesized across codecs"

                let request = """{"jsonrpc":"2.0","id":2,"method":"morphir.backend.generateModel.start","params":{}}"""
                Expect.equal (code (boundedReply guest request)) (Some -32013) "The session refuses the method"

            testCase "a legacy host refuses an unsolicited bounded selection" <| fun _ ->
                let reply = JsonNode.Parse boundedGuestResult |> nonNull
                let result = reply["result"] |> nonNull
                result["protocolVersion"] <- JsonValue.Create "0.2.0-draft.1"
                reply["id"] <- JsonValue.Create 0
                let mutable notifications = 0

                let channel =
                    { new ISingleMessageChannel with
                        member _.RequestAsync(_, _, _) =
                            Task.FromResult<byte array | null>(Encoding.UTF8.GetBytes(reply.ToJsonString()))
                        member _.NotifyAsync(_, _, _) =
                            notifications <- notifications + 1
                            Task.FromResult<byte array | null> null }

                let host = HostSession(channel, boundedHostInfo ())
                Expect.isError
                    (host.InitializeAsync(CancellationToken.None).Result)
                    "An unsolicited bounded capability cannot become live"
                Expect.equal notifications 0 "The refused result sends no initialized notification"

            testCase "an opted-in host and guest initialize through the single-message channel" <| fun _ ->
                let request = JsonNode.Parse boundedInitialize |> nonNull
                let offer = decode MepJson.Default.InitializeParams (request["params"] |> nonNull)
                let access = offer.ModelAccess |> Option.defaultWith (fun () -> failtest "Missing fixture offer")
                let guest = boundedGuest ()
                let calls = ResizeArray<string>()

                let send method (message: ReadOnlyMemory<byte>) cancellation =
                    calls.Add method
                    guest.Dispatcher.DispatchAsync(message, cancellation)

                let channel =
                    { new ISingleMessageChannel with
                        member _.RequestAsync(method, message, cancellation) = send method message cancellation
                        member _.NotifyAsync(method, message, cancellation) = send method message cancellation }

                let host = HostSession.CreateWithModelAccess(channel, offer.Host, access, null)
                let result = host.InitializeAsync(CancellationToken.None).Result

                match result with
                | Error failure -> failtestf "Expected successful bounded negotiation, got %A" failure
                | Ok session ->
                    Expect.equal session.ProtocolVersion (ProtocolVersion.Create "0.2.0-draft.2") "Exact draft"
                    let selected =
                        session.Capabilities.ModelAccess
                        |> Option.defaultWith (fun () -> failtest "Missing live selection")
                    Expect.sequenceEqual selected.Tuples access.Tuples "The live session has the offered tuple"
                    Expect.equal selected.Limits access.Limits "Every recorded limit survived the wire exchange"
                    match selected.Tuples with
                    | :? System.Collections.Generic.IList<ModelAccessTuple> as tuples ->
                        Expect.throwsT<NotSupportedException>
                            (fun () -> tuples[0] <- { tuples[0] with Codec = ModelAccessCodec.MorphirYaml })
                            "The negotiated tuple list cannot be changed through a public session view"
                    | _ -> ()

                    use invalid = JsonDocument.Parse(boundedStart.Replace("\"morphir-json\"", "\"morphir-yaml\""))
                    let parameters = Nullable(invalid.RootElement.GetProperty "params")
                    let refused = host.RequestAsync(Method.GenerateModelStart, parameters, CancellationToken.None).Result
                    Expect.isError refused "The host refuses a tuple outside the live selection"
                    Expect.sequenceEqual calls ["morphir.initialize"; "morphir.initialized"] "Initialization finishes before dispatch"

            testCase "the guest retains host tuple order and uses the lower limit in each direction" <| fun _ ->
                let request = JsonNode.Parse boundedInitialize |> nonNull
                let reply = JsonNode.Parse boundedGuestResult |> nonNull
                let offered = ((request["params"] |> nonNull)["modelAccess"] |> nonNull)
                let supported = (((reply["result"] |> nonNull)["capabilities"] |> nonNull)["modelAccess"] |> nonNull)
                let hostTuples = (offered["tuples"] |> nonNull).AsArray()
                let guestTuples = (supported["tuples"] |> nonNull).AsArray()
                let classic = (hostTuples[0] |> nonNull).DeepClone()
                classic["irVersion"] <- JsonValue.Create "3.1.0"
                guestTuples.Add(classic.DeepClone())
                hostTuples.Insert(0, classic)
                (offered["limits"] |> nonNull)["maxNodes"] <- JsonValue.Create 120
                (supported["limits"] |> nonNull)["maxDepth"] <- JsonValue.Create 12

                let actual = boundedReply (boundedGuestFrom reply) (request.ToJsonString())
                let agreed = (((actual["result"] |> nonNull)["capabilities"] |> nonNull)["modelAccess"] |> nonNull)
                let support = decode MepJson.Default.ModelAccessSupport agreed
                Expect.sequenceEqual
                    (support.Tuples |> Seq.map _.IrVersion.Value)
                    ["3.1.0"; "4.0.0"]
                    "Guest declaration order cannot change host preference"
                Expect.equal support.Limits.MaxNodes 120 "The lower host limit"
                Expect.equal support.Limits.MaxDepth 12 "The lower guest limit"

            testCase "the host refuses selections outside observed guest support or empty agreements" <| fun _ ->
                let offer =
                    decode MepJson.Default.InitializeParams ((JsonNode.Parse boundedInitialize |> nonNull)["params"] |> nonNull)
                let support = offer.ModelAccess |> Option.defaultWith (fun () -> failtest "Missing bounded offer")
                let accepted = ResizeArray<string>()

                for observe, violation in [ true, "overstated depth"; false, "empty agreement" ] do
                    let guest =
                        boundedGuestFrom (JsonNode.Parse(boundedGuestResult.Replace("\"maxDepth\":64", "\"maxDepth\":32")) |> nonNull)
                    let notifications = ResizeArray<string>()
                    let channel =
                        { new ISingleMessageChannel with
                            member _.RequestAsync(method, message, cancellation) = task {
                                let! response = guest.Dispatcher.DispatchAsync(message, cancellation)
                                if method = Method.Initialize.WireValue && response <> null then
                                    let node = JsonNode.Parse(response) |> nonNull
                                    let result = node["result"] |> nonNull
                                    let capabilities = result["capabilities"] |> nonNull
                                    let access = capabilities["modelAccess"] |> nonNull
                                    if observe then (access["limits"] |> nonNull)["maxDepth"] <- JsonValue.Create 64
                                    else access["tuples"] <- JsonArray()
                                    return (Encoding.UTF8.GetBytes(node.ToJsonString()) : byte array | null)
                                else return response }
                            member _.NotifyAsync(method, _, _) =
                                notifications.Add method
                                Task.FromResult<byte array | null> null }
                    let host = HostSession.CreateWithModelAccess(channel, offer.Host, support, null)
                    if observe then Expect.isOk (host.DescribeAsync(CancellationToken.None).Result) "Guest advertised a lower bound"
                    match host.InitializeAsync(CancellationToken.None).Result with
                    | Ok _ -> accepted.Add violation
                    | Error _ -> Expect.isEmpty notifications "A refused selection sends no initialized notification"
                Expect.isEmpty accepted "Neither invalid selection can initialize"

            testCase "draft.1 initialization ignores an offer it has no codec for" <| fun _ ->
                let offer = JsonNode.Parse boundedInitialize |> nonNull
                let parameters = offer["params"] |> nonNull
                parameters["protocolVersions"] <- JsonNode.Parse """["0.2.0-draft.1"]"""
                parameters["modelAccess"] <- JsonNode.Parse """{"futureFormat":true}"""
                let reply = boundedReply (boundedGuest ()) (offer.ToJsonString())
                Expect.isNone (code reply) "A draft.1 caller can send an unknown optional future member"
                let initialized = decode MepJson.Default.InitializeResult (reply["result"] |> nonNull)
                Expect.equal initialized.ProtocolVersion Lifecycle.protocolVersion "Legacy fallback"
                Expect.isNone initialized.Capabilities.ModelAccess "There is no bounded negotiation on draft.1"

            testCase "describe reports the configured bounded tuples and limits" <| fun _ ->
                let request =
                    """{"jsonrpc":"2.0","id":1,"method":"morphir.extension.describe","params":{"protocolVersions":["0.2.0-draft.2"]}}"""
                let actual = boundedReply (boundedGuest ()) request
                let capabilities = ((actual["result"] |> nonNull)["capabilities"] |> nonNull)
                let expected = JsonNode.Parse boundedGuestResult |> nonNull
                let advertised = ((expected["result"] |> nonNull)["capabilities"] |> nonNull)["modelAccess"]
                Expect.isTrue (JsonNode.DeepEquals(capabilities["modelAccess"], advertised)) "Exact bounded claims"

            testCase "bounded start admits the exact tuple, target and limits before calling the generator" <| fun _ ->
                let mutable calls = 0
                let handler =
                    Handlers.raw (fun _ _ ->
                        calls <- calls + 1
                        Task.FromResult(Ok(JsonRpc.Constant """{"state":"complete","sessionId":"orders-generation","artifacts":[],"diagnostics":[]}""")))
                let guest =
                    boundedGuestWithMethods
                        (JsonNode.Parse boundedGuestResult |> nonNull)
                        (readOnlyDict [ "morphir.backend.generateModel.start", handler ])

                boundedReply guest boundedInitialize |> ignore

                for invalid, failure in
                    [
                        boundedStart.Replace("\"morphir-json\"", "\"morphir-yaml\""), -32013
                        boundedStart.Replace("\"target\":\"text\"", "\"target\":\"unknown\""), -32013
                        boundedStart.Replace("\"maxDepth\":64", "\"maxDepth\":65"), -32602
                    ] do
                    Expect.equal (code (boundedReply guest invalid)) (Some failure) "The start must use the admitted inputs"

                let notification = (JsonNode.Parse boundedStart |> nonNull).AsObject()
                notification.Remove "id" |> ignore
                Expect.isNone (dispatch guest (notification.ToJsonString())) "A notification receives no reply"

                Expect.equal calls 0 "Refused requests never call the generator"
                Expect.equal (code (boundedReply guest boundedStart)) None "The admitted start succeeds"
                Expect.equal calls 1 "Exactly one valid start reached the generator"

            testCase "advance admits only the outstanding round and preserves it on refusal" <| fun _ ->
                let mutable advances = 0
                let start = Handlers.constant """{"state":"continue","sessionId":"orders-generation","round":1,"requests":[],"artifacts":[],"diagnostics":[]}"""
                let advance =
                    Handlers.raw (fun _ _ ->
                        advances <- advances + 1
                        match advances with
                        | 1 -> Task.FromResult(Error(RpcErrors.create ErrorCode.InvalidParams))
                        | 2 -> Task.FromException<Result<JsonElement, RpcError>>(RpcException(RpcErrors.create ErrorCode.InvalidParams))
                        | _ -> Task.FromResult(Ok(JsonRpc.Constant """{"state":"complete","sessionId":"orders-generation","artifacts":[],"diagnostics":[]}""")))

                let guest =
                    boundedGuestWithMethods
                        (JsonNode.Parse boundedGuestResult |> nonNull)
                        (readOnlyDict [ Method.GenerateModelStart.WireValue, start; Method.GenerateModelAdvance.WireValue, advance ])
                let first = """{"jsonrpc":"2.0","id":3,"method":"morphir.backend.generateModel.advance","params":{"sessionId":"orders-generation","round":1,"deliveries":[]}}"""

                boundedReply guest boundedInitialize |> ignore
                Expect.equal (code (boundedReply guest first)) (Some -32602) "No operation has started"
                Expect.equal advances 0 "An unopened operation does not dispatch"
                Expect.equal (code (boundedReply guest boundedStart)) None "The operation requests round one"
                Expect.equal (code (boundedReply guest (first.Replace("\"round\":1", "\"round\":2")))) (Some -32602) "The next round is premature"
                Expect.equal advances 0 "An invalid round does not dispatch"
                Expect.equal (code (boundedReply guest first)) (Some -32602) "The guest refuses invalid deliveries"
                Expect.equal (code (boundedReply guest first)) (Some -32602) "A handler can also refuse by throwing RpcException"
                Expect.equal (code (boundedReply guest first)) None "A corrected delivery may retry the same round"
                Expect.equal (code (boundedReply guest first)) (Some -32602) "A completed operation cannot advance"
                Expect.equal (code (boundedReply guest boundedStart)) (Some -32602) "A completed ID cannot be reused"
                Expect.equal advances 3 "Only three deliveries reached the generator"

            testCase "matched document deliveries obey all aggregate batch ceilings" <| fun _ ->
                let selectedDocument id =
                    let request =
                        modelAccessTranscript
                        |> List.find (fun entry ->
                            entry.Direction = HostToExtension
                            && JsonNode.DeepEquals(entry.Node["id"], JsonValue.Create(id: int)))
                    let parameters = request.Node["params"] |> nonNull
                    (parameters["deliveries"] |> nonNull)[0] |> nonNull

                let deliveries = JsonArray((selectedDocument 7).DeepClone(), (selectedDocument 8).DeepClone())
                let template = JsonNode.Parse """{"jsonrpc":"2.0","id":3,"method":"morphir.backend.generateModel.advance","params":{"sessionId":"orders-generation","round":1}}""" |> nonNull
                let parameters = template["params"] |> nonNull
                parameters["deliveries"] <- deliveries
                let advance = template.ToJsonString()

                for label, documents, encodedBytes, decodedBytes, expected in
                    [ "exact bounds", 2, 672, 503, None
                      "document count", 1, 672, 503, Some -32602
                      "aggregate encoded length", 2, 671, 503, Some -32602
                      "aggregate decoded length", 2, 672, 502, Some -32602 ] do
                    let mutable calls = 0
                    let start =
                        Handlers.constant """{"state":"continue","sessionId":"orders-generation","round":1,"requests":[{"kind":"document","requestId":"final-value","documentId":"pkg/acme/orders/domain/total-in-_usd.value"},{"kind":"document","requestId":"order-value","documentId":"pkg/acme/orders/domain/order.value"}],"artifacts":[],"diagnostics":[]}"""
                    let deliver =
                        Handlers.raw (fun _ _ ->
                            calls <- calls + 1
                            Task.FromResult(Ok(JsonRpc.Constant """{"state":"complete","sessionId":"orders-generation","artifacts":[],"diagnostics":[]}""")))
                    let guest =
                        boundedGuestWithMethods
                            (JsonNode.Parse boundedGuestResult |> nonNull)
                            (readOnlyDict [ Method.GenerateModelStart.WireValue, start; Method.GenerateModelAdvance.WireValue, deliver ])

                    let startRequest = JsonNode.Parse boundedStart |> nonNull
                    let parameters = startRequest["params"] |> nonNull
                    let limits = parameters["limits"] |> nonNull
                    limits["maxDocumentsPerBatch"] <- JsonValue.Create documents
                    limits["maxEncodedBytesPerExchange"] <- JsonValue.Create encodedBytes
                    limits["maxDecodedBytesPerExchange"] <- JsonValue.Create decodedBytes
                    boundedReply guest boundedInitialize |> ignore
                    Expect.equal (code (boundedReply guest (startRequest.ToJsonString()))) None "Start admits the declared ceilings"
                    Expect.equal (code (boundedReply guest advance)) expected label
                    Expect.equal calls (if Option.isNone expected then 1 else 0) "Only a whole admitted batch reaches the generator"

            testCase "a successful cancel releases the operation and malformed continuation cannot advance" <| fun _ ->
                let valid = Handlers.constant """{"state":"continue","sessionId":"orders-generation","round":1,"requests":[],"artifacts":[],"diagnostics":[]}"""
                let cancel = Handlers.constant """{"sessionId":"orders-generation"}"""
                let advance = Handlers.constant """{"state":"complete","sessionId":"orders-generation","artifacts":[],"diagnostics":[]}"""
                let create start =
                    boundedGuestWithMethods
                        (JsonNode.Parse boundedGuestResult |> nonNull)
                        (readOnlyDict
                            [ Method.GenerateModelStart.WireValue, start
                              Method.GenerateModelAdvance.WireValue, advance
                              Method.GenerateModelCancel.WireValue, cancel ])
                let next = """{"jsonrpc":"2.0","id":3,"method":"morphir.backend.generateModel.advance","params":{"sessionId":"orders-generation","round":1,"deliveries":[]}}"""
                let stop = """{"jsonrpc":"2.0","id":4,"method":"morphir.backend.generateModel.cancel","params":{"sessionId":"orders-generation"}}"""

                let guest = create valid
                boundedReply guest boundedInitialize |> ignore
                Expect.equal (code (boundedReply guest boundedStart)) None "Start opens the operation"
                Expect.equal (code (boundedReply guest stop)) None "Cancel acknowledges state release"
                Expect.equal (code (boundedReply guest next)) (Some -32602) "No advance after cancel"

                let malformed =
                    create (Handlers.constant """{"state":"continue","sessionId":"orders-generation","round":2,"requests":[],"artifacts":[],"diagnostics":[]}""")
                boundedReply malformed boundedInitialize |> ignore
                Expect.equal (code (boundedReply malformed boundedStart)) (Some -32603) "The first continuation must have round one"
                Expect.equal (code (boundedReply malformed next)) (Some -32602) "A malformed reply did not open a round"

            testCase "shutdown refuses the result of an already running bounded handler" <| fun _ ->
                let entered = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
                let release = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
                let delayed =
                    Handlers.raw (fun _ _ -> task {
                        entered.SetResult(())
                        do! release.Task
                        return Ok(JsonRpc.Constant """{"state":"complete","sessionId":"orders-generation","artifacts":[],"diagnostics":[]}""") })
                let guest =
                    boundedGuestWithMethods
                        (JsonNode.Parse boundedGuestResult |> nonNull)
                        (readOnlyDict [ Method.GenerateModelStart.WireValue, delayed ])
                boundedReply guest boundedInitialize |> ignore
                let pending =
                    guest.Dispatcher.DispatchAsync(ReadOnlyMemory(Encoding.UTF8.GetBytes boundedStart), CancellationToken.None)
                try
                    CF.wait entered.Task
                    let stop = """{"jsonrpc":"2.0","id":3,"method":"morphir.shutdown"}"""
                    Expect.isNone (code (boundedReply guest stop)) "Shutdown commits while the generator is held"
                finally
                    release.SetResult(())
                let actual = JsonNode.Parse(CF.wait pending |> nonNull) |> nonNull
                Expect.equal (code actual) (Some -32014) "Closed generation cannot return a successful step"

            testCase "typed host and guest preserve the orders wire variants and reject a foreign delivery" <| fun _ ->
                let start = Handlers.constant """{"sessionId":"orders-generation","round":1,"requests":[{"requestId":"domain-first","kind":"inventory","selection":"orders-domain-members","pageSize":2}],"artifacts":[],"diagnostics":[],"state":"continue"}"""
                let mutable advances = 0
                let advance =
                    Handlers.raw (fun _ _ ->
                        advances <- advances + 1
                        let result =
                            if advances = 1 then
                                """{"state":"continue","sessionId":"orders-generation","round":2,"requests":[{"requestId":"order-value","kind":"document","documentId":"pkg/acme/orders/domain/order.value"}],"artifacts":[],"diagnostics":[]}"""
                            else
                                """{"state":"complete","sessionId":"orders-generation","artifacts":[{"path":"order.txt","offset":0,"decodedByteLength":3,"sha256":"c6ebc76be5dc1f8b433f8d6fd9bd85cd9325086038442db8614bb799fec6fd85","content":"NzMK","end":true}],"diagnostics":[]}"""
                        Task.FromResult(Ok(JsonRpc.Constant result)))
                let guest =
                    boundedGuestWithMethods
                        (JsonNode.Parse boundedGuestResult |> nonNull)
                        (readOnlyDict [ Method.GenerateModelStart.WireValue, start; Method.GenerateModelAdvance.WireValue, advance ])
                let delivered = ResizeArray<JsonNode>()
                let channel =
                    { new ISingleMessageChannel with
                        member _.RequestAsync(method, message, cancellation) =
                            if method = Method.GenerateModelAdvance.WireValue then
                                delivered.Add((JsonNode.Parse(message.Span) |> nonNull)["params"] |> nonNull)
                            guest.Dispatcher.DispatchAsync(message, cancellation)
                        member _.NotifyAsync(_, message, cancellation) =
                            guest.Dispatcher.DispatchAsync(message, cancellation) }
                let offer =
                    decode MepJson.Default.InitializeParams ((JsonNode.Parse boundedInitialize |> nonNull)["params"] |> nonNull)
                let support = offer.ModelAccess |> Option.defaultWith (fun () -> failtest "Missing fixture support")
                let host = HostSession.CreateWithModelAccess(channel, offer.Host, support, null)
                let ct = CancellationToken.None
                Expect.isOk (host.InitializeAsync(ct).Result) "Both peers agreed model access"

                let startParams = decode MepJson.Default.ModelAccessStart ((JsonNode.Parse boundedStart |> nonNull)["params"] |> nonNull)
                match host.StartGenerationAsync(startParams, ct).Result with
                | Ok(ModelAccessStep.Continue("orders-generation", 1, requests, _, _)) ->
                    Expect.sequenceEqual requests [ModelAccessRequest.Inventory("domain-first", "orders-domain-members", 2, None)] "Typed inventory request"
                | unexpected -> failtestf "Expected the first inventory request, got %A" unexpected

                let inventory = """{"sessionId":"orders-generation","round":1,"deliveries":[{"requestId":"domain-first","kind":"inventory","snapshotId":"orders-snapshot","entries":[{"type":{"documentId":"pkg/acme/orders/domain/order.type","packageName":"acme/orders","modulePath":"domain","role":"definition","name":"order"}},{"value":{"documentId":"pkg/acme/orders/domain/order.value","packageName":"acme/orders","modulePath":"domain","role":"definition","name":"order"}}],"nextCursor":"domain-page-two"}]}"""
                let fromJson text = decode MepJson.Default.ModelAccessAdvance (JsonNode.Parse(text: string) |> nonNull)
                let foreign = fromJson (inventory.Replace("orders-snapshot", "foreign-snapshot"))
                Expect.isError (host.AdvanceGenerationAsync(foreign, ct).Result) "The host rejects a foreign snapshot"
                Expect.equal delivered.Count 0 "A locally refused delivery never reaches the guest"
                match host.AdvanceGenerationAsync(fromJson inventory, ct).Result with
                | Ok(ModelAccessStep.Continue("orders-generation", 2, requests, _, _)) ->
                    Expect.sequenceEqual requests [ModelAccessRequest.Document("order-value", "pkg/acme/orders/domain/order.value")] "Typed document request"
                    Expect.isTrue (JsonNode.DeepEquals(delivered[0], JsonNode.Parse inventory)) "Inventory uses the required single-key variants"
                | unexpected -> failtestf "Expected the second, document round, got %A" unexpected

                let doc = """{"sessionId":"orders-generation","round":2,"deliveries":[{"requestId":"order-value","kind":"document","snapshotId":"orders-snapshot","documentId":"pkg/acme/orders/domain/order.value","decodedByteLength":246,"sha256":"23b1c85f2ee470a468e335098619d394fd23d7a70ee592d39a28612dc5e98fa0","content":"ewogICJmb3JtYXRWZXJzaW9uIjogNCwKICAibmFtZSI6ICJvcmRlciIsCiAgImRlZiI6IHsKICAgICJQdWJsaWMiOiB7CiAgICAgICJFeHByZXNzaW9uQm9keSI6IHsKICAgICAgICAiaW5wdXRUeXBlcyI6IHt9LAogICAgICAgICJvdXRwdXRUeXBlIjogIm1vcnBoaXIvU0RLOmJhc2ljcyNpbnQiLAogICAgICAgICJib2R5IjogeyAiTGl0ZXJhbCI6IHsgIkludGVnZXJMaXRlcmFsIjogNzMgfSB9CiAgICAgIH0KICAgIH0KICB9Cn0K"}]}"""
                let docParams = JsonNode.Parse doc |> nonNull
                let delivery = (docParams["deliveries"] |> nonNull)[0] |> nonNull
                for label, invalid in invalidDocumentDeliveries delivery do
                    let changed = docParams.DeepClone()
                    (changed["deliveries"] |> nonNull)[0] <- invalid
                    let result = host.AdvanceGenerationAsync(fromJson (changed.ToJsonString()), ct).Result
                    Expect.isError result $"The host refuses {label}"
                    Expect.equal delivered.Count 1 "An invalid selected document never reaches the guest"
                match host.AdvanceGenerationAsync(fromJson doc, ct).Result with
                | Ok(ModelAccessStep.Complete("orders-generation", chunks, _)) ->
                    Expect.isTrue (JsonNode.DeepEquals(delivered[1], JsonNode.Parse doc)) "Document wire payload"
                    Expect.equal chunks[0].Path "order.txt" "Artifact path"
                    Expect.equal chunks[0].Content "NzMK" "Artifact payload"
                    Expect.isTrue chunks[0].End "Artifact is closed"
                | unexpected -> failtestf "Expected completion with the artifact, got %A" unexpected
                Expect.isError (host.AdvanceGenerationAsync(fromJson doc, ct).Result) "Host rejects a terminal replay"
                Expect.equal delivered.Count 2 "The replay does not transmit"

            testCase "bounded byte calls refuse oversized messages on both sides of the adapter" <| fun _ ->
                let offer =
                    decode MepJson.Default.InitializeParams ((JsonNode.Parse boundedInitialize |> nonNull)["params"] |> nonNull)
                let supported = offer.ModelAccess |> Option.defaultWith (fun () -> failtest "Missing fixture support")
                let limits = { supported.Limits with MaxFrameBytes = 4096 }
                let support = { supported with Limits = limits }
                let start = decode MepJson.Default.ModelAccessStart ((JsonNode.Parse boundedStart |> nonNull)["params"] |> nonNull)
                let padding = String('x', 6000)
                let ct = CancellationToken.None

                for oversizedRequest in [ true; false ] do
                    let oversizedReply =
                        """{"state":"complete","sessionId":"orders-generation","artifacts":[],"diagnostics":[{"severity":"warning","message":"%s"}]}"""
                        |> fun template -> template.Replace("%s", padding)
                    let guest =
                        boundedGuestWithMethods
                            (JsonNode.Parse boundedGuestResult |> nonNull)
                            (readOnlyDict [ Method.GenerateModelStart.WireValue, Handlers.constant oversizedReply ])
                    let mutable requests = 0
                    let channel =
                        { new ISingleMessageChannel with
                            member _.RequestAsync(method, message, cancellation) =
                                if method = Method.GenerateModelStart.WireValue then requests <- requests + 1
                                guest.Dispatcher.DispatchAsync(message, cancellation)
                            member _.NotifyAsync(_, message, cancellation) =
                                guest.Dispatcher.DispatchAsync(message, cancellation) }
                    let host = HostSession.CreateWithModelAccess(channel, offer.Host, support, null)
                    Expect.isOk (host.InitializeAsync(ct).GetAwaiter().GetResult()) "Agreement precedes the byte limit"
                    if oversizedRequest then
                        let expanded = JsonRpc.Constant("{\"padding\":\"" + padding + "\"}")
                        let operation = { start with Limits = limits; Options = expanded }
                        try
                            host.StartGenerationAsync(operation, ct).GetAwaiter().GetResult() |> ignore
                            failtest "Expected the encoded request to be refused"
                        with :? System.IO.IOException as error ->
                            Expect.stringContains error.Message "maxFrameBytes" "Report the negotiated limit"
                        Expect.equal requests 0 "An oversized request never reaches the adapter"
                    else
                        match host.StartGenerationAsync({ start with Limits = limits }, ct).GetAwaiter().GetResult() with
                        | Error error -> Expect.stringContains error.Message "maxFrameBytes" "Report the refused reply"
                        | Ok _ -> failtest "An oversized reply cannot complete the operation"
                        Expect.equal requests 1 "The request was admitted before the adapter returned the oversized reply"

            testCase "the pinned shared bounded transcript passes through the real guest gate" <| fun _ ->
                let replies =
                    modelAccessTranscript
                    |> Seq.choose (fun entry ->
                        if entry.Direction = ExtensionToHost then
                            Some((entry.Node["id"] |> nonNull).GetValue<int>(), entry.Node)
                        else None)
                    |> readOnlyDict
                let answers ids =
                    let remaining = System.Collections.Generic.Queue<int>(ids: int list)
                    let handler =
                        Handlers.raw (fun _ _ ->
                            Expect.isGreaterThan remaining.Count 0 "Only the scripted calls reach the extension"
                            let reply = replies[remaining.Dequeue()]["result"] |> nonNull
                            Task.FromResult(Ok(JsonRpc.Constant(reply.ToJsonString()))))
                    remaining, handler
                let starts, start = answers [2; 9; 10]
                let advances, advance = answers [3; 5; 6; 7; 8]
                let cancels, cancel = answers [11]
                let guest =
                    boundedGuestWithMethods
                        (JsonNode.Parse boundedGuestResult |> nonNull)
                        (readOnlyDict
                            [ Method.GenerateModelStart.WireValue, start
                              Method.GenerateModelAdvance.WireValue, advance
                              Method.GenerateModelCancel.WireValue, cancel ])

                for entry in modelAccessTranscript do
                    if entry.Direction = HostToExtension then
                        match Option.ofObj entry.Node["id"] with
                        | None -> Expect.isNone (dispatch guest entry.Text) "Notifications have no reply"
                        | Some requestId ->
                            let id = requestId.GetValue<int>()
                            let expected = replies[id]
                            if id = 5 then
                                let invalid = entry.Text.Replace("\"name\":\"order\"", "\"name\":\"order\",\"memberSelection\":\"foreign\"")
                                Expect.equal
                                    (code (boundedReply guest invalid))
                                    (Some -32602)
                                    "A named entry cannot advertise a member selection"
                            if id = 7 then
                                let paramsNode = entry.Node["params"] |> nonNull
                                let delivery = (paramsNode["deliveries"] |> nonNull)[0] |> nonNull
                                let invalidDeliveries = invalidDocumentDeliveries delivery
                                let content = (delivery["content"] |> nonNull).GetValue<string>()
                                Expect.isTrue (content.EndsWith("o=", StringComparison.Ordinal)) "The fixture contains canonical final padding bits"
                                let noncanonical = delivery.DeepClone()
                                noncanonical["content"] <- JsonValue.Create(content.Substring(0, content.Length - 2) + "p=")
                                for label, invalid in invalidDeliveries @ ["noncanonical padding bits", noncanonical] do
                                    let invalidRequest = entry.Node.DeepClone()
                                    let invalidParams = invalidRequest["params"] |> nonNull
                                    (invalidParams["deliveries"] |> nonNull)[0] <- invalid
                                    let pending = advances.Count
                                    Expect.equal
                                        (code (boundedReply guest (invalidRequest.ToJsonString())))
                                        (Some -32602)
                                        $"The guest refuses {label}"
                                    Expect.equal advances.Count pending "The invalid document never reaches the handler"
                            let actual = boundedReply guest entry.Text
                            Expect.isTrue (JsonNode.DeepEquals(actual["id"], requestId)) "The response retains the request ID"
                            match code expected with
                            | Some expectedCode -> Expect.equal (code actual) (Some expectedCode) "The stale round is refused"
                            | None ->
                                let expectedResult =
                                    let result = expected["result"] |> nonNull
                                    if id = 1 then
                                        // The SDK writes the false defaults of the inherited capability fields.
                                        let initialized = decode MepJson.Default.InitializeResult result
                                        JsonSerializer.SerializeToNode(initialized, MepJson.Default.InitializeResult) |> nonNull
                                    else result
                                Expect.isTrue
                                    (JsonNode.DeepEquals(actual["result"], expectedResult))
                                    $"Transcript result for call {id}: {actual}"

                Expect.equal (starts.Count, advances.Count, cancels.Count) (0, 0, 0) "Every intended handler call was observed"
        ]

/// Replays the host messages of a transcript into a guest and checks each reply against the transcript reply.
let private replay (entries: Entry list) (skip: Set<int>) =
    let initialize =
        entries |> List.find (fun entry -> entry.Text.Contains "\"morphir.initialize\"")

    let result =
        entries
        |> List.find (fun entry -> entry.Direction = ExtensionToHost && entry.Entry = initialize.Entry + 1)

    let guest = guestOf ((JsonNode.Parse(current result.Text) |> nonNull)["result"] |> nonNull)

    for entry in entries do
        if entry.Direction = HostToExtension && not (skip.Contains entry.Entry) then
            let reply = dispatch guest (current entry.Text)

            match entry.Node["id"] with
            | null -> Expect.isNone reply $"Entry {entry.Entry}: a notification gets no reply"
            | id ->
                let expected =
                    entries
                    |> List.find (fun other ->
                        other.Direction = ExtensionToHost
                        && other.Entry > entry.Entry
                        && JsonNode.DeepEquals(other.Node["id"], id))

                let actual = reply |> Option.defaultWith (fun () -> failtest $"Entry {entry.Entry}: no reply")
                Expect.isTrue (JsonNode.DeepEquals(actual["id"], id)) $"Entry {entry.Entry}: same id"
                Expect.equal (code actual) (code expected.Node) $"Entry {entry.Entry}: same outcome"

                match expected.Node["error"] with
                | null -> ()
                | error ->
                    Expect.equal
                        (((actual["error"] |> nonNull)["message"] |> nonNull).GetValue<string>())
                        ((error["message"] |> nonNull).GetValue<string>())
                        $"Entry {entry.Entry}: same message"

    guest

/// A stream that records what is written to it.
type private Recording(inner: Stream) =
    inherit Stream()
    let recorded = new MemoryStream()
    member _.BytesWritten = lock recorded (fun () -> recorded.Length)
    member _.Frames =
        let copy = new MemoryStream(recorded.ToArray())

        [ let mutable reading = true

          while reading do
              match ContentLengthFraming.ReadAsync(copy, CancellationToken.None).Result with
              | FrameRead.Message body -> yield JsonNode.Parse(body) |> nonNull
              | _ -> reading <- false ]

    override _.CanRead = false
    override _.CanSeek = false
    override _.CanWrite = true
    override _.Length = raise (NotSupportedException())

    override _.Position
        with get () = raise (NotSupportedException())
        and set _ = raise (NotSupportedException())

    override _.Flush() = inner.Flush()
    override _.FlushAsync(token) = inner.FlushAsync token
    override _.Read(_, _, _) = raise (NotSupportedException())
    override _.Seek(_, _) = raise (NotSupportedException())
    override _.SetLength _ = raise (NotSupportedException())

    override _.Write(buffer: byte array, offset, count) =
        lock recorded (fun () -> recorded.Write(buffer, offset, count))
        inner.Write(buffer, offset, count)

    override _.WriteAsync(buffer: ReadOnlyMemory<byte>, token) =
        lock recorded (fun () -> recorded.Write(buffer.Span))
        inner.WriteAsync(buffer, token)

[<Tests>]
let tests =
    Transcripts.ensureThreads.Force()

    testSequenced
    <| testList
        "MEP session"
        [ testCase "requests before initialization get -32014; describe and initialize are allowed"
          <| fun _ ->
              for method in Method.All do
                  let admission = Lifecycle.admit SessionState.Created method.WireValue method.Kind

                  match method with
                  | Method.Describe
                  | Method.Initialize
                  | Method.Exit -> Expect.equal admission Admission.Proceed $"{method} is allowed"
                  | _ ->
                      Expect.equal
                          admission
                          (Admission.Refuse(RpcErrors.create ErrorCode.NotInitialized))
                          $"{method} is refused"

          testCase "replay of lifecycle-violations.md"
          <| fun _ ->
              let entries = read "lifecycle-violations.md"
              let guest = replay entries Set.empty
              Expect.equal guest.State SessionState.Exited "Exit ends the session"
          |> testLabel "PlatformSpecific"

          testCase "the capabilities that lifecycle-violations.md did not advertise are refused"
          <| fun _ ->
              let entries = read "lifecycle-violations.md"
              let guest = guestOf ((JsonNode.Parse(current entries[5].Text) |> nonNull)["result"] |> nonNull)
              dispatch guest (current entries[4].Text) |> ignore

              for method in [ Method.Progress; Method.CancelRequest; Method.Generate ] do
                  match Lifecycle.admit guest.State method.WireValue method.Kind with
                  | Admission.Refuse error -> Expect.equal error.Code -32013 $"{method} is not advertised"
                  | Admission.Proceed -> failtest $"{method} must be refused"
          |> testLabel "PlatformSpecific"

          testCase "replay of protocol-errors.md"
          <| fun _ ->
              // Entries 5 and 6 need the compile request type, which belongs to add-fsharp-compile, not to the core.
              replay (read "protocol-errors.md") (Set.ofList [ 5 ]) |> ignore
          |> testLabel "PlatformSpecific"

          testCase "replies with a wrong or unknown id are protocol diagnostics of the host connection"
          <| fun _ ->
              let entries = read "protocol-errors.md"
              let fromGuest = Pipe()
              let toGuest = Pipe()

              let host =
                  Connection(fromGuest.Reader.AsStream(), toGuest.Writer.AsStream(), Dispatcher(readOnlyDict []))

              let run = host.RunAsync CancellationToken.None

              let requests =
                  [ for entry in entries do
                        if entry.Direction = HostToExtension && entry.Node["id"] <> null then
                            host.RequestAsync(
                                (entry.Node["method"] |> nonNull).GetValue<string>(),
                                None,
                                CancellationToken.None
                            ) ]

              use output = fromGuest.Writer.AsStream()

              for entry in entries do
                  if entry.Direction = ExtensionToHost then
                      ContentLengthFraming.WriteAsync(
                          output,
                          ReadOnlyMemory(Encoding.UTF8.GetBytes entry.Text),
                          CancellationToken.None
                      )
                          .Wait()

              Expect.isTrue (Task.WhenAll(requests).Wait(TimeSpan.FromSeconds 10.)) "Every request is answered"
              output.Dispose()
              Expect.isTrue (run.Wait(TimeSpan.FromSeconds 10.)) "The connection ends"

              Expect.equal
                  (host.Diagnostics |> List.ofSeq)
                  [ ConnectionDiagnostic.UnsolicitedResponse(Some(RequestId.Number 30UL))
                    ConnectionDiagnostic.UnsolicitedResponse(Some(RequestId.Number 99UL)) ]
                  "Entries 8 and 9"
          |> testLabel "PlatformSpecific"

          testCase "a second initialize fails and leaves the session initialized"
          <| fun _ ->
              let entries = read "session-model-transport.md"
              let guest = guestOf (entries[1].Node["result"] |> nonNull)
              let first = dispatch guest entries[0].Text |> Option.get
              Expect.isNone (code first) "First initialize succeeds"
              let second = dispatch guest (entries[0].Text.Replace("\"id\": 0", "\"id\": 9")) |> Option.get
              Expect.equal (code second) (Some -32600) "Second initialize fails"

              Expect.equal
                  (((second["error"] |> nonNull)["message"] |> nonNull).GetValue<string>())
                  "Extension is already initialized"
                  "The upstream guests' message"

              match guest.State with
              | SessionState.Initialized _ -> ()
              | other -> failtest $"Still initialized, not {other}"
          |> testLabel "PlatformSpecific"

          testCase "a legacy-only host gets -32011, and a later initialize succeeds"
          <| fun _ ->
              let entries = read "session-model-transport.md"
              let guest = guestOf (entries[1].Node["result"] |> nonNull)
              let legacy = entries[0].Text.Replace("[\"0.2.0-draft.1\", \"0.1\"]", "[\"0.1\"]")
              let refused = dispatch guest legacy |> Option.get
              Expect.equal (code refused) (Some -32011) "No common version"

              Expect.equal
                  (((refused["error"] |> nonNull)["data"] |> nonNull).ToJsonString())
                  """{"hostVersions":["0.1"],"extensionVersions":["0.2.0-draft.1"]}"""
                  "The versions of both peers, as the Rust guest sends them"
              Expect.equal guest.State SessionState.Created "Still uninitialized"
              Expect.isNone (dispatch guest entries[0].Text |> Option.get |> code) "Then initialize succeeds"
          |> testLabel "PlatformSpecific"

          testCase "the host offers only 0.2.0-draft.1"
          <| fun _ -> Expect.equal (Lifecycle.protocolVersions |> List.ofSeq |> List.map string) [ "0.2.0-draft.1" ] "No 0.1"

          testCase "a full in-memory session gives the transcript messages"
          <| fun _ ->
              let entries = read "session-model-transport.md"
              let expectedParams = entries[0].Node["params"] |> nonNull
              let expectedResult = entries[1].Node["result"] |> nonNull
              let guest = guestOf expectedResult
              let toGuest = Pipe()
              let toHost = Pipe()
              let hostWrites = new Recording(toGuest.Writer.AsStream())
              let guestWrites = new Recording(toHost.Writer.AsStream())
              let hostReads = new CF.ObservedRead(toHost.Reader.AsStream())
              let guestRun = guest.RunAsync(toGuest.Reader.AsStream(), guestWrites, CancellationToken.None)
              let initialize = decode MepJson.Default.InitializeParams expectedParams

              let host =
                  HostSession(hostReads, hostWrites, initialize.Host, Option.toObj initialize.ModelTransport)

              let hostRun = host.RunAsync CancellationToken.None
              let wait (operation: Task<'T>) = operation.WaitAsync(TimeSpan.FromSeconds 10.).GetAwaiter().GetResult()

              Expect.isOk (wait (host.DescribeAsync CancellationToken.None)) "describe"
              let session = wait (host.InitializeAsync CancellationToken.None)
              Expect.isOk session "initialize"
              Expect.isOk (wait (host.PingAsync CancellationToken.None)) "ping"
              Expect.isOk (wait (host.InfoAsync CancellationToken.None)) "info"
              Expect.isOk (wait (host.CapabilitiesAsync CancellationToken.None)) "capabilities"
              Expect.isOk (wait (host.ShutdownAsync CancellationToken.None)) "shutdown"

              match wait (host.PingAsync CancellationToken.None) with
              | Error error -> Expect.equal error.Code -32014 "The host refuses to send after shutdown"
              | Ok _ -> failtest "Ping after shutdown"

              // Enter the read after the shutdown response before exit changes the host's local state.
              wait (hostReads.AfterBytes guestWrites.BytesWritten)
              host.ExitAsync().WaitAsync(TimeSpan.FromSeconds 10.).GetAwaiter().GetResult()
              Expect.equal (wait guestRun) ConnectionEnd.Stopped "The guest stops at exit"
              toHost.Writer.Complete()
              Expect.equal (wait hostRun) ConnectionEnd.EndOfStream "The host reads to the end"

              let sent = hostWrites.Frames
              let replies = guestWrites.Frames
              let methods = sent |> List.map (fun frame -> (frame["method"] |> nonNull).GetValue<string>())

              Expect.equal
                  methods
                  [ "morphir.extension.describe"
                    "morphir.initialize"
                    "morphir.initialized"
                    "morphir.ping"
                    "morphir.extension.info"
                    "morphir.extension.capabilities"
                    "morphir.shutdown"
                    "morphir.exit" ]
                  "Messages in lifecycle order"

              let offered = expectedParams.DeepClone()
              offered["protocolVersions"] <- JsonArray(JsonValue.Create "0.2.0-draft.1")
              Expect.isTrue (JsonNode.DeepEquals(sent[1]["params"], offered)) "initialize params, with only 0.2.0-draft.1"
              Expect.isTrue (JsonNode.DeepEquals(replies[1]["result"], expectedResult)) "initialize result"
              Expect.isTrue (JsonNode.DeepEquals(sent[2], entries[2].Node)) "initialized notification"
              Expect.equal ((replies[2]["result"] |> nonNull).ToJsonString()) """{"ok":true}""" "ping result"
              Expect.equal ((replies[5]["result"] |> nonNull).ToJsonString()) "{}" "shutdown result"
              Expect.isTrue (JsonNode.DeepEquals(sent[7], entries[entries.Length - 1].Node)) "exit notification"
              Expect.equal replies.Length 6 "One reply for each request, none for notifications"
          |> testLabel "PlatformSpecific" ]

// Tests only. Synthetic adapter messages cannot establish inner-WASM delivery, process exit or stdout EOF.
[<Tests>]
let conformanceLifecycleTests =
    testSequenced <| testList "MEP conformance lifecycle probes" [
        testCase "fixed probes use explicit empty params and preserve actual peer refusal bytes" <| fun _ ->
            use lifetime = new CF.Lifetime()
            let edge = CF.Edge()
            let session = CF.create edge lifetime
            CF.initialize session
            CF.wait ((CF.control session).SendUnknownNotificationAsync CancellationToken.None) |> CF.expectSubmitted |> ignore
            CF.wait (session.ShutdownAsync CancellationToken.None) |> CF.expectOk |> ignore
            let reply = CF.wait ((CF.control session).ProbeAfterShutdownAsync CancellationToken.None) |> CF.expectSubmitted |> Option.get
            Expect.equal reply.RequestId (RequestId.Number 4UL) "Actual matching request ID"
            Expect.equal reply.Body (CF.error "4" -32014) "Actual adapter response, not local RpcError reconstruction"
            CF.finish (session.ExitAsync())
            Expect.equal (CF.methods edge.Bodies) ["morphir.extension.describe"; "morphir.initialize"; "morphir.initialized"; "morphir.mck.unknown"; "morphir.shutdown"; "morphir.ping"; "morphir.exit"] "One session and channel"
            for body in edge.Bodies |> List.skip 2 do CF.emptyParams body
            Expect.equal (CF.ids edge.Bodies) [1UL; 2UL; 3UL; 4UL] "Notifications consume no IDs"

        testCase "ordinary session keeps zero seed, local post-shutdown guard and absent params" <| fun _ ->
            let edge = CF.Edge()
            let session = HostSession(edge :> ISingleMessageChannel, CF.host, CF.offer ())
            CF.wait (session.InitializeAsync CancellationToken.None) |> CF.expectOk |> ignore
            CF.wait (session.ShutdownAsync CancellationToken.None) |> CF.expectOk |> ignore
            let before = edge.Bodies
            Expect.isError (CF.wait (session.PingAsync CancellationToken.None)) "Normal guard remains local"
            Expect.equal edge.Bodies before "No bypass for an ordinary session"
            CF.finish (session.ExitAsync())
            Expect.equal (CF.ids edge.Bodies) [0UL; 1UL] "Unchanged product counter"
            for body in edge.Bodies |> List.skip 1 do CF.noParams body
            Expect.isNone session.Conformance "No product control"

        testCase "absent null and empty object remain distinct JSON bodies" <| fun _ ->
            let texts = ["""{"jsonrpc":"2.0","method":"morphir.exit"}"""; """{"jsonrpc":"2.0","method":"morphir.exit","params":null}"""; """{"jsonrpc":"2.0","method":"morphir.exit","params":{}}"""]
            let decoded = texts |> List.map (fun text -> JsonRpc.Read(ReadOnlyMemory(CF.utf8 text)))
            Expect.equal (decoded |> List.map Result.isOk) [true; false; true] "Existing parser accepts omission and object; null retains its existing refusal"
            Expect.equal (texts |> List.distinct |> List.length) 3 "Sender canonicalization is not body equivalence"

        testCase "wrong-state probes refuse without I/O IDs or consuming the later opportunity" <| fun _ ->
            use lifetime = new CF.Lifetime()
            let edge = CF.Edge()
            let session = CF.create edge lifetime
            CF.wait ((CF.control session).SendUnknownNotificationAsync CancellationToken.None) |> CF.expectNotSubmitted
            CF.wait ((CF.control session).ProbeAfterShutdownAsync CancellationToken.None) |> CF.expectNotSubmitted
            Expect.isEmpty edge.Bodies "No invented request IDs"
            CF.initialize session
            CF.wait ((CF.control session).SendUnknownNotificationAsync CancellationToken.None) |> CF.expectSubmitted |> ignore
            CF.wait (session.ShutdownAsync CancellationToken.None) |> CF.expectOk |> ignore
            CF.wait ((CF.control session).ProbeAfterShutdownAsync CancellationToken.None) |> CF.expectSubmitted |> ignore
            Expect.equal (CF.ids edge.Bodies) [1UL; 2UL; 3UL; 4UL] "Rejected probes consumed none"

        testCase "shutdown wins admission and queued unknown notification rechecks committed state" <| fun _ ->
            use lifetime = new CF.Lifetime()
            let edge = CF.Edge()
            let session = CF.create edge lifetime
            CF.initialize session
            use barrier = new CF.Barrier()
            edge.After <- fun method token -> if method = "morphir.shutdown" then barrier.Pause token :> Task else Task.CompletedTask
            let shutdown = session.ShutdownAsync CancellationToken.None
            CF.wait barrier.Entered |> ignore // Peer has prepared its reply, but adapter has not returned it.
            let notification = (CF.control session).SendUnknownNotificationAsync CancellationToken.None
            barrier.Release()
            CF.wait shutdown |> CF.expectOk |> ignore
            CF.wait notification |> CF.expectNotSubmitted
            Expect.equal (CF.methods edge.Bodies |> List.last) "morphir.shutdown" "No stale Initialized admission"
            CF.wait ((CF.control session).ProbeAfterShutdownAsync CancellationToken.None) |> CF.expectSubmitted |> ignore
            Expect.equal (CF.ids edge.Bodies) [1UL; 2UL; 3UL; 4UL] "Local refusal allocates no ID"

        testCase "unknown notification wins admission and settles before shutdown is submitted" <| fun _ ->
            use lifetime = new CF.Lifetime()
            let edge = CF.Edge()
            let session = CF.create edge lifetime
            CF.initialize session
            use barrier = new CF.Barrier()
            edge.Before <- fun method token -> if method = "morphir.mck.unknown" then barrier.Pause token :> Task else Task.CompletedTask
            let notification = (CF.control session).SendUnknownNotificationAsync CancellationToken.None
            CF.wait barrier.Entered |> ignore
            let shutdown = session.ShutdownAsync CancellationToken.None
            Expect.equal (CF.methods edge.Bodies |> List.last) "morphir.mck.unknown" "Shutdown waits outside the adapter"
            barrier.Release()
            CF.wait notification |> CF.expectSubmitted |> ignore
            CF.wait shutdown |> CF.expectOk |> ignore
            Expect.equal (CF.methods edge.Bodies |> List.skip 3) ["morphir.mck.unknown"; "morphir.shutdown"] "Winning order"

        for probe in ["unknown"; "after-shutdown"] do
            testCase $"concurrent duplicate {probe} probes have one reservation and submission" <| fun _ ->
                use lifetime = new CF.Lifetime()
                let edge = CF.Edge()
                let session = CF.create edge lifetime
                CF.initialize session
                if probe = "after-shutdown" then CF.wait (session.ShutdownAsync CancellationToken.None) |> CF.expectOk |> ignore
                let method = if probe = "unknown" then "morphir.mck.unknown" else "morphir.ping"
                use barrier = new CF.Barrier()
                edge.Before <- fun name token -> if name = method then barrier.Pause token :> Task else Task.CompletedTask
                let invoke () = if probe = "unknown" then (CF.control session).SendUnknownNotificationAsync CancellationToken.None else (CF.control session).ProbeAfterShutdownAsync CancellationToken.None
                let one = invoke ()
                CF.wait barrier.Entered |> ignore
                let two = invoke ()
                barrier.Release()
                CF.wait one |> CF.expectSubmitted |> ignore
                CF.wait two |> CF.expectNotSubmitted
                Expect.equal (CF.methods edge.Bodies |> List.filter ((=) method) |> List.length) 1 "One actual callback, not two reservations reported as writes"

        for exitFirst in [true; false] do
            testCase $"exit versus post-shutdown probe respects admission order exitFirst={exitFirst}" <| fun _ ->
                use lifetime = new CF.Lifetime()
                let edge = CF.Edge()
                let session = CF.create edge lifetime
                CF.initialize session
                CF.wait (session.ShutdownAsync CancellationToken.None) |> CF.expectOk |> ignore
                use barrier = new CF.Barrier()
                let winningMethod = if exitFirst then "morphir.exit" else "morphir.ping"
                edge.Before <- fun method token -> if method = winningMethod then barrier.Pause token :> Task else Task.CompletedTask
                if exitFirst then
                    let exit = session.ExitAsync()
                    CF.wait barrier.Entered |> ignore
                    let probe = (CF.control session).ProbeAfterShutdownAsync CancellationToken.None
                    barrier.Release()
                    CF.finish exit
                    CF.wait probe |> CF.expectNotSubmitted
                    Expect.equal (CF.methods edge.Bodies |> List.last) "morphir.exit" "Nothing after terminal reservation"
                else
                    let probe = (CF.control session).ProbeAfterShutdownAsync CancellationToken.None
                    CF.wait barrier.Entered |> ignore
                    let exit = session.ExitAsync()
                    Expect.equal (CF.methods edge.Bodies |> List.last) "morphir.ping" "Exit is queued"
                    barrier.Release()
                    CF.wait probe |> CF.expectSubmitted |> ignore
                    CF.finish exit
                    Expect.equal (CF.methods edge.Bodies |> List.skip 4) ["morphir.ping"; "morphir.exit"] "Admitted probe settles first"
                let before = edge.Bodies
                CF.wait ((CF.control session).ProbeAfterShutdownAsync CancellationToken.None) |> CF.expectNotSubmitted
                Expect.equal edge.Bodies before "No duplicate after exit"

        testCase "exit terminal reservation blocks a probe before the actual stream write completes" <| fun _ ->
            use edge = new CF.StreamEdge()
            let session = edge.Session
            let describe = session.DescribeAsync CancellationToken.None
            edge.Read() |> ignore
            edge.Send(CF.response "1" CF.claims)
            CF.wait describe |> CF.expectOk |> ignore
            let receipt = (CF.control session).DescribeReceipt |> Option.get
            (CF.control session).BindSelectedOffer(receipt, CF.offer ()) |> CF.expectOk
            let initialize = session.InitializeAsync CancellationToken.None
            edge.Read() |> ignore
            let result = """{"protocolVersion":"0.2.0-draft.1","extension":{"id":"org.example.conformance","name":"Test peer","version":"1.0.0","types":["frontend","backend"]},"capabilities":{"modelTransport":OFF}}""".Replace("OFF", CF.offerJson)
            edge.Send(CF.response "2" result)
            edge.Read() |> CF.emptyParams
            CF.wait initialize |> CF.expectOk |> ignore
            let shutdown = session.ShutdownAsync CancellationToken.None
            edge.Read() |> CF.emptyParams
            edge.Send(CF.response "3" "{}")
            CF.wait shutdown |> CF.expectOk |> ignore
            edge.ArmWrites()
            let exit = session.ExitAsync()
            CF.wait edge.WriteBarrier.Entered |> ignore
            let before = CF.prepared session
            let probe = (CF.control session).ProbeAfterShutdownAsync CancellationToken.None
            Expect.equal (CF.prepared session) before "Queued probe has no body before exit completion"
            edge.WriteBarrier.Release()
            edge.Read() |> CF.emptyParams
            CF.finish exit
            CF.wait probe |> CF.expectNotSubmitted
            Expect.equal (CF.ids (CF.prepared session)) [1UL; 2UL; 3UL] "Exit reservation cannot admit a fourth request"

        for failInitialized in [false; true] do
            testCase $"initialize owns automatic initialized and state commit failure={failInitialized}" <| fun _ ->
                use lifetime = new CF.Lifetime()
                let edge = CF.Edge()
                let session = CF.create edge lifetime
                CF.describeAndBind session |> ignore
                use barrier = new CF.Barrier()
                edge.Before <- fun method token -> if method = "morphir.initialized" then barrier.Pause token :> Task else Task.CompletedTask
                if failInitialized then edge.Replace <- fun method reply -> if method = "morphir.initialized" then raise (IOException "notification failed") else reply
                let initializing = session.InitializeAsync CancellationToken.None
                CF.wait barrier.Entered |> ignore
                let before = CF.prepared session
                let admitted = CF.events session |> List.filter (function ConformanceEvent.OperationAdmitted _ -> true | _ -> false) |> List.length
                let ping = session.PingAsync CancellationToken.None
                Expect.equal (CF.prepared session) before "Queued ordinary send has no prepared body"
                Expect.equal (CF.events session |> List.filter (function ConformanceEvent.OperationAdmitted _ -> true | _ -> false) |> List.length) admitted "No successful admission while initialize owns the operation"
                barrier.Release()
                if failInitialized then
                    Expect.isError (CF.wait initializing) "Initialization did not commit"
                    Expect.isError (CF.wait ping) "Queued request sees terminal failure"
                    Expect.equal (CF.methods edge.Bodies |> List.last) "morphir.initialized" "No ping reaches failed channel"
                else
                    CF.wait initializing |> CF.expectOk |> ignore
                    CF.wait ping |> CF.expectOk |> ignore
                    Expect.equal (CF.methods edge.Bodies |> List.skip 1) ["morphir.initialize"; "morphir.initialized"; "morphir.ping"] "No recursive permit deadlock or interleaving"

        testCase "queued cancellation consumes no request ID and does not reserve a probe" <| fun _ ->
            use lifetime = new CF.Lifetime()
            use queuedCancellation = new CancellationTokenSource()
            let edge = CF.Edge()
            let session = CF.create edge lifetime
            CF.initialize session
            use barrier = new CF.Barrier()
            edge.Before <- fun method token -> if method = "morphir.ping" then barrier.Pause token :> Task else Task.CompletedTask
            let ping = session.PingAsync CancellationToken.None
            CF.wait barrier.Entered |> ignore
            let queued = (CF.control session).SendUnknownNotificationAsync queuedCancellation.Token
            queuedCancellation.Cancel()
            CF.wait queued |> CF.expectNotSubmitted
            barrier.Release()
            CF.wait ping |> CF.expectOk |> ignore
            CF.wait ((CF.control session).SendUnknownNotificationAsync CancellationToken.None) |> CF.expectSubmitted |> ignore
            Expect.equal (CF.ids edge.Bodies) [1UL; 2UL; 3UL] "Queued cancellation allocated no ID"

        testCase "admitted cancellation of an ignoring adapter ends bounded lifetime with incomplete provenance" <| fun _ ->
            use lifetime = new CF.Lifetime()
            use requestCancellation = new CancellationTokenSource()
            let edge = CF.Edge()
            let session = CF.create edge lifetime
            CF.initialize session
            use barrier = new CF.Barrier()
            // Ignore request cancellation, but the explicit session lifetime still owns this bounded await.
            edge.Before <- fun method _ -> if method = "morphir.mck.unknown" then barrier.Pause lifetime.Token :> Task else Task.CompletedTask
            let pending = (CF.control session).SendUnknownNotificationAsync requestCancellation.Token
            CF.wait barrier.Entered |> ignore
            requestCancellation.Cancel()
            lifetime.Cancel()
            match CF.wait pending with
            | ConformanceProbeOutcome.Incomplete _ -> ()
            | other -> failtestf "Admitted submission cannot be rewritten as never submitted: %A" other
            Expect.isFalse ((CF.control session).Snapshot().IsComplete) "No fabricated silence or completion"
            Expect.isTrue (CF.events session |> List.exists (function ConformanceEvent.OperationAdmitted _ -> true | _ -> false)) "Reservation is retained as admission only"
            Expect.isTrue (CF.events session |> List.exists (function ConformanceEvent.OperationCancelled _ -> true | _ -> false)) "Cancellation does not erase the admitted operation"
    ]

[<Tests>]
let conformanceSelectedOfferTests =
    testSequenced <| testList "MEP conformance selected offer binding" [
        testCase "one original describe receipt binds once and initialize uses IDs one then two" <| fun _ ->
            use lifetime = new CF.Lifetime()
            let edge = CF.Edge()
            let session = CF.create edge lifetime
            Expect.isError (CF.wait (session.InitializeAsync CancellationToken.None)) "Unbound initialize refuses locally"
            Expect.isEmpty edge.Bodies "No speculative offer or ID"
            CF.initialize session
            Expect.equal (CF.ids edge.Bodies) [1UL; 2UL] "Binding has no wire operation"
            Expect.equal (CF.methods edge.Bodies) ["morphir.extension.describe"; "morphir.initialize"; "morphir.initialized"] "Same factory/session/adapter"
            use document = CF.json edge.Bodies[1]
            let actual = document.RootElement.GetProperty("params").GetProperty("modelTransport")
            use expected = JsonDocument.Parse CF.offerJson
            Expect.isTrue (JsonElement.DeepEquals(actual, expected.RootElement)) "Exactly selected pair order and limits"

        testCase "foreign receipt cannot bind another session or consume its local opportunity" <| fun _ ->
            use lifetime = new CF.Lifetime()
            let edgeOne, edgeTwo = CF.Edge(), CF.Edge()
            let one, two = CF.create edgeOne lifetime, CF.create edgeTwo lifetime
            let receipt = CF.describeAndBind one
            CF.wait (two.DescribeAsync CancellationToken.None) |> CF.expectOk |> ignore
            Expect.isError ((CF.control two).BindSelectedOffer(receipt, CF.offer ())) "Receipt belongs to another session"
            let own = (CF.control two).DescribeReceipt |> Option.get
            (CF.control two).BindSelectedOffer(own, CF.offer ()) |> CF.expectOk
            CF.wait (two.InitializeAsync CancellationToken.None) |> CF.expectOk |> ignore
            Expect.equal (CF.ids edgeTwo.Bodies) [1UL; 2UL] "Foreign rejection has no I/O or ID"

        testCase "later describe cannot overwrite original receipt and rebinding refuses without I/O" <| fun _ ->
            use lifetime = new CF.Lifetime()
            let edge = CF.Edge()
            let session = CF.create edge lifetime
            let original = CF.describeAndBind session
            CF.wait (session.DescribeAsync CancellationToken.None) |> CF.expectOk |> ignore
            Expect.equal ((CF.control session).DescribeReceipt |> Option.get) original "Original successful exchange owns receipt"
            let before = edge.Bodies
            Expect.isError ((CF.control session).BindSelectedOffer(original, CF.offer ())) "One-shot binding"
            Expect.equal edge.Bodies before "Binding refusal is local"

        testCase "busy binding refuses atomically and succeeds only after the active operation settles" <| fun _ ->
            use lifetime = new CF.Lifetime()
            let edge = CF.Edge()
            let session = CF.create edge lifetime
            CF.wait (session.DescribeAsync CancellationToken.None) |> CF.expectOk |> ignore
            let receipt = (CF.control session).DescribeReceipt |> Option.get
            use barrier = new CF.Barrier()
            edge.Before <- fun _ token -> barrier.Pause token :> Task
            let pending = session.DescribeAsync CancellationToken.None
            CF.wait barrier.Entered |> ignore
            Expect.isError ((CF.control session).BindSelectedOffer(receipt, CF.offer ())) "Synchronous bind never waits behind active async operation"
            barrier.Release()
            CF.wait pending |> CF.expectOk |> ignore
            (CF.control session).BindSelectedOffer(receipt, CF.offer ()) |> CF.expectOk

        for invalid in ["unadvertised"; "duplicate"] do
            testCase $"selected offer rejects {invalid} tuple without consuming ID or binding opportunity" <| fun _ ->
                use lifetime = new CF.Lifetime()
                let edge = CF.Edge()
                let session = CF.create edge lifetime
                CF.wait (session.DescribeAsync CancellationToken.None) |> CF.expectOk |> ignore
                let receipt = (CF.control session).DescribeReceipt |> Option.get
                let offered = CF.offer ()
                let tuples = if invalid = "duplicate" then [|offered.Tuples[0]; offered.Tuples[0]|] else [|{ offered.Tuples[0] with IrVersion = SemanticVersion.Create "3.0.0" }|]
                Expect.isError ((CF.control session).BindSelectedOffer(receipt, { offered with Tuples = tuples })) "Invalid owned tuple set"
                Expect.equal (CF.ids edge.Bodies) [1UL] "No wire request for bind"
                (CF.control session).BindSelectedOffer(receipt, CF.offer ()) |> CF.expectOk
                CF.wait (session.InitializeAsync CancellationToken.None) |> CF.expectOk |> ignore
                Expect.equal (CF.ids edge.Bodies) [1UL; 2UL] "Valid bind remains available"

        testCase "binding owns tuple and guarantee buffers used by both encoding and result validation" <| fun _ ->
            use lifetime = new CF.Lifetime()
            let edge = CF.Edge()
            let session = CF.create edge lifetime
            CF.wait (session.DescribeAsync CancellationToken.None) |> CF.expectOk |> ignore
            let receipt = (CF.control session).DescribeReceipt |> Option.get
            let offered = CF.offer ()
            let tuples, guarantees = Array.ofSeq offered.Tuples, Array.ofSeq offered.Guarantees
            (CF.control session).BindSelectedOffer(receipt, { offered with Tuples = tuples; Guarantees = guarantees }) |> CF.expectOk
            tuples[0] <- { tuples[0] with IrVersion = SemanticVersion.Create "3.0.0" }
            guarantees[0] <- "urn:test:mutated"
            CF.wait (session.InitializeAsync CancellationToken.None) |> CF.expectOk |> ignore
            use document = CF.json edge.Bodies[1]
            use expected = JsonDocument.Parse CF.offerJson
            Expect.isTrue (JsonElement.DeepEquals(document.RootElement.GetProperty("params").GetProperty("modelTransport"), expected.RootElement)) "Retained identical snapshot encoded and accepted"

        testCase "result binding rejects an unoffered selection rather than reading mutated caller storage" <| fun _ ->
            use lifetime = new CF.Lifetime()
            let edge = CF.Edge()
            let session = CF.create edge lifetime
            CF.describeAndBind session |> ignore
            edge.Replace <- fun method reply ->
                if method = "morphir.initialize" then
                    match reply with null -> null | body -> Encoding.UTF8.GetString(body).Replace("4.0.0", "3.0.0") |> CF.utf8
                else reply
            Expect.isError (CF.wait (session.InitializeAsync CancellationToken.None)) "Existing result-selection verification is retained"
            Expect.equal (CF.methods edge.Bodies |> List.last) "morphir.initialize" "No initialized notification for an invalid selection"

        testCase "failed describe gives no receipt and cannot be replaced by a typed local result" <| fun _ ->
            use lifetime = new CF.Lifetime()
            let edge = CF.Edge()
            edge.Replace <- fun _ _ -> CF.error "1" -32601
            let session = CF.create edge lifetime
            Expect.isError (CF.wait (session.DescribeAsync CancellationToken.None)) "Observed peer refusal"
            Expect.isNone (CF.control session).DescribeReceipt "No successful original exchange"
            Expect.isError (CF.wait (session.InitializeAsync CancellationToken.None)) "Still unbound"
            Expect.equal (CF.ids edge.Bodies) [1UL] "No second factory or speculative initialization"

        testCase "rebind concurrent with initialization and after failed initialization stays local" <| fun _ ->
            use lifetime = new CF.Lifetime()
            let edge = CF.Edge()
            let session = CF.create edge lifetime
            let receipt = CF.describeAndBind session
            use barrier = new CF.Barrier()
            edge.Before <- fun method token -> if method = "morphir.initialize" then barrier.Pause token :> Task else Task.CompletedTask
            edge.Replace <- fun method reply -> if method = "morphir.initialize" then CF.error "2" -32011 else reply
            let initializing = session.InitializeAsync CancellationToken.None
            CF.wait barrier.Entered |> ignore
            Expect.isError ((CF.control session).BindSelectedOffer(receipt, CF.offer ())) "Attempt is reserved"
            barrier.Release()
            Expect.isError (CF.wait initializing) "Peer refusal"
            Expect.isError ((CF.control session).BindSelectedOffer(receipt, CF.offer ())) "Failed attempt cannot reset binding"
            Expect.equal (CF.ids edge.Bodies) [1UL; 2UL] "No rebind message"
    ]

[<Tests>]
let operationProjectionSessionTests =
    testSequenced <| testList "MEP conformance operation projections" [
        testCase "unbound initialize is explicitly local and invents no operation or wire bytes" <| fun _ ->
            use lifetime = new CF.Lifetime()
            let edge = CF.Edge()
            let session = CF.create edge lifetime
            let result = CF.wait ((CF.control session).InitializeAsync CancellationToken.None)
            Expect.isError result.Result "InitializeFailure remains typed"
            Expect.isNone result.Observation.Operation "No admission"
            Expect.isNone result.Observation.RequestId "No allocator reservation"
            Expect.isNone result.Observation.RequestBody "No prepared body"
            Expect.isNone result.Observation.ResponseBody "No peer answer"
            Expect.isEmpty result.Notifications "No initialized notification"
            Expect.isEmpty edge.Bodies "Same existing guard"
            match result.Observation.Origin with
            | ConformanceOperationOrigin.AnsweredLocally(ConformanceOperationFailure.LocalLifecycleRefusal _) -> ()
            | _ -> failtest "Local refusal must remain distinct"

        testCase "request shutdown and postshutdown probe expose their own correlated exchanges" <| fun _ ->
            use lifetime = new CF.Lifetime()
            let edge = CF.Edge()
            let session = CF.create edge lifetime
            CF.initialize session
            let control = CF.control session
            let ping = CF.wait (control.RequestAsync(Method.Ping, Nullable(), CancellationToken.None))
            ping.Result |> CF.expectOk |> ignore
            Expect.equal ping.Observation.RequestId (Some(RequestId.Number 3UL)) "Ping's actual ID"
            Expect.equal ping.Observation.RequestBody (Some edge.Bodies[3]) "Ping's actual body"
            Expect.isSome ping.Observation.ResponseBody "Correlated ping answer"
            let shutdown = CF.wait (control.ShutdownAsync CancellationToken.None)
            shutdown.Result |> CF.expectOk |> ignore
            Expect.equal shutdown.Observation.RequestId (Some(RequestId.Number 4UL)) "Shutdown's actual ID"
            Expect.equal shutdown.Observation.RequestBody (Some edge.Bodies[4]) "Shutdown's actual body"
            let probe = CF.wait (control.ProbeAfterShutdownObservedAsync CancellationToken.None)
            Expect.isError probe.Result "Actual peer refusal remains an RpcError"
            Expect.equal probe.Observation.RequestId (Some(RequestId.Number 5UL)) "Probe's actual ID"
            Expect.equal probe.Observation.RequestBody (Some edge.Bodies[5]) "Probe's actual canonical params"
            Expect.isSome probe.Observation.ResponseBody "Peer error body is evidence"
            match probe.Observation.Origin with
            | ConformanceOperationOrigin.Submitted -> ()
            | _ -> failtest "A correlated peer error is not a local answer"
            let refused = CF.wait (control.RequestAsync(Method.Ping, Nullable(), CancellationToken.None))
            Expect.isError refused.Result "Ordinary postshutdown guard remains"
            Expect.isNone refused.Observation.RequestId "Local refusal allocates no ID"
            Expect.equal edge.Bodies.Length 6 "No hidden second send or factory"

        testCase "queued cancelled projection does not borrow an admitted operation" <| fun _ ->
            use lifetime = new CF.Lifetime()
            let edge = CF.Edge()
            let session = CF.create edge lifetime
            CF.initialize session
            let control = CF.control session
            use barrier = new CF.Barrier()
            edge.Before <- fun method token -> if method = "morphir.ping" then barrier.Pause token :> Task else Task.CompletedTask
            let first = control.RequestAsync(Method.Ping, Nullable(), CancellationToken.None)
            CF.wait barrier.Entered |> ignore
            use cancellation = new CancellationTokenSource()
            let queued = control.RequestAsync(Method.Ping, Nullable(), cancellation.Token)
            cancellation.Cancel()
            let refused = CF.wait queued
            Expect.isError refused.Result "Queued cancellation settles"
            Expect.isNone refused.Observation.Operation "No borrowed admission"
            Expect.isNone refused.Observation.RequestId "No borrowed ID"
            Expect.isNone refused.Observation.RequestBody "No borrowed body"
            Expect.isNone refused.Observation.ResponseBody "No borrowed answer"
            match refused.Observation.Origin with
            | ConformanceOperationOrigin.AnsweredLocally(ConformanceOperationFailure.Cancelled _) -> ()
            | _ -> failtest "Queued cancellation retains its primary cause"
            barrier.Release()
            (CF.wait first).Result |> CF.expectOk |> ignore
            Expect.equal (CF.ids edge.Bodies) [1UL; 2UL; 3UL] "Only admitted operations use the allocator"
    ]

[<Tests>]
let fixedUnknownOperationProjectionTests =
    testSequenced <| testList "MEP conformance operation projections" [
        testCase "fixed unknown-method request is initialized-only and retains its peer error exchange" <| fun _ ->
            use lifetime = new CF.Lifetime()
            let edge = CF.Edge()
            let session = CF.create edge lifetime
            let control = CF.control session
            let local = CF.wait (control.RequestUnknownMethodAsync CancellationToken.None)
            Expect.isError local.Result "Created state refuses locally"
            Expect.isNone local.Observation.Operation "No admission before initialize"
            Expect.isNone local.Observation.RequestId "No allocator use before initialize"
            Expect.isEmpty edge.Bodies "No raw unknown send"
            CF.initialize session
            let result = CF.wait (control.RequestUnknownMethodAsync CancellationToken.None)
            Expect.isError result.Result "Unknown method is an actual peer refusal"
            Expect.equal result.Observation.RequestId (Some(RequestId.Number 3UL)) "Actual fixed request ID"
            Expect.equal (CF.methods [result.Observation.RequestBody |> Option.get]) ["morphir.mck.unknown"] "The only admitted unknown request name"
            Expect.isSome result.Observation.ResponseBody "Correlated peer error retained"
            match result.Observation.Origin with
            | ConformanceOperationOrigin.Submitted -> ()
            | _ -> failtest "A checked peer error must not be relabeled local"
    ]
