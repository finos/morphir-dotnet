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
    testList
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
