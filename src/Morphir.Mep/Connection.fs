// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Mep

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Runtime.ExceptionServices
open System.Text.Json
open System.Threading
open System.Threading.Tasks

/// A protocol problem that a connection found. The connection keeps running unless the framing failed.
[<RequireQualifiedAccess>]
type ConnectionDiagnostic =
    /// A response whose id matches no pending request.
    | UnsolicitedResponse of Id: RequestId option
    /// A message that the reader refused.
    | RefusedMessage of Refusal: MessageRefusal
    /// A frame that the reader refused. The connection ends.
    | FramingFailed of Failure: FramingFailure

    /// Stable text for diagnostics.
    override this.ToString() =
        match this with
        | UnsolicitedResponse(Some id) -> $"A response with the id {id} answers no request."
        | UnsolicitedResponse None -> "An error response with no id answers no request."
        | RefusedMessage refusal -> $"A message was refused: {refusal}"
        | FramingFailed failure -> $"The stream framing failed: {failure}"

/// Why a connection stopped reading.
[<RequireQualifiedAccess>]
type ConnectionEnd =
    /// The peer closed the stream between frames.
    | EndOfStream
    /// The session ended (morphir.exit).
    | Stopped
    /// A frame was refused; the reader cannot find the next frame boundary.
    | FramingFailed of Failure: FramingFailure

    /// Stable text for diagnostics.
    override this.ToString() =
        match this with
        | EndOfStream -> "End of stream"
        | Stopped -> "Stopped"
        | FramingFailed failure -> $"Framing failed: {failure}"

/// A JSON-RPC connection on a framed stream pair (design RPC-1): it dispatches inbound requests, matches inbound
/// responses to the pending-request table, and sends and receives $/cancelRequest.
[<Sealed>]
type Connection(input: Stream, output: Stream, dispatcher: Dispatcher, isDone: Func<bool>) =
    let writeLock = new SemaphoreSlim(1, 1)
    let pending = ConcurrentDictionary<RequestId, TaskCompletionSource<Result<JsonElement, RpcError>>>()
    let inflight = ConcurrentDictionary<RequestId, CancellationTokenSource>()
    let diagnostics = ConcurrentQueue<ConnectionDiagnostic>()
    let mutable nextId = -1L

    let write (message: JsonRpcMessage) =
        task {
            let bytes = JsonRpc.Write message
            do! writeLock.WaitAsync()

            try
                do! ContentLengthFraming.WriteAsync(output, ReadOnlyMemory bytes, CancellationToken.None)
            finally
                writeLock.Release() |> ignore
        }

    let report diagnostic = diagnostics.Enqueue diagnostic

    let cancelInflight (parameters: JsonElement option) =
        let id =
            parameters
            |> Option.bind (fun element ->
                try
                    match JsonSerializer.Deserialize(element, MepJson.Default.CancelParams :> Serialization.Metadata.JsonTypeInfo) with
                    | :? CancelParams as value -> Some value.Id
                    | _ -> None
                with :? JsonException ->
                    None)

        match id |> Option.bind (fun id -> match inflight.TryGetValue id with | true, source -> Some source | _ -> None) with
        | Some source ->
            try
                source.Cancel()
            with :? ObjectDisposedException ->
                ()
        | None -> ()

    let runRequest (id: RequestId) (message: JsonRpcMessage) (cancellation: CancellationToken) =
        let source = CancellationTokenSource.CreateLinkedTokenSource cancellation

        if not (inflight.TryAdd(id, source)) then
            // A duplicate id while the first request runs: answer it, and keep the first registration.
            source.Dispose()

            write (JsonRpcMessage.ErrorResponse(Some id, RpcErrors.withMessage ErrorCode.InvalidRequest "The request id is already in use."))
            :> Task
        else
            let handled = dispatcher.DispatchMessageAsync(message, source.Token)

            task {
                let! reply =
                    task {
                        try
                            return! handled
                        with _ ->
                            return Some(JsonRpcMessage.ErrorResponse(Some id, RpcErrors.create ErrorCode.InternalError))
                    }

                let reply =
                    if source.IsCancellationRequested then
                        Some(JsonRpcMessage.ErrorResponse(Some id, RpcErrors.create ErrorCode.RequestCancelled))
                    else
                        reply

                inflight.TryRemove id |> ignore
                source.Dispose()

                match reply with
                | Some reply -> do! write reply
                | None -> ()
            }
            :> Task

    let complete (id: RequestId option) (result: Result<JsonElement, RpcError>) =
        match id |> Option.bind (fun id -> match pending.TryRemove id with | true, waiter -> Some waiter | _ -> None) with
        | Some waiter -> waiter.TrySetResult result |> ignore
        | None -> report (ConnectionDiagnostic.UnsolicitedResponse id)

    /// A connection that runs until the stream ends or the framing fails.
    new(input: Stream, output: Stream, dispatcher: Dispatcher) =
        Connection(input, output, dispatcher, Func<bool>(fun () -> false))

    /// The protocol diagnostics found so far.
    member _.Diagnostics: IReadOnlyList<ConnectionDiagnostic> = diagnostics.ToArray()

    /// Reads frames until the stream ends, the session ends or a frame is refused. Inbound requests run
    /// concurrently, so that a cancel can reach them; a handler that changes the session completes synchronously.
    /// On every exit, also a cancellation or a read failure, the connection cancels its running handlers, waits for
    /// them, and settles each pending outbound request with an error; a cancellation or read failure then reaches
    /// the caller.
    member _.RunAsync(cancellation: CancellationToken) : Task<ConnectionEnd> =
        task {
            use lifetime = CancellationTokenSource.CreateLinkedTokenSource cancellation
            let running = ResizeArray<Task>()
            let mutable finished = None
            let mutable failure: ExceptionDispatchInfo option = None

            try
                while finished.IsNone do
                    let! frame = ContentLengthFraming.ReadAsync(input, cancellation)

                    match frame with
                    | FrameRead.EndOfStream -> finished <- Some ConnectionEnd.EndOfStream
                    | FrameRead.Refused failure ->
                        report (ConnectionDiagnostic.FramingFailed failure)
                        finished <- Some(ConnectionEnd.FramingFailed failure)
                    | FrameRead.Message body ->
                        match JsonRpc.Read(ReadOnlyMemory body) with
                        | Error refusal ->
                            report (ConnectionDiagnostic.RefusedMessage refusal)

                            if refusal.Answer then
                                do! write (JsonRpc.Reply refusal)
                        | Ok(JsonRpcMessage.Response(id, result)) -> complete (Some id) (Ok result)
                        | Ok(JsonRpcMessage.ErrorResponse(id, error)) -> complete id (Error error)
                        | Ok(JsonRpcMessage.Notification(method, parameters)) when method = Method.CancelRequest.WireValue ->
                            if dispatcher.Admits(method, MethodKind.Notification) then
                                cancelInflight parameters
                        | Ok(JsonRpcMessage.Notification _ as message) ->
                            let! _ = dispatcher.DispatchMessageAsync(message, lifetime.Token)
                            ()
                        | Ok(JsonRpcMessage.Request(id, _, _) as message) ->
                            running.Add(runRequest id message lifetime.Token)

                        if finished.IsNone && isDone.Invoke() then
                            finished <- Some ConnectionEnd.Stopped
            with error ->
                failure <- Some(ExceptionDispatchInfo.Capture error)

            // No more replies can arrive: stop the running handlers before waiting for them.
            lifetime.Cancel()

            try
                do! Task.WhenAll running
            with _ ->
                ()

            for KeyValue(id, waiter) in pending do
                if pending.TryRemove(id) |> fst then
                    waiter.TrySetResult(Error(RpcErrors.withMessage ErrorCode.InternalError "The connection closed."))
                    |> ignore

            match failure, finished with
            | Some error, _ ->
                error.Throw()
                return Unchecked.defaultof<_>
            | None, Some ended -> return ended
            | None, None -> return ConnectionEnd.Stopped
        }

    /// Sends a request and waits for its reply. Cancelling the token sends $/cancelRequest; the reply is then the
    /// peer's answer, normally -32800.
    member _.RequestAsync(method: string, parameters: JsonElement option, cancellation: CancellationToken) =
        let id = RequestId.Number(uint64 (Interlocked.Increment &nextId))
        let waiter = TaskCompletionSource<Result<JsonElement, RpcError>>(TaskCreationOptions.RunContinuationsAsynchronously)
        pending[id] <- waiter

        task {
            do! write (JsonRpcMessage.Request(id, method, parameters))

            use _ =
                cancellation.Register(fun () ->
                    let cancel = JsonSerializer.SerializeToElement({ Id = id }, MepJson.Default.CancelParams)
                    write (JsonRpcMessage.Notification(Method.CancelRequest.WireValue, Some cancel)) |> ignore)

            return! waiter.Task
        }

    /// Sends a notification.
    member _.NotifyAsync(method: string, parameters: JsonElement option) : Task =
        write (JsonRpcMessage.Notification(method, parameters))
