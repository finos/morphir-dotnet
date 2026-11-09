// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Frontend.FSharp.Extension

open System
open System.Collections.Generic
open System.Reflection
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Morphir.Compilation
open Morphir.Frontend.FSharp
open Morphir.IR
open Morphir.Mep

type private CompileDiagnostic = Morphir.Compilation.Diagnostic
type private MepDiagnostic = Morphir.Mep.Diagnostic

/// The F# frontend as a MEP 0.2.0-draft.1 guest. It serves morphir.frontend.compileModel with JSON model tuples for
/// IR versions 3.0.0 and 4.0.0, through the in-process frontend.
[<RequireQualifiedAccess>]
module FSharpGuest =

    /// The exact IR versions that the guest claims. Classic versions 1 and 2 have no tuple: a host compiles them at
    /// 3.0.0 and writes the classic model again (MEP-3).
    let irVersions = [ "4.0.0"; "3.0.0" ]

    /// A JSON compileModel tuple for an IR version.
    let tuple (irVersion: string) : ModelTransportTuple =
        { Operation = ModelOperation.CompileModel
          Profile = "morphir-ir"
          Codec = ModelCodec.MorphirJson
          EnvelopeVersion = SemanticVersion.Create "0.1.0-draft.1"
          IrVersion = SemanticVersion.Create irVersion }

    /// The guest bounds. A model and its base64 payload must fit in one 64 MiB frame.
    let limits: ModelTransportLimits =
        { MaxEncodedBytes = 48 * 1024 * 1024
          MaxDecodedBytes = 36 * 1024 * 1024
          MaxDepth = 1024
          MaxNodes = 20_000_000 }

    /// The model-transport support of the guest, before negotiation.
    let support: ModelTransportSupport =
        { Tuples = [| for version in irVersions -> tuple version |]
          Limits = limits
          Guarantees = [||] }

    let private version =
        let assembly = typeof<ModelTransportTuple>.Assembly.GetName()
        let own = Assembly.GetExecutingAssembly()

        match own.GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
        | null -> string (own.GetName().Version)
        | attribute -> attribute.InformationalVersion.Split('+')[0]
        |> fun text -> if String.IsNullOrEmpty text then string assembly.Version else text

    /// Who the guest is.
    let extension: ExtensionInfo =
        { Id = "org.finos.morphir.fsharp-frontend"
          Name = "Morphir F# frontend"
          Version = version
          Description = Some "Compiles a subset of F# into a Morphir IR model."
          Types = [| ExtensionType.Frontend |]
          Author = Some "FINOS Morphir"
          Homepage = Some "https://github.com/finos/morphir-dotnet"
          License = Some "Apache-2.0"
          MinSdkVersion = None }

    /// The frontend capability: F# documents with the extension .fs, several documents in one request.
    let frontend: FrontendCapability =
        { Languages =
            [| { Id = "fsharp"
                 FileExtensions = [| ".fs" |] } |]
          IrVersions = Array.ofList irVersions
          Compile = false
          Incremental = false
          Fragments = false
          MultiDocument = true }

    /// The capabilities of every session.
    let capabilities: ExtensionCapabilities =
        { Frontend = Some frontend
          Backend = None
          Workspace = None
          ModelTransport = Some support
          Streaming = false
          Incremental = false
          Cancellation = false
          Progress = false
          Extra = Dictionary() }

    /// The claim set that morphir.extension.describe returns.
    let claims: CapabilityClaimSet =
        { ClaimsVersion = SemanticVersion.Create "0.1.0-draft.2"
          ProtocolVersions = Lifecycle.protocolVersions
          Extension = extension
          Capabilities =
            readOnlyDict
                [ "frontend", JsonSerializer.SerializeToElement(frontend, MepJson.Default.FrontendCapability)
                  "modelTransport", JsonSerializer.SerializeToElement(support, MepJson.Default.ModelTransportSupport) ]
          Requires = None
          Critical = [||] }

    let private severity (value: Morphir.Compilation.DiagnosticSeverity) =
        match value with
        | Morphir.Compilation.DiagnosticSeverity.Error -> Morphir.Mep.DiagnosticSeverity.Error
        | Morphir.Compilation.DiagnosticSeverity.Warning -> Morphir.Mep.DiagnosticSeverity.Warning
        | Morphir.Compilation.DiagnosticSeverity.Info -> Morphir.Mep.DiagnosticSeverity.Info
        | Morphir.Compilation.DiagnosticSeverity.Hint -> Morphir.Mep.DiagnosticSeverity.Hint

    let private location (value: Morphir.Compilation.Location) : SourceLocation =
        let position (point: Position) : SourcePosition =
            { Line = uint32 point.Line
              Character = uint32 point.Character }

        { Uri = value.Uri
          Range =
            { Start = position value.Range.Start
              End = position value.Range.End } }

    /// A frontend diagnostic in the MEP shape. Severity, code, message, location, related locations and data stay.
    let diagnostic (value: CompileDiagnostic) : MepDiagnostic =
        { Severity = severity value.Severity
          Code =
            match value.CodeOrNull with
            | null -> None
            | code -> Some code.Value
          Message = value.Message
          Location =
            match value.LocationOrNull with
            | null -> None
            | found -> Some(location found)
          Related =
            [| for related in value.Related ->
                   { Location = location related.Location
                     Message = related.Message } |]
          Data = Option.ofNullable value.Data }

    let private invalidParams (message: string) =
        RpcErrors.withMessage ErrorCode.InvalidParams message

    /// The deterministic revision of a model: the SHA-256 of the context digest, the frontend identity and the
    /// guest version. It is not the payload digest (MEP-2).
    let revision (contextDigest: Sha256Digest) =
        let text = $"{contextDigest.Value}\n{FSharpFrontend.identity.Value}\n{version}"
        (Sha256Digest.Compute(Encoding.UTF8.GetBytes text)).Value

    /// The frontend request for a compileModel request: the declared target of the frontend whose version is the
    /// output tuple's IR version.
    let private toRequest (frontend: IFrontend) (request: CompileModelRequest) : Result<CompileRequest, RpcError> =
        let requested = Versioning.parse request.Output.IrVersion.Value

        let target =
            match requested with
            | Ok version ->
                frontend.Capability.Outputs
                |> Seq.tryFind (fun output -> FormatVersion.sameVersion output.Version version)
            | Error _ -> None

        match LanguageId.Parse request.LanguageId, CanonicalNames.parsePath request.Package.Name, target with
        | Error failure, _, _ -> Error(invalidParams $"The language id is not valid: {failure}")
        | _, Error failure, _ -> Error(invalidParams $"The package name is not valid: {failure}")
        | _, _, None -> Error(invalidParams $"The F# frontend does not produce IR {request.Output.IrVersion}.")
        | Ok language, Ok path, Some target ->
            let documents =
                [ for document in request.Sources.Documents ->
                      match LanguageId.Parse document.LanguageId with
                      | Ok documentLanguage ->
                          Ok(
                              { Uri = document.Uri
                                LanguageId = documentLanguage
                                Version = document.Version
                                Text = document.Text }
                              : Morphir.Compilation.SourceDocument
                          )
                      | Error failure -> Error(invalidParams $"The language id of {document.Uri} is not valid: {failure}") ]

            match documents |> List.tryPick (function Error error -> Some error | Ok _ -> None) with
            | Some error -> Error error
            | None ->
                CompileRequest.Parse(
                    language,
                    Option.toObj request.Sources.Root,
                    [ for document in documents -> Result.defaultWith (fun _ -> failwith "unreachable") document ],
                    PackageName path,
                    (match request.Package.ExposedModules with
                     | Some modules -> modules :> IEnumerable<string> | null
                     | None -> null),
                    target,
                    request.ContextDigest.Value
                )
                |> Result.mapError (fun failure -> invalidParams (failure.ToString()))

    /// Serves one compileModel request through the in-process frontend. A source problem is a failed result with
    /// diagnostics. An invalid request is -32602, and an operational frontend failure is -32603.
    let compile
        (frontend: IFrontend)
        (writers: WriterRegistry)
        (agreed: ModelTransportSupport option)
        (request: CompileModelRequest)
        (cancellation: CancellationToken)
        : Task<Result<CompileModelResult, RpcError>> =
        task {
            match CompileModel.checkRequest agreed request with
            | Error failure -> return Error(invalidParams (failure.ToString()))
            | Ok request when request.Options.TypesOnly ->
                // A follow-up change adds typesOnly; until then the guest refuses it, and does not compile a full model.
                return Error(invalidParams "The F# frontend does not support typesOnly compilation yet.")
            | Ok request ->
                match toRequest frontend request with
                | Error error -> return Error error
                | Ok compileRequest ->
                    let! outcome = frontend.CompileAsync(compileRequest, cancellation)

                    match outcome with
                    | FrontendOutcome.Failed failure ->
                        return Error(RpcErrors.withMessage ErrorCode.InternalError failure.Message)
                    | FrontendOutcome.Completed result ->
                        let written =
                            result
                            |> Checked.bind (fun compiled ->
                                writers.Resolve compileRequest.Output.Version
                                |> Checked.bind (fun writer ->
                                    writer.Writer.Write(writer.Target, WriteOptions.Compact, compiled.Model))
                                |> Checked.map (fun bytes -> bytes, compiled.Modules))

                        let diagnostics: IReadOnlyList<MepDiagnostic> =
                            [| for value in written.Diagnostics -> diagnostic value |]

                        match Checked.tryValue written with
                        | Some(bytes, modules) ->
                            match
                                ModelTransport.createEnvelope
                                    request.Output
                                    request.Package.Name
                                    (revision request.ContextDigest)
                                    (ReadOnlyMemory bytes)
                            with
                            | Ok model ->
                                // The model must fit the agreed limits, or the host must refuse it.
                                let limits = agreed |> Option.map _.Limits |> Option.defaultValue support.Limits

                                match ModelTransport.admit request.Output limits model with
                                | Ok _ -> return Ok(CompileModelResult.Compiled(model, diagnostics, modules))
                                | Error refused ->
                                    return
                                        Error(
                                            RpcErrors.withMessage
                                                ErrorCode.InternalError
                                                $"The compiled model does not fit the agreed limit ({refused.Code}): {refused}"
                                        )
                            | Error failure ->
                                return Error(RpcErrors.withMessage ErrorCode.InternalError (failure.ToString()))
                        | None -> return Ok(CompileModelResult.Failed diagnostics)
        }

    /// A guest over a frontend and its writers. The compileModel handler reads the agreed tuples of the session.
    let create (frontend: IFrontend) (writers: WriterRegistry) : Guest =
        let mutable guest: Guest option = None

        let agreed () =
            match guest with
            | Some current ->
                match current.State with
                | SessionState.Initialized session -> session.Capabilities.ModelTransport
                | _ -> None
            | None -> None

        let handler =
            Handlers.raw (fun parameters cancellation ->
                match parameters with
                | None -> Task.FromResult(Error(RpcErrors.create ErrorCode.InvalidParams))
                | Some element ->
                    let request =
                        try
                            match JsonSerializer.Deserialize(element, MepJson.Default.CompileModelRequest) with
                            | null -> None
                            | value -> Some value
                        with :? JsonException ->
                            None

                    match request with
                    | None -> Task.FromResult(Error(RpcErrors.create ErrorCode.InvalidParams))
                    | Some request ->
                        task {
                            let! result = compile frontend writers (agreed ()) request cancellation
                            return result |> Result.map CompileModel.writeResult
                        })

        let created =
            Guest(
                { Claims = claims
                  Capabilities = capabilities
                  Methods = readOnlyDict [ Method.CompileModel.WireValue, handler ] }
            )

        guest <- Some created
        created

    /// The writers of the guest: classic and v4.
    let writers = WriterRegistry.Create [ ClassicIrWriter() :> IIrWriter; V4IrWriter() ]

    /// The guest over the in-process F# frontend.
    let registration () = create FSharpFrontend.registration writers
