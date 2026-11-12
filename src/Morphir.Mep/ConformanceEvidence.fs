// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Mep

open System
open System.Collections.Generic
open System.Text.Json
open System.Threading

/// Internal capture limits. They do not change the protocol or transport bounds.
[<Sealed>]
type internal ConformanceEvidenceLimits private (maxEvents: int, maxPayloadBytes: int64) =
    member _.MaxEvents = maxEvents
    member _.MaxPayloadBytes = maxPayloadBytes

    static member Create(maxEvents: int, maxPayloadBytes: int64) =
        if maxEvents <= 0 then invalidArg (nameof maxEvents) "The event limit must be positive."
        if maxPayloadBytes <= 0L then invalidArg (nameof maxPayloadBytes) "The payload limit must be positive."
        ConformanceEvidenceLimits(maxEvents, maxPayloadBytes)

/// Owned bytes. Both input and output are copied; no retained array is exposed.
[<Sealed>]
type internal ConformanceBody private (body: byte array) =
    let owned = Array.copy body
    member _.ToArray() = Array.copy owned
    member internal _.Memory = ReadOnlyMemory<byte> owned
    static member internal Copy(body: byte array) = ConformanceBody body

/// An admission identity, not a JSON-RPC ID or an individual frame identity.
[<Sealed>]
type internal ConformanceOperationId private (owner: obj, value: int64) =
    member _.Value = value
    member internal _.BelongsTo(candidate: obj) = obj.ReferenceEquals(owner, candidate)
    static member internal Record(owner: obj, value: int64) = ConformanceOperationId(owner, value)
    override _.ToString() = $"Operation {value}"

/// Several frames, such as initialize and initialized, can belong to one admitted operation.
[<Sealed>]
type internal ConformanceFrameId private (value: int64, operation: ConformanceOperationId option) =
    member _.Value = value
    member _.Operation = operation
    static member internal Record(value: int64, operation: ConformanceOperationId option) = ConformanceFrameId(value, operation)
    override _.ToString() = $"Frame {value}"

/// Opaque original exchange. Its owner is private and binding also requires the session's exact receipt object.
[<Sealed>]
type internal ConformanceDescribeReceipt private
    (owner: obj, operation: ConformanceOperationId, id: RequestId, request: ConformanceBody, response: ConformanceBody) =
    member internal _.BelongsTo(candidate: obj) = obj.ReferenceEquals(owner, candidate)
    member private _.Operation = operation
    member private _.RequestId = id
    member private _.RequestBody = request.ToArray()
    member private _.ResponseBody = response.ToArray()
    static member internal Record(owner, operation, id, request, response) =
        ConformanceDescribeReceipt(owner, operation, id, request, response)

/// An actual correlated response body, sharing the recorder's owned storage rather than reserializing a result.
[<Sealed>]
type internal ConformanceObservedReply private (id: RequestId, body: ConformanceBody) =
    member _.RequestId = id
    member _.Body = body.ToArray()
    static member internal Record(id, body) = ConformanceObservedReply(id, body)

/// Issued at the winning pending-request completion, never selected later from a snapshot.
[<Sealed>]
type internal ConformanceExchange private
    (operation: ConformanceOperationId, id: RequestId, request: ConformanceBody, response: ConformanceBody) =
    member _.Reply = ConformanceObservedReply.Record(id, response)
    member internal _.Receipt(owner: obj) = ConformanceDescribeReceipt.Record(owner, operation, id, request, response)
    static member internal Record(operation, id, request, response) = ConformanceExchange(operation, id, request, response)

/// The value and its transport observation travel together; a locally settled waiter has no exchange.
type internal ConformanceCallResult<'T> =
    { Result: Result<'T, RpcError>
      Exchange: ConformanceExchange option
      /// True for a checked response or a settled successful notification. False never proves absence of I/O;
      /// incomplete/local settlement must be read with the retained admission and transport events.
      Submitted: bool }

/// Why capture cannot establish a complete observation, retaining the original cause where available.
[<RequireQualifiedAccess>]
type internal ConformanceCaptureFailure =
    | ImplementationUnavailable
    | EventLimitExceeded of Limit: int
    | PayloadLimitExceeded of Limit: int64
    | ObservationFailed of Cause: exn
    | WriteFailed of Cause: exn
    | LifetimeCancelled
    | SequenceExhausted
    | FramingFailed of Cause: FramingFailure
    | BodyTooLarge of Bytes: int

/// Completeness is independent of whether the peer accepted a request.
[<RequireQualifiedAccess>]
type internal ConformanceCaptureStatus =
    | Complete
    | Incomplete of Cause: ConformanceCaptureFailure

/// Transport-independent observations of the actual channel end, not process exit or EOF beyond this channel.
[<RequireQualifiedAccess>]
type internal ConformanceTransportEnd =
    | EndOfStream
    | Stopped
    | FramingFailed of Cause: FramingFailure

/// Immutable event payloads. Private cases prevent a consumer from manufacturing recorder events.
/// Observation patterns below return copies of byte payloads rather than the owned buffers.
type internal ConformanceEvent =
    private
    | LocalRefusedCore of Operation: ConformanceOperationId * Cause: RpcError
    | OperationAdmittedCore of Operation: ConformanceOperationId
    | OutboundBodyPreparedCore of Frame: ConformanceFrameId * Id: RequestId option * Body: ConformanceBody
    | FramedWriteCompletedCore of Frame: ConformanceFrameId
    | FramedWriteFailedCore of Frame: ConformanceFrameId * Cause: exn
    | InboundBodyObservedCore of Body: ConformanceBody
    | AdapterCallStartedCore of Frame: ConformanceFrameId
    | AdapterCallReturnedCore of Frame: ConformanceFrameId * Body: ConformanceBody option
    | OperationCancelledCore of Operation: ConformanceOperationId
    | ChannelEndedCore of End: ConformanceTransportEnd
    | CaptureIncompleteCore of Cause: ConformanceCaptureFailure

/// Read-only views of events. These patterns cannot append to a session's private recorder.
[<RequireQualifiedAccess>]
module internal ConformanceEvent =
    let (|LocalRefused|_|) event =
        match event with LocalRefusedCore(operation, cause) -> Some(operation, cause) | _ -> None

    let (|OperationAdmitted|_|) event =
        match event with OperationAdmittedCore operation -> Some operation | _ -> None

    let (|OutboundBodyPrepared|_|) event =
        match event with
        | OutboundBodyPreparedCore(operation, id, body) -> Some(operation, id, body.ToArray())
        | _ -> None

    let (|FramedWriteCompleted|_|) event =
        match event with FramedWriteCompletedCore operation -> Some operation | _ -> None

    let (|FramedWriteFailed|_|) event =
        match event with FramedWriteFailedCore(operation, cause) -> Some(operation, cause) | _ -> None

    let (|InboundBodyObserved|_|) event =
        match event with InboundBodyObservedCore body -> Some(body.ToArray()) | _ -> None

    let (|AdapterCallStarted|_|) event =
        match event with AdapterCallStartedCore operation -> Some operation | _ -> None

    let (|AdapterCallReturned|_|) event =
        match event with
        | AdapterCallReturnedCore(operation, body) -> Some(operation, body |> Option.map _.ToArray())
        | _ -> None

    let (|OperationCancelled|_|) event =
        match event with OperationCancelledCore operation -> Some operation | _ -> None

    let (|ChannelEnded|_|) event =
        match event with ChannelEndedCore ended -> Some ended | _ -> None

    let (|CaptureIncomplete|_|) event =
        match event with CaptureIncompleteCore cause -> Some cause | _ -> None

/// An immutable sequence entry. No consumer-facing constructor can mint an observation.
[<Sealed>]
type internal ConformanceEventEntry private (sequence: int64, event: ConformanceEvent) =
    member _.Sequence = sequence
    member _.Event = event
    static member internal Record(sequence: int64, event: ConformanceEvent) = ConformanceEventEntry(sequence, event)

/// Immutable capture snapshot. Complete means no observation was lost, not protocol or guest success.
[<Sealed>]
type internal ConformanceSnapshot private (entries: ConformanceEventEntry array, status: ConformanceCaptureStatus) =
    let owned: IReadOnlyList<ConformanceEventEntry> = Array.AsReadOnly(Array.copy entries)
    member _.Events = owned
    member _.Status = status
    member _.IsComplete =
        match status with
        | ConformanceCaptureStatus.Complete -> true
        | ConformanceCaptureStatus.Incomplete _ -> false

    static member internal Unavailable() =
        ConformanceSnapshot([||], ConformanceCaptureStatus.Incomplete ConformanceCaptureFailure.ImplementationUnavailable)

    static member internal Record(entries: ConformanceEventEntry array, status: ConformanceCaptureStatus) =
        ConformanceSnapshot(entries, status)

[<RequireQualifiedAccess>]
type internal ConformanceSubmission =
    | Framed
    | Adapter

/// Constructed and retained privately by one opted-in transport. No attached recorder escapes to consumers.
[<Sealed>]
type internal ConformanceCapture private (limits: ConformanceEvidenceLimits, lifetime: CancellationToken) =
    let gate = obj ()
    let entries = ResizeArray<ConformanceEventEntry>()
    let reservations = Dictionary<ConformanceFrameId, int>()
    let owner = obj ()
    let admitted = HashSet<ConformanceOperationId>()
    let cancelled = HashSet<ConformanceOperationId>()
    let requests = Dictionary<RequestId, ConformanceOperationId * ConformanceBody>()
    let stop = CancellationTokenSource.CreateLinkedTokenSource lifetime
    let mutable status = ConformanceCaptureStatus.Complete
    let mutable sequence = 0L
    let mutable operation = 0L
    let mutable frame = 0L
    let mutable payload = 0L
    let mutable reserved = 0
    let mutable ended = false
    let mutable endReason: ConformanceTransportEnd option = None

    let append event =
        sequence <- sequence + 1L
        entries.Add(ConformanceEventEntry.Record(sequence, event))

    let lose cause =
        match status with
        | ConformanceCaptureStatus.Incomplete _ -> ()
        | ConformanceCaptureStatus.Complete ->
            status <- ConformanceCaptureStatus.Incomplete cause
            reservations.Clear()
            reserved <- 0
            if entries.Count < limits.MaxEvents && sequence < Int64.MaxValue then
                append (CaptureIncompleteCore cause)

    let active () =
        if not ended && lifetime.IsCancellationRequested then lose ConformanceCaptureFailure.LifetimeCancelled
        match status with
        | ConformanceCaptureStatus.Complete -> not ended
        | ConformanceCaptureStatus.Incomplete _ -> false

    // Cancellation runs outside the recorder lock; transport terminal callbacks may settle pending waiters.
    let owned action =
        let result, incomplete =
            lock gate (fun () ->
                active () |> ignore
                let result = action ()
                result, (match status with ConformanceCaptureStatus.Incomplete _ -> true | _ -> false))
        if incomplete then stop.Cancel()
        result

    let reserve events bytes =
        if not (active ()) then false
        elif events > limits.MaxEvents - entries.Count - reserved then
            lose (ConformanceCaptureFailure.EventLimitExceeded limits.MaxEvents)
            false
        elif int64 events > Int64.MaxValue - sequence - int64 reserved then
            lose ConformanceCaptureFailure.SequenceExhausted
            false
        elif bytes > limits.MaxPayloadBytes - payload then
            lose (ConformanceCaptureFailure.PayloadLimitExceeded limits.MaxPayloadBytes)
            false
        else
            reserved <- reserved + events
            payload <- payload + bytes
            true

    let recordReserved key event =
        match reservations.TryGetValue key with
        | true, count when count > 0 ->
            append event
            reserved <- reserved - 1
            if count = 1 then reservations.Remove key |> ignore
            else reservations[key] <- count - 1
            true
        | _ -> false

    let copy bytes =
        try Some(ConformanceBody.Copy bytes)
        with error ->
            lose (ConformanceCaptureFailure.ObservationFailed error)
            None

    member _.Token = stop.Token
    member _.LifetimeIsCancelled = lifetime.IsCancellationRequested
    member _.EndReason = lock gate (fun () -> endReason)
    member _.CanSubmit = owned active

    member _.Snapshot() =
        owned (fun () -> ConformanceSnapshot.Record(entries.ToArray(), status))

    member _.Incomplete(cause: ConformanceCaptureFailure) = owned (fun () -> lose cause)

    member _.AdmitOperation() =
        owned (fun () ->
            if operation = Int64.MaxValue then
                lose ConformanceCaptureFailure.SequenceExhausted
                None
            elif reserve 1 0L then
                operation <- operation + 1L
                let key = ConformanceOperationId.Record(owner, operation)
                admitted.Add key |> ignore
                reserved <- reserved - 1
                append (OperationAdmittedCore key)
                Some key
            else None)

    member _.LocalRefused(cause: RpcError) =
        owned (fun () ->
            if operation = Int64.MaxValue then lose ConformanceCaptureFailure.SequenceExhausted
            elif reserve 1 0L then
                operation <- operation + 1L
                reserved <- reserved - 1
                append (LocalRefusedCore(ConformanceOperationId.Record(owner, operation), cause)))

    member _.OperationCancelled(key: ConformanceOperationId) =
        owned (fun () ->
            if admitted.Contains key && cancelled.Add key then
                match status with
                | ConformanceCaptureStatus.Complete ->
                    if reserve 1 0L then
                        reserved <- reserved - 1
                        append (OperationCancelledCore key)
                | ConformanceCaptureStatus.Incomplete _ ->
                    if entries.Count < limits.MaxEvents && sequence < Int64.MaxValue then append (OperationCancelledCore key))

    member _.Prepare(id: RequestId option, bytes: byte array, kind: ConformanceSubmission, admission: ConformanceOperationId option) =
        owned (fun () ->
            let phases = match kind with ConformanceSubmission.Framed -> 2 | ConformanceSubmission.Adapter -> 3
            if admission |> Option.exists (fun key -> not (key.BelongsTo owner && admitted.Contains key)) then
                lose (ConformanceCaptureFailure.ObservationFailed(InvalidOperationException "Foreign or unadmitted operation."))
                None
            elif frame = Int64.MaxValue then
                lose ConformanceCaptureFailure.SequenceExhausted
                None
            elif not (reserve phases (int64 bytes.Length)) then None
            else
                match copy bytes with
                | None -> None
                | Some body ->
                    frame <- frame + 1L
                    let key = ConformanceFrameId.Record(frame, admission)
                    reservations.Add(key, phases)
                    match admission, id with
                    | Some operation, Some requestId -> requests[requestId] <- operation, body
                    | _ -> ()
                    recordReserved key (OutboundBodyPreparedCore(key, id, body)) |> ignore
                    Some key)

    member _.WriteCompleted(key: ConformanceFrameId) =
        owned (fun () -> active () && recordReserved key (FramedWriteCompletedCore key))

    member _.WriteFailed(key: ConformanceFrameId, cause: exn) =
        owned (fun () ->
            if not (recordReserved key (FramedWriteFailedCore(key, cause))) then
                if entries.Count < limits.MaxEvents && sequence < Int64.MaxValue then
                    append (FramedWriteFailedCore(key, cause))
            lose (ConformanceCaptureFailure.WriteFailed cause))

    member _.ObserveInbound(bytes: byte array) =
        owned (fun () ->
            if not (reserve 1 (int64 bytes.Length)) then None
            else
                match copy bytes with
                | None -> None
                | Some body ->
                    reserved <- reserved - 1
                    append (InboundBodyObservedCore body)
                    Some body)

    member _.AdapterStarted(key: ConformanceFrameId) =
        owned (fun () -> active () && recordReserved key (AdapterCallStartedCore key))

    /// One owned reply body is shared across return and inbound phases. Null has no inbound-body event.
    member _.AdapterReturned(key: ConformanceFrameId, bytes: byte array | null) : ConformanceBody option option =
        owned (fun () ->
            if not (active ()) then None
            else
                match bytes with
                | null ->
                    if recordReserved key (AdapterCallReturnedCore(key, None)) then Some None else None
                | bytes ->
                    if not (reserve 1 (int64 bytes.Length)) then None
                    else
                        match copy bytes with
                        | None -> None
                        | Some body ->
                            if recordReserved key (AdapterCallReturnedCore(key, Some body)) then
                                reserved <- reserved - 1
                                append (InboundBodyObservedCore body)
                                Some(Some body)
                            else
                                lose (ConformanceCaptureFailure.ObservationFailed(InvalidOperationException "Missing adapter return reservation."))
                                None)

    /// Called only by the actual winning response-completion path, with its already captured inbound body.
    member _.CompleteExchange(operation: ConformanceOperationId, id: RequestId, response: ConformanceBody) =
        owned (fun () ->
            match requests.TryGetValue id with
            | true, (registered, request) when active () && obj.ReferenceEquals(operation, registered) ->
                requests.Remove id |> ignore
                Some(ConformanceExchange.Record(operation, id, request, response))
            | _ -> None)

    member _.End(ending: ConformanceTransportEnd) =
        owned (fun () ->
            if not ended then
                match status with
                | ConformanceCaptureStatus.Complete ->
                    if reserve 1 0L then
                        reserved <- reserved - 1
                        append (ChannelEndedCore ending)
                | ConformanceCaptureStatus.Incomplete _ ->
                    if entries.Count < limits.MaxEvents && sequence < Int64.MaxValue then
                        append (ChannelEndedCore ending)
                endReason <- Some ending
                ended <- true)
        stop.Cancel()

    static member internal Create(limits: ConformanceEvidenceLimits, lifetime: CancellationToken) =
        ArgumentNullException.ThrowIfNull limits
        ConformanceCapture(limits, lifetime)
