// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Compilation.Mep

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Morphir.Compilation
open Morphir.IR
open Morphir.Mep

type private CompileDiagnostic = Morphir.Compilation.Diagnostic
type private MepDiagnostic = Morphir.Mep.Diagnostic
type private CompileCapability = Morphir.Compilation.FrontendCapability
type private CompileLanguage = Morphir.Compilation.LanguageCapability

/// <summary>The diagnostic codes of the MEP frontend adapter. Each failure names the extension.</summary>
[<RequireQualifiedAccess>]
module MepFrontendCodes =
    /// <summary>The extension could not start, or it did not initialize.</summary>
    [<CompiledName("Unavailable")>]
    let unavailable = DiagnosticCode.Create "extension.unavailable"

    /// <summary>The extension sent output that is not a valid frame, message or result, or it returned an error.</summary>
    [<CompiledName("ProtocolError")>]
    let protocolError = DiagnosticCode.Create "extension.protocol_error"

    /// <summary>The session does not admit the language, the number of documents or the IR version.</summary>
    [<CompiledName("NotAdmitted")>]
    let notAdmitted = DiagnosticCode.Create "extension.not_admitted"

    /// <summary>The session agreed no compileModel tuple, so the host sends no request.</summary>
    [<CompiledName("TupleUnavailable")>]
    let tupleUnavailable = DiagnosticCode.Create "extension.tuple_unavailable"

    /// <summary>The extension did not answer before the deadline.</summary>
    [<CompiledName("Timeout")>]
    let timeout = DiagnosticCode.Create "extension.timeout"

/// <summary>How the host starts a frontend extension.</summary>
type MepFrontendOptions =
    {
        /// <summary>The extension executable. It starts with explicit arguments and no shell.</summary>
        Executable: string
        /// <summary>The arguments, one item for each argument.</summary>
        Arguments: IReadOnlyList<string>
        /// <summary>The working directory of the extension, or null for the current directory.</summary>
        WorkingDirectory: string | null
        /// <summary>The deadline for each request, and for the end of the session.</summary>
        Deadline: TimeSpan
        /// <summary>Where the extension's stderr goes, line by line.</summary>
        Stderr: TextWriter
        /// <summary>The host identity that the session sends.</summary>
        Host: PeerInfo
    }

    override this.ToString() = $"Extension {this.Executable}"

    /// <summary>
    /// Options for C# callers: the executable and its arguments, the working directory (or null), where stderr goes,
    /// and the host name and version that the session sends. The deadline is two minutes.
    /// </summary>
    static member Create
        (
            executable: string,
            arguments: IEnumerable<string>,
            workingDirectory: string | null,
            stderr: TextWriter,
            hostName: string,
            hostVersion: string
        ) : MepFrontendOptions =
        { Executable = executable
          Arguments = Array.ofSeq arguments
          WorkingDirectory = workingDirectory
          Deadline = TimeSpan.FromMinutes 2.0
          Stderr = stderr
          Host =
            { Kind = PeerKind.Cli
              Name = hostName
              Version = hostVersion } }

/// <summary>
/// A frontend over an owned MEP 0.2.0-draft.1 guest session. <c>ConnectAsync</c> starts a process and initializes
/// the session; the capability then comes from the agreed session, and each compile checks it before dispatch.
/// Classic versions 1 and 2 have no tuple: the adapter compiles at 3.0.0 and returns the typed classic model, which
/// the host writes at the requested version (MEP-3). Dispose the frontend to end the session.
/// </summary>
[<Sealed>]
type MepFrontend
    private
    (
        guest: IMepGuestSession,
        identity: ComponentId,
        capability: CompileCapability,
        transport: FrontendTransport
    ) =

    static let jsonTuple (version: string) : ModelTransportTuple =
        { Operation = ModelOperation.CompileModel
          Profile = "morphir-ir"
          Codec = ModelCodec.MorphirJson
          EnvelopeVersion = SemanticVersion.Create "0.1.0-draft.1"
          IrVersion = SemanticVersion.Create version }

    /// The host offer: JSON compileModel tuples, 4.0.0 first, and bounds that fit in one 64 MiB frame.
    static let offer: ModelTransportSupport =
        { Tuples = [| jsonTuple "4.0.0"; jsonTuple "3.0.0" |]
          Limits =
            { MaxEncodedBytes = 60 * 1024 * 1024
              MaxDecodedBytes = 45 * 1024 * 1024
              MaxDepth = 4096
              MaxNodes = 50_000_000 }
          Guarantees = [||] }

    /// The diagnostic code of a guest failure.
    static let codeOf (failed: MepGuestFailure) =
        match failed with
        | MepGuestFailure.Unavailable _ -> MepFrontendCodes.unavailable
        | MepGuestFailure.ProtocolError _ -> MepFrontendCodes.protocolError
        | MepGuestFailure.Timeout _ -> MepFrontendCodes.timeout

    static let failure (code: DiagnosticCode) (message: string) : FrontendFailure =
        { Message = message
          Diagnostics = [| Diagnostic.error code message |] }

    /// A MEP position is unsigned; above Int32.MaxValue it is kept at Int32.MaxValue, not wrapped to a negative.
    static let clamp (value: uint32) =
        if value > uint32 Int32.MaxValue then Int32.MaxValue else int value

    static let mepPosition (position: SourcePosition) =
        Position.Create(clamp position.Line, clamp position.Character)

    static let mepLocation (location: SourceLocation) =
        Location.Create(location.Uri, Range.Create(mepPosition location.Range.Start, mepPosition location.Range.End))

    /// A MEP diagnostic in the compile shape. A code that is not a valid diagnostic code is left out.
    static let toDiagnostic (diagnostic: MepDiagnostic) : CompileDiagnostic =
        let severity =
            match diagnostic.Severity with
            | Morphir.Mep.DiagnosticSeverity.Error -> Morphir.Compilation.DiagnosticSeverity.Error
            | Morphir.Mep.DiagnosticSeverity.Warning -> Morphir.Compilation.DiagnosticSeverity.Warning
            | Morphir.Mep.DiagnosticSeverity.Info -> Morphir.Compilation.DiagnosticSeverity.Info
            | Morphir.Mep.DiagnosticSeverity.Hint -> Morphir.Compilation.DiagnosticSeverity.Hint

        let code =
            diagnostic.Code
            |> Option.bind (fun text -> DiagnosticCode.Parse text |> Result.toOption)
            |> Option.toObj

        let location = diagnostic.Location |> Option.map mepLocation |> Option.toObj
        let created = CompileDiagnostic.Create(severity, code, diagnostic.Message, location)

        let related =
            diagnostic.Related
            |> Seq.fold
                (fun (current: CompileDiagnostic) item ->
                    current.WithRelated
                        { Location = mepLocation item.Location
                          Message = item.Message })
                created

        match diagnostic.Data with
        | Some data -> related.WithData data
        | None -> related

    /// The targets that the agreed tuples serve: 4.0.0 gives IR v4, and 3.0.0 gives classic versions 1, 2 and 3.
    static let outputsOf (agreed: ModelTransportSupport option) =
        let versions =
            agreed
            |> Option.map (fun support ->
                support.Tuples
                |> Seq.filter (fun tuple -> tuple = jsonTuple tuple.IrVersion.Value)
                |> Seq.map (fun tuple -> tuple.IrVersion.Value)
                |> Set.ofSeq)
            |> Option.defaultValue Set.empty

        [| if versions.Contains "4.0.0" then
               yield
                   { Family = Model.ModelFamily.V4
                     Version = Versioning.Classic 4 }
           if versions.Contains "3.0.0" then
               for version in [ 1; 2; 3 ] do
                   yield
                       { Family = Model.ModelFamily.Classic
                         Version = Versioning.Classic version } |]

    let negotiated = guest.Session
    let name = negotiated.Extension.Id

    let agreed = negotiated.Capabilities.ModelTransport

    /// The tuple for a target: 4.0.0 for v4, and 3.0.0 for every classic version.
    let tupleFor (target: IrTarget) =
        let version =
            // Exhaustive, so a new model family fails to compile until its tuple is decided.
            match target.Family with
            | Model.ModelFamily.V4 -> "4.0.0"
            | Model.ModelFamily.Classic -> "3.0.0"

        agreed
        |> Option.bind (fun support -> support.Tuples |> Seq.tryFind (fun tuple -> tuple = jsonTuple version))

    let refuseWith code (message: string) =
        Task.FromResult(FrontendOutcome.Failed(failure code $"Extension {name}: {message}"))

    let refuse = refuseWith MepFrontendCodes.notAdmitted

    let read (tuple: ModelTransportTuple) (bytes: ReadOnlyMemory<byte>) : Result<FamilyModel, string> =
        // The envelope was admitted against the agreed depth limit, so the reader uses the same limit, not the
        // JsonDocument default of 64.
        let limits = agreed |> Option.map _.Limits |> Option.defaultValue offer.Limits
        use document = JsonDocument.Parse(bytes, JsonDocumentOptions(MaxDepth = limits.MaxDepth))

        if tuple.IrVersion.Value = "4.0.0" then
            Morphir.Json.V4.V4Json.readDistribution document.RootElement
            |> Result.map (fun read -> FamilyModel.V4 read.Distribution)
            |> Result.mapError string
        else
            Morphir.Json.Classic.ClassicJson.readTypedDistribution document.RootElement
            |> Result.map (fun read -> FamilyModel.Classic read.Distribution)
            |> Result.mapError string

    let protocol (message: string) =
        FrontendOutcome.Failed(failure MepFrontendCodes.protocolError $"Extension {name}: {message}")

    /// Sends one request and waits for its reply, the deadline or the end of the connection.
    let send (method: Method) (parameters: JsonElement) (cancellation: CancellationToken) =
        task {
            match! guest.SendAsync(method, parameters, cancellation) with
            | MepGuestReply.Result result -> return Ok(Ok result)
            | MepGuestReply.Error error -> return Ok(Error error)
            | MepGuestReply.Failed failed -> return Error(FrontendOutcome.Failed(failure (codeOf failed) failed.Message))
        }

    /// <summary>The extension id as a component identity.</summary>
    member _.Identity = identity

    /// <summary>The agreed session.</summary>
    member _.Session = negotiated

    /// <summary>True when the owned extension session has ended.</summary>
    member _.HasExited = guest.IsClosed

    /// <summary>Starts the extension, initializes a session and returns the frontend, or an operational failure.</summary>
    static member ConnectAsync(options: MepFrontendOptions, cancellation: CancellationToken) : Task<Result<MepFrontend, FrontendFailure>> =
        MepFrontend.ConnectAsync(options, null, cancellation)

    /// <summary>
    /// Starts the extension with the given whole environment (null for the host's: then it is the overload without
    /// an environment), initializes a session and returns the frontend, or an operational failure.
    /// </summary>
    static member ConnectAsync
        (
            options: MepFrontendOptions,
            environment: IReadOnlyDictionary<string, string> | null,
            cancellation: CancellationToken
        ) : Task<Result<MepFrontend, FrontendFailure>> =
        task {
            let guestOptions: MepGuestOptions =
                { Executable = options.Executable
                  Arguments = options.Arguments
                  WorkingDirectory = options.WorkingDirectory
                  Deadline = options.Deadline
                  Stderr = options.Stderr
                  Host = options.Host
                  Environment = environment }

            let! started = MepGuestProcess.StartAsync(guestOptions, offer, cancellation)
            return! MepFrontend.FromSessionAsync(options, FrontendTransport.Mep, Result.map (fun guest -> guest :> IMepGuestSession) started)
        }

    static member private FromSessionAsync(options: MepFrontendOptions, transport: FrontendTransport, started: Result<IMepGuestSession, MepGuestFailure>) =
        task {
            match started with
            | Error failed -> return Error(failure (codeOf failed) failed.Message)
            | Ok guest when ReservedExtensionIds.IsReserved guest.Session.Extension.Id ->
                // A guest must not pass as a built-in frontend: every morphir. id is reserved for built-ins.
                do! (guest :> IAsyncDisposable).DisposeAsync()

                return
                    Error(
                        failure
                            MepFrontendCodes.unavailable
                            $"Extension {options.Executable} reported the extension id {guest.Session.Extension.Id}, which is reserved for built-in frontends and backends (every id that starts with {ReservedExtensionIds.Prefix}). An extension must have its own id."
                    )
            | Ok guest ->
                let negotiated = guest.Session

                let identity =
                    match ComponentId.Parse negotiated.Extension.Id with
                    | Ok id -> id
                    | Error _ -> ComponentId.Create "mep-extension"

                let languages =
                    negotiated.Capabilities.Frontend
                    |> Option.map (fun frontend ->
                        [| for language in frontend.Languages do
                               match LanguageId.Parse language.Id with
                               | Ok id ->
                                   yield
                                       ({ Id = id
                                          FileExtensions = language.FileExtensions }
                                       : CompileLanguage)
                               | Error _ -> () |])
                    |> Option.defaultValue [||]

                let capability: CompileCapability =
                    { Languages = languages
                      Outputs = outputsOf negotiated.Capabilities.ModelTransport
                      Incremental = false
                      MultiDocument =
                        negotiated.Capabilities.Frontend
                        |> Option.map _.MultiDocument
                        |> Option.defaultValue false }

                return Ok(new MepFrontend(guest, identity, capability, transport))
        }

    /// <summary>Initializes an owned WASM channel and reuses the ordinary frontend model validation.</summary>
    static member ConnectChannelCheckedAsync(options: MepFrontendOptions, channel: ISingleMessageChannel, cancellation: CancellationToken) : Task<Checked<IFrontend>> =
        task {
            let! started = MepChannelGuest.StartCheckedAsync(channel, options.Host, offer, options.Deadline, cancellation)
            let result = match started with MepChannelStart.Started guest -> Ok guest | MepChannelStart.Failed error -> Error error
            match! MepFrontend.FromSessionAsync(options, FrontendTransport.Wasm, result) with
            | Ok frontend -> return Checked.pass (frontend :> IFrontend)
            | Error failure -> return Checked.fail (List.ofSeq failure.Diagnostics)
        }
    /// <summary>
    /// Starts the extension and initializes a session, for C# callers: a passed result holds the frontend, and a
    /// failed result holds the diagnostics of the failure. Dispose the frontend to end the session.
    /// </summary>
    static member ConnectCheckedAsync(options: MepFrontendOptions, cancellation: CancellationToken) : Task<Checked<IFrontend>> =
        task {
            match! MepFrontend.ConnectAsync(options, cancellation) with
            | Ok frontend -> return Checked.pass (frontend :> IFrontend)
            | Error failure -> return Checked.fail (List.ofSeq failure.Diagnostics)
        }

    interface IFrontend with
        member _.Identity = identity
        member _.Transport = transport
        member _.Capability = capability

        member _.CompileAsync(request, cancellation) =
            let language = request.LanguageId.Value

            if not (capability.Languages |> Seq.exists (fun item -> item.Id = request.LanguageId)) then
                refuse $"the session does not admit the language {language}."
            elif request.Documents.Count > 1 && not capability.MultiDocument then
                refuse "the session does not admit more than one document in a request (multiDocument is false)."
            elif (ModelTransport.agreedTuples agreed ModelOperation.CompileModel).Count = 0 then
                refuseWith MepFrontendCodes.tupleUnavailable "the session agreed no compileModel tuple, so the host sends no request."
            elif not (capability.Outputs |> Seq.exists (fun output -> output.Family = request.Output.Family && FormatVersion.sameVersion output.Version request.Output.Version)) then
                let tuples =
                    agreed
                    |> Option.map (fun support -> support.Tuples |> Seq.map string |> String.concat "; ")
                    |> Option.defaultValue "none"

                refuse $"the session does not admit IR {request.Output.Version |> Versioning.version}. Agreed compileModel tuples: {tuples}."
            else
                match tupleFor request.Output with
                | None -> refuse "the session agreed no compileModel tuple for the requested IR version."
                | Some tuple ->
                    let (PackageName path) = request.Package

                    let mepRequest: CompileModelRequest =
                        { LanguageId = language
                          Sources =
                            { Root = request.Root
                              Documents =
                                [| for document in request.Documents ->
                                       { Uri = document.Uri
                                         LanguageId = document.LanguageId.Value
                                         Version = document.Version
                                         Text = document.Text } |] }
                          Package =
                            { Name = CanonicalNames.pathToString path
                              ExposedModules =
                                request.ExposedModules
                                |> Option.map (fun modules -> Array.ofList modules :> IReadOnlyList<string>) }
                          Dependencies = [||]
                          Options =
                            { TypesOnly = false
                              IrVersion = tuple.IrVersion
                              Extra = Dictionary() }
                          ContextDigest = Sha256Digest.Create(String('0', 64))
                          Output = tuple }

                    let mepRequest =
                        { mepRequest with
                            ContextDigest = CompileModel.contextDigest mepRequest }

                    let parameters = JsonSerializer.SerializeToElement(mepRequest, MepJson.Default.CompileModelRequest)

                    task {
                        match! send Method.CompileModel parameters cancellation with
                        | Error outcome -> return outcome
                        | Ok(Error error) -> return protocol $"compileModel failed with {error.Code}: {error.Message}"
                        | Ok(Ok element) ->
                            match CompileModel.readResult element with
                            | Error refused -> return protocol $"the compileModel result is refused: {refused}"
                            | Ok(CompileModelResult.Failed diagnostics) ->
                                let converted = [ for diagnostic in diagnostics -> toDiagnostic diagnostic ]

                                let converted =
                                    if converted |> List.exists _.IsError then
                                        converted
                                    else
                                        converted
                                        @ [ Diagnostic.error
                                                MepFrontendCodes.protocolError
                                                $"Extension {name} reported a failed compilation without an error diagnostic." ]

                                return FrontendOutcome.Completed(Checked.fail converted)
                            | Ok(CompileModelResult.Compiled(model, diagnostics, modules)) ->
                                let limits = agreed |> Option.map _.Limits |> Option.defaultValue offer.Limits

                                match ModelTransport.admit tuple limits model with
                                | Error refused -> return protocol $"the model envelope is refused ({refused.Code}): {refused}"
                                | Ok admitted ->
                                    match read tuple admitted.Bytes with
                                    | Error message -> return protocol $"the model does not read as IR {tuple.IrVersion}: {message}"
                                    | Ok family ->
                                        let compiled = { Model = family; Modules = modules }
                                        let converted = [ for diagnostic in diagnostics -> toDiagnostic diagnostic ]
                                        return FrontendOutcome.Completed(Checked.Of(compiled, converted))
                    }

    interface IAsyncDisposable with
        member _.DisposeAsync() = (guest :> IAsyncDisposable).DisposeAsync()
