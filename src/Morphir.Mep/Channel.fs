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
type internal SingleMessageChannel(guest: ISingleMessageChannel) =
    let calls = new SemaphoreSlim(1, 1)
    let ended = TaskCompletionSource<ConnectionEnd>(TaskCreationOptions.RunContinuationsAsynchronously)
    let gate = obj ()
    let mutable failure: SessionFailure option = None
    let mutable nextId = -1L

    let fail (reason: SessionFailure) =
        lock gate (fun () ->
            if failure.IsNone then
                failure <- Some reason)

        ended.TrySetResult ConnectionEnd.Stopped |> ignore

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
    let invoke (method: string) (cancellation: CancellationToken) (call: unit -> Task<byte array | null>) =
        task {
            try
                let! reply = call ()

                if cancellation.IsCancellationRequested then
                    return Error(SessionFailure.Cancelled method)
                else
                    return Ok reply
            with
            | _ when cancellation.IsCancellationRequested -> return Error(SessionFailure.Cancelled method)
            | error -> return Error(SessionFailure.ChannelFailed(method, error))
        }

    /// The reply to a request: the guest's response or error response for the same id.
    let reply (method: string) (id: RequestId) (bytes: byte array | null) =
        match bytes with
        | null -> Error(SessionFailure.NoReply method)
        | bytes when bytes.Length = 0 -> Error(SessionFailure.NoReply method)
        | bytes when bytes.Length > ContentLengthFraming.MaxBodyBytes ->
            Error(SessionFailure.ReplyTooLarge(method, bytes.Length))
        | bytes ->
            match JsonRpc.Read(ReadOnlyMemory bytes) with
            | Error refusal -> Error(SessionFailure.InvalidReply(method, refusal))
            | Ok(JsonRpcMessage.Response(replyId, result)) when replyId = id -> Ok(Ok result)
            | Ok(JsonRpcMessage.ErrorResponse(Some replyId, error)) when replyId = id -> Ok(Error error)
            | Ok(JsonRpcMessage.Response(other, _)) -> Error(SessionFailure.ReplyForAnotherRequest(method, id, Some other))
            | Ok(JsonRpcMessage.ErrorResponse(other, _)) -> Error(SessionFailure.ReplyForAnotherRequest(method, id, other))
            | Ok(JsonRpcMessage.Request(_, returned, _))
            | Ok(JsonRpcMessage.Notification(returned, _)) -> Error(SessionFailure.NotAResponse(method, returned))

    /// Runs one call while no other call is inside the guest, and ends the session when check refuses the result.
    let exchange
        (method: string)
        (cancellation: CancellationToken)
        (call: unit -> Task<byte array | null>)
        (check: byte array | null -> Result<Result<'T, RpcError>, SessionFailure>)
        : Task<Result<'T, RpcError>> =
        task {
            match failure with
            | Some reason -> return Error(endedWith reason)
            | None ->
                let! entered = enter cancellation

                if not entered then
                    return Error(RpcErrors.create ErrorCode.RequestCancelled)
                else
                    try
                        match failure with
                        | Some reason -> return Error(endedWith reason)
                        | None ->
                            let! outcome = invoke method cancellation call

                            match outcome |> Result.bind check with
                            | Ok result -> return result
                            | Error reason ->
                                fail reason
                                return Error(failedWith reason)
                    finally
                        calls.Release() |> ignore
        }

    interface IHostChannel with
        member _.RunAsync cancellation = ended.Task.WaitAsync cancellation
        member _.Diagnostics = Array.empty :> IReadOnlyList<_>
        member _.Failure = failure

        member _.RequestAsync(method, parameters, cancellation) =
            let id = RequestId.Number(uint64 (Interlocked.Increment &nextId))

            task {
                let message = encode (JsonRpcMessage.Request(id, method, parameters))

                return!
                    exchange
                        method
                        cancellation
                        (fun () -> guest.RequestAsync(method, ReadOnlyMemory message, cancellation))
                        (reply method id)
            }

        member _.NotifyAsync(method, parameters, cancellation) =
            task {
                let message = encode (JsonRpcMessage.Notification(method, parameters))

                let! result =
                    exchange
                        method
                        cancellation
                        (fun () -> guest.NotifyAsync(method, ReadOnlyMemory message, cancellation))
                        (fun bytes ->
                            match bytes with
                            | null -> Ok(Ok())
                            | bytes when bytes.Length = 0 -> Ok(Ok())
                            | _ -> Error(SessionFailure.AnsweredNotification method))

                if result.IsOk && method = Method.Exit.WireValue then
                    ended.TrySetResult ConnectionEnd.Stopped |> ignore

                return result
            }

/// The channels of a host session.
[<RequireQualifiedAccess>]
module internal HostChannels =
    /// A channel on a framed stream pair. The guest's morphir.progress reports go to progress; state is the session's
    /// lifecycle state, which gates the guest's messages as it gates the host's.
    let ofStreams
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

        StreamChannel(Connection(input, output, inbound, Func<bool>(fun () -> state () = SessionState.Exited)))

    /// A channel to a guest that takes one message per call. Such a guest cannot send a message of its own.
    let ofSingleMessage (guest: ISingleMessageChannel) (_: unit -> SessionState) (_: ConcurrentQueue<ProgressParams>) =
        SingleMessageChannel guest :> IHostChannel
