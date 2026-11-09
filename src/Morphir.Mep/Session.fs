// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Mep

open System
open System.Collections.Generic
open System.Text.Json

/// What both peers agreed at morphir.initialize.
type NegotiatedSession =
    {
        /// The agreed MEP version.
        ProtocolVersion: ProtocolVersion
        /// The host.
        Host: PeerInfo
        /// The extension.
        Extension: ExtensionInfo
        /// The capabilities of the session. modelTransport holds only the agreed tuples and limits.
        Capabilities: ExtensionCapabilities
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"Session {this.ProtocolVersion} of {this.Extension} with {this.Host}"

/// The lifecycle state of a session (design SES-1). Hosts and guests use the same states and rules.
[<RequireQualifiedAccess>]
type SessionState =
    /// Before initialization: only describe and initialize are allowed.
    | Created
    /// After a successful initialize.
    | Initialized of Session: NegotiatedSession
    /// After shutdown: only exit is allowed.
    | ShutDown of Session: NegotiatedSession
    /// After exit.
    | Exited

    /// Stable text for diagnostics.
    override this.ToString() =
        match this with
        | Created -> "Created"
        | Initialized _ -> "Initialized"
        | ShutDown _ -> "Shut down"
        | Exited -> "Exited"

    /// For C# callers: one function for each state.
    member this.Match<'R>
        (
            created: Func<'R>,
            initialized: Func<NegotiatedSession, 'R>,
            shutDown: Func<NegotiatedSession, 'R>,
            exited: Func<'R>
        ) : 'R =
        match this with
        | Created -> created.Invoke()
        | Initialized session -> initialized.Invoke session
        | ShutDown session -> shutDown.Invoke session
        | Exited -> exited.Invoke()

/// The pure lifecycle rules (design SES-1).
[<RequireQualifiedAccess>]
module Lifecycle =
    /// The only MEP version that morphir-dotnet speaks.
    let protocolVersion = ProtocolVersion.Create "0.2.0-draft.1"

    /// The versions that a morphir-dotnet host offers and a morphir-dotnet guest claims.
    let protocolVersions: IReadOnlyList<ProtocolVersion> = [| protocolVersion |]

    /// The ping result of the pinned transcripts.
    let pingResult = """{"ok":true}"""

    /// The shutdown result of the pinned transcripts.
    let shutdownResult = "{}"

    /// Picks the common protocol version by exact match. No common version gives -32011.
    let negotiateVersion (offered: IReadOnlyList<ProtocolVersion>) : Result<ProtocolVersion, RpcError> =
        if offered |> Seq.contains protocolVersion then
            Ok protocolVersion
        else
            let text = offered |> Seq.map _.Value |> String.concat ", "

            // data has the same members as the upstream Rust guest sends.
            let data =
                let buffer = System.Buffers.ArrayBufferWriter<byte>()

                do
                    use writer = new Utf8JsonWriter(buffer)
                    writer.WriteStartObject()
                    writer.WriteStartArray "hostVersions"

                    for version in offered do
                        writer.WriteStringValue version.Value

                    writer.WriteEndArray()
                    writer.WriteStartArray "extensionVersions"

                    for version in protocolVersions do
                        writer.WriteStringValue version.Value

                    writer.WriteEndArray()
                    writer.WriteEndObject()

                use document = JsonDocument.Parse(buffer.WrittenMemory)
                document.RootElement.Clone()

            Error
                { RpcErrors.withMessage
                      ErrorCode.ProtocolVersionMismatch
                      $"No common protocol version: the host offered [{text}] and the extension supports [{protocolVersion}]." with
                    Data = Some data }

    let private hasTuple (capabilities: ExtensionCapabilities) (operation: ModelOperation) =
        capabilities.ModelTransport
        |> Option.exists (fun support -> support.Tuples |> Seq.exists (fun tuple -> tuple.Operation = operation))

    let private hasType (session: NegotiatedSession) (extensionType: ExtensionType) =
        session.Extension.Types |> Seq.contains extensionType

    /// The capability that a method needs, as a sentence for the -32013 message, and whether the session has it.
    let capability (session: NegotiatedSession) (method: Method) : (string * bool) option =
        let capabilities = session.Capabilities

        match method with
        | Method.Compile ->
            Some(
                "frontend compilation",
                capabilities.Frontend |> Option.exists (fun frontend -> frontend.Compile)
            )
        | Method.CompileModel -> Some("frontend model compilation", hasTuple capabilities ModelOperation.CompileModel)
        | Method.Generate ->
            Some("backend generation", capabilities.Backend |> Option.exists (fun backend -> backend.Generate))
        | Method.GenerateModel -> Some("backend model generation", hasTuple capabilities ModelOperation.GenerateModel)
        | Method.Validate -> Some("validation", hasType session ExtensionType.Validator)
        | Method.Transform -> Some("transformation", hasType session ExtensionType.Transform)
        | Method.WorkspaceDiscover ->
            Some(
                "workspace discovery",
                capabilities.Workspace |> Option.exists (fun workspace -> workspace.Discover)
            )
        | Method.CancelRequest -> Some("cancellation", capabilities.Cancellation)
        | Method.Progress -> Some("progress", capabilities.Progress)
        | _ -> None

    let private notInitialized = Admission.Refuse(RpcErrors.create ErrorCode.NotInitialized)

    /// Decides an inbound message for the receiver, or an outbound message for the sender: the same rules apply
    /// to both. A method that MEP does not have is admitted, so that the dispatcher answers -32601.
    let admit (state: SessionState) (wireMethod: string) (_kind: MethodKind) : Admission =
        let method = Method.Find wireMethod

        match state, method with
        | SessionState.Exited, _ -> notInitialized
        | _, Some Method.Exit -> Admission.Proceed
        | SessionState.Created, Some(Method.Describe | Method.Initialize) -> Admission.Proceed
        | SessionState.Created, _
        | SessionState.ShutDown _, _ -> notInitialized
        | SessionState.Initialized _, Some Method.Initialize ->
            // The upstream Rust and MoonBit guests both answer -32600 with this message.
            Admission.Refuse(RpcErrors.withMessage ErrorCode.InvalidRequest "Extension is already initialized")
        | SessionState.Initialized session, Some method ->
            match capability session method with
            | Some(_, true)
            | None -> Admission.Proceed
            | Some(name, false) ->
                Admission.Refuse(
                    RpcErrors.withMessage ErrorCode.CapabilityUnavailable $"The extension did not advertise {name}."
                )
        | SessionState.Initialized _, None -> Admission.Proceed
