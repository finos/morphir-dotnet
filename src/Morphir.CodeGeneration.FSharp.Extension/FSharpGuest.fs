// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.CodeGeneration.FSharp.Extension

open System
open System.Buffers
open System.Collections.Generic
open System.Reflection
open System.Text.Json
open System.Threading.Tasks
open Morphir.CodeGeneration.FSharp
open Morphir.IR.Model
open Morphir.Mep

type private Pipeline = Morphir.IR.Pipeline.MessageSeverity
type private VMessage = Morphir.IR.Pipeline.VMessage
type private VFileTree = Morphir.IR.Pipeline.VFileTree

/// The F# generator as a MEP 0.2.0-draft.1 backend guest, independent of the host's built-in morphir.fsharp. It
/// serves morphir.backend.generateModel for the target fsharp, with JSON model tuples for IR versions 4.0.0 and
/// 3.0.0, through the same generator (Transpiler.generateModelWith) as the built-in.
[<RequireQualifiedAccess>]
module FSharpGuest =

    /// The target id that the guest generates.
    [<Literal>]
    let Target = "fsharp"

    /// The exact IR versions that the guest claims. 4.0.0 reads IR v4 JSON (formatVersion 4 or "4.0.0"); 3.0.0
    /// reads classic JSON at formatVersion 3. Classic versions 1 and 2 have no tuple: a host converts them to 3.0.0
    /// first (MEP-3).
    let irVersions = [ "4.0.0"; "3.0.0" ]

    /// A JSON generateModel tuple for an IR version.
    let tuple (irVersion: string) : ModelTransportTuple =
        { Operation = ModelOperation.GenerateModel
          Profile = "morphir-ir"
          Codec = ModelCodec.MorphirJson
          EnvelopeVersion = SemanticVersion.Create "0.1.0-draft.1"
          IrVersion = SemanticVersion.Create irVersion }

    /// The guest bounds, the same as the F# frontend guest: a model and its base64 payload fit in one 64 MiB frame.
    let limits: ModelTransportLimits =
        { MaxEncodedBytes = 48 * 1024 * 1024
          MaxDecodedBytes = 36 * 1024 * 1024
          MaxDepth = 1024
          MaxNodes = 20_000_000 }

    /// The model-transport support of the guest, before negotiation. It claims no guarantee.
    let support: ModelTransportSupport =
        { Tuples = [| for version in irVersions -> tuple version |]
          Limits = limits
          Guarantees = [||] }

    /// The guest's own release version, from its assembly. It is not the host's version.
    let private version =
        let own = Assembly.GetExecutingAssembly()

        match own.GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
        | null -> string (own.GetName().Version)
        | attribute -> attribute.InformationalVersion.Split('+')[0]

    /// Who the guest is. The id is not the id of the built-in backend (morphir.fsharp), so a host can register both.
    let extension: ExtensionInfo =
        { Id = "org.finos.morphir.fsharp-backend"
          Name = "Morphir F# backend"
          Version = version
          Description = Some "Generates F# source from a Morphir library model."
          Types = [| ExtensionType.Backend |]
          Author = Some "FINOS Morphir"
          Homepage = Some "https://github.com/finos/morphir-dotnet"
          License = Some "Apache-2.0"
          MinSdkVersion = None }

    /// The backend capability: the target fsharp for the claimed IR versions. The legacy morphir.backend.generate
    /// is not served.
    let backend: BackendCapability =
        { Targets = [| Target |]
          IrVersions = Array.ofList irVersions
          Generate = false }

    /// The capabilities of every session.
    let capabilities: ExtensionCapabilities =
        { Frontend = None
          Backend = Some backend
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
                [ "backend", JsonSerializer.SerializeToElement(backend, MepJson.Default.BackendCapability)
                  "modelTransport", JsonSerializer.SerializeToElement(support, MepJson.Default.ModelTransportSupport) ]
          Requires = None
          Critical = [||] }

    let private invalidParams (message: string) =
        RpcErrors.withMessage ErrorCode.InvalidParams message

    /// The guest trusts no evidence. It claims no guarantee, so a required guarantee is refused before trust applies.
    let private trustNone = Func<ModelGuaranteeEvidence, bool>(fun _ -> false)

    /// The generation options, with the rule of the built-in backend: only the Boolean option optimize, which is
    /// true when absent.
    let options (values: IReadOnlyDictionary<string, JsonElement>) : Result<GenerationOptions, RpcError> =
        values
        |> Seq.fold
            (fun (result: Result<GenerationOptions, RpcError>) (KeyValue(key, value)) ->
                result
                |> Result.bind (fun current ->
                    match key, value.ValueKind with
                    | "optimize", (JsonValueKind.True | JsonValueKind.False) -> Ok { current with Optimize = value.GetBoolean() }
                    | _ -> Error(invalidParams $"The F# backend accepts only the Boolean option 'optimize'; received '{key}'.")))
            (Ok GenerationOptions.Default)

    /// The declared formatVersion text of a JSON model: a string, or the raw text of a number.
    let private declaredVersion (root: JsonElement) =
        if root.ValueKind <> JsonValueKind.Object then
            None
        else
            let declared =
                root.EnumerateObject() |> Seq.filter (fun property -> property.NameEquals "formatVersion") |> List.ofSeq

            match declared with
            | [ property ] when property.Value.ValueKind = JsonValueKind.String -> property.Value.GetString() |> Option.ofObj
            | [ property ] when property.Value.ValueKind = JsonValueKind.Number -> Some(property.Value.GetRawText())
            | _ -> None

    /// Reads the admitted payload as the model of its input tuple. The declared formatVersion must be the tuple's IR
    /// version: 4 or 4.0.0 for 4.0.0, and 3 or 3.0.0 for 3.0.0.
    let readModel (admitted: AdmittedModel) : Result<VersionedModel, RpcError> =
        let irVersion = admitted.Tuple.IrVersion.Value

        try
            use document =
                JsonDocument.Parse(admitted.Bytes, JsonDocumentOptions(MaxDepth = limits.MaxDepth))

            let root = document.RootElement

            let accepted =
                match irVersion with
                | "4.0.0" -> [ "4"; "4.0.0" ]
                | "3.0.0" -> [ "3"; "3.0.0" ]
                | _ -> []

            match declaredVersion root with
            | Some declared when List.contains declared accepted ->
                let source = SourceFormatVersion.Create declared

                if irVersion = "4.0.0" then
                    Morphir.Json.V4.V4Json.readDistribution root
                    |> Result.map (fun read -> { SourceVersion = source; Distribution = DistributionModel.V4 read.Distribution })
                    |> Result.mapError (fun failure -> invalidParams $"The model is not IR 4.0.0 ({failure.Rule} at {failure.Location}): {failure.Message}")
                else
                    Morphir.Json.Classic.ClassicJson.readDistribution root
                    |> Result.map (fun read -> { SourceVersion = source; Distribution = DistributionModel.Classic read.Distribution })
                    |> Result.mapError (fun failure -> invalidParams $"The model is not IR 3.0.0 ({failure.Rule} at {failure.Location}): {failure.Message}")
            | Some declared -> Error(invalidParams $"The model declares formatVersion {declared}, which is not the input tuple's IR version {irVersion}.")
            | None -> Error(invalidParams $"The model has no single formatVersion for the input tuple's IR version {irVersion}.")
        with :? JsonException as failure ->
            Error(invalidParams $"The model is not JSON for IR {irVersion}: {failure.Message}")

    let private severity (value: Pipeline) =
        match value with
        | Pipeline.Fatal
        | Pipeline.Error -> DiagnosticSeverity.Error
        | Pipeline.Warning -> DiagnosticSeverity.Warning
        | Pipeline.Info -> DiagnosticSeverity.Info

    /// The structured data of a generator message: its logical location and source, when it has them.
    let private dataOf (message: VMessage) : JsonElement option =
        match message.Location, message.Source with
        | None, None -> None
        | location, source ->
            let buffer = ArrayBufferWriter<byte>()

            do
                use writer = new Utf8JsonWriter(buffer)
                writer.WriteStartObject()
                location |> Option.iter (fun text -> writer.WriteString("location", text))
                source |> Option.iter (fun text -> writer.WriteString("source", text))
                writer.WriteEndObject()

            use document = JsonDocument.Parse(buffer.WrittenMemory)
            Some(document.RootElement.Clone())

    /// A generator message in the MEP shape: severity, the rule id as code, the message, and the logical location
    /// and source as data. Generator messages have no source range.
    let diagnostic (message: VMessage) : Diagnostic =
        { Severity = severity message.Severity
          Code = message.RuleId
          Message = message.Message
          Location = None
          Related = [||]
          Data = dataOf message }

    let private failed (diagnostics: IReadOnlyList<Diagnostic>) : GenerateResult =
        { Success = false
          Artifacts = [||]
          Diagnostics = diagnostics }

    /// Checks an artifact path before it leaves the guest: relative, with / as the only separator, no empty, . or ..
    /// segment, and no \ or :. The generator's names make every path safe today; this check does not rely on that.
    let checkArtifactPath (path: string) : Result<unit, string> =
        if String.IsNullOrEmpty path then
            Error "An artifact path is empty."
        elif path.IndexOfAny [| '\\'; ':' |] >= 0 then
            Error $"The artifact path '{path}' has a \\ or a :."
        else
            match path.Split '/' |> Array.tryFind (fun segment -> segment = "" || segment = "." || segment = "..") with
            | Some segment -> Error $"The artifact path '{path}' has the segment '{segment}'; it must be relative, with no empty, . or .. segment."
            | None -> Ok()

    /// The files of a generated tree, in the generator's declared order, then any others by path. Every file is text.
    let artifacts (tree: VFileTree) : Result<IReadOnlyList<Artifact>, RpcError> =
        let files = Morphir.IR.Pipeline.VFileTree.toFileMap tree

        let declared =
            Morphir.IR.Pipeline.VFileTree.tryGet Morphir.IR.Pipeline.VFileTree.declaredOrder tree
            |> Option.defaultValue []
            |> List.filter files.ContainsKey

        let order = declared @ (files |> Map.keys |> Seq.filter (fun key -> not (List.contains key declared)) |> List.ofSeq)

        let artifact (location: string) : Result<Artifact, RpcError> =
            match checkArtifactPath location, Morphir.IR.Pipeline.VFile.readText files[location] with
            | Error reason, _ -> Error(RpcErrors.withMessage ErrorCode.InternalError reason)
            | Ok(), Error failure ->
                Error(RpcErrors.withMessage ErrorCode.InternalError $"The generated file {location} cannot be read: {failure}")
            | Ok(), Ok text ->
                Ok
                    { Path = location
                      Content = text
                      Binary = false }

        order
        |> List.fold
            (fun (result: Result<Artifact list, RpcError>) location ->
                result |> Result.bind (fun done' -> artifact location |> Result.map (fun item -> done' @ [ item ])))
            (Ok [])
        |> Result.map (fun items -> Array.ofList items :> IReadOnlyList<Artifact>)

    let private cancelled = RpcErrors.create ErrorCode.RequestCancelled

    /// Serves one generateModel request. A request that the session does not admit, a target other than fsharp,
    /// invalid options or a payload that is not IR of the input tuple is -32602, and nothing is generated. A model
    /// that the generator rejects is a failed result with its diagnostics. A failure to read the output is -32603.
    let generate (agreed: ModelTransportSupport option) (request: GenerateModelRequest) (cancellation: System.Threading.CancellationToken) : Result<GenerateResult, RpcError> =
        match GenerateModel.admit agreed trustNone request with
        | _ when cancellation.IsCancellationRequested -> Error cancelled
        | Error failure -> Error(failure.ToRpcError())
        | Ok _ when not (String.Equals(request.Target, Target, StringComparison.Ordinal)) ->
            Error(invalidParams $"The F# backend generates only the target '{Target}'; the request names '{request.Target}'.")
        | Ok admitted ->
            let prepared =
                options request.Options
                |> Result.bind (fun generation -> readModel admitted.Model |> Result.map (fun model -> generation, model))

            match prepared with
            | Error error -> Error error
            | Ok(_, model) when model.Kind <> DistributionKind.Library ->
                Ok(
                    failed
                        [| { Severity = DiagnosticSeverity.Error
                             Code = Some "unsupported-distribution"
                             Message = $"F# generation requires a Library distribution; the model is {model.Kind}."
                             Location = None
                             Related = [||]
                             Data = None } |]
                )
            | Ok(generation, model) ->
                let tree = Transpiler.generateModelWith generation model Morphir.IR.Pipeline.VFile.empty
                let diagnostics: IReadOnlyList<Diagnostic> = [| for message in tree.Messages -> diagnostic message |]

                // The generator cannot stop part way, so the token is checked before and after it, as the built-in
                // backend does.
                if cancellation.IsCancellationRequested then
                    Error cancelled
                elif Morphir.IR.Pipeline.VFileTree.hasErrors tree then
                    Ok(failed diagnostics)
                else
                    artifacts tree
                    |> Result.map (fun files ->
                        { Success = true
                          Artifacts = files
                          Diagnostics = diagnostics })

    /// A guest whose generateModel handler reads the agreed tuples of its session and calls serve.
    let create
        (serve: ModelTransportSupport option -> GenerateModelRequest -> System.Threading.CancellationToken -> Result<GenerateResult, RpcError>)
        : Guest =
        let mutable guest: Guest option = None

        let agreed () =
            match guest with
            | Some current ->
                match current.State with
                | SessionState.Initialized session -> session.Capabilities.ModelTransport
                | _ -> None
            | None -> None

        let handler =
            Handlers.request MepJson.Default.GenerateModelRequest MepJson.Default.GenerateResult (fun request cancellation ->
                // Off the connection's read loop: while it runs, the guest reads and answers other frames, and a
                // $/cancelRequest reaches the token.
                Task.Run((fun () -> serve (agreed ()) request cancellation), cancellation))

        let created =
            Guest(
                { Claims = claims
                  Capabilities = capabilities
                  Methods = readOnlyDict [ Method.GenerateModel.WireValue, handler ] }
            )

        guest <- Some created
        created

    /// The guest over the generator.
    let registration () : Guest = create generate
