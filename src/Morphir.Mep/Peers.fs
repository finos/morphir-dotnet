// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Mep

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Text.Json.Serialization.Metadata
open System.Threading
open System.Runtime.CompilerServices
open System.Threading.Tasks

/// What a guest is and what it serves.
type GuestDefinition =
    {
        /// The result of morphir.extension.describe. Its extension is the extension of every session.
        Claims: CapabilityClaimSet
        /// What the extension supports. Its modelTransport is the guest support before negotiation.
        Capabilities: ExtensionCapabilities
        /// The extension methods, such as morphir.validator.validate. Lifecycle methods cannot be replaced.
        Methods: IReadOnlyDictionary<string, MethodHandler>
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"Guest {this.Claims.Extension}"

module private Results =
    let decode<'T when 'T: not null> (info: JsonTypeInfo<'T>) (element: JsonElement) : 'T option =
        try
            match JsonSerializer.Deserialize(element, info :> JsonTypeInfo) with
            | :? 'T as value -> Some value
            | _ -> None
        with :? JsonException ->
            None

    let invalidResult =
        RpcErrors.withMessage ErrorCode.InternalError "The result does not match the method's result type."

    let typed<'T when 'T: not null> (info: JsonTypeInfo<'T>) (reply: Task<Result<JsonElement, RpcError>>) =
        task {
            match! reply with
            | Error error -> return Error error
            | Ok element ->
                match decode info element with
                | Some value -> return Ok value
                | None -> return Error invalidResult
        }

/// The guest side of a session (design SES-2): the lifecycle methods, the gate and the extension methods.
[<Sealed>]
type Guest(definition: GuestDefinition) =
    let mutable state = SessionState.Created

    let initialize =
        Handlers.request MepJson.Default.InitializeParams MepJson.Default.InitializeResult (fun parameters _ ->
            match Lifecycle.negotiateVersion parameters.ProtocolVersions with
            | Error error -> Task.FromResult(Error error)
            | Ok version ->
                let transport =
                    match parameters.ModelTransport, definition.Capabilities.ModelTransport with
                    | Some offer, Some support -> Some(ModelTransport.select offer support)
                    | _ -> None

                let capabilities =
                    { definition.Capabilities with
                        ModelTransport = transport }

                state <-
                    SessionState.Initialized
                        { ProtocolVersion = version
                          Host = parameters.Host
                          Extension = definition.Claims.Extension
                          Capabilities = capabilities }

                Task.FromResult(
                    Ok
                        { ProtocolVersion = version
                          Extension = definition.Claims.Extension
                          Capabilities = capabilities }
                ))

    let lifecycle: (Method * MethodHandler) list =
        [ Method.Initialize, initialize
          Method.Describe,
          Handlers.request MepJson.Default.DescribeParams MepJson.Default.CapabilityClaimSet (fun _ _ ->
              Task.FromResult(Ok definition.Claims))
          Method.Initialized, Handlers.notification (fun _ -> Task.CompletedTask)
          Method.Ping, Handlers.constant Lifecycle.pingResult
          Method.Info,
          Handlers.requestWithoutParams MepJson.Default.ExtensionInfo (fun _ -> Task.FromResult(Ok definition.Claims.Extension))
          Method.Capabilities,
          Handlers.requestWithoutParams MepJson.Default.ExtensionCapabilities (fun _ ->
              match state with
              | SessionState.Initialized session -> Task.FromResult(Ok session.Capabilities)
              | _ -> Task.FromResult(Error(RpcErrors.create ErrorCode.NotInitialized)))
          Method.Shutdown,
          Handlers.raw (fun _ _ ->
              match state with
              | SessionState.Initialized session ->
                  state <- SessionState.ShutDown session
                  Task.FromResult(Ok(JsonRpc.Constant Lifecycle.shutdownResult))
              | _ -> Task.FromResult(Error(RpcErrors.create ErrorCode.NotInitialized)))
          Method.Exit,
          Handlers.notification (fun _ ->
              state <- SessionState.Exited
              Task.CompletedTask) ]

    let table =
        let methods = Dictionary<string, MethodHandler>(StringComparer.Ordinal)

        for KeyValue(name, handler) in definition.Methods do
            methods[name] <- handler

        for method, handler in lifecycle do
            methods[method.WireValue] <- handler

        methods :> IReadOnlyDictionary<_, _>

    let dispatcher =
        Dispatcher(table, Func<string, MethodKind, Admission>(fun method kind -> Lifecycle.admit state method kind))

    /// The lifecycle state.
    member _.State = state

    /// The dispatcher, for single-message transports such as the WASM runtime.
    member _.Dispatcher = dispatcher

    /// True after morphir.exit.
    member _.IsDone = state = SessionState.Exited

    /// Serves the session on a framed stream pair until exit, the end of the stream or a framing failure.
    member this.RunAsync(input: Stream, output: Stream, cancellation: CancellationToken) : Task<ConnectionEnd> =
        Connection(input, output, dispatcher, Func<bool>(fun () -> this.IsDone)).RunAsync cancellation

/// Why the host could not initialize a session.
[<RequireQualifiedAccess>]
type InitializeFailure =
    /// The guest refused initialize, for example with -32011.
    | Refused of Error: RpcError
    /// The result does not match the initialize-result type.
    | InvalidResult
    /// The guest chose a protocol version that the host did not offer.
    | UnexpectedVersion of Version: ProtocolVersion
    /// The guest selected model-transport support that the host did not offer.
    | SelectionRefused of Reason: string

    /// Stable text for diagnostics.
    override this.ToString() =
        match this with
        | Refused error -> $"The extension refused initialize: {error}"
        | InvalidResult -> "The initialize result does not match its type."
        | UnexpectedVersion version -> $"The extension chose the protocol version {version}, which the host did not offer."
        | SelectionRefused reason -> reason

    /// For C# callers: one function for each case.
    member this.Match<'R>
        (
            refused: Func<RpcError, 'R>,
            invalidResult: Func<'R>,
            unexpectedVersion: Func<ProtocolVersion, 'R>,
            selectionRefused: Func<string, 'R>
        ) : 'R =
        match this with
        | Refused error -> refused.Invoke error
        | InvalidResult -> invalidResult.Invoke()
        | UnexpectedVersion version -> unexpectedVersion.Invoke version
        | SelectionRefused reason -> selectionRefused.Invoke reason

/// Session-owned callbacks. Consumers receive the attached capability, never its transport or recorder.
type internal ConformanceOperationCallbacks = {
    Describe: CancellationToken -> Task<ConformanceOperationResult<CapabilityClaimSet, RpcError>>
    Initialize: CancellationToken -> Task<ConformanceOperationResult<NegotiatedSession, InitializeFailure>>
    Request: Method * Nullable<JsonElement> * CancellationToken -> Task<ConformanceOperationResult<JsonElement, RpcError>>
    Shutdown: CancellationToken -> Task<ConformanceOperationResult<JsonElement, RpcError>>
    UnknownRequest: CancellationToken -> Task<ConformanceOperationResult<JsonElement, RpcError>>
    UnknownNotification: CancellationToken -> Task<ConformanceOperationResult<unit, RpcError>>
    AfterShutdown: CancellationToken -> Task<ConformanceOperationResult<JsonElement, RpcError>>
    Exit: unit -> Task<ConformanceOperationResult<unit, RpcError>>
    Original: unit -> ConformanceOperationObservation option
}

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
        afterShutdown: CancellationToken -> Task<ConformanceProbeOutcome>,
        operations: ConformanceOperationCallbacks
    ) =
    member internal _.Limits = limits
    member internal _.Lifetime = lifetime
    member _.Snapshot() = channel.ConformanceSnapshot |> Option.defaultWith ConformanceSnapshot.Unavailable
    member _.DescribeReceipt = receipt ()
    member _.OriginalDescribeObservation = operations.Original()
    member _.DescribeAsync(cancellation: CancellationToken) = operations.Describe cancellation
    member _.InitializeAsync(cancellation: CancellationToken) = operations.Initialize cancellation
    member _.RequestAsync(method: Method, parameters: Nullable<JsonElement>, cancellation: CancellationToken) = operations.Request(method, parameters, cancellation)
    member _.ShutdownAsync(cancellation: CancellationToken) = operations.Shutdown cancellation
    member _.RequestUnknownMethodAsync(cancellation: CancellationToken) = operations.UnknownRequest cancellation
    member _.SendUnknownNotificationObservedAsync(cancellation: CancellationToken) = operations.UnknownNotification cancellation
    member _.ProbeAfterShutdownObservedAsync(cancellation: CancellationToken) = operations.AfterShutdown cancellation
    member _.ExitAsync() = operations.Exit()

    member _.BindSelectedOffer
        (describe: ConformanceDescribeReceipt, offer: ModelTransportSupport)
        : Result<unit, ConformanceOfferBindingFailure> =
        bind (describe, offer)

    member _.SendUnknownNotificationAsync(cancellation: CancellationToken) = unknown cancellation

    member _.ProbeAfterShutdownAsync(cancellation: CancellationToken) = afterShutdown cancellation

    static member internal Attach(channel, limits: ConformanceEvidenceLimits, lifetime, receipt, bind, unknown, afterShutdown, operations) =
        ArgumentNullException.ThrowIfNull limits
        ConformanceControl(channel, limits, lifetime, receipt, bind, unknown, afterShutdown, operations)

/// The host side of a session (design SES-2), on a framed stream pair or on a channel to a guest that takes one
/// message per call (design CHAN-1). The host applies the lifecycle rules before it sends, so it does not send a
/// request that the guest must refuse, and both channels refuse the same requests with the same error.
[<Sealed>]
type HostSession
    private
    (
        connect: (unit -> SessionState) -> ConcurrentQueue<ProgressParams> -> IHostChannel,
        host: PeerInfo,
        offer: ModelTransportSupport option,
        conformanceOptions: (ConformanceEvidenceLimits * CancellationToken) option
    ) =
    let mutable state = SessionState.Created
    let mutable initializing: NegotiatedSession option = None
    let progress = ConcurrentQueue<ProgressParams>()
    let channel = connect (fun () -> initializing |> Option.map SessionState.Initialized |> Option.defaultValue state) progress
    let operationGate = new SemaphoreSlim(1, 1)
    let receiptGate = obj ()
    let receiptOwner = obj ()
    let mutable originalReceipt: ConformanceDescribeReceipt option = None
    let mutable advertised: ModelTransportSupport option = None
    let mutable boundOffer: ModelTransportSupport option = None
    let mutable initializeAttempted = false
    let mutable unknownReserved = false
    let mutable afterShutdownReserved = false
    let mutable terminal: ConformanceOperationFailure option = None
    let conformanceLifetime = conformanceOptions |> Option.map snd |> Option.defaultValue CancellationToken.None
    let canonicalParameters =
        conformanceOptions |> Option.map (fun _ ->
            use document = JsonDocument.Parse "{}"
            document.RootElement.Clone())

    let rpcFailure = function
        | ConformanceOperationFailure.LocalLifecycleRefusal cause -> cause
        | ConformanceOperationFailure.Cancelled _ -> RpcErrors.create ErrorCode.RequestCancelled
        | ConformanceOperationFailure.ChannelFailed cause -> RpcErrors.withMessage ErrorCode.InternalError $"The session failed: {cause}"
        | ConformanceOperationFailure.ConnectionEnded cause -> RpcErrors.withMessage ErrorCode.InternalError $"The connection ended: {cause}"
        | ConformanceOperationFailure.ProbeAlreadyReserved -> RpcErrors.withMessage ErrorCode.InvalidRequest "This conformance probe was already reserved."
        | ConformanceOperationFailure.TransportFailed cause -> RpcErrors.withMessage ErrorCode.InternalError cause.Message
        | ConformanceOperationFailure.CaptureFailed _ -> RpcErrors.withMessage ErrorCode.InternalError "Conformance capture is incomplete."
        | ConformanceOperationFailure.ImplementationUnavailable -> RpcErrors.withMessage ErrorCode.InternalError "Conformance observation is unavailable."

    let health () =
        match terminal, channel.Failure with
        | Some cause, _ -> Some cause
        | _, Some cause -> Some(ConformanceOperationFailure.ChannelFailed cause)
        | _ ->
            match channel.ConformanceSnapshot with
            | Some snapshot ->
                match snapshot.Status with
                | ConformanceCaptureStatus.Incomplete cause -> Some(ConformanceOperationFailure.CaptureFailed cause)
                | ConformanceCaptureStatus.Complete ->
                    match channel.ConformanceEnd with
                    | Some ConformanceTransportEnd.EndOfStream -> Some(ConformanceOperationFailure.ConnectionEnded ConnectionEnd.EndOfStream)
                    | Some ConformanceTransportEnd.Stopped -> Some(ConformanceOperationFailure.ConnectionEnded ConnectionEnd.Stopped)
                    | Some(ConformanceTransportEnd.FramingFailed cause) -> Some(ConformanceOperationFailure.ConnectionEnded(ConnectionEnd.FramingFailed cause))
                    | None when conformanceLifetime.IsCancellationRequested -> Some(ConformanceOperationFailure.Cancelled(OperationCanceledException conformanceLifetime))
                    | None -> None
            | None -> Some ConformanceOperationFailure.ImplementationUnavailable

    let lifecycle (method: string) (kind: MethodKind) () =
        match Lifecycle.admit state method kind with
        | Admission.Proceed -> Ok()
        | Admission.Refuse cause -> Error(ConformanceOperationFailure.LocalLifecycleRefusal cause)

    let refuse cause = channel.LocalRefused(rpcFailure cause)

    // One owner covers admission, exchange and commit. Reading/correlation/cancel writes never take this gate.
    let withAdmission
        (cancellation: CancellationToken)
        (guard: unit -> Result<unit, ConformanceOperationFailure>)
        (reserve: unit -> unit)
        (work: ConformanceOperationId -> Task<'T>)
        (failed: ConformanceOperationId option -> ConformanceOperationFailure -> 'T) =
        task {
            use queued = CancellationTokenSource.CreateLinkedTokenSource([| cancellation; conformanceLifetime; channel.ConformanceToken |])
            let mutable entered = false
            let mutable admitted: ConformanceOperationId option = None
            try
                try
                    do! operationGate.WaitAsync queued.Token
                    entered <- true
                    // A check performed before this acquisition grants no permission to send.
                    let admission =
                        match health () with
                        | Some cause -> Error cause
                        | None when cancellation.IsCancellationRequested -> Error(ConformanceOperationFailure.Cancelled(OperationCanceledException cancellation))
                        | None -> guard ()
                    match admission with
                    | Error cause ->
                        refuse cause
                        return failed None cause
                    | Ok() ->
                        match channel.AdmitOperation() with
                        | None ->
                            let cause = health () |> Option.defaultValue ConformanceOperationFailure.ImplementationUnavailable
                            refuse cause
                            return failed None cause
                        | Some key ->
                            admitted <- Some key
                            reserve ()
                            let! result = work key
                            if cancellation.IsCancellationRequested || conformanceLifetime.IsCancellationRequested then channel.OperationCancelled key
                            return result
                with error ->
                    let cause =
                        match health () with
                        | Some failure -> failure
                        | None ->
                            match error with
                            | :? OperationCanceledException as cancelled -> ConformanceOperationFailure.Cancelled cancelled
                            | _ -> ConformanceOperationFailure.TransportFailed error
                    match admitted with
                    | Some key ->
                        if error :? OperationCanceledException || cancellation.IsCancellationRequested || conformanceLifetime.IsCancellationRequested then channel.OperationCancelled key
                    | None -> refuse cause
                    return failed admitted cause
            finally
                if entered then operationGate.Release() |> ignore
        }

    let requestIn (operation: ConformanceOperationId option) (method: string) (parameters: JsonElement option) (cancellation: CancellationToken) =
        match operation with
        | Some key -> channel.RequestObservedAsync(key, method, parameters, cancellation)
        | None ->
            task {
                let! result = channel.RequestAsync(method, parameters, cancellation)
                return { Result = result; Exchange = None; Submitted = false }
            }

    let notifyIn (operation: ConformanceOperationId option) (method: string) (parameters: JsonElement option) (cancellation: CancellationToken) =
        match operation with
        | Some key -> channel.NotifyObservedAsync(key, method, parameters, cancellation)
        | None ->
            task {
                let! result = channel.NotifyAsync(method, parameters, cancellation)
                return { Result = result; Exchange = None; Submitted = false }
            }

    let copyOffer (offer: ModelTransportSupport) : ModelTransportSupport =
        { Tuples = Array.AsReadOnly(Array.ofSeq offer.Tuples)
          Limits = { MaxEncodedBytes = offer.Limits.MaxEncodedBytes; MaxDecodedBytes = offer.Limits.MaxDecodedBytes; MaxDepth = offer.Limits.MaxDepth; MaxNodes = offer.Limits.MaxNodes }
          Guarantees = Array.AsReadOnly(Array.ofSeq offer.Guarantees) }

    let bindSelectedOffer (describe: ConformanceDescribeReceipt, offer: ModelTransportSupport) =
        // Copy before acquisition. The identical immutable candidate is validated, retained, encoded and verified.
        let candidate = try Ok(copyOffer offer) with error -> Error(ConformanceOfferBindingFailure.InvalidOffer error)
        match candidate with
        | Error cause -> Error cause
        | Ok _ when not (operationGate.Wait 0) -> Error ConformanceOfferBindingFailure.Busy
        | Ok candidate ->
            try
                let rejected cause =
                    channel.LocalRefused(RpcErrors.withMessage ErrorCode.InvalidRequest "Conformance offer binding was refused locally.")
                    Error cause
                match health () with
                | Some(ConformanceOperationFailure.ChannelFailed cause) -> rejected (ConformanceOfferBindingFailure.ChannelFailed cause)
                | Some(ConformanceOperationFailure.CaptureFailed cause) -> rejected (ConformanceOfferBindingFailure.CaptureFailed cause)
                | Some _ -> rejected (ConformanceOfferBindingFailure.WrongState state)
                | None ->
                    let original = lock receiptGate (fun () -> originalReceipt)
                    if isNull (box describe) || not (describe.BelongsTo receiptOwner) then rejected ConformanceOfferBindingFailure.ForeignReceipt
                    elif original |> Option.forall (fun receipt -> not (obj.ReferenceEquals(receipt, describe))) then rejected ConformanceOfferBindingFailure.OriginalDescribeRequired
                    elif initializeAttempted then rejected ConformanceOfferBindingFailure.InitializationAttempted
                    elif boundOffer.IsSome then rejected ConformanceOfferBindingFailure.AlreadyBound
                    elif state <> SessionState.Created then rejected (ConformanceOfferBindingFailure.WrongState state)
                    else
                        let tuples = advertised |> Option.map _.Tuples |> Option.defaultValue (Array.empty :> IReadOnlyList<ModelTransportTuple>)
                        let unsupported = candidate.Tuples |> Seq.tryFind (fun selected -> not (Seq.contains selected tuples))
                        let valid =
                            match unsupported with
                            | Some tuple -> Error(ModelTransportFailure.TupleNotOffered tuple)
                            // Self-verification validates duplicates/positive limits without imposing guest limit/order on a host offer.
                            | None -> ModelTransport.verifySelection candidate candidate |> Result.map ignore
                        match valid with
                        | Error cause -> rejected (ConformanceOfferBindingFailure.InvalidSelection cause)
                        | Ok() ->
                            match channel.AdmitOperation() with
                            | None -> rejected (ConformanceOfferBindingFailure.CaptureFailed ConformanceCaptureFailure.ImplementationUnavailable)
                            | Some _ ->
                                boundOffer <- Some candidate
                                Ok()
            finally
                operationGate.Release() |> ignore

    let observedFailure (result: Result<'T, RpcError>) =
        health () |> Option.defaultWith (fun () ->
            match result with
            | Error cause -> ConformanceOperationFailure.LocalLifecycleRefusal cause
            | Ok _ -> ConformanceOperationFailure.CaptureFailed(ConformanceCaptureFailure.ObservationFailed(InvalidOperationException "No completed transport observation accompanies the result.")))

    // Reads only this admitted operation's private transport facts. Snapshot status is used for
    // completeness, never to select request or response bytes.
    let project (operation: ConformanceOperationId option) (result: Result<'T, 'Error>) (failure: ConformanceOperationFailure option) =
        let snapshot = channel.ConformanceSnapshot
        let incomplete =
            match channel.Failure with
            | Some cause -> Some(ConformanceOperationFailure.ChannelFailed cause)
            | None ->
                match snapshot with
                | Some value ->
                    match value.Status with
                    | ConformanceCaptureStatus.Incomplete cause -> Some(ConformanceOperationFailure.CaptureFailed cause)
                    | _ -> failure |> Option.orElseWith health
                | None -> Some ConformanceOperationFailure.ImplementationUnavailable
        let cause = incomplete |> Option.defaultValue ConformanceOperationFailure.ImplementationUnavailable
        let captureLost = snapshot |> Option.exists (fun value -> not value.IsComplete)
        let allocation, frames =
            operation |> Option.bind channel.OperationFacts |> Option.defaultValue (None, [])
        let observation (fact: ConformanceTransportFact) =
            let origin =
                if fact.Completed && fact.Validated && not captureLost then ConformanceOperationOrigin.submitted
                else ConformanceOperationOrigin.incomplete cause
            ConformanceOperationObservation.Record(operation, fact.RequestId, Some(ConformanceBodyView.Observe fact.Request), fact.Response |> Option.map ConformanceBodyView.Observe, origin, fact.Completed)
        let primary =
            frames |> List.tryFind (fun fact -> fact.RequestId.IsSome)
            |> Option.orElseWith (fun () -> List.tryHead frames)
        let observed, notifications =
            match primary with
            | Some fact ->
                observation fact,
                (frames |> List.filter (fun other -> not (obj.ReferenceEquals(other.Frame, fact.Frame))) |> List.map observation)
            | None ->
                let origin = if operation.IsNone then ConformanceOperationOrigin.local cause else ConformanceOperationOrigin.incomplete cause
                ConformanceOperationObservation.Record(operation, allocation, None, None, origin, false), []
        ConformanceOperationResult<_,_>.Record(result, observed, notifications)

    let failedRpc operation cause = project operation (Error(rpcFailure cause)) (Some cause)

    let sendUnknownObserved cancellation =
        let guard () =
            match lifecycle "morphir.mck.unknown" MethodKind.Notification () with
            | Error cause -> Error cause
            | Ok() when unknownReserved -> Error ConformanceOperationFailure.ProbeAlreadyReserved
            | Ok() -> Ok()
        withAdmission cancellation guard (fun () -> unknownReserved <- true)
            (fun key -> task {
                let! observed = notifyIn (Some key) "morphir.mck.unknown" canonicalParameters cancellation
                return project (Some key) observed.Result None })
            failedRpc

    let probeAfterShutdownObserved cancellation =
        let guard () =
            match state with
            | SessionState.ShutDown _ when afterShutdownReserved -> Error ConformanceOperationFailure.ProbeAlreadyReserved
            | SessionState.ShutDown _ -> Ok()
            | _ -> Error(ConformanceOperationFailure.LocalLifecycleRefusal(RpcErrors.withMessage ErrorCode.InvalidRequest "The conformance ping requires a completed shutdown."))
        withAdmission cancellation guard (fun () -> afterShutdownReserved <- true)
            (fun key -> task {
                let! observed = requestIn (Some key) Method.Ping.WireValue canonicalParameters cancellation
                return project (Some key) observed.Result None })
            failedRpc

    let legacyProbe (observed: ConformanceOperationObservation) =
        match observed.Origin, observed.Operation with
        | ConformanceOperationOrigin.Submitted, Some key ->
            let reply =
                channel.OperationFacts key |> Option.bind (fun (_, frames) ->
                    frames |> List.tryPick (fun fact ->
                        match fact.RequestId, fact.Response with
                        | Some id, Some body when fact.Validated -> Some(ConformanceObservedReply.Record(id, body))
                        | _ -> None))
            ConformanceProbeOutcome.submitted key reply
        | ConformanceOperationOrigin.Incomplete cause, Some key -> ConformanceProbeOutcome.incomplete key cause
        | ConformanceOperationOrigin.AnsweredLocally cause, _ -> ConformanceProbeOutcome.notSubmitted cause
        | _ -> ConformanceProbeOutcome.notSubmitted ConformanceOperationFailure.ImplementationUnavailable

    let sendUnknown cancellation = task {
        let! result = sendUnknownObserved cancellation
        return legacyProbe result.Observation }

    let probeAfterShutdown cancellation = task {
        let! result = probeAfterShutdownObserved cancellation
        return legacyProbe result.Observation }

    let requestObserved (method: Method) (parameters: JsonElement option) cancellation =
        // Clone before queueing; the caller may dispose its source document while waiting.
        let ownedParameters = try Ok(parameters |> Option.map _.Clone()) with error -> Error error
        match ownedParameters with
        | Error error ->
            let cause = ConformanceOperationFailure.TransportFailed error
            refuse cause
            Task.FromResult(failedRpc None cause)
        | Ok ownedParameters ->
            let guard () =
                match method with
                | Method.Initialize | Method.Shutdown ->
                    Error(ConformanceOperationFailure.LocalLifecycleRefusal(RpcErrors.withMessage ErrorCode.InvalidRequest "Use the dedicated session operation for this lifecycle request."))
                | _ when method.Kind <> MethodKind.Request ->
                    Error(ConformanceOperationFailure.LocalLifecycleRefusal(RpcErrors.withMessage ErrorCode.InvalidRequest "A notification cannot be submitted as a raw request."))
                | _ -> lifecycle method.WireValue MethodKind.Request ()
            withAdmission cancellation guard ignore
                (fun key -> task {
                    let! observed = requestIn (Some key) method.WireValue ownedParameters cancellation
                    return project (Some key) observed.Result None })
                failedRpc

    let unknownRequestObserved cancellation =
        let guard () =
            match state with
            | SessionState.Initialized _ -> Ok()
            | _ -> Error(ConformanceOperationFailure.LocalLifecycleRefusal(RpcErrors.create ErrorCode.NotInitialized))
        withAdmission cancellation guard ignore
            (fun key -> task {
                let! observed = requestIn (Some key) "morphir.mck.unknown" canonicalParameters cancellation
                return project (Some key) observed.Result None })
            failedRpc

    let send (method: Method) (parameters: JsonElement option) (cancellation: CancellationToken) =
        match conformanceOptions with
        | None ->
            match Lifecycle.admit state method.WireValue MethodKind.Request with
            | Admission.Refuse error -> Task.FromResult(Error error)
            | Admission.Proceed -> channel.RequestAsync(method.WireValue, parameters, cancellation)
        | Some _ -> task {
            let! observed = requestObserved method parameters cancellation
            return observed.Result }

    let sendTyped (info: JsonTypeInfo<'T>) (method: Method) cancellation =
        Results.typed info (send method None cancellation)

    // Encoding and selection verification use this same effective offer on both construction policies.
    let initializeCore (operation: ConformanceOperationId option) (effectiveOffer: ModelTransportSupport option) (cancellation: CancellationToken) =
        let parameters =
            JsonSerializer.SerializeToElement(
                { ProtocolVersions = Lifecycle.protocolVersions
                  Host = host
                  ModelTransport = effectiveOffer },
                MepJson.Default.InitializeParams
            )
        task {
            let! observed = requestIn operation Method.Initialize.WireValue (Some parameters) cancellation
            match observed.Result with
            | Error error -> return Error(InitializeFailure.Refused error)
            | Ok element ->
                match Results.decode MepJson.Default.InitializeResult element with
                | None -> return Error InitializeFailure.InvalidResult
                | Some result when result.ProtocolVersion <> Lifecycle.protocolVersion ->
                    return Error(InitializeFailure.UnexpectedVersion result.ProtocolVersion)
                | Some result ->
                    let selection =
                        match effectiveOffer, result.Capabilities.ModelTransport with
                        | _, None -> Ok None
                        | Some offered, Some selected ->
                            ModelTransport.verifySelection offered selected
                            |> Result.map Some
                            |> Result.mapError _.ToString()
                        | None, Some _ ->
                            Error $"The extension {result.Extension} selected model transport, which the host did not offer."
                    match selection with
                    | Error reason -> return Error(InitializeFailure.SelectionRefused reason)
                    | Ok transport ->
                        let session =
                            { ProtocolVersion = result.ProtocolVersion
                              Host = host
                              Extension = result.Extension
                              Capabilities = { result.Capabilities with ModelTransport = transport } }
                        match operation with
                        | None -> state <- SessionState.Initialized session
                        | Some _ -> initializing <- Some session
                        try
                            let parameters = if operation.IsSome then canonicalParameters else None
                            let! notification = notifyIn operation Method.Initialized.WireValue parameters cancellation
                            match notification.Result with
                            | Ok() ->
                                match operation with
                                | Some _ ->
                                    match health () with
                                    | Some cause ->
                                        terminal <- Some cause
                                        return Error(InitializeFailure.Refused(rpcFailure cause))
                                    | None ->
                                        state <- SessionState.Initialized session
                                        return Ok session
                                | None -> return Ok session
                            | Error error ->
                                if operation.IsSome then terminal <- Some(observedFailure notification.Result)
                                return Error(InitializeFailure.Refused error)
                        finally
                            initializing <- None
        }

    let shutdownCore (operation: ConformanceOperationId option) (cancellation: CancellationToken) =
        task {
            let parameters = if operation.IsSome then canonicalParameters else None
            let! observed = requestIn operation Method.Shutdown.WireValue parameters cancellation
            match observed.Result, state with
            | Ok _, SessionState.Initialized session -> state <- SessionState.ShutDown session
            | _ -> ()
            return observed.Result
        }

    let describeObserved cancellation =
        let parameters = JsonSerializer.SerializeToElement(
            { ProtocolVersions = Lifecycle.protocolVersions }, MepJson.Default.DescribeParams)

        withAdmission cancellation (lifecycle Method.Describe.WireValue MethodKind.Request) ignore
            (fun key -> task {
                let! observed = requestIn (Some key) Method.Describe.WireValue (Some parameters) cancellation
                match observed.Result with
                | Error cause -> return project (Some key) (Error cause) None
                | Ok element ->
                    match Results.decode MepJson.Default.CapabilityClaimSet element with
                    | None -> return project (Some key) (Error Results.invalidResult) None
                    | Some claims ->
                        match observed.Exchange with
                        | Some exchange ->
                            // The exchange accompanies the winning pending completion. No snapshot lookup or
                            // reserialization can substitute duplicate/unsolicited bodies for this response.
                            let support =
                                match claims.Capabilities.TryGetValue "modelTransport" with
                                | false, _ -> Ok None
                                | true, element ->
                                    Results.decode MepJson.Default.ModelTransportSupport element
                                    |> Option.map (copyOffer >> Some >> Ok)
                                    |> Option.defaultValue (Error Results.invalidResult)
                            match support with
                            | Error cause -> return project (Some key) (Error cause) None
                            | Ok support ->
                                lock receiptGate (fun () ->
                                    if originalReceipt.IsNone then
                                        advertised <- support
                                        originalReceipt <- Some(exchange.Receipt receiptOwner))
                                return project (Some key) (Ok claims) None
                        | _ -> return project (Some key) (Error(rpcFailure (observedFailure observed.Result))) None })
            failedRpc


    let initializeObserved cancellation =
        let guard () =
            match lifecycle Method.Initialize.WireValue MethodKind.Request () with
            | Error cause -> Error cause
            | Ok() when initializeAttempted -> Error(ConformanceOperationFailure.LocalLifecycleRefusal(RpcErrors.withMessage ErrorCode.InvalidRequest "Initialize has already been attempted."))
            | Ok() when boundOffer.IsNone -> Error(ConformanceOperationFailure.LocalLifecycleRefusal(RpcErrors.withMessage ErrorCode.InvalidRequest "A selected offer must be bound before initialize."))
            | Ok() -> Ok()
        withAdmission cancellation guard (fun () -> initializeAttempted <- true)
            (fun key -> task {
                let effectiveOffer = boundOffer
                let! result = initializeCore (Some key) effectiveOffer cancellation
                return project (Some key) result None })
            (fun operation cause -> project operation (Error(InitializeFailure.Refused(rpcFailure cause))) (Some cause))

    let shutdownObserved cancellation =
        withAdmission cancellation (lifecycle Method.Shutdown.WireValue MethodKind.Request) ignore
            (fun key -> task {
                let! result = shutdownCore (Some key) cancellation
                return project (Some key) result None })
            failedRpc

    let exitObserved () =
        withAdmission CancellationToken.None (lifecycle Method.Exit.WireValue MethodKind.Notification)
            (fun () -> terminal <- Some(ConformanceOperationFailure.LocalLifecycleRefusal(RpcErrors.withMessage ErrorCode.InvalidRequest "Exit has been reserved.")))
            (fun key -> task {
                let! observed = notifyIn (Some key) Method.Exit.WireValue canonicalParameters CancellationToken.None
                state <- SessionState.Exited
                return project (Some key) observed.Result None })
            failedRpc

    let originalObservation () =
        lock receiptGate (fun () -> originalReceipt |> Option.map (fun receipt ->
            let operation, id, request, response = receipt.ObservedData
            ConformanceOperationObservation.Record(Some operation, Some id, Some request, Some response, ConformanceOperationOrigin.submitted, true)))

    let conformance =
        conformanceOptions |> Option.map (fun (limits, lifetime) ->
            let callbacks = {
                Describe = describeObserved
                Initialize = initializeObserved
                Request = fun (method, parameters, ct) -> requestObserved method (Option.ofNullable parameters) ct
                Shutdown = shutdownObserved
                UnknownRequest = unknownRequestObserved
                UnknownNotification = sendUnknownObserved
                AfterShutdown = probeAfterShutdownObserved
                Exit = exitObserved
                Original = originalObservation
            }
            ConformanceControl.Attach(channel, limits, lifetime, (fun () -> lock receiptGate (fun () -> originalReceipt)), bindSelectedOffer, sendUnknown, probeAfterShutdown, callbacks))
    /// A host session on a framed stream pair: input carries the guest's frames and output the host's. offer is the
    /// model-transport support the host offers, or null for none.
    new(input: Stream, output: Stream, host: PeerInfo, offer: ModelTransportSupport | null) =
        HostSession(HostChannels.ofStreams input output, host, Option.ofObj offer, None)

    /// A legacy-only host session on a framed stream pair: it offers no model transport.
    new(input: Stream, output: Stream, host: PeerInfo) =
        HostSession(HostChannels.ofStreams input output, host, None, None)

    /// A host session on a channel to a guest that takes one message per call, such as a WASM guest. offer is the
    /// model-transport support the host offers, or null for none. The session passes one message per call, accepts
    /// at most one message back, never sends $/cancelRequest, and ends at the first violation or cancellation
    /// (see Failure).
    new(channel: ISingleMessageChannel, host: PeerInfo, offer: ModelTransportSupport | null) =
        HostSession(HostChannels.ofSingleMessage channel, host, Option.ofObj offer, None)

    /// A legacy-only host session on a channel to a guest that takes one message per call: it offers no model
    /// transport.
    new(channel: ISingleMessageChannel, host: PeerInfo) =
        HostSession(HostChannels.ofSingleMessage channel, host, None, None)

    /// Internal opt-in on the same framed channel, with requests starting at ID1.
    /// Owns bounded actual capture, operation admission and one-shot selected-offer binding on this session only.
    static member internal CreateConformance
        (input: Stream, output: Stream, host: PeerInfo, limits: ConformanceEvidenceLimits, lifetime: CancellationToken) =
        ArgumentNullException.ThrowIfNull input
        ArgumentNullException.ThrowIfNull output
        ArgumentNullException.ThrowIfNull host
        HostSession(
            HostChannels.ofConformanceStreams limits lifetime input output,
            host,
            None,
            Some(limits, lifetime)
        )

    /// Internal opt-in on the caller's same actual single-message adapter, with requests starting at ID1.
    static member internal CreateConformance
        (channel: ISingleMessageChannel, host: PeerInfo, limits: ConformanceEvidenceLimits, lifetime: CancellationToken) =
        ArgumentNullException.ThrowIfNull channel
        ArgumentNullException.ThrowIfNull host
        HostSession(
            HostChannels.ofConformanceSingleMessage limits lifetime channel,
            host,
            None,
            Some(limits, lifetime)
        )

    /// None on ordinary constructors; attached observations and fixed conformance controls on opted-in sessions.
    member internal _.Conformance = conformance

    /// Starts reading the guest's messages. The task ends when the connection ends. On a single-message channel
    /// there is nothing to read: the task ends with Stopped at exit or when the session fails, and requests do not
    /// need it to run.
    member _.RunAsync(cancellation: CancellationToken) : Task<ConnectionEnd> = channel.RunAsync cancellation

    /// The lifecycle state.
    member _.State = state

    /// The protocol diagnostics of the connection. A single-message channel reports its problems in Failure.
    member _.Diagnostics = channel.Diagnostics

    /// Why the session ended before exit: the guest broke a rule of its single-message channel, or a call to it was
    /// cancelled. None on a framed stream pair, and while the session runs.
    member _.Failure: SessionFailure option = channel.Failure

    /// For C# callers: why the session ended before exit, or null. See Failure.
    member _.FailureOrNull() : SessionFailure | null = Option.toObj channel.Failure

    /// The progress reports received from the guest, for requests of this session.
    member _.Progress: IReadOnlyCollection<ProgressParams> = progress.ToArray()

    /// Asks for the extension's claims, before or during a session.
    member _.DescribeAsync(cancellation: CancellationToken) =
        match conformanceOptions with
        | None ->
            let parameters = JsonSerializer.SerializeToElement(
                { ProtocolVersions = Lifecycle.protocolVersions }, MepJson.Default.DescribeParams)
            Results.typed MepJson.Default.CapabilityClaimSet (send Method.Describe (Some parameters) cancellation)
        | Some _ -> task {
            let! observed = describeObserved cancellation
            return observed.Result }

    /// Initializes the session: offers only 0.2.0-draft.1, checks the guest's choice and selection, and then sends
    /// morphir.initialized. When the session fails during either message, the result is Refused with the error that
    /// the session gives, and Failure says why.
    member _.InitializeAsync(cancellation: CancellationToken) : Task<Result<NegotiatedSession, InitializeFailure>> =
        match conformanceOptions with
        | None ->
            match Lifecycle.admit state Method.Initialize.WireValue MethodKind.Request with
            | Admission.Refuse error -> Task.FromResult(Error(InitializeFailure.Refused error))
            | Admission.Proceed -> initializeCore None offer cancellation
        | Some _ -> task {
            let! observed = initializeObserved cancellation
            return observed.Result }

    /// Sends morphir.ping. The result is the guest's reply object.
    member _.PingAsync(cancellation: CancellationToken) : Task<Result<JsonElement, RpcError>> =
        send Method.Ping None cancellation

    /// Asks for the extension info.
    member _.InfoAsync(cancellation: CancellationToken) =
        sendTyped MepJson.Default.ExtensionInfo Method.Info cancellation

    /// Asks for the session capabilities.
    member _.CapabilitiesAsync(cancellation: CancellationToken) =
        sendTyped MepJson.Default.ExtensionCapabilities Method.Capabilities cancellation

    /// Sends an extension method request, such as morphir.validator.validate, with raw params or none.
    member _.RequestAsync(method: Method, parameters: Nullable<JsonElement>, cancellation: CancellationToken) =
        send method (Option.ofNullable parameters) cancellation

    /// Sends morphir.shutdown. After it, only exit is allowed. The result is the guest's reply object.
    member _.ShutdownAsync(cancellation: CancellationToken) : Task<Result<JsonElement, RpcError>> =
        match conformanceOptions with
        | None ->
            match Lifecycle.admit state Method.Shutdown.WireValue MethodKind.Request with
            | Admission.Refuse error -> Task.FromResult(Error error)
            | Admission.Proceed -> shutdownCore None cancellation
        | Some _ -> task {
            let! observed = shutdownObserved cancellation
            return observed.Result }

    /// Sends morphir.exit and ends the session. A session that failed passes nothing to the guest.
    member _.ExitAsync() : Task =
        match conformanceOptions with
        | None ->
            task {
                let! _ = channel.NotifyAsync(Method.Exit.WireValue, None, CancellationToken.None)
                state <- SessionState.Exited
            }
        | Some _ -> task {
            let! _ = exitObserved ()
            return () }

/// Lets C# callers handle an F# Result without branching on its cases.
[<Extension; AbstractClass; Sealed>]
type ResultExtensions =
    /// Calls ok with the value of a success, or error with the error of a failure.
    [<Extension>]
    static member Match(result: Result<'T, 'E>, ok: Func<'T, 'R>, error: Func<'E, 'R>) : 'R =
        match result with
        | Ok value -> ok.Invoke value
        | Error failure -> error.Invoke failure

/// <summary>
/// The extension ids that only a host's own built-in frontends and backends may use: every id that starts with
/// <c>morphir.</c>, including ids of built-ins that a host does not ship yet. A host refuses a guest that reports one,
/// so a guest cannot pass as a built-in. <c>org.finos.morphir.*</c> and every other id are not reserved.
/// </summary>
[<AbstractClass; Sealed>]
type ReservedExtensionIds =
    /// <summary>The reserved prefix, <c>morphir.</c>, compared ordinally (extension ids are lowercase).</summary>
    static member Prefix = "morphir."

    /// <summary>True when the id starts with the reserved prefix. Null and empty text are not reserved.</summary>
    static member IsReserved(id: string | null) : bool =
        match id with
        | null -> false
        | text -> text.StartsWith(ReservedExtensionIds.Prefix, StringComparison.Ordinal)
