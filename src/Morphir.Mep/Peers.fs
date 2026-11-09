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
open System.Threading.Tasks

/// What a guest is and what it serves.
type GuestDefinition =
    {
        /// The result of morphir.extension.describe.
        Claims: CapabilityClaimSet
        /// Who the extension is.
        Extension: ExtensionInfo
        /// What the extension supports. Its modelTransport is the guest support before negotiation.
        Capabilities: ExtensionCapabilities
        /// The extension methods, such as morphir.validator.validate. Lifecycle methods cannot be replaced.
        Methods: IReadOnlyDictionary<string, MethodHandler>
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"Guest {this.Extension}"

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
                          Extension = definition.Extension
                          Capabilities = capabilities }

                Task.FromResult(
                    Ok
                        { ProtocolVersion = version
                          Extension = definition.Extension
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
          Handlers.requestWithoutParams MepJson.Default.ExtensionInfo (fun _ -> Task.FromResult(Ok definition.Extension))
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

/// The host side of a session on a framed stream pair (design SES-2). The host applies the lifecycle rules before
/// it sends, so it does not send a request that the guest must refuse.
[<Sealed>]
type HostSession(input: Stream, output: Stream, host: PeerInfo, offer: ModelTransportSupport option) =
    let mutable state = SessionState.Created
    let progress = ConcurrentQueue<ProgressParams>()

    let inbound =
        Dispatcher(
            readOnlyDict
                [ Method.Progress.WireValue,
                  Handlers.typedNotification MepJson.Default.ProgressParams (fun value ->
                      progress.Enqueue value
                      Task.CompletedTask) ],
            Func<string, MethodKind, Admission>(fun method kind -> Lifecycle.admit state method kind)
        )

    let connection =
        Connection(input, output, inbound, Func<bool>(fun () -> state = SessionState.Exited))

    let send (method: Method) (parameters: JsonElement option) (cancellation: CancellationToken) =
        match Lifecycle.admit state method.WireValue MethodKind.Request with
        | Admission.Refuse error -> Task.FromResult(Error error)
        | Admission.Proceed -> connection.RequestAsync(method.WireValue, parameters, cancellation)

    /// Starts reading the guest's messages. The task ends when the connection ends.
    member _.RunAsync(cancellation: CancellationToken) : Task<ConnectionEnd> = connection.RunAsync cancellation

    /// The lifecycle state.
    member _.State = state

    /// The protocol diagnostics of the connection.
    member _.Diagnostics = connection.Diagnostics

    /// The progress reports received from the guest, for requests of this session.
    member _.Progress: IReadOnlyCollection<ProgressParams> = progress.ToArray()

    /// Asks for the extension's claims, before or during a session.
    member _.DescribeAsync(cancellation: CancellationToken) =
        let parameters =
            JsonSerializer.SerializeToElement(
                { ProtocolVersions = Lifecycle.protocolVersions },
                MepJson.Default.DescribeParams
            )

        Results.typed MepJson.Default.CapabilityClaimSet (send Method.Describe (Some parameters) cancellation)

    /// Initializes the session: offers only 0.2.0-draft.1, checks the guest's choice and selection, and then sends
    /// morphir.initialized.
    member _.InitializeAsync(cancellation: CancellationToken) : Task<Result<NegotiatedSession, InitializeFailure>> =
        let parameters =
            JsonSerializer.SerializeToElement(
                { ProtocolVersions = Lifecycle.protocolVersions
                  Host = host
                  ModelTransport = offer },
                MepJson.Default.InitializeParams
            )

        task {
            match! send Method.Initialize (Some parameters) cancellation with
            | Error error -> return Error(InitializeFailure.Refused error)
            | Ok element ->
                match Results.decode MepJson.Default.InitializeResult element with
                | None -> return Error InitializeFailure.InvalidResult
                | Some result when result.ProtocolVersion <> Lifecycle.protocolVersion ->
                    return Error(InitializeFailure.UnexpectedVersion result.ProtocolVersion)
                | Some result ->
                    let selection =
                        match offer, result.Capabilities.ModelTransport with
                        | _, None -> Ok None
                        | Some offer, Some selection ->
                            ModelTransport.verifySelection offer selection
                            |> Result.map Some
                            |> Result.mapError string
                        | None, Some _ ->
                            Error
                                $"The extension {result.Extension} selected model transport, which the host did not offer."

                    match selection with
                    | Error reason -> return Error(InitializeFailure.SelectionRefused reason)
                    | Ok transport ->
                        let session =
                            { ProtocolVersion = result.ProtocolVersion
                              Host = host
                              Extension = result.Extension
                              Capabilities =
                                { result.Capabilities with
                                    ModelTransport = transport } }

                        state <- SessionState.Initialized session
                        do! connection.NotifyAsync(Method.Initialized.WireValue, None)
                        return Ok session
        }

    /// Sends morphir.ping.
    member _.PingAsync(cancellation: CancellationToken) : Task<Result<unit, RpcError>> =
        task {
            match! send Method.Ping None cancellation with
            | Ok _ -> return Ok()
            | Error error -> return Error error
        }

    /// Asks for the extension info.
    member _.InfoAsync(cancellation: CancellationToken) =
        Results.typed MepJson.Default.ExtensionInfo (send Method.Info None cancellation)

    /// Asks for the session capabilities.
    member _.CapabilitiesAsync(cancellation: CancellationToken) =
        Results.typed MepJson.Default.ExtensionCapabilities (send Method.Capabilities None cancellation)

    /// Sends an extension method request, such as morphir.validator.validate, with raw params.
    member _.RequestAsync(method: Method, parameters: JsonElement option, cancellation: CancellationToken) =
        send method parameters cancellation

    /// Sends morphir.shutdown. After it, only exit is allowed.
    member _.ShutdownAsync(cancellation: CancellationToken) : Task<Result<unit, RpcError>> =
        task {
            match! send Method.Shutdown None cancellation with
            | Ok _ ->
                match state with
                | SessionState.Initialized session -> state <- SessionState.ShutDown session
                | _ -> ()

                return Ok()
            | Error error -> return Error error
        }

    /// Sends morphir.exit and ends the session.
    member _.ExitAsync() : Task =
        task {
            do! connection.NotifyAsync(Method.Exit.WireValue, None)
            state <- SessionState.Exited
        }
