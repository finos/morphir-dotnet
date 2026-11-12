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

          testCase "the capabilities that lifecycle-violations.md did not advertise are refused"
          <| fun _ ->
              let entries = read "lifecycle-violations.md"
              let guest = guestOf ((JsonNode.Parse(current entries[5].Text) |> nonNull)["result"] |> nonNull)
              dispatch guest (current entries[4].Text) |> ignore

              for method in [ Method.Progress; Method.CancelRequest; Method.Generate ] do
                  match Lifecycle.admit guest.State method.WireValue method.Kind with
                  | Admission.Refuse error -> Expect.equal error.Code -32013 $"{method} is not advertised"
                  | Admission.Proceed -> failtest $"{method} must be refused"

          testCase "replay of protocol-errors.md"
          <| fun _ ->
              // Entries 5 and 6 need the compile request type, which belongs to add-fsharp-compile, not to the core.
              replay (read "protocol-errors.md") (Set.ofList [ 5 ]) |> ignore

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
              let guestRun = guest.RunAsync(toGuest.Reader.AsStream(), guestWrites, CancellationToken.None)
              let initialize = decode MepJson.Default.InitializeParams expectedParams

              let host =
                  HostSession(toHost.Reader.AsStream(), hostWrites, initialize.Host, Option.toObj initialize.ModelTransport)

              let hostRun = host.RunAsync CancellationToken.None
              let wait (task: Task<'T>) = task.Wait(TimeSpan.FromSeconds 10.) |> ignore; task.Result

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

              host.ExitAsync().Wait()
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
              Expect.equal replies.Length 6 "One reply for each request, none for notifications" ]

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
