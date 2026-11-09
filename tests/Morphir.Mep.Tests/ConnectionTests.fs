// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Mep.Tests.ConnectionTests

open System
open System.IO
open System.IO.Pipelines
open System.Text
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
                  "test.slow",
                  Handlers.raw (fun _ token ->
                      task {
                          use _ = token.Register(fun () -> cancelled.TrySetResult() |> ignore)
                          do! Task.Delay(Timeout.Infinite, token)
                          return Ok(JsonRpc.Constant "{}")
                      }) ]
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
                  Dispatcher(
                      readOnlyDict
                          [ "test.slow",
                            Handlers.raw (fun _ token ->
                                task {
                                    use _ = token.Register(fun () -> cancelled.TrySetResult() |> ignore)
                                    do! Task.Delay(Timeout.Infinite, token)
                                    return Ok(JsonRpc.Constant "{}")
                                }) ]
                  )

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
