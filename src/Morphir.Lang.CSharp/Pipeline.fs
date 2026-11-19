// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp

open System
open System.Collections.Generic
open System.Text.Json
open System.Threading
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.Model
open Morphir.Lang.CSharp.Backend
open Morphir.Lang.CSharp.Emit
open Morphir.MiddleEnd

/// <summary>
/// One diagnostic of the pipeline function: a stable code, a message, the location in the model or in the options, and the
/// Roslyn severity, so that the source generator and the MSBuild task report it with no mapping of their own.
/// </summary>
[<Sealed>]
type CSharpPipelineDiagnostic internal (code: string, message: string, location: string, severity: Microsoft.CodeAnalysis.DiagnosticSeverity) =
    /// <summary>The stable code, for example <c>unsupported-version</c> or <c>csharp.admission</c>.</summary>
    member _.Code = code
    /// <summary>The message.</summary>
    member _.Message = message
    /// <summary>The location in the model (for example <c>$.formatVersion</c>) or in the options. Empty when it has none.</summary>
    member _.Location = location
    /// <summary>The severity, as Roslyn has it.</summary>
    member _.Severity = severity
    /// <summary>True when the severity is an error. A result with an error has no files.</summary>
    member _.IsError = severity = Microsoft.CodeAnalysis.DiagnosticSeverity.Error

    override _.ToString() =
        if String.IsNullOrEmpty location then $"{code}: {message}" else $"{code} at {location}: {message}"

/// <summary>The result of the pipeline function: the files in the order of their paths, or the diagnostics that stop it.</summary>
[<Sealed>]
type CSharpPipelineResult internal (files: GeneratedSource list, diagnostics: CSharpPipelineDiagnostic list) =
    /// <summary>True when no diagnostic is an error. Then <see cref="Files"/> has the generated files.</summary>
    member _.Succeeded = diagnostics |> List.forall (fun diagnostic -> not diagnostic.IsError)
    /// <summary>The generated files, in the order of their paths. Empty when the pipeline failed.</summary>
    member _.Files: IReadOnlyList<GeneratedSource> = List.toArray files :> _
    /// <summary>The diagnostics of each stage, in order. Info and warning diagnostics do not stop the pipeline.</summary>
    member _.Diagnostics: IReadOnlyList<CSharpPipelineDiagnostic> = List.toArray diagnostics :> _

    override _.ToString() = $"{List.length files} files, {List.length diagnostics} diagnostics"

/// <summary>
/// One request to the pipeline function: the bytes of a model and the options. A request with no target framework writes
/// the polyfill file, as the CLI does: the file has guards, so it compiles for each framework.
/// </summary>
[<Sealed>]
type CSharpPipelineRequest private (model: byte[], namespaceOverride: NamespaceOverride option, writesPolyfill: bool) =

    /// <summary>A request for the bytes of a model file (JSON, with or without a byte order mark). The bytes are copied.</summary>
    static member Create(model: ReadOnlySpan<byte>) : CSharpPipelineRequest =
        CSharpPipelineRequest(model.ToArray(), None, true)

    /// <summary>The bytes of the model.</summary>
    member _.Model: ReadOnlyMemory<byte> = ReadOnlyMemory model

    member internal _.ModelBytes = model

    member internal _.Namespace = namespaceOverride

    /// <summary>True when the output has the polyfill file.</summary>
    member _.WritesPolyfill = writesPolyfill

    /// <summary>The same request with all files in the namespace of the override.</summary>
    member _.WithNamespace(value: NamespaceOverride) : CSharpPipelineRequest =
        CSharpPipelineRequest(model, Some value, writesPolyfill)

    /// <summary>
    /// The same request for a project of the target framework that MSBuild gives as <c>TargetFrameworkIdentifier</c> and
    /// <c>TargetFrameworkVersion</c> (for example <c>.NETCoreApp</c> and <c>v10.0</c>). The output has the polyfill file
    /// unless the framework has the runtime types of C# 15 (<c>.NETCoreApp</c> 11.0 or later). Text that is not a known
    /// framework keeps the file, because its guards make it safe for each framework.
    /// </summary>
    member _.ForTargetFramework(identifier: string, version: string) : CSharpPipelineRequest =
        CSharpPipelineRequest(model, namespaceOverride, not (CSharpPipelineRequest.HasLanguageRuntimeTypes(identifier, version)))

    /// <summary>True when the framework has the runtime types of C# 15, so that a project of it needs no polyfill file.</summary>
    static member HasLanguageRuntimeTypes(identifier: string, version: string) : bool =
        let text = if isNull version then "" else version.TrimStart('v', 'V')

        match Version.TryParse text with
        | true, parsed -> String.Equals(identifier, ".NETCoreApp", StringComparison.OrdinalIgnoreCase) && parsed.Major >= 11
        | _ -> false

    override _.ToString() = $"model of {model.Length} bytes"

/// <summary>
/// The pipeline function of C# generation (design.html, BLD-1): it reads the model, upgrades a classic model to the current
/// IR, runs the target-side passes of <c>csharp</c> and the backend. It has no file or process access. The source generator
/// and the MSBuild task call it, and the CLI runs the same stages, so one model gives the same C# bytes through the three.
/// </summary>
[<AbstractClass; Sealed>]
type CSharpPipeline private () =

    /// <summary>The largest JSON depth that the pipeline reads, the default of the CLI.</summary>
    static member MaxDepth = 128

    /// <summary>The largest number of JSON values that the pipeline reads, the default of the CLI.</summary>
    static member MaxNodes = 1_000_000

    static member private Error(code: string, location: string, message: string) =
        CSharpPipelineDiagnostic(code, message, location, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)

    static member private OfDiagnostic(diagnostic: Diagnostic, fallbackCode: string) =
        let location =
            if diagnostic.Data.HasValue then
                let data = diagnostic.Data.Value

                match data.ValueKind with
                | JsonValueKind.Object ->
                    match data.TryGetProperty "location" with
                    | true, text when text.ValueKind = JsonValueKind.String -> text.GetString()
                    | _ -> ""
                | _ -> ""
            else
                ""

        let severity =
            match diagnostic.Severity with
            | DiagnosticSeverity.Error -> Microsoft.CodeAnalysis.DiagnosticSeverity.Error
            | DiagnosticSeverity.Warning -> Microsoft.CodeAnalysis.DiagnosticSeverity.Warning
            | DiagnosticSeverity.Info -> Microsoft.CodeAnalysis.DiagnosticSeverity.Info
            | DiagnosticSeverity.Hint -> Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden

        let code =
            match diagnostic.CodeOrNull with
            | null -> fallbackCode
            | value -> value.Value

        CSharpPipelineDiagnostic(code, diagnostic.Message, (if isNull location then "" else location), severity)

    /// <summary>Reads the model as the CLI reads a JSON file: the same limits, rules and codes.</summary>
    static member private Decode(bytes: byte[], ct: CancellationToken) : Result<VersionedModel, CSharpPipelineDiagnostic> =
        let start = if bytes.Length >= 3 && bytes[0] = 0xEFuy && bytes[1] = 0xBBuy && bytes[2] = 0xBFuy then 3 else 0
        let memory = ReadOnlyMemory(bytes, start, bytes.Length - start)

        // Count the values before a DOM is made. Property names and closing tokens are not values.
        let mutable reader = Utf8JsonReader(memory.Span, JsonReaderOptions(MaxDepth = Int32.MaxValue))
        let mutable nodes = 0
        let mutable overLimit = false

        while not overLimit && reader.Read() do
            ct.ThrowIfCancellationRequested()

            match reader.TokenType with
            | JsonTokenType.EndArray
            | JsonTokenType.EndObject
            | JsonTokenType.PropertyName -> ()
            | _ ->
                nodes <- nodes + 1

                if nodes > CSharpPipeline.MaxNodes || reader.CurrentDepth + 1 > CSharpPipeline.MaxDepth then
                    overLimit <- true

        if overLimit then
            Error(CSharpPipeline.Error("input-limit", "$", "The model exceeds the read limits of depth or size."))
        else
            use document = JsonDocument.Parse(memory, JsonDocumentOptions(MaxDepth = CSharpPipeline.MaxDepth))
            let root = document.RootElement
            let isObject = root.ValueKind = JsonValueKind.Object

            let has (name: string) =
                isObject
                && (match root.TryGetProperty name with
                    | true, _ -> true
                    | _ -> false)

            if has "envelopeVersion" || has "morphir_model" || has "jsonrpc" then
                Error(CSharpPipeline.Error("unsupported-profile", "$", "MEP model-envelope admission is unavailable; supply bare IR."))
            elif isObject && (root.EnumerateObject() |> Seq.filter (fun property -> property.NameEquals "formatVersion") |> Seq.length) > 1 then
                Error(CSharpPipeline.Error("duplicate_format_version", "$.formatVersion", "The model declares formatVersion more than once."))
            else
                let text =
                    match isObject, (if isObject then root.TryGetProperty "formatVersion" else (false, Unchecked.defaultof<_>)) with
                    | true, (true, version) ->
                        if version.ValueKind = JsonValueKind.String then version.GetString() else version.GetRawText()
                    | _ -> ""

                match SourceFormatVersion.parse text with
                | Error _ ->
                    Error(CSharpPipeline.Error("unsupported-version", "$.formatVersion", "The model requires an explicit supported formatVersion."))
                | Ok version ->
                    let distribution =
                        if text = "4" || text.StartsWith("4.", StringComparison.Ordinal) then
                            Morphir.Json.V4.V4Json.readDistribution root
                            |> Result.map (fun decoded -> DistributionModel.V4 decoded.Distribution)
                            |> Result.mapError (fun error -> CSharpPipeline.Error(error.Rule, error.Location, error.Message))
                        else
                            Morphir.Json.Classic.ClassicJson.readDistribution root
                            |> Result.map (fun decoded -> DistributionModel.Classic decoded.Distribution)
                            |> Result.mapError (fun error -> CSharpPipeline.Error(error.Rule, error.Location, error.Message))

                    distribution |> Result.map (fun value -> { SourceVersion = version; Distribution = value })

    static member private Run(request: CSharpPipelineRequest, ct: CancellationToken) : CSharpPipelineResult =
        let failed diagnostics = CSharpPipelineResult([], diagnostics)
        let bytes = request.ModelBytes

        let decoded =
            try
                CSharpPipeline.Decode(bytes, ct)
            with :? JsonException as error ->
                Error(CSharpPipeline.Error("invalid-json", "$", $"The model is not valid JSON: {error.Message}"))

        match decoded with
        | Error diagnostic -> failed [ diagnostic ]
        | Ok loaded ->
            let snapshot = Sha256Digest.OfBytes bytes
            let artifact = ArtifactSelector.Workspace "input"

            // A classic model goes to the current IR first, as the CLI does for a backend that reads the current IR only.
            let upgraded =
                if loaded.Family = ModelFamily.Classic then
                    let result = CurrentIr.upgrade { Model = loaded; Artifact = artifact; Snapshot = snapshot }
                    let notes = result.Diagnostics |> Seq.map (fun item -> CSharpPipeline.OfDiagnostic(item, "ir.upgrade_failed")) |> List.ofSeq

                    match Checked.tryValue result with
                    | Some outcome -> Ok(outcome.Model, outcome.Applications, notes)
                    | None -> Error notes
                else
                    Ok(loaded, [], [])

            match upgraded with
            | Error diagnostics -> failed diagnostics
            | Ok(model, applications, upgradeNotes) ->
                ct.ThrowIfCancellationRequested()

                let passed =
                    PassPipeline.Run(
                        PassStage.BeforeBackend CSharpTargetPasses.target,
                        CSharpTargetPasses.all,
                        model,
                        artifact,
                        loaded.SourceVersion.Version,
                        snapshot,
                        applications
                    )

                let passNotes =
                    passed.Diagnostics |> Seq.map (fun item -> CSharpPipeline.OfDiagnostic(item, "transform.diagnostic")) |> List.ofSeq

                match Checked.tryValue passed with
                | None -> failed (upgradeNotes @ passNotes)
                | Some result ->
                    let skipped =
                        match result.SkippedOrNull with
                        | null -> []
                        | note -> [ CSharpPipeline.OfDiagnostic(note, "transform.diagnostic") ]

                    // The host check of the CLI: no feature that the backend cannot take remains after the passes.
                    let order =
                        match CurrentModel.tryOf result.Model with
                        | Some current ->
                            FeatureOrder.check (BackendProfile.Create(CSharpTargetPasses.target, CSharpTargetPasses.cannotTake)) current
                            |> Checked.diagnostics
                            |> Seq.map (fun item -> CSharpPipeline.OfDiagnostic(item, "transform.diagnostic"))
                            |> List.ofSeq
                        | None -> []

                    let notes = upgradeNotes @ passNotes @ skipped @ order

                    if notes |> List.exists (fun note -> note.IsError) then
                        failed notes
                    else
                        let generated = CSharpBackend.GenerateWith(result.Model, GenerationOptions.Default, request.Namespace, (fun _ -> false), ct)

                        match generated.Status with
                        | GenerationStatus.Cancelled -> raise (OperationCanceledException ct)
                        | GenerationStatus.Failure ->
                            failed (notes @ (generated.Diagnostics |> List.map (fun item -> CSharpPipeline.Error(item.Rule, item.Location, item.Message))))
                        | GenerationStatus.Success ->
                            let files =
                                if request.WritesPolyfill then
                                    generated.Sources
                                else
                                    generated.Sources |> List.filter (fun source -> source.RelativePath <> Polyfill.path)

                            CSharpPipelineResult(files, notes)

    /// <summary>
    /// Generates the C# files of the model of the request. A model that cannot be read, upgraded, passed or generated gives
    /// diagnostics and no files; the function does not throw for it. Cancellation throws <see cref="OperationCanceledException"/>.
    /// </summary>
    static member Generate(request: CSharpPipelineRequest, cancellationToken: CancellationToken) : CSharpPipelineResult =
        CSharpPipeline.Run(request, cancellationToken)

    /// <summary>The path of the polyfill file in the output.</summary>
    static member PolyfillPath = Polyfill.path
