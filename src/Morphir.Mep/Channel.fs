// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Mep

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks

/// A guest that takes one whole JSON-RPC message per call and gives at most one message back, such as the handle
/// export of a WASM guest (design CHAN-1). A runtime implements it, and HostSession is its only caller. The
/// implementation passes bytes: the host session checks what comes back, so the implementation decides only whether
/// the guest's runtime delivers a notification at all.
[<Interface>]
type ISingleMessageChannel =
    /// Passes one request to the guest in one call and returns what the guest returned: the bytes of one message, or
    /// null when it returned none. method is the method of the message; message is one complete UTF-8 JSON-RPC
    /// message with no Content-Length header. When the token is cancelled, the implementation stops the call and the
    /// task ends with an OperationCanceledException. The host never sends $/cancelRequest on this channel.
    abstract RequestAsync:
        method: string * message: ReadOnlyMemory<byte> * cancellation: CancellationToken -> Task<byte array | null>

    /// Passes one notification to the guest in one call, or withholds it when the guest's runtime does not deliver
    /// notifications to this guest; a withheld notification does not call the guest. Returns the bytes the guest
    /// returned, or null when it returned none or the notification was withheld. The host fails the session when
    /// bytes come back.
    abstract NotifyAsync:
        method: string * message: ReadOnlyMemory<byte> * cancellation: CancellationToken -> Task<byte array | null>

/// Why a host session on a single-message channel ended before morphir.exit (design CHAN-1). After it, the session
/// passes no message to the guest and refuses every request.
[<RequireQualifiedAccess>]
type SessionFailure =
    /// The guest returned no message for a request.
    | NoReply of Method: string
    /// The guest returned a response whose id is not the id of the request. Actual is absent for an error response
    /// with no id.
    | ReplyForAnotherRequest of Method: string * Expected: RequestId * Actual: RequestId option
    /// The guest returned a request or a notification, such as morphir.progress, instead of the response.
    | NotAResponse of Method: string * Returned: string
    /// The guest returned bytes that are not one JSON-RPC 2.0 message.
    | InvalidReply of Method: string * Refusal: MessageRefusal
    /// The guest returned a message over the MEP message limit, ContentLengthFraming.MaxBodyBytes.
    | ReplyTooLarge of Method: string * Bytes: int
    /// The guest returned bytes for a notification.
    | AnsweredNotification of Method: string
    /// The caller cancelled the call while the guest was inside it.
    | Cancelled of Method: string
    /// The channel failed the call with an exception, for example a trap in the guest's runtime.
    | ChannelFailed of Method: string * Error: exn

    /// The method of the message during which the session ended.
    member this.Method =
        match this with
        | NoReply method
        | ReplyForAnotherRequest(method, _, _)
        | NotAResponse(method, _)
        | InvalidReply(method, _)
        | ReplyTooLarge(method, _)
        | AnsweredNotification method
        | Cancelled method
        | ChannelFailed(method, _) -> method

    /// Stable text for diagnostics.
    override this.ToString() =
        match this with
        | NoReply method -> $"The extension returned no reply to {method}."
        | ReplyForAnotherRequest(method, expected, Some actual) ->
            $"The extension answered {method} (id {expected}) with a response for the id {actual}."
        | ReplyForAnotherRequest(method, expected, None) ->
            $"The extension answered {method} (id {expected}) with an error response that has no id."
        | NotAResponse(method, returned) ->
            $"The extension answered {method} with the message {returned}, which is not a response."
        | InvalidReply(method, refusal) ->
            $"The extension answered {method} with bytes that are not a JSON-RPC message: {refusal}"
        | ReplyTooLarge(method, bytes) ->
            $"The extension's reply to {method} has {bytes} bytes, over the limit of {ContentLengthFraming.MaxBodyBytes}."
        | AnsweredNotification method -> $"The extension returned a message for the notification {method}."
        | Cancelled method -> $"The call {method} was cancelled while the extension handled it."
        | ChannelFailed(method, error) -> $"The channel failed during {method}: {error.Message}"

    /// For C# callers: one function for each case. The actual id of a reply for another request is null when it was
    /// absent.
    member this.Match<'R>
        (
            noReply: Func<string, 'R>,
            replyForAnotherRequest: Func<string, RequestId, RequestId | null, 'R>,
            notAResponse: Func<string, string, 'R>,
            invalidReply: Func<string, MessageRefusal, 'R>,
            replyTooLarge: Func<string, int, 'R>,
            answeredNotification: Func<string, 'R>,
            cancelled: Func<string, 'R>,
            channelFailed: Func<string, exn, 'R>
        ) : 'R =
        match this with
        | NoReply method -> noReply.Invoke method
        | ReplyForAnotherRequest(method, expected, actual) ->
            replyForAnotherRequest.Invoke(method, expected, Option.toObj actual)
        | NotAResponse(method, returned) -> notAResponse.Invoke(method, returned)
        | InvalidReply(method, refusal) -> invalidReply.Invoke(method, refusal)
        | ReplyTooLarge(method, bytes) -> replyTooLarge.Invoke(method, bytes)
        | AnsweredNotification method -> answeredNotification.Invoke method
        | Cancelled method -> cancelled.Invoke method
        | ChannelFailed(method, error) -> channelFailed.Invoke(method, error)

/// How a host session reaches its guest (design CHAN-1): send a request and get its reply, and send a notification.
/// HostSession applies the lifecycle rules before it uses the channel, so both channels refuse the same requests.
type internal IHostChannel =
    /// Read-only projection of this transport's own recorder; absent on ordinary channels.
    abstract ConformanceSnapshot: ConformanceSnapshot option
    abstract ConformanceToken: CancellationToken
    abstract ConformanceEnd: ConformanceTransportEnd option
    abstract AdmitOperation: unit -> ConformanceOperationId option
    abstract LocalRefused: RpcError -> unit
    abstract OperationCancelled: ConformanceOperationId -> unit
    abstract OperationFacts: ConformanceOperationId -> (RequestId option * ConformanceTransportFact list) option
    abstract RequestObservedAsync:
        operation: ConformanceOperationId * method: string * parameters: JsonElement option * cancellation: CancellationToken -> Task<ConformanceCallResult<JsonElement>>
    abstract NotifyObservedAsync:
        operation: ConformanceOperationId * method: string * parameters: JsonElement option * cancellation: CancellationToken -> Task<ConformanceCallResult<unit>>
    /// Runs the channel until it ends.
    abstract RunAsync: cancellation: CancellationToken -> Task<ConnectionEnd>
    /// The protocol diagnostics found so far.
    abstract Diagnostics: IReadOnlyList<ConnectionDiagnostic>
    /// Why the session ended before exit, when it did.
    abstract Failure: SessionFailure option

    /// Sends a request and waits for its reply.
    abstract RequestAsync:
        method: string * parameters: JsonElement option * cancellation: CancellationToken ->
            Task<Result<JsonElement, RpcError>>

    /// Sends a notification. An error means the session failed.
    abstract NotifyAsync:
        method: string * parameters: JsonElement option * cancellation: CancellationToken -> Task<Result<unit, RpcError>>

/// The first channel: a connection on a framed stream pair (design RPC-1). Requests run concurrently, a cancelled
/// request sends $/cancelRequest, and the guest can send its own messages, such as morphir.progress.
[<Sealed>]
type internal StreamChannel(connection: Connection) =
    interface IHostChannel with
        member _.ConformanceSnapshot = connection.ConformanceSnapshot
        member _.ConformanceToken = connection.ConformanceToken
        member _.ConformanceEnd = connection.ConformanceEnd
        member _.AdmitOperation() = connection.AdmitOperation()
        member _.LocalRefused cause = connection.LocalRefused cause
        member _.OperationCancelled operation = connection.OperationCancelled operation
        member _.OperationFacts operation = connection.OperationFacts operation
        member _.RequestObservedAsync(operation, method, parameters, cancellation) = connection.RequestObservedAsync(operation, method, parameters, cancellation)
        member _.NotifyObservedAsync(operation, method, parameters, _) = connection.NotifyObservedAsync(operation, method, parameters)
        member _.RunAsync cancellation = connection.RunAsync cancellation
        member _.Diagnostics = connection.Diagnostics
        member _.Failure = None

        member _.RequestAsync(method, parameters, cancellation) =
            connection.RequestAsync(method, parameters, cancellation)

        member _.NotifyAsync(method, parameters, _) =
            task {
                do! connection.NotifyAsync(method, parameters)
                return Ok()
            }

/// The second channel: a guest that takes one message per call (design CHAN-1). It passes each message as one
/// complete JSON-RPC message, one call at a time, and accepts at most one message back: for a request, the response
/// with the request's id; for a notification, nothing. Anything else, and a cancelled call, ends the session, and
/// after that no message reaches the guest.
[<Sealed>]
type internal SingleMessageChannel private
    (guest: ISingleMessageChannel, idStart: RequestIdStartPolicy, capture: ConformanceCapture option) =
    let calls = new SemaphoreSlim(1, 1)
    let ended = TaskCompletionSource<ConnectionEnd>(TaskCreationOptions.RunContinuationsAsynchronously)
    let gate = obj ()
    let mutable failure: SessionFailure option = None
    let mutable nextId = idStart.InitialCounter
    let lifetimeRegistration =
        capture |> Option.map (fun owned -> owned.Token.Register(fun () -> ended.TrySetResult ConnectionEnd.Stopped |> ignore))

    let fail (reason: SessionFailure) =
        lock gate (fun () ->
            if failure.IsNone then
                failure <- Some reason)

        ended.TrySetResult ConnectionEnd.Stopped |> ignore
        capture |> Option.iter (fun owned -> owned.End ConformanceTransportEnd.Stopped)
        lifetimeRegistration |> Option.iter _.Dispose()

    /// The error of the request or notification that ended the session.
    let failedWith (reason: SessionFailure) =
        match reason with
        | SessionFailure.Cancelled _ -> RpcErrors.create ErrorCode.RequestCancelled
        | reason -> RpcErrors.withMessage ErrorCode.InternalError $"The session failed: {reason}"

    /// The error of a message after the session ended. The guest is not called.
    let endedWith (reason: SessionFailure) =
        RpcErrors.withMessage ErrorCode.InternalError $"The session has ended: {reason}"

    /// The bytes of one message, within the limit that a frame body has on a stream.
    let encode (message: JsonRpcMessage) =
        let bytes = JsonRpc.Write message

        if bytes.Length > ContentLengthFraming.MaxBodyBytes then
            invalidArg (nameof message) (FramingFailure.BodyTooLarge.ToString())

        bytes

    /// Waits until no other call is inside the guest. False when the caller cancelled while waiting; the guest was
    /// not called, so the session continues.
    let enter (cancellation: CancellationToken) =
        task {
            try
                do! calls.WaitAsync cancellation
                return true
            with :? OperationCanceledException ->
                return false
        }

    /// Makes one call. A cancelled token ends the call as cancelled, whatever the guest returned.
    let invoke
        (method: string)
        (operation: ConformanceFrameId option)
        (cancellation: CancellationToken)
        (transportCancellation: CancellationToken)
        (call: CancellationToken -> Task<byte array | null>) =
        let cancelled () =
            cancellation.IsCancellationRequested || (capture |> Option.exists _.LifetimeIsCancelled)
        task {
            try
                let! reply = call transportCancellation
                match capture, operation with Some owned, Some key -> owned.AdapterCompleted key | _ -> ()
                let observed =
                    match capture, operation with
                    | Some owned, Some key ->
                        match reply with
                        | null ->
                            match owned.AdapterReturned(key, null) with
                            | Some observed -> Ok(observed |> Option.map _.Memory, observed)
                            | None -> Error(SessionFailure.ChannelFailed(method, InvalidOperationException "Adapter observation is incomplete."))
                        | bytes when bytes.Length > ContentLengthFraming.MaxBodyBytes ->
                            // Retain the original channel-size failure below, without copying an invalid body.
                            owned.Incomplete(ConformanceCaptureFailure.BodyTooLarge bytes.Length)
                            Error(SessionFailure.ReplyTooLarge(method, bytes.Length))
                        | bytes ->
                            match owned.AdapterReturned(key, bytes) with
                            | Some observed -> Ok(observed |> Option.map _.Memory, observed)
                            | None -> Error(SessionFailure.ChannelFailed(method, InvalidOperationException "Adapter observation is incomplete."))
                    | _ ->
                        match reply with null -> Ok(None, None) | bytes -> Ok(Some(ReadOnlyMemory<byte> bytes), None)
                if cancelled () then
                    capture |> Option.iter (fun owned -> owned.Incomplete(ConformanceCaptureFailure.ObservationFailed(OperationCanceledException cancellation)))
                    return Error(SessionFailure.Cancelled method)
                else
                    return observed
            with
            | error when cancelled () ->
                capture |> Option.iter (fun owned -> owned.Incomplete(ConformanceCaptureFailure.ObservationFailed error))
                return Error(SessionFailure.Cancelled method)
            | error ->
                capture |> Option.iter (fun owned -> owned.Incomplete(ConformanceCaptureFailure.ObservationFailed error))
                return Error(SessionFailure.ChannelFailed(method, error))
        }

    /// The reply to a request: the guest's response or error response for the same id.
    let reply (method: string) (id: RequestId) (bytes: ReadOnlyMemory<byte> option) =
        match bytes with
        | None -> Error(SessionFailure.NoReply method)
        | Some bytes when bytes.Length = 0 -> Error(SessionFailure.NoReply method)
        | Some bytes when bytes.Length > ContentLengthFraming.MaxBodyBytes ->
            Error(SessionFailure.ReplyTooLarge(method, bytes.Length))
        | Some bytes ->
            match JsonRpc.Read bytes with
            | Error refusal -> Error(SessionFailure.InvalidReply(method, refusal))
            | Ok(JsonRpcMessage.Response(replyId, result)) when replyId = id -> Ok(Ok result)
            | Ok(JsonRpcMessage.ErrorResponse(Some replyId, error)) when replyId = id -> Ok(Error error)
            | Ok(JsonRpcMessage.Response(other, _)) -> Error(SessionFailure.ReplyForAnotherRequest(method, id, Some other))
            | Ok(JsonRpcMessage.ErrorResponse(other, _)) -> Error(SessionFailure.ReplyForAnotherRequest(method, id, other))
            | Ok(JsonRpcMessage.Request(_, returned, _))
            | Ok(JsonRpcMessage.Notification(returned, _)) -> Error(SessionFailure.NotAResponse(method, returned))

    /// Runs one call while no other call is inside the guest, and ends the session when check refuses the result.
    let exchange
        (admission: ConformanceOperationId option)
        (method: string)
        (id: RequestId option)
        (message: byte array)
        (cancellation: CancellationToken)
        (call: CancellationToken -> Task<byte array | null>)
        (check: ReadOnlyMemory<byte> option -> Result<Result<'T, RpcError>, SessionFailure>)
        : Task<ConformanceCallResult<'T>> =
        task {
            use bound =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellation,
                    match capture with Some owned -> owned.Token | None -> CancellationToken.None
                )
            let effectiveCancellation = match capture with Some _ -> bound.Token | None -> cancellation
            match failure with
            | Some reason -> return { Result = Error(endedWith reason); Exchange = None; Submitted = false }
            | None ->
                let! entered = enter effectiveCancellation

                if not entered then
                    return { Result = Error(RpcErrors.create ErrorCode.RequestCancelled); Exchange = None; Submitted = false }
                else
                    try
                        match failure with
                        | Some reason -> return { Result = Error(endedWith reason); Exchange = None; Submitted = false }
                        | None ->
                            let prepared =
                                match capture with
                                | None -> Some None
                                | Some owned ->
                                    owned.Prepare(id, message, ConformanceSubmission.Adapter, admission)
                                    |> Option.bind (fun key -> if owned.AdapterStarted key then Some(Some key) else None)
                            match prepared with
                            | None -> return { Result = Error(RpcErrors.withMessage ErrorCode.InternalError "Conformance capture cannot submit this adapter call."); Exchange = None; Submitted = false }
                            | Some operation ->
                                let! outcome = invoke method operation cancellation effectiveCancellation call
                                let validatedOutcome = outcome |> Result.bind (fun (bytes, original) -> check bytes |> Result.map (fun result -> result, original))
                                match validatedOutcome with
                                | Ok(result, original) ->
                                    match capture, operation with Some owned, Some key -> owned.AdapterValidated key | _ -> ()
                                    let observed =
                                        match capture, admission, id, original with
                                        | Some owned, Some owner, Some requestId, Some body -> owned.CompleteExchange(owner, requestId, body)
                                        | _ -> None
                                    return { Result = result; Exchange = observed; Submitted = true }
                                | Error reason ->
                                    fail reason
                                    return { Result = Error(failedWith reason); Exchange = None; Submitted = false }
                    finally
                        calls.Release() |> ignore
        }

    let request admission method parameters cancellation =
        let id = RequestId.Number(uint64 (Interlocked.Increment &nextId))
        match capture, admission with Some owned, Some key -> owned.RequestAllocated(key, id) | _ -> ()
        task {
            let message = encode (JsonRpcMessage.Request(id, method, parameters))
            return! exchange admission method (Some id) message cancellation
                        (fun token -> guest.RequestAsync(method, ReadOnlyMemory message, token)) (reply method id)
        }

    let notify admission method parameters cancellation =
        task {
            let message = encode (JsonRpcMessage.Notification(method, parameters))
            let! result =
                exchange admission method None message cancellation
                    (fun token -> guest.NotifyAsync(method, ReadOnlyMemory message, token))
                    (fun bytes ->
                        match bytes with
                        | None -> Ok(Ok())
                        | Some bytes when bytes.Length = 0 -> Ok(Ok())
                        | _ -> Error(SessionFailure.AnsweredNotification method))
            if result.Result.IsOk && method = Method.Exit.WireValue then
                ended.TrySetResult ConnectionEnd.Stopped |> ignore
                capture |> Option.iter (fun owned -> owned.End ConformanceTransportEnd.Stopped)
                lifetimeRegistration |> Option.iter _.Dispose()
            return result
        }

    /// Ordinary single-message requests start at ID0.
    new(guest: ISingleMessageChannel) = SingleMessageChannel(guest, RequestIdStartPolicy.Product, None)

    /// Uses the caller's same adapter with a per-channel conformance counter starting at ID1.
    static member internal CreateConformance(guest: ISingleMessageChannel, limits: ConformanceEvidenceLimits, lifetime: CancellationToken) =
        SingleMessageChannel(guest, RequestIdStartPolicy.Conformance, Some(ConformanceCapture.Create(limits, lifetime)))

    interface IHostChannel with
        member _.ConformanceSnapshot = capture |> Option.map (fun owned -> owned.Snapshot())
        member _.ConformanceToken = capture |> Option.map _.Token |> Option.defaultValue CancellationToken.None
        member _.ConformanceEnd = capture |> Option.bind _.EndReason
        member _.AdmitOperation() = capture |> Option.bind (fun owned -> owned.AdmitOperation())
        member _.LocalRefused cause = capture |> Option.iter (fun owned -> owned.LocalRefused cause)
        member _.OperationCancelled operation = capture |> Option.iter (fun owned -> owned.OperationCancelled operation)
        member _.OperationFacts operation = capture |> Option.bind (fun owned -> owned.OperationFacts operation)
        member _.RequestObservedAsync(operation, method, parameters, cancellation) = request (Some operation) method parameters cancellation
        member _.NotifyObservedAsync(operation, method, parameters, cancellation) = notify (Some operation) method parameters cancellation
        member _.RunAsync cancellation = ended.Task.WaitAsync cancellation
        member _.Diagnostics = Array.empty :> IReadOnlyList<_>
        member _.Failure = failure

        member _.RequestAsync(method, parameters, cancellation) =
            task { let! result = request None method parameters cancellation
                   return result.Result }

        member _.NotifyAsync(method, parameters, cancellation) =
            task { let! result = notify None method parameters cancellation
                   return result.Result }

/// The channels of a host session.
[<RequireQualifiedAccess>]
module internal HostChannels =
    let private ofStreamsWithPolicy
        (idStart: RequestIdStartPolicy)
        (conformance: (ConformanceEvidenceLimits * CancellationToken) option)
        (input: Stream)
        (output: Stream)
        (state: unit -> SessionState)
        (progress: ConcurrentQueue<ProgressParams>)
        : IHostChannel =
        let inbound =
            Dispatcher(
                readOnlyDict
                    [ Method.Progress.WireValue,
                      Handlers.typedNotification MepJson.Default.ProgressParams (fun value _ ->
                          progress.Enqueue value
                          Task.CompletedTask) ],
                Func<string, MethodKind, Admission>(fun method kind -> Lifecycle.admit (state ()) method kind)
            )

        let isDone = Func<bool>(fun () -> state () = SessionState.Exited)

        let connection =
            match idStart, conformance with
            | RequestIdStartPolicy.Product, None -> Connection(input, output, inbound, isDone)
            | RequestIdStartPolicy.Conformance, Some(limits, lifetime) ->
                Connection.CreateConformance(input, output, inbound, isDone, limits, lifetime)
            | _ -> invalidOp "The internal channel policy and conformance options disagree."

        StreamChannel connection

    /// A channel on a framed stream pair. The guest's morphir.progress reports go to progress; state is the session's
    /// lifecycle state, which gates the guest's messages as it gates the host's. Requests start at ID0.
    let ofStreams input output = ofStreamsWithPolicy RequestIdStartPolicy.Product None input output

    /// The same framed channel, with requests starting at ID1 for an opted-in conformance session only.
    let ofConformanceStreams limits lifetime input output =
        ofStreamsWithPolicy RequestIdStartPolicy.Conformance (Some(limits, lifetime)) input output

    let private ofSingleMessageWithPolicy
        (idStart: RequestIdStartPolicy)
        (conformance: (ConformanceEvidenceLimits * CancellationToken) option)
        (guest: ISingleMessageChannel)
        (_: unit -> SessionState)
        (_: ConcurrentQueue<ProgressParams>)
        : IHostChannel =
        match idStart, conformance with
        | RequestIdStartPolicy.Product, None -> SingleMessageChannel guest :> IHostChannel
        | RequestIdStartPolicy.Conformance, Some(limits, lifetime) -> SingleMessageChannel.CreateConformance(guest, limits, lifetime) :> IHostChannel
        | _ -> invalidOp "The internal channel policy and conformance options disagree."

    /// A channel to a guest that takes one message per call. Such a guest cannot send a message of its own.
    let ofSingleMessage guest = ofSingleMessageWithPolicy RequestIdStartPolicy.Product None guest

    /// The same single-message channel, with requests starting at ID1 for an opted-in conformance session only.
    let ofConformanceSingleMessage limits lifetime guest =
        ofSingleMessageWithPolicy RequestIdStartPolicy.Conformance (Some(limits, lifetime)) guest
