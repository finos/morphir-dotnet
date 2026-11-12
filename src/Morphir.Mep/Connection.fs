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

    /// For C# callers: one function for each case. The id of an unsolicited response is null when it was absent.
    member this.Match<'R>
        (
            unsolicitedResponse: Func<RequestId | null, 'R>,
            refusedMessage: Func<MessageRefusal, 'R>,
            framingFailed: Func<FramingFailure, 'R>
        ) : 'R =
        match this with
        | UnsolicitedResponse id -> unsolicitedResponse.Invoke(Option.toObj id)
        | RefusedMessage refusal -> refusedMessage.Invoke refusal
        | FramingFailed failure -> framingFailed.Invoke failure

    /// For C# callers: an unsolicited response with an id, or with no id when it is null.
    static member UnsolicitedResponseWith(id: RequestId | null) = UnsolicitedResponse(Option.ofObj id)

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

    /// For C# callers: one function for each case.
    member this.Match<'R>(endOfStream: Func<'R>, stopped: Func<'R>, framingFailed: Func<FramingFailure, 'R>) : 'R =
        match this with
        | EndOfStream -> endOfStream.Invoke()
        | Stopped -> stopped.Invoke()
        | FramingFailed failure -> framingFailed.Invoke failure

/// The first request ID for ordinary transports or an explicitly opted-in conformance session.
[<RequireQualifiedAccess>]
type internal RequestIdStartPolicy =
    | Product
    | Conformance

    member this.InitialCounter =
        match this with
        | Product -> -1L
        | Conformance -> 0L

/// A JSON-RPC connection on a framed stream pair (design RPC-1): it dispatches inbound requests, matches inbound
/// responses to the pending-request table, and sends and receives $/cancelRequest.
[<Sealed>]
type Connection
    private
    (
        input: Stream,
        output: Stream,
        dispatcher: Dispatcher,
        isDone: Func<bool>,
        idStart: RequestIdStartPolicy,
        capture: ConformanceCapture option
    ) =
    let writeLock = new SemaphoreSlim(1, 1)
    let pending = ConcurrentDictionary<RequestId, TaskCompletionSource<ConformanceCallResult<JsonElement>>>()
    let pendingOperations = ConcurrentDictionary<RequestId, ConformanceOperationId>()
    let inflight = ConcurrentDictionary<RequestId, CancellationTokenSource>()
    let diagnostics = ConcurrentQueue<ConnectionDiagnostic>()
    let mutable nextId = idStart.InitialCounter
    let pendingGate = obj ()
    let mutable closed = false

    let closePending () =
        lock pendingGate (fun () ->
            closed <- true
            for KeyValue(id, waiter) in pending do
                if pending.TryRemove(id) |> fst then
                    pendingOperations.TryRemove id |> ignore
                    waiter.TrySetResult
                        { Result = Error(RpcErrors.withMessage ErrorCode.InternalError "The connection closed.")
                          Exchange = None
                          Submitted = false }
                    |> ignore)

    let lifetimeRegistration = capture |> Option.map (fun owned -> owned.Token.Register(Action closePending))

    let writeCaptured (owned: ConformanceCapture) (admission: ConformanceOperationId option) (message: JsonRpcMessage) =
        task {
            let mutable entered = false
            let mutable prepared = None
            let mutable framingCompleted = false
            try
                try
                    do! writeLock.WaitAsync owned.Token
                    entered <- true
                    let bytes = JsonRpc.Write message
                    if bytes.Length > ContentLengthFraming.MaxBodyBytes then
                        invalidArg (nameof message) (FramingFailure.BodyTooLarge.ToString())
                    let id =
                        match message with
                        | JsonRpcMessage.Request(id, _, _) | JsonRpcMessage.Response(id, _) -> Some id
                        | JsonRpcMessage.ErrorResponse(id, _) -> id
                        | JsonRpcMessage.Notification _ -> None
                    match owned.Prepare(id, bytes, ConformanceSubmission.Framed, admission) with
                    | None -> return raise (OperationCanceledException("Conformance capture cannot retain this write.", owned.Token))
                    | Some operation ->
                        prepared <- Some operation
                        do! ContentLengthFraming.WriteAsync(output, ReadOnlyMemory bytes, owned.Token)
                        framingCompleted <- true
                        if not (owned.WriteCompleted operation) then
                            return raise (OperationCanceledException("Conformance write observation is incomplete.", owned.Token))
                with error ->
                    match prepared with
                    | Some operation when not framingCompleted -> owned.WriteFailed(operation, error)
                    | _ -> owned.Incomplete(ConformanceCaptureFailure.ObservationFailed error)
                    return raise error
            finally
                if entered then writeLock.Release() |> ignore
        }

    let writeWithOperation (admission: ConformanceOperationId option) (message: JsonRpcMessage) =
        task {
            match capture with
            | Some owned -> do! writeCaptured owned admission message
            | None ->
                let bytes = JsonRpc.Write message
                do! writeLock.WaitAsync()
                try
                    do! ContentLengthFraming.WriteAsync(output, ReadOnlyMemory bytes, CancellationToken.None)
                finally
                    writeLock.Release() |> ignore
        }

    let write message = writeWithOperation None message

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

    let complete (id: RequestId option) (result: Result<JsonElement, RpcError>) (body: ConformanceBody option) =
        lock pendingGate (fun () ->
            match id with
            | Some id ->
                match pending.TryRemove id with
                | true, waiter ->
                    let admission = match pendingOperations.TryRemove id with true, key -> Some key | _ -> None
                    // Only this TryRemove winner can associate this body with the waiter it actually completes.
                    let exchange =
                        match capture, admission, body with
                        | Some owned, Some operation, Some response -> owned.CompleteExchange(operation, id, response)
                        | _ -> None
                    if not (waiter.TrySetResult { Result = result; Exchange = exchange; Submitted = exchange.IsSome }) then
                        capture |> Option.iter (fun owned -> owned.Incomplete(ConformanceCaptureFailure.ObservationFailed(InvalidOperationException "The winning response could not complete its waiter.")))
                | _ -> report (ConnectionDiagnostic.UnsolicitedResponse(Some id))
            | None -> report (ConnectionDiagnostic.UnsolicitedResponse None))

    let request (admission: ConformanceOperationId option) (method: string) (parameters: JsonElement option) (cancellation: CancellationToken) =
        match capture with
        | None ->
            let id = RequestId.Number(uint64 (Interlocked.Increment &nextId))
            let waiter = TaskCompletionSource<ConformanceCallResult<JsonElement>>(TaskCreationOptions.RunContinuationsAsynchronously)
            pending[id] <- waiter
            task {
                do! write (JsonRpcMessage.Request(id, method, parameters))
                use _ =
                    cancellation.Register(fun () ->
                        let cancel = JsonSerializer.SerializeToElement({ Id = id }, MepJson.Default.CancelParams)
                        write (JsonRpcMessage.Notification(Method.CancelRequest.WireValue, Some cancel)) |> ignore)
                return! waiter.Task
            }
        | Some owned ->
            task {
                let registered =
                    lock pendingGate (fun () ->
                        if closed || not owned.CanSubmit then None
                        else
                            let id = RequestId.Number(uint64 (Interlocked.Increment &nextId))
                            let waiter = TaskCompletionSource<ConformanceCallResult<JsonElement>>(TaskCreationOptions.RunContinuationsAsynchronously)
                            admission |> Option.iter (fun key -> pendingOperations[id] <- key)
                            pending[id] <- waiter
                            Some(id, waiter))
                match registered with
                | None ->
                    return { Result = Error(RpcErrors.withMessage ErrorCode.InternalError "The conformance connection is closed."); Exchange = None; Submitted = false }
                | Some(id, waiter) ->
                    let cancellationWrites = ConcurrentQueue<Task>()
                    try
                        do! writeWithOperation admission (JsonRpcMessage.Request(id, method, parameters))
                        use registration =
                            cancellation.Register(fun () ->
                                let work =
                                    task {
                                        try
                                            let cancel = JsonSerializer.SerializeToElement({ Id = id }, MepJson.Default.CancelParams)
                                            do! writeWithOperation admission (JsonRpcMessage.Notification(Method.CancelRequest.WireValue, Some cancel))
                                        with error -> owned.Incomplete(ConformanceCaptureFailure.ObservationFailed error)
                                    }
                                cancellationWrites.Enqueue(work :> Task))
                        let! result = waiter.Task
                        registration.Dispose()
                        do! Task.WhenAll(cancellationWrites.ToArray())
                        return result
                    with error ->
                        pending.TryRemove id |> ignore
                        pendingOperations.TryRemove id |> ignore
                        owned.Incomplete(ConformanceCaptureFailure.ObservationFailed error)
                        do! Task.WhenAll(cancellationWrites.ToArray())
                        return { Result = Error(RpcErrors.withMessage ErrorCode.InternalError "The conformance request did not complete."); Exchange = None; Submitted = false }
            }

    /// A connection that also stops when isDone returns true. Ordinary requests start at ID0.
    new(input: Stream, output: Stream, dispatcher: Dispatcher, isDone: Func<bool>) =
        Connection(input, output, dispatcher, isDone, RequestIdStartPolicy.Product, None)

    /// A connection that runs until the stream ends or the framing fails.
    new(input: Stream, output: Stream, dispatcher: Dispatcher) =
        Connection(input, output, dispatcher, Func<bool>(fun () -> false))

    /// Uses the same framed connection with a per-connection conformance counter starting at ID1.
    static member internal CreateConformance
        (input: Stream, output: Stream, dispatcher: Dispatcher, isDone: Func<bool>, limits: ConformanceEvidenceLimits, lifetime: CancellationToken) =
        Connection(input, output, dispatcher, isDone, RequestIdStartPolicy.Conformance, Some(ConformanceCapture.Create(limits, lifetime)))

    member internal _.ConformanceSnapshot = capture |> Option.map (fun owned -> owned.Snapshot())
    member internal _.ConformanceToken = capture |> Option.map _.Token |> Option.defaultValue CancellationToken.None
    member internal _.ConformanceEnd = capture |> Option.bind _.EndReason
    member internal _.AdmitOperation() = capture |> Option.bind (fun owned -> owned.AdmitOperation())
    member internal _.LocalRefused(cause) = capture |> Option.iter (fun owned -> owned.LocalRefused cause)
    member internal _.OperationCancelled(operation) = capture |> Option.iter (fun owned -> owned.OperationCancelled operation)

    member internal _.RequestObservedAsync(operation, method, parameters, cancellation) =
        request (Some operation) method parameters cancellation

    member internal _.NotifyObservedAsync(operation, method, parameters) =
        task {
            try
                do! writeWithOperation (Some operation) (JsonRpcMessage.Notification(method, parameters))
                return { Result = Ok(); Exchange = None; Submitted = true }
            with error ->
                capture |> Option.iter (fun owned -> owned.Incomplete(ConformanceCaptureFailure.ObservationFailed error))
                return { Result = Error(RpcErrors.withMessage ErrorCode.InternalError "The conformance notification did not complete."); Exchange = None; Submitted = false }
        }

    /// The protocol diagnostics found so far.
    member _.Diagnostics: IReadOnlyList<ConnectionDiagnostic> = diagnostics.ToArray()

    /// Reads frames until the stream ends, the session ends or a frame is refused. Inbound requests run
    /// concurrently, so that a cancel can reach them; a handler that changes the session completes synchronously.
    /// At the end of the stream or of the session, running handlers finish and their replies are written, as the
    /// upstream guests answer every request before they exit. After a refused frame, a read failure or a
    /// cancellation, the connection cancels its running handlers. Cancelling the token also stops a connection that
    /// waits for its handlers. On every exit, each pending outbound request is settled with an error, and a
    /// cancellation or read failure then reaches the caller.
    member _.RunAsync(cancellation: CancellationToken) : Task<ConnectionEnd> =
        task {
            use lifetime =
                match capture with
                | None -> CancellationTokenSource.CreateLinkedTokenSource cancellation
                | Some owned -> CancellationTokenSource.CreateLinkedTokenSource(cancellation, owned.Token)
            let readCancellation = match capture with None -> cancellation | Some _ -> lifetime.Token
            let running = ResizeArray<Task>()
            let mutable finished = None
            let mutable failure: ExceptionDispatchInfo option = None

            try
                while finished.IsNone do
                    let! frame = ContentLengthFraming.ReadAsync(input, readCancellation)

                    match frame with
                    | FrameRead.EndOfStream -> finished <- Some ConnectionEnd.EndOfStream
                    | FrameRead.Refused failure ->
                        report (ConnectionDiagnostic.FramingFailed failure)
                        finished <- Some(ConnectionEnd.FramingFailed failure)
                    | FrameRead.Message body ->
                        let observed =
                            match capture with
                            | Some owned ->
                                match owned.ObserveInbound body with
                                | Some original -> Some original
                                | None -> raise (OperationCanceledException("Conformance inbound observation is incomplete.", owned.Token))
                            | None -> None
                        match JsonRpc.Read(ReadOnlyMemory body) with
                        | Error refusal ->
                            report (ConnectionDiagnostic.RefusedMessage refusal)

                            if refusal.Answer then
                                do! write (JsonRpc.Reply refusal)
                        | Ok(JsonRpcMessage.Response(id, result)) -> complete (Some id) (Ok result) observed
                        | Ok(JsonRpcMessage.ErrorResponse(id, error)) -> complete id (Error error) observed
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
                capture |> Option.iter (fun owned -> owned.Incomplete(ConformanceCaptureFailure.ObservationFailed error))

            // A refused frame, a read failure or a cancellation stops the running handlers. After a normal end they
            // finish; the token is linked to the caller's token, so the caller can still stop them.
            match failure, finished with
            | None, Some(ConnectionEnd.EndOfStream | ConnectionEnd.Stopped) -> ()
            | _ -> lifetime.Cancel()

            try
                do! Task.WhenAll running
            with _ ->
                ()

            match capture with
            | Some owned ->
                match finished with
                | Some ConnectionEnd.EndOfStream -> owned.End ConformanceTransportEnd.EndOfStream
                | Some ConnectionEnd.Stopped -> owned.End ConformanceTransportEnd.Stopped
                | Some(ConnectionEnd.FramingFailed cause) ->
                    owned.Incomplete(ConformanceCaptureFailure.FramingFailed cause)
                    owned.End(ConformanceTransportEnd.FramingFailed cause)
                | None -> ()
                closePending ()
                lifetimeRegistration |> Option.iter _.Dispose()
            | None ->
                for KeyValue(id, waiter) in pending do
                    if pending.TryRemove(id) |> fst then
                        waiter.TrySetResult { Result = Error(RpcErrors.withMessage ErrorCode.InternalError "The connection closed."); Exchange = None; Submitted = false }
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
        task { let! result = request None method parameters cancellation
               return result.Result }

    /// Sends a notification.
    member _.NotifyAsync(method: string, parameters: JsonElement option) : Task =
        write (JsonRpcMessage.Notification(method, parameters))

    /// For C# callers: sends a request with params or none, and waits for its reply.
    member this.RequestAsync(method: string, parameters: Nullable<JsonElement>, cancellation: CancellationToken) =
        this.RequestAsync(method, Option.ofNullable parameters, cancellation)

    /// For C# callers: sends a notification with params or none.
    member this.NotifyAsync(method: string, parameters: Nullable<JsonElement>) : Task =
        this.NotifyAsync(method, Option.ofNullable parameters)
