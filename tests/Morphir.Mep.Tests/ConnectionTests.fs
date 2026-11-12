// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Mep.Tests.ConnectionTests

open System
open System.IO
open System.IO.Pipelines
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.Mep

/// One direction of an in-memory transport.
type private Wire() =
    let pipe = Pipe()
    member val Reader: Stream = pipe.Reader.AsStream()
    member val Writer: Stream = pipe.Writer.AsStream()

let private send (stream: Stream) (json: string) =
    ContentLengthFraming.WriteAsync(stream, ReadOnlyMemory(Encoding.UTF8.GetBytes json), CancellationToken.None)
    |> fun task -> task.Wait()

let private receive (stream: Stream) =
    use timeout = new CancellationTokenSource(TimeSpan.FromSeconds 10.)

    match ContentLengthFraming.ReadAsync(stream, timeout.Token).Result with
    | FrameRead.Message body -> Encoding.UTF8.GetString body
    | other -> failtest $"Expected a frame, got {other}"

/// A stream whose read waits until Fail is called, and then throws an I/O error.
type private BrokenStream() =
    inherit Stream()
    let failed = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    member _.Fail() = failed.TrySetResult() |> ignore
    override _.CanRead = true
    override _.CanSeek = false
    override _.CanWrite = false
    override _.Length = raise (NotSupportedException())

    override _.Position
        with get () = raise (NotSupportedException())
        and set _ = raise (NotSupportedException())

    override _.Flush() = ()
    override _.Read(_, _, _) = raise (IOException "The stream broke.")

    override _.ReadAsync(_: Memory<byte>, _: CancellationToken) =
        ValueTask<int>(
            task {
                do! failed.Task
                return raise (IOException "The stream broke.")
            }
        )

    override _.Seek(_, _) = raise (NotSupportedException())
    override _.SetLength _ = raise (NotSupportedException())
    override _.Write(_, _, _) = raise (NotSupportedException())

/// A handler that waits until its token cancels and records that it saw the cancellation, where Task.Delay ends
/// with it. It does not use token.Register inside `use` (issue 585): the cancel can run Task.Delay's callback first;
/// the handler then resumes on another thread and disposes that registration before the cancelling thread reaches
/// it, so the callback is lost although the token cancelled.
let private slowHandler (cancelled: TaskCompletionSource<unit>) =
    Handlers.raw (fun _ token ->
        task {
            try
                do! Task.Delay(Timeout.Infinite, token)
            with :? OperationCanceledException when token.IsCancellationRequested ->
                cancelled.TrySetResult() |> ignore
                raise (OperationCanceledException(token))

            return Ok(JsonRpc.Constant "{}")
        })

/// A guest connection over two pipes, with a slow method that records its cancellation.
let private guest (cancelled: TaskCompletionSource<unit>) =
    let toGuest = Wire()
    let toHost = Wire()

    let dispatcher =
        Dispatcher(
            readOnlyDict
                [ Method.Ping.WireValue, Handlers.constant """{"ok":true}"""
                  Method.Describe.WireValue,
                  Handlers.request MepJson.Default.DescribeParams MepJson.Default.DescribeParams (fun value _ ->
                      Task.FromResult(Ok value))
                  "test.slow", slowHandler cancelled ]
        )

    let connection = Connection(toGuest.Reader, toHost.Writer, dispatcher)
    let run = connection.RunAsync CancellationToken.None
    toGuest, toHost, connection, run, dispatcher

[<Tests>]
let tests =
    Transcripts.ensureThreads.Force()

    testSequenced
    <| testList
        "MEP connection"
        [ for id in [ "18446744073709551615"; "\"req-abc\""; "\"\""; "0" ] do
              testCase $"a cancelled request with id {id} gets -32800 and its handler token cancels"
              <| fun _ ->
                  let cancelled = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
                  let toGuest, toHost, _, _, _ = guest cancelled
                  send toGuest.Writer $$"""{"jsonrpc":"2.0","id":{{id}},"method":"test.slow"}"""
                  send toGuest.Writer ("""{"jsonrpc":"2.0","method":"$/cancelRequest","params":{"id":""" + id + "}}")
                  Expect.isTrue (cancelled.Task.Wait(TimeSpan.FromSeconds 10.)) "The handler token cancels"

                  Expect.equal
                      (receive toHost.Reader)
                      ("""{"jsonrpc":"2.0","id":""" + id + ""","error":{"code":-32800,"message":"Request cancelled"}}""")
                      "Cancelled reply with the same id token"

          testCase "a cancel for an unknown id is ignored"
          <| fun _ ->
              let toGuest, toHost, _, _, _ = guest (TaskCompletionSource<unit>())
              send toGuest.Writer """{"jsonrpc":"2.0","method":"$/cancelRequest","params":{"id":99}}"""
              send toGuest.Writer """{"jsonrpc":"2.0","id":1,"method":"morphir.ping"}"""
              Expect.equal (receive toHost.Reader) """{"jsonrpc":"2.0","id":1,"result":{"ok":true}}""" "Ping still works"

          testCase "a response with an unknown id is a protocol diagnostic"
          <| fun _ ->
              let toGuest, toHost, connection, _, _ = guest (TaskCompletionSource<unit>())
              send toGuest.Writer """{"jsonrpc":"2.0","id":99,"result":{"ok":true}}"""
              send toGuest.Writer """{"jsonrpc":"2.0","id":1,"method":"morphir.ping"}"""
              receive toHost.Reader |> ignore

              Expect.contains
                  connection.Diagnostics
                  (ConnectionDiagnostic.UnsolicitedResponse(Some(RequestId.Number 99UL)))
                  "Unsolicited response"

          testCase "the stream path and the single-message path give equal bytes"
          <| fun _ ->
              let request =
                  """{"jsonrpc":"2.0","id":"d","method":"morphir.extension.describe","params":{"protocolVersions":["0.2.0-draft.1"]}}"""

              let toGuest, toHost, _, _, dispatcher = guest (TaskCompletionSource<unit>())
              send toGuest.Writer request
              let streamed = receive toHost.Reader

              let single =
                  dispatcher.DispatchAsync(ReadOnlyMemory(Encoding.UTF8.GetBytes request), CancellationToken.None).Result
                  |> Option.ofObj
                  |> Option.map Encoding.UTF8.GetString

              Expect.equal (Some streamed) single "Byte-equal bodies"

          testCase "a host request gets its response and a host cancel reaches the guest"
          <| fun _ ->
              let cancelled = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
              let toGuest, toHost, _, _, _ = guest cancelled
              let host = Connection(toHost.Reader, toGuest.Writer, Dispatcher(readOnlyDict []))
              host.RunAsync CancellationToken.None |> ignore

              match host.RequestAsync(Method.Ping.WireValue, None, CancellationToken.None).Result with
              | Ok result -> Expect.equal (result.GetRawText()) """{"ok":true}""" "Ping result"
              | Error error -> failtest $"Ping failed: {error}"

              use source = new CancellationTokenSource()
              let slow = host.RequestAsync("test.slow", None, source.Token)
              source.CancelAfter(TimeSpan.FromMilliseconds 50.)
              Expect.isTrue (cancelled.Task.Wait(TimeSpan.FromSeconds 10.)) "The guest handler cancels"

              match slow.Result with
              | Error error -> Expect.equal error.Code (int ErrorCode.RequestCancelled) "-32800"
              | Ok _ -> failtest "Expected the cancelled reply"

          testCase "at the end of the stream running handlers finish and their replies are written"
          <| fun _ ->
              let toGuest = Wire()
              let toHost = Wire()
              let release = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)

              let dispatcher =
                  Dispatcher(
                      readOnlyDict
                          [ "test.compile",
                            Handlers.raw (fun _ _ ->
                                task {
                                    do! release.Task
                                    return Ok(JsonRpc.Constant """{"done":true}""")
                                }) ]
                  )

              let run = Connection(toGuest.Reader, toHost.Writer, dispatcher).RunAsync CancellationToken.None
              send toGuest.Writer """{"jsonrpc":"2.0","id":1,"method":"test.compile"}"""
              toGuest.Writer.Dispose()
              Expect.isFalse (run.Wait(TimeSpan.FromMilliseconds 200.)) "The connection waits for the running handler"
              release.SetResult()
              Expect.equal (receive toHost.Reader) """{"jsonrpc":"2.0","id":1,"result":{"done":true}}""" "The reply is written"
              Expect.isTrue (run.Wait(TimeSpan.FromSeconds 10.)) "The connection ends"
              Expect.equal run.Result ConnectionEnd.EndOfStream "End of stream"

          testCase "cancelling the token stops a connection that waits for a handler at the end of the stream"
          <| fun _ ->
              let cancelled = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
              let toGuest = Wire()
              let toHost = Wire()

              let dispatcher =
                  Dispatcher(readOnlyDict [ "test.slow", slowHandler cancelled ])

              use source = new CancellationTokenSource()
              let run = Connection(toGuest.Reader, toHost.Writer, dispatcher).RunAsync source.Token
              send toGuest.Writer """{"jsonrpc":"2.0","id":1,"method":"test.slow"}"""
              toGuest.Writer.Dispose()
              Expect.isFalse (run.Wait(TimeSpan.FromMilliseconds 200.)) "The connection waits for the running handler"
              source.Cancel()
              Expect.isTrue (cancelled.Task.Wait(TimeSpan.FromSeconds 10.)) "The handler token cancels"
              Expect.isTrue (run.Wait(TimeSpan.FromSeconds 10.)) "The connection ends"
              Expect.equal run.Result ConnectionEnd.EndOfStream "End of stream"

          testCase "a framing failure cancels running handlers and ends the connection"
          <| fun _ ->
              let cancelled = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
              let toGuest, _, _, run, _ = guest cancelled
              send toGuest.Writer """{"jsonrpc":"2.0","id":1,"method":"test.slow"}"""
              let bytes = Encoding.ASCII.GetBytes "log line\r\n\r\n"
              toGuest.Writer.Write(bytes, 0, bytes.Length)
              toGuest.Writer.Flush()
              Expect.isTrue (cancelled.Task.Wait(TimeSpan.FromSeconds 10.)) "The handler token cancels"
              Expect.isTrue (run.Wait(TimeSpan.FromSeconds 10.)) "The connection ends"
              Expect.equal run.Result (ConnectionEnd.FramingFailed FramingFailure.NotFramed) "Not framed"

          testCase "cancelling the connection settles its pending requests"
          <| fun _ ->
              let fromGuest = Wire()
              let toGuest = Wire()
              let host = Connection(fromGuest.Reader, toGuest.Writer, Dispatcher(readOnlyDict []))
              use source = new CancellationTokenSource()
              let run = host.RunAsync source.Token
              let pending = host.RequestAsync(Method.Ping.WireValue, None, CancellationToken.None)
              receive toGuest.Reader |> ignore
              source.Cancel()
              Expect.isTrue (pending.Wait(TimeSpan.FromSeconds 10.)) "The pending request completes"
              Expect.isError pending.Result "With an error, because no reply can come"

              Expect.throwsT<AggregateException>
                  (fun () -> run.Wait(TimeSpan.FromSeconds 10.) |> ignore)
                  "The cancellation reaches the caller"

          testCase "a read failure settles pending requests and reaches the caller"
          <| fun _ ->
              let toGuest = Wire()
              let broken = new BrokenStream()
              let host = Connection(broken, toGuest.Writer, Dispatcher(readOnlyDict []))
              let pending = host.RequestAsync(Method.Ping.WireValue, None, CancellationToken.None)
              receive toGuest.Reader |> ignore
              let run = host.RunAsync CancellationToken.None
              broken.Fail()
              Expect.isTrue (pending.Wait(TimeSpan.FromSeconds 10.)) "The pending request completes"
              Expect.isError pending.Result "With an error"

              match Expect.throwsC (fun () -> run.Wait(TimeSpan.FromSeconds 10.) |> ignore) id with
              | :? AggregateException as error -> Expect.isTrue (error.InnerException :? IOException) "The read failure"
              | other -> failtest $"Unexpected {other}"

          testCase "a refused frame ends the connection with a diagnostic"
          <| fun _ ->
              let toGuest, _, connection, run, _ = guest (TaskCompletionSource<unit>())
              let bytes = Encoding.ASCII.GetBytes "log line\r\n\r\n"
              toGuest.Writer.Write(bytes, 0, bytes.Length)
              toGuest.Writer.Flush()
              Expect.isTrue (run.Wait(TimeSpan.FromSeconds 10.)) "The connection ends"
              Expect.equal run.Result (ConnectionEnd.FramingFailed FramingFailure.NotFramed) "Not framed"

              Expect.contains
                  connection.Diagnostics
                  (ConnectionDiagnostic.FramingFailed FramingFailure.NotFramed)
                  "Protocol diagnostic" ]

/// Test-owned transport controls only. These helpers never manufacture recorder events or guest receipts.
/// The missing internal API is the accepted V3 proposal, not a new public test API.
module internal ConformanceFixture =
    let wait (work: Task<'T>) = work.WaitAsync(TimeSpan.FromSeconds 5.).GetAwaiter().GetResult()
    let finish (work: Task) = work.WaitAsync(TimeSpan.FromSeconds 5.).GetAwaiter().GetResult()
    let signal () = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    /// Cancel and observe owned adapter/reader tasks even when an assertion fails before a barrier is released.
    type Lifetime() =
        let source = new CancellationTokenSource()
        let owned = System.Collections.Concurrent.ConcurrentBag<Task>()
        member _.Token = source.Token
        member _.Cancel() = source.Cancel()
        member _.Track(work: Task) = owned.Add work
        interface IDisposable with
            member _.Dispose() =
                source.Cancel()
                try
                    try finish (Task.WhenAll(owned.ToArray()))
                    with
                    | :? TimeoutException -> failtest "Owned conformance test tasks did not settle during cleanup"
                    | :? OperationCanceledException -> ()
                    | :? IOException -> ()
                finally
                    source.Dispose()
    let utf8 (text: string) = Encoding.UTF8.GetBytes text
    let json (body: byte array) = JsonDocument.Parse(ReadOnlyMemory body)
    let host: PeerInfo = { Kind = PeerKind.Cli; Name = "conformance-test"; Version = "1.0.0" }
    let limits () : ConformanceEvidenceLimits = ConformanceEvidenceLimits.Create(512, 256L * 1024L * 1024L)
    let control (session: HostSession) : ConformanceControl = session.Conformance |> Option.defaultWith (fun () -> failtest "Missing conformance control")
    let events (session: HostSession) : ConformanceEvent list = (control session).Snapshot().Events |> Seq.map _.Event |> List.ofSeq
    let prepared session =
        events session |> List.choose (function ConformanceEvent.OutboundBodyPrepared(_, _, body) -> Some body | _ -> None)
    let inbound session =
        events session |> List.choose (function ConformanceEvent.InboundBodyObserved body -> Some body | _ -> None)
    let localCount session =
        events session |> List.filter (function ConformanceEvent.LocalRefused _ -> true | _ -> false) |> List.length
    let ids (bodies: byte array list) =
        bodies |> List.choose (fun body ->
            use document = json body
            match document.RootElement.TryGetProperty "id" with
            | true, id -> Some(id.GetUInt64())
            | _ -> None)
    let methodOf body =
        use document = json body
        document.RootElement.GetProperty("method").GetString() |> Option.ofObj |> Option.get
    let methods bodies = bodies |> List.map methodOf
    let expectOk result =
        match result with Ok value -> value | Error error -> failtestf "Expected success, got %A" error
    let expectNotSubmitted (outcome: ConformanceProbeOutcome) =
        match outcome with
        | ConformanceProbeOutcome.NotSubmitted _ -> ()
        | other -> failtestf "Expected a local refusal, got %A" other
    let expectSubmitted (outcome: ConformanceProbeOutcome) : ConformanceObservedReply option =
        match outcome with
        | ConformanceProbeOutcome.Submitted(_, reply) -> reply
        | other -> failtestf "Expected submission, got %A" other

    // Independently authored narrow model offer. The tests do not implement the generic kit selector.
    let offerJson = """{"tuples":[{"operation":"morphir.frontend.compileModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"4.0.0"},{"operation":"morphir.backend.generateModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"4.0.0"}],"limits":{"maxEncodedBytes":8192,"maxDecodedBytes":4096,"maxDepth":64,"maxNodes":256},"guarantees":["urn:test:owned"]}"""
    let offer () = JsonSerializer.Deserialize(offerJson, MepJson.Default.ModelTransportSupport) |> nonNull
    let claims = """{"claimsVersion":"0.1.0-draft.2","protocolVersions":["0.2.0-draft.1"],"extension":{"id":"org.example.conformance","name":"Test peer","version":"1.0.0","types":["frontend","backend"]},"capabilities":{"modelTransport":OFF}}""".Replace("OFF", offerJson)
    let response id result = utf8 ($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{result}}}")
    let error id code = utf8 ($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"error\":{{\"code\":{code},\"message\":\"test peer refusal\"}}}}")

    /// A deterministic await barrier. The five-second bounds detect deadlocks, never establish ordering.
    type Barrier() =
        let entered = signal ()
        let released = signal ()
        member _.Entered = entered.Task
        member _.Release() = released.TrySetResult() |> ignore
        member _.Pause(token: CancellationToken) =
            entered.TrySetResult() |> ignore
            released.Task.WaitAsync token
        interface IDisposable with
            member _.Dispose() = released.TrySetResult() |> ignore

    /// Records the complete actual callback bytes, including IDs. Null means only an adapter return.
    type Edge() =
        let calls = System.Collections.Concurrent.ConcurrentQueue<string * byte array>()
        let mutable before: string -> CancellationToken -> Task = fun _ _ -> Task.CompletedTask
        let mutable after: string -> CancellationToken -> Task = fun _ _ -> Task.CompletedTask
        let mutable replace: string -> byte array | null -> byte array | null = fun _ value -> value
        let mutable shutDown = false
        let mutable active = 0
        let mutable overlap = false
        let mutable track: Task -> unit = ignore
        let answer method (body: byte array) =
            use document = json body
            match document.RootElement.TryGetProperty "id" with
            | false, _ -> null
            | true, id ->
                let id = id.GetRawText()
                match method with
                | "morphir.extension.describe" -> response id claims
                | "morphir.initialize" ->
                    let selected =
                        match document.RootElement.TryGetProperty "params" with
                        | true, parameters when parameters.ValueKind = JsonValueKind.Object ->
                            match parameters.TryGetProperty "modelTransport" with
                            | true, offer -> Some(offer.GetRawText())
                            | _ -> None
                        | _ -> None
                    match selected with
                    | Some offer ->
                        response id ("""{"protocolVersion":"0.2.0-draft.1","extension":{"id":"org.example.conformance","name":"Test peer","version":"1.0.0","types":["frontend","backend"]},"capabilities":{"modelTransport":OFF}}""".Replace("OFF", offer))
                    | None ->
                        // A legacy-shaped initialize is valid peer input. Record it and
                        // answer without inventing an offer, so no-I/O assertions can fail.
                        response id """{"protocolVersion":"0.2.0-draft.1","extension":{"id":"org.example.conformance","name":"Test peer","version":"1.0.0","types":["frontend","backend"]},"capabilities":{}}"""
                | "morphir.shutdown" -> shutDown <- true; response id "{}"
                | "morphir.ping" when shutDown -> error id -32014
                | "morphir.ping" -> response id "{\"ok\":true}"
                | _ -> error id -32601
        let invoke method (message: ReadOnlyMemory<byte>) token =
            let work =
                task {
                    if Interlocked.Increment(&active) > 1 then overlap <- true
                    try
                        let body = message.ToArray()
                        calls.Enqueue(method, body)
                        do! before method token
                        let result = answer method body |> replace method
                        do! after method token
                        return result
                    finally
                        Interlocked.Decrement(&active) |> ignore
                }
            track (work :> Task)
            work
        member _.Bodies = calls.ToArray() |> Array.map snd |> List.ofArray
        member _.Overlapped = overlap
        member _.Before with set value = before <- value
        member _.After with set value = after <- value
        member _.Replace with set value = replace <- value
        member _.Track with set value = track <- value
        interface ISingleMessageChannel with
            member _.RequestAsync(method, message, token) = invoke method message token
            member _.NotifyAsync(method, message, token) = invoke method message token

    let create (edge: Edge) (lifetime: Lifetime) : HostSession =
        edge.Track <- lifetime.Track
        let session = HostSession.CreateConformance(edge :> ISingleMessageChannel, host, limits (), lifetime.Token)
        lifetime.Track(session.RunAsync lifetime.Token)
        session
    let describeAndBind (session: HostSession) =
        wait (session.DescribeAsync CancellationToken.None) |> expectOk |> ignore
        let receipt = (control session).DescribeReceipt |> Option.defaultWith (fun () -> failtest "No original describe receipt")
        (control session).BindSelectedOffer(receipt, offer ()) |> expectOk
        receipt
    /// JIT-test inspection of existing copied-body getters only. No receipt is constructed or changed here.
    /// Resolve and validate the shape before opening a transport, so missing metadata is fixture setup failure.
    let receiptBodyReader () =
        let flags = System.Reflection.BindingFlags.Instance ||| System.Reflection.BindingFlags.NonPublic ||| System.Reflection.BindingFlags.DeclaredOnly
        let getter name =
            let property =
                typeof<ConformanceDescribeReceipt>.GetProperty(name, flags)
                |> Option.ofObj
                |> Option.defaultWith (fun () -> invalidOp $"Fixture setup: missing private receipt getter {name}.")
            let read =
                property.GetGetMethod true
                |> Option.ofObj
                |> Option.defaultWith (fun () -> invalidOp $"Fixture setup: missing read method for {name}.")
            if read.IsStatic || read.IsPublic || property.PropertyType <> typeof<byte array>
               || property.GetIndexParameters().Length <> 0 || not (isNull (property.GetSetMethod true)) then
                invalidOp $"Fixture setup: receipt getter {name} is not the expected private read-only byte-array view."
            fun (receipt: ConformanceDescribeReceipt) ->
                match property.GetValue receipt with
                | :? (byte array) as bytes -> bytes
                | _ -> invalidOp $"Fixture setup: receipt getter {name} did not return copied bytes."
        let request, response = getter "RequestBody", getter "ResponseBody"
        fun receipt -> request receipt, response receipt
    let initialize (session: HostSession) =
        describeAndBind session |> ignore
        wait (session.InitializeAsync CancellationToken.None) |> expectOk |> ignore
    let emptyParams body =
        use document = json body
        let root = document.RootElement
        Expect.equal (root.GetProperty("params").GetRawText()) "{}" "Canonical private sender choice is an explicit empty object"
    let noParams body =
        use document = json body
        Expect.isFalse (document.RootElement.TryGetProperty "params" |> fst) "Product optional-member default stays absent"

    /// Blocks the actual first stream write until release or lifetime cancellation.
    type BlockedWrite(inner: Stream, initiallyBlocked: bool) =
        inherit Stream()
        let barrier = new Barrier()
        let mutable armed = initiallyBlocked
        let mutable afterFlush: unit -> unit = ignore
        let mutable afterFlushAsync: CancellationToken -> Task = fun _ -> Task.CompletedTask
        member _.Barrier = barrier
        member _.Arm() = armed <- true
        member _.AfterFlush with set value = afterFlush <- value
        member _.AfterFlushAsync with set value = afterFlushAsync <- value
        override _.CanRead = false
        override _.CanWrite = true
        override _.CanSeek = false
        override _.Length = raise (NotSupportedException())
        override _.Position with get () = raise (NotSupportedException()) and set _ = raise (NotSupportedException())
        override _.Read(_, _, _) = raise (NotSupportedException())
        override _.Seek(_, _) = raise (NotSupportedException())
        override _.SetLength _ = raise (NotSupportedException())
        override _.Write(_, _, _) = raise (NotSupportedException())
        override _.Flush() = inner.Flush()
        override _.FlushAsync token =
            task {
                do! inner.FlushAsync token
                afterFlush ()
                do! afterFlushAsync token
            } :> Task
        override _.WriteAsync(buffer: ReadOnlyMemory<byte>, token) =
            ValueTask(
                task {
                    if armed then do! barrier.Pause token
                    do! inner.WriteAsync(buffer, token)
                }
            )

    /// Signals the next real read after a known byte boundary. The preceding frame has then been parsed by RunAsync.
    type ObservedRead(inner: Stream) =
        inherit Stream()
        let gate = obj ()
        let mutable consumed = 0L
        let mutable boundary: (int64 * TaskCompletionSource<unit>) option = None
        member _.AfterBytes(count: int64) =
            lock gate (fun () ->
                if count <= 0L || boundary.IsSome then invalidOp "Fixture setup: invalid or already armed read boundary."
                let completion = signal ()
                boundary <- Some(count, completion)
                completion.Task)
        override _.CanRead = true
        override _.CanWrite = false
        override _.CanSeek = false
        override _.Length = raise (NotSupportedException())
        override _.Position with get () = raise (NotSupportedException()) and set _ = raise (NotSupportedException())
        override _.Read(_, _, _) = raise (NotSupportedException())
        override _.Seek(_, _) = raise (NotSupportedException())
        override _.SetLength _ = raise (NotSupportedException())
        override _.Write(_, _, _) = raise (NotSupportedException())
        override _.Flush() = raise (NotSupportedException())
        override _.ReadAsync(buffer: Memory<byte>, token) =
            ValueTask<int>(task {
                lock gate (fun () ->
                    match boundary with
                    | Some(count, completion) when consumed >= count ->
                        boundary <- None
                        completion.TrySetResult() |> ignore
                    | _ -> ())
                let! count = inner.ReadAsync(buffer, token)
                lock gate (fun () -> consumed <- consumed + int64 count)
                return count
            })

    let framedBytes (body: byte array) =
        let length = body.Length.ToString(Globalization.CultureInfo.InvariantCulture)
        int64 (Encoding.ASCII.GetByteCount $"Content-Length: {length}\r\n\r\n") + int64 body.Length

    type StreamEdge(?blocked: bool, ?budget: ConformanceEvidenceLimits) =
        let fromPeer = Pipe()
        let toPeer = Pipe()
        let lifetime = new CancellationTokenSource()
        let owned = System.Collections.Concurrent.ConcurrentBag<Task>()
        let input = fromPeer.Reader.AsStream()
        let output = toPeer.Writer.AsStream()
        let peerInput = toPeer.Reader.AsStream()
        let peerOutput = fromPeer.Writer.AsStream()
        let blockedOutput = new BlockedWrite(output, defaultArg blocked false)
        let observedInput = new ObservedRead(input)
        let session = HostSession.CreateConformance(observedInput, blockedOutput, host, defaultArg budget (limits ()), lifetime.Token)
        let run = session.RunAsync lifetime.Token
        member _.Session = session
        member _.Lifetime = lifetime
        member _.Track(work: Task) = owned.Add work
        member _.WriteBarrier = blockedOutput.Barrier
        member _.ArmWrites() = blockedOutput.Arm()
        member _.AfterFlush with set value = blockedOutput.AfterFlush <- value
        member _.AfterFlushAsync with set value = blockedOutput.AfterFlushAsync <- value
        member _.ReadAfterBytes(count) = observedInput.AfterBytes count
        member _.Run = run
        member _.Read() =
            match wait (ContentLengthFraming.ReadAsync(peerInput, lifetime.Token)) with
            | FrameRead.Message body -> body
            | other -> failtestf "Expected an actual frame, got %A" other
        member _.Send(body: byte array) = finish (ContentLengthFraming.WriteAsync(peerOutput, ReadOnlyMemory body, lifetime.Token))
        member _.End() = peerOutput.Dispose()
        interface IDisposable with
            member _.Dispose() =
                lifetime.Cancel()
                try wait run |> ignore with :? OperationCanceledException -> ()
                finish (Task.WhenAll(owned.ToArray()))
                input.Dispose(); output.Dispose(); peerInput.Dispose(); peerOutput.Dispose()
                observedInput.Dispose(); blockedOutput.Dispose(); lifetime.Dispose()

[<Tests>]
let conformanceStreamTests =
    let moduleName = "MEP conformance stream evidence"
    let wait = ConformanceFixture.wait
    testSequenced <| testList moduleName [
        testCase "describe starts at one and retains exact sent and whitespace reply bodies" <| fun _ ->
            use edge = new ConformanceFixture.StreamEdge()
            let pending = edge.Session.DescribeAsync CancellationToken.None
            let sent = edge.Read()
            let reply = ConformanceFixture.utf8 (" { \"jsonrpc\": \"2.0\", \"id\": 1, \"result\": " + ConformanceFixture.claims + " } \n")
            Expect.equal (ConformanceFixture.ids [sent]) [1UL] "Actual wire ID"
            edge.Send reply
            wait pending |> ConformanceFixture.expectOk |> ignore
            Expect.equal (ConformanceFixture.prepared edge.Session) [sent] "Bytes before framing, without reserialization"
            Expect.equal (ConformanceFixture.inbound edge.Session) [reply] "Observed bytes retain whitespace"
            Expect.isTrue (ConformanceFixture.events edge.Session |> List.exists (function ConformanceEvent.FramedWriteCompleted _ -> true | _ -> false)) "Completed framing is separately recorded"

        testCase "original describe receipt retains winning raw body after same-ID duplicates" <| fun _ ->
            let readReceipt = ConformanceFixture.receiptBodyReader ()
            use edge = new ConformanceFixture.StreamEdge()
            use flush = new ConformanceFixture.Barrier()
            edge.AfterFlushAsync <- fun token -> flush.Pause token :> Task
            let pending = edge.Session.DescribeAsync CancellationToken.None
            edge.Track(pending :> Task)
            let sent = edge.Read()
            wait flush.Entered |> ignore
            let winner = ConformanceFixture.utf8 (" \n{ \"jsonrpc\": \"2.0\", \"id\": 1, \"result\": " + ConformanceFixture.claims + " }\r\n")
            let duplicate = ConformanceFixture.response "1" (ConformanceFixture.claims.Replace("4.0.0", "3.0.0"))
            let unsolicited = ConformanceFixture.utf8 ("\t{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":" + ConformanceFixture.claims.Replace("4.0.0", "2.0.0") + "} ")
            let bodies = [winner; duplicate; unsolicited]
            let nextRead = edge.ReadAfterBytes(bodies |> List.sumBy ConformanceFixture.framedBytes)
            for body in bodies do edge.Send body
            wait nextRead |> ignore
            Expect.isFalse pending.IsCompleted "The actual request write continuation is still held, after all three inbound bodies were processed"
            Expect.isNone (ConformanceFixture.control edge.Session).DescribeReceipt "The host continuation has not issued a receipt yet"
            Expect.equal (ConformanceFixture.inbound edge.Session) bodies "Actual winning and same-ID unsolicited bodies are retained in wire order"
            let duplicates = edge.Session.Diagnostics |> Seq.filter (function ConnectionDiagnostic.UnsolicitedResponse(Some(RequestId.Number 1UL)) -> true | _ -> false) |> Seq.length
            Expect.equal duplicates 2 "Only the first matching reply completed the pending registration"
            flush.Release()
            wait pending |> ConformanceFixture.expectOk |> ignore
            let control = ConformanceFixture.control edge.Session
            let receipt = control.DescribeReceipt |> Option.defaultWith (fun () -> failtest "No original receipt after completed describe")
            let requestBytes, responseBytes = readReceipt receipt
            Expect.equal requestBytes sent "Receipt retains the actual original request bytes"
            Expect.equal responseBytes winner "Receipt retains winning raw whitespace and payload, never a later matching-ID snapshot or typed-result serialization"
            let offered = ConformanceFixture.offer ()
            let duplicateOffer = { offered with Tuples = offered.Tuples |> Seq.map (fun tuple -> { tuple with IrVersion = SemanticVersion.Create "3.0.0" }) |> Array.ofSeq }
            Expect.isError (control.BindSelectedOffer(receipt, duplicateOffer)) "The duplicate advertisement did not replace the winning claims"
            control.BindSelectedOffer(receipt, offered) |> ConformanceFixture.expectOk
            let _, retained = readReceipt receipt
            Expect.equal retained winner "Binding keeps the original copied body"
            Expect.equal (ConformanceFixture.ids (ConformanceFixture.prepared edge.Session)) [1UL] "Local offer binding consumed no wire ID"

        testCase "independent conformance sessions each start at one" <| fun _ ->
            for _ in 1..2 do
                use edge = new ConformanceFixture.StreamEdge()
                let pending = edge.Session.DescribeAsync CancellationToken.None
                Expect.equal (ConformanceFixture.ids [edge.Read()]) [1UL] "No global counter"
                edge.Send(ConformanceFixture.response "1" ConformanceFixture.claims)
                wait pending |> ignore

        testCase "ordinary stream session still starts at zero with no control" <| fun _ ->
            use fromPeer = new MemoryStream(ConformanceFixture.utf8 "")
            use output = new MemoryStream()
            let session = HostSession(fromPeer, output, ConformanceFixture.host)
            Expect.isNone session.Conformance "Explicit opt-in only"
            let pending = session.DescribeAsync CancellationToken.None
            output.Position <- 0L
            match wait (ContentLengthFraming.ReadAsync(output, CancellationToken.None)) with
            | FrameRead.Message body -> Expect.equal (ConformanceFixture.ids [body]) [0UL] "Product seed"
            | other -> failtestf "Expected frame, got %A" other
            wait (session.RunAsync CancellationToken.None) |> ignore
            wait pending |> ignore

        testCase "invalid and unsolicited inbound bodies are observed before parsing and correlation" <| fun _ ->
            use edge = new ConformanceFixture.StreamEdge()
            let pending = edge.Session.DescribeAsync CancellationToken.None
            edge.Read() |> ignore
            let invalid = ConformanceFixture.utf8 "{ broken json"
            let unsolicited = ConformanceFixture.response "99" "{}"
            edge.Send invalid
            edge.Read() |> ignore // Actual parse-error response, not the describe response.
            edge.Send unsolicited
            edge.Send(ConformanceFixture.response "1" ConformanceFixture.claims)
            wait pending |> ignore
            Expect.equal (ConformanceFixture.inbound edge.Session) [invalid; unsolicited; ConformanceFixture.response "1" ConformanceFixture.claims] "Original input order"

        testCase "blocked write has prepared bytes but no completed write and lifetime settles it" <| fun _ ->
            use edge = new ConformanceFixture.StreamEdge(blocked = true)
            let pending = edge.Session.DescribeAsync CancellationToken.None
            wait edge.WriteBarrier.Entered |> ignore
            Expect.equal (ConformanceFixture.prepared edge.Session |> List.length) 1 "Prepared at existing serialization boundary"
            Expect.isFalse (ConformanceFixture.events edge.Session |> List.exists (function ConformanceEvent.FramedWriteCompleted _ -> true | _ -> false)) "Blocked write is not wire completion"
            edge.Lifetime.Cancel()
            wait pending |> ignore
            Expect.isFalse ((ConformanceFixture.control edge.Session).Snapshot().IsComplete) "Uncertain submission is incomplete"

        testCase "successful flush followed by capture cancellation is not a failed write" <| fun _ ->
            use edge = new ConformanceFixture.StreamEdge()
            let mutable flushed = false
            edge.AfterFlush <- fun () ->
                flushed <- true
                edge.Lifetime.Cancel()
            let result = wait (edge.Session.DescribeAsync CancellationToken.None)
            Expect.isTrue flushed "The actual stream flush completed before cancellation"
            Expect.isError result "Cancellation still settles the request"
            Expect.equal (ConformanceFixture.prepared edge.Session |> List.length) 1 "Actual preparation remains recorded"
            Expect.isFalse
                (ConformanceFixture.events edge.Session |> List.exists (function ConformanceEvent.FramedWriteFailed _ -> true | _ -> false))
                "Losing a completion observation cannot invent a transport failure"
            Expect.isFalse ((ConformanceFixture.control edge.Session).Snapshot().IsComplete) "Observation loss stays incomplete"

        testCase "reader and owned cancel write progress while request awaits an ignoring peer" <| fun _ ->
            use edge = new ConformanceFixture.StreamEdge()
            use requestCancellation = new CancellationTokenSource()
            let pending = edge.Session.DescribeAsync requestCancellation.Token
            edge.Read() |> ignore
            requestCancellation.Cancel()
            let cancel = edge.Read()
            Expect.equal (ConformanceFixture.methods [cancel]) ["$/cancelRequest"] "Cancel does not wait for operation admission"
            Expect.equal (ConformanceFixture.ids [cancel]) [] "Cancellation notification consumes no request ID"
            edge.Send(ConformanceFixture.response "99" "{}")
            edge.Send(ConformanceFixture.response "1" ConformanceFixture.claims)
            wait pending |> ignore
            Expect.contains (ConformanceFixture.inbound edge.Session) (ConformanceFixture.response "99" "{}") "Independent reader still observes peer bytes"

        testCase "lifetime settles a pending request when the peer ignores the cancel notification" <| fun _ ->
            use edge = new ConformanceFixture.StreamEdge()
            use requestCancellation = new CancellationTokenSource()
            let pending = edge.Session.DescribeAsync requestCancellation.Token
            edge.Read() |> ignore
            requestCancellation.Cancel()
            Expect.equal (ConformanceFixture.methods [edge.Read()]) ["$/cancelRequest"] "Actual cancel write completed without operation permit"
            edge.Lifetime.Cancel()
            Expect.isError (wait pending) "An ignoring peer cannot keep the owned waiter alive"
            Expect.isEmpty (ConformanceFixture.inbound edge.Session) "No local cancellation error is a received peer reply"

        testCase "terminal drain settles the admitted waiter and refuses its queued registration" <| fun _ ->
            use edge = new ConformanceFixture.StreamEdge()
            let admitted = edge.Session.DescribeAsync CancellationToken.None
            let sent = edge.Read()
            let queued = edge.Session.DescribeAsync CancellationToken.None
            edge.End()
            wait edge.Run |> ignore
            Expect.isError (wait admitted) "Active pending request drained"
            Expect.isError (wait queued) "No registration can survive the terminal drain"
            Expect.equal (ConformanceFixture.prepared edge.Session) [sent] "Queued operation never submits after EOF"
            Expect.isEmpty (ConformanceFixture.inbound edge.Session) "Drain errors are local"

        testCase "terminal drain refuses a later request without an abandoned registration" <| fun _ ->
            use edge = new ConformanceFixture.StreamEdge()
            edge.End()
            wait edge.Run |> ignore
            let before = ConformanceFixture.prepared edge.Session
            Expect.isError (wait (edge.Session.DescribeAsync CancellationToken.None)) "Already ended"
            Expect.equal (ConformanceFixture.prepared edge.Session) before "No post-drain submission"
            Expect.isNone (ConformanceFixture.control edge.Session).DescribeReceipt "Local error cannot issue a receipt"
    ]

[<Tests>]
let conformanceCaptureIntegrityTests =
    testSequenced <| testList "MEP conformance capture integrity" [
        testCase "snapshot mutation cannot rewrite retained stream bodies or event order" <| fun _ ->
            use edge = new ConformanceFixture.StreamEdge()
            let pending = edge.Session.DescribeAsync CancellationToken.None
            let sent = edge.Read()
            let reply = ConformanceFixture.response "1" ConformanceFixture.claims
            edge.Send reply
            ConformanceFixture.wait pending |> ignore
            let snapshot = (ConformanceFixture.control edge.Session).Snapshot()
            for body in ConformanceFixture.prepared edge.Session @ ConformanceFixture.inbound edge.Session do Array.fill body 0 body.Length 0uy
            Expect.equal (ConformanceFixture.prepared edge.Session) [sent] "Snapshot owns outbound copies"
            Expect.equal (ConformanceFixture.inbound edge.Session) [reply] "Snapshot owns inbound copies"
            let sequences = snapshot.Events |> Seq.map _.Sequence |> List.ofSeq
            Expect.equal sequences (List.sort sequences |> List.distinct) "Strict monotonic sequence"

        for events, payload in [0, 1L; -1, 1L; 1, 0L; 1, -1L] do
            testCase $"nonpositive capture budget {events}/{payload} is rejected" <| fun _ ->
                Expect.throwsT<ArgumentException> (fun () -> ConformanceEvidenceLimits.Create(events, payload) |> ignore) "Admission refuses invalid bounds"

        testCase "event overflow closes capture instead of silently dropping evidence" <| fun _ ->
            use edge = new ConformanceFixture.StreamEdge(budget = ConformanceEvidenceLimits.Create(1, 1048576L))
            let pending = edge.Session.DescribeAsync CancellationToken.None
            ConformanceFixture.wait pending |> ignore
            let snapshot = (ConformanceFixture.control edge.Session).Snapshot()
            Expect.isFalse snapshot.IsComplete "Overflow is sticky incomplete"
            let before = ConformanceFixture.prepared edge.Session
            Expect.isError (ConformanceFixture.wait (edge.Session.DescribeAsync CancellationToken.None)) "Fails closed"
            Expect.equal (ConformanceFixture.prepared edge.Session) before "No later submission"

        testCase "payload overflow fails before a body over the remaining budget is submitted" <| fun _ ->
            use edge = new ConformanceFixture.StreamEdge(budget = ConformanceEvidenceLimits.Create(512, 1L))
            ConformanceFixture.wait (edge.Session.DescribeAsync CancellationToken.None) |> ignore
            Expect.isFalse ((ConformanceFixture.control edge.Session).Snapshot().IsComplete) "Cannot retain the prepared body"
            Expect.isEmpty (ConformanceFixture.prepared edge.Session) "No truncated body masquerades as complete evidence"
    ]
