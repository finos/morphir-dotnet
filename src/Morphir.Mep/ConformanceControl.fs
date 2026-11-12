// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Mep

open System
open System.Collections.Generic
open System.Text.Json
open System.Threading
open System.Threading.Tasks

/// Typed refusal of the one-shot selected-offer binding, without an invented wire response.
[<RequireQualifiedAccess>]
type internal ConformanceOfferBindingFailure =
    | ImplementationUnavailable
    | Busy
    | OriginalDescribeRequired
    | ForeignReceipt
    | AlreadyBound
    | InitializationAttempted
    | WrongState of State: SessionState
    | InvalidSelection of Cause: ModelTransportFailure
    | ChannelFailed of Cause: SessionFailure
    | CaptureFailed of Cause: ConformanceCaptureFailure
    | LifetimeCancelled
    | InvalidOffer of Cause: exn

/// Retains local and transport causes rather than presenting them as peer errors.
[<RequireQualifiedAccess>]
type internal ConformanceOperationFailure =
    | ImplementationUnavailable
    | LocalLifecycleRefusal of Cause: RpcError
    | ProbeAlreadyReserved
    | ChannelFailed of Cause: SessionFailure
    | ConnectionEnded of Cause: ConnectionEnd
    | TransportFailed of Cause: exn
    | CaptureFailed of Cause: ConformanceCaptureFailure
    | Cancelled of Cause: OperationCanceledException

/// Origin at this channel boundary. Submitted never establishes an inner guest invocation.
type internal ConformanceOperationOrigin =
    private
    | SubmittedOperation
    | LocalOperation of ConformanceOperationFailure
    | IncompleteOperation of ConformanceOperationFailure

[<RequireQualifiedAccess>]
module internal ConformanceOperationOrigin =
    let (|Submitted|_|) =
        function
        | SubmittedOperation -> Some()
        | _ -> None

    let (|AnsweredLocally|_|) =
        function
        | LocalOperation cause -> Some cause
        | _ -> None

    let (|Incomplete|_|) =
        function
        | IncompleteOperation cause -> Some cause
        | _ -> None

    let internal submitted = SubmittedOperation
    let internal local cause = LocalOperation cause
    let internal incomplete cause = IncompleteOperation cause

/// Immutable owned view of one operation's actual request/notification and winning reply.
[<Sealed>]
type internal ConformanceOperationObservation
    private
    (
        operation: ConformanceOperationId option,
        id: RequestId option,
        request: ConformanceBodyView option,
        response: ConformanceBodyView option,
        origin: ConformanceOperationOrigin,
        completed: bool
    ) =
    member _.Operation = operation
    member _.RequestId = id

    member _.RequestBody =
        request
        |> Option.map (fun body -> body.ToArray())

    member _.ResponseBody =
        response
        |> Option.map (fun body -> body.ToArray())

    member _.Origin = origin
    /// True establishes actual framing completion or adapter return. False does not prove absence of I/O.
    /// Incomplete origin retains its typed cause; adapter completion never proves an inner guest invocation.
    member _.SubmissionCompleted = completed

    static member internal Record(operation, id, request, response, origin, completed) =
        ConformanceOperationObservation(operation, id, request, response, origin, completed)

/// Typed result and immutable operation observations. Notification frames remain separate.
[<Sealed>]
type internal ConformanceOperationResult<'T, 'Error>
    private
    (
        result: Result<'T, 'Error>,
        observation: ConformanceOperationObservation,
        notifications: ConformanceOperationObservation list
    ) =
    let owned: IReadOnlyList<ConformanceOperationObservation> =
        Array.AsReadOnly(Array.ofList notifications)

    member _.Result = result
    member _.Observation = observation
    member _.Notifications = owned

    static member internal Record(result, observation, notifications) =
        ConformanceOperationResult(result, observation, notifications)

/// Submission provenance. Private cases prevent consumers from manufacturing submission outcomes.
type internal ConformanceProbeOutcome =
    private
    | NotSubmittedCore of Cause: ConformanceOperationFailure
    | SubmittedCore of Operation: ConformanceOperationId * Reply: ConformanceObservedReply option
    | IncompleteCore of Operation: ConformanceOperationId * Cause: ConformanceOperationFailure

/// Read-only outcome views and internal construction by the attached session coordinator.
[<RequireQualifiedAccess>]
module internal ConformanceProbeOutcome =
    let (|NotSubmitted|_|) outcome =
        match outcome with
        | NotSubmittedCore cause -> Some cause
        | _ -> None

    let (|Submitted|_|) outcome =
        match outcome with
        | SubmittedCore(operation, reply) -> Some(operation, reply)
        | _ -> None

    let (|Incomplete|_|) outcome =
        match outcome with
        | IncompleteCore(operation, cause) -> Some(operation, cause)
        | _ -> None

    let unavailable () =
        NotSubmittedCore ConformanceOperationFailure.ImplementationUnavailable

    let notSubmitted cause = NotSubmittedCore cause
    let submitted operation reply = SubmittedCore(operation, reply)
    let incomplete operation cause = IncompleteCore(operation, cause)
