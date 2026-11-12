// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Mep

open System
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
        match outcome with NotSubmittedCore cause -> Some cause | _ -> None

    let (|Submitted|_|) outcome =
        match outcome with SubmittedCore(operation, reply) -> Some(operation, reply) | _ -> None

    let (|Incomplete|_|) outcome =
        match outcome with IncompleteCore(operation, cause) -> Some(operation, cause) | _ -> None

    let unavailable () = NotSubmittedCore ConformanceOperationFailure.ImplementationUnavailable
    let notSubmitted cause = NotSubmittedCore cause
    let submitted operation reply = SubmittedCore(operation, reply)
    let incomplete operation cause = IncompleteCore(operation, cause)

/// Capability attached privately by HostSession. Consumers cannot substitute transport or coordinator callbacks.
[<Sealed>]
type internal ConformanceControl private
    (
        channel: IHostChannel,
        limits: ConformanceEvidenceLimits,
        lifetime: CancellationToken,
        receipt: unit -> ConformanceDescribeReceipt option,
        bind: ConformanceDescribeReceipt * ModelTransportSupport -> Result<unit, ConformanceOfferBindingFailure>,
        unknown: CancellationToken -> Task<ConformanceProbeOutcome>,
        afterShutdown: CancellationToken -> Task<ConformanceProbeOutcome>
    ) =
    member internal _.Limits = limits
    member internal _.Lifetime = lifetime
    member _.Snapshot() = channel.ConformanceSnapshot |> Option.defaultWith ConformanceSnapshot.Unavailable
    member _.DescribeReceipt = receipt ()

    member _.BindSelectedOffer
        (describe: ConformanceDescribeReceipt, offer: ModelTransportSupport)
        : Result<unit, ConformanceOfferBindingFailure> =
        bind (describe, offer)

    member _.SendUnknownNotificationAsync(cancellation: CancellationToken) = unknown cancellation

    member _.ProbeAfterShutdownAsync(cancellation: CancellationToken) = afterShutdown cancellation

    static member internal Attach(channel, limits: ConformanceEvidenceLimits, lifetime, receipt, bind, unknown, afterShutdown) =
        ArgumentNullException.ThrowIfNull limits
        ConformanceControl(channel, limits, lifetime, receipt, bind, unknown, afterShutdown)
