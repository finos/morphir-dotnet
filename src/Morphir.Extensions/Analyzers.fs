namespace Morphir.Extensions

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Morphir.IR.Model

/// <summary>
/// For C# callers: read the MEP <c>Diagnostic</c> and <c>ExtensionInfo</c> records without F# options, and build a
/// diagnostic without them. The MEP wire types keep their option fields for the codec.
/// </summary>
[<Extension; AbstractClass; Sealed>]
type MepRecords =
    /// <summary>The wire value of a severity: <c>error</c>, <c>warning</c>, <c>info</c> or <c>hint</c>.</summary>
    [<Extension>]
    static member WireValue(severity: Morphir.Mep.DiagnosticSeverity) : string =
        match severity with
        | Morphir.Mep.DiagnosticSeverity.Error -> "error"
        | Morphir.Mep.DiagnosticSeverity.Warning -> "warning"
        | Morphir.Mep.DiagnosticSeverity.Info -> "info"
        | Morphir.Mep.DiagnosticSeverity.Hint -> "hint"

    /// <summary>True when the severity is <c>error</c>. C# compares F# unions only with this member or <c>Equals</c>.</summary>
    [<Extension>]
    static member IsError(severity: Morphir.Mep.DiagnosticSeverity) : bool =
        severity = Morphir.Mep.DiagnosticSeverity.Error

    /// <summary>The rule code, or null when there is none.</summary>
    [<Extension>]
    static member CodeOrNull(diagnostic: Morphir.Mep.Diagnostic) : string | null = Option.toObj diagnostic.Code

    /// <summary>The location, or null when there is none.</summary>
    [<Extension>]
    static member LocationOrNull(diagnostic: Morphir.Mep.Diagnostic) : Morphir.Mep.SourceLocation | null =
        Option.toObj diagnostic.Location

    /// <summary>The structured data, or null when there is none.</summary>
    [<Extension>]
    static member DataOrNull(diagnostic: Morphir.Mep.Diagnostic) : Nullable<JsonElement> = Option.toNullable diagnostic.Data

    /// <summary>The description, or null when there is none.</summary>
    [<Extension>]
    static member DescriptionOrNull(info: Morphir.Mep.ExtensionInfo) : string | null = Option.toObj info.Description

    /// <summary>The author, or null when there is none.</summary>
    [<Extension>]
    static member AuthorOrNull(info: Morphir.Mep.ExtensionInfo) : string | null = Option.toObj info.Author

    /// <summary>The home page, or null when there is none.</summary>
    [<Extension>]
    static member HomepageOrNull(info: Morphir.Mep.ExtensionInfo) : string | null = Option.toObj info.Homepage

    /// <summary>The license, or null when there is none.</summary>
    [<Extension>]
    static member LicenseOrNull(info: Morphir.Mep.ExtensionInfo) : string | null = Option.toObj info.License

    /// <summary>The lowest SDK version, or null when there is none.</summary>
    [<Extension>]
    static member MinSdkVersionOrNull(info: Morphir.Mep.ExtensionInfo) : string | null = Option.toObj info.MinSdkVersion

    /// <summary>For C# callers: a diagnostic with an optional code and location, no related information and no data.</summary>
    static member CreateDiagnostic
        (
            severity: Morphir.Mep.DiagnosticSeverity,
            message: string,
            code: string | null,
            location: Morphir.Mep.SourceLocation | null
        ) : Morphir.Mep.Diagnostic =
        {
            Severity = severity
            Code = Option.ofObj code
            Message = message
            Location = Option.ofObj location
            Related = [||]
            Data = None
        }

/// <summary>
/// A diagnostic and the id of the analyzer that reported it. The diagnostic is the MEP <c>Diagnostic</c> type, not a
/// copy, so built-in and external analyzers report the same shape. Only the runner builds one.
/// </summary>
type AnalyzerDiagnostic =
    private
        {
            extension: ExtensionId
            diagnostic: Morphir.Mep.Diagnostic
        }

    /// <summary>The analyzer that reported the diagnostic.</summary>
    member this.Extension = this.extension

    /// <summary>The diagnostic, in the MEP shape.</summary>
    member this.Diagnostic = this.diagnostic

    /// <summary>The analyzer id and the diagnostic.</summary>
    override this.ToString() = $"{this.extension}: {this.diagnostic}"

/// <summary>
/// An analyzer: it reads a decoded model and returns diagnostics. It does not change the model, write files or read
/// the result of another analyzer. Its entry gives its identity and its validator claim.
/// </summary>
type IModelAnalyzer =
    /// <summary>The registry entry: the identity and the validator claim.</summary>
    abstract Entry: ExtensionEntry

    /// <summary>
    /// The diagnostics for the model, in the order in which the analyzer reports them. The runner adds the
    /// analyzer's id to each one.
    /// </summary>
    abstract AnalyzeAsync:
        model: DistributionModel * ct: CancellationToken -> Task<IReadOnlyList<Morphir.Mep.Diagnostic>>

/// <summary>Why the runner did not run an analyzer.</summary>
[<RequireQualifiedAccess>]
type AnalyzerSkipReason =
    /// <summary>The configuration sets <c>extensions."&lt;id&gt;".enabled = false</c>.</summary>
    | TurnedOff
    /// <summary>The validator claim does not include the model's format version.</summary>
    | UnsupportedModel

    /// <summary>The text that the CLI writes.</summary>
    member this.WireValue =
        match this with
        | TurnedOff -> "turned-off"
        | UnsupportedModel -> "unsupported-model"

    /// <summary>The text that the CLI writes.</summary>
    override this.ToString() = this.WireValue

/// <summary>What happened to one analyzer in one run.</summary>
[<RequireQualifiedAccess>]
type AnalyzerOutcome =
    /// <summary>The analyzer ran and returned its diagnostics.</summary>
    | Ran
    /// <summary>The analyzer threw an exception or returned no valid list. The detail is for people.</summary>
    | Failed of detail: string
    /// <summary>The runner did not run the analyzer.</summary>
    | NotRun of reason: AnalyzerSkipReason

    /// <summary>The status that the CLI writes: <c>ran</c>, <c>failed</c> or <c>not-run</c>.</summary>
    member this.WireValue =
        match this with
        | Ran -> "ran"
        | Failed _ -> "failed"
        | NotRun _ -> "not-run"

    /// <summary>For C# callers that make a decision on the outcome: one function for each case.</summary>
    member this.Match<'R>(ran: Func<'R>, failed: Func<string, 'R>, notRun: Func<AnalyzerSkipReason, 'R>) : 'R =
        match this with
        | Ran -> ran.Invoke()
        | Failed detail -> failed.Invoke detail
        | NotRun reason -> notRun.Invoke reason

    /// <summary>For C# callers: the skip reason, or null when the analyzer was not skipped.</summary>
    member this.SkipReasonOrNull: AnalyzerSkipReason | null =
        match this with
        | NotRun reason -> reason
        | Ran
        | Failed _ -> null

    /// <summary>For C# callers: the skip reason as the CLI writes it, or null when the analyzer was not skipped.</summary>
    member this.ReasonOrNull: string | null =
        match this with
        | NotRun reason -> reason.WireValue
        | Ran
        | Failed _ -> null

    /// <summary>For C# callers: the failure detail, or null when the analyzer did not fail.</summary>
    member this.DetailOrNull: string | null =
        match this with
        | Failed detail -> detail
        | Ran
        | NotRun _ -> null

    /// <summary>The status, and the reason or detail when there is one.</summary>
    override this.ToString() =
        match this with
        | Ran -> "ran"
        | Failed detail -> $"failed: {detail}"
        | NotRun reason -> $"not run: {reason}"

/// <summary>One analyzer and its outcome. Only the runner builds one.</summary>
type AnalyzerRun =
    private
        {
            analyzer: ExtensionId
            outcome: AnalyzerOutcome
        }

    /// <summary>The analyzer id.</summary>
    member this.Analyzer = this.analyzer

    /// <summary>What happened.</summary>
    member this.Outcome = this.outcome

    /// <summary>The id and the outcome.</summary>
    override this.ToString() = $"{this.analyzer}: {this.outcome}"

/// <summary>
/// The merged result of every analyzer for one model. Only the runner builds one, so its order and its host
/// diagnostics always follow the run rule.
/// </summary>
type AnalysisResult =
    private
        {
            runs: AnalyzerRun array
            diagnostics: AnalyzerDiagnostic array
            /// The analyzers whose entry could not be read, so they have no id and no run.
            brokenEntries: int
            family: ModelFamily
            version: SourceFormatVersion
        }

    static member private FamilyText(family: ModelFamily) =
        match family with
        | ModelFamily.Classic -> "Classic"
        | ModelFamily.V4 -> "V4"

    static member private HostDiagnostic(code: string, severity, message: string) : Morphir.Mep.Diagnostic = {
        Severity = severity
        Code = Some code
        Message = message
        Location = None
        Related = [||]
        Data = None
    }

    /// <summary>Each analyzer and its outcome, ordered by id.</summary>
    member this.Runs: IReadOnlyList<AnalyzerRun> = this.runs

    /// <summary>The analyzer diagnostics: by analyzer id, then in the order in which each analyzer reported.</summary>
    member this.Diagnostics: IReadOnlyList<AnalyzerDiagnostic> = this.diagnostics

    /// <summary>
    /// Diagnostics of the runner itself, with no analyzer id: one <c>error</c> when an analyzer has no readable entry,
    /// and one <c>info</c> (<c>no-analyzer</c>) that names the family when no analyzer ran for the model.
    /// </summary>
    member this.HostDiagnostics: IReadOnlyList<Morphir.Mep.Diagnostic> = [|
        if this.brokenEntries > 0 then
            AnalysisResult.HostDiagnostic(
                "analyzer-entry",
                Morphir.Mep.DiagnosticSeverity.Error,
                $"{this.brokenEntries} analyzers have no readable registry entry and did not run."
            )

        let anyRan =
            this.runs
            |> Array.exists (fun run ->
                match run.outcome with
                | AnalyzerOutcome.Ran
                | AnalyzerOutcome.Failed _ -> true
                | AnalyzerOutcome.NotRun _ -> false
            )

        if not anyRan then
            AnalysisResult.HostDiagnostic(
                "no-analyzer",
                Morphir.Mep.DiagnosticSeverity.Info,
                $"No enabled analyzer reads the {AnalysisResult.FamilyText this.family} family at format version {this.version.Text}."
            )
    |]

    /// <summary>True when a diagnostic has the severity <c>error</c>.</summary>
    member this.HasErrors =
        Seq.append (this.diagnostics |> Seq.map _.Diagnostic) this.HostDiagnostics
        |> Seq.exists (fun diagnostic -> diagnostic.Severity = Morphir.Mep.DiagnosticSeverity.Error)

    /// <summary>True when an analyzer failed or had no readable entry.</summary>
    member this.HasFailures =
        this.brokenEntries > 0
        || this.runs
           |> Array.exists (fun run ->
               match run.outcome with
               | AnalyzerOutcome.Failed _ -> true
               | AnalyzerOutcome.Ran
               | AnalyzerOutcome.NotRun _ -> false
           )

    /// <summary>The number of runs and diagnostics.</summary>
    override this.ToString() =
        $"{this.runs.Length} analyzers; {this.diagnostics.Length + this.HostDiagnostics.Count} diagnostics"

/// <summary>
/// Runs analyzers on one model. It selects the analyzers whose validator claim reads the model's format version and
/// that the configuration does not turn off, runs them one at a time in id order, and merges their diagnostics. An
/// exception from one analyzer is a failed outcome for that analyzer only.
/// </summary>
[<AbstractClass; Sealed>]
type AnalyzerRunner =
    /// The entry of a C# analyzer can be null or throw despite the signature.
    static member private TryEntry(analyzer: IModelAnalyzer) : ExtensionEntry option =
        try
            if obj.ReferenceEquals(analyzer, null) then None
            else
                let entry = analyzer.Entry
                if obj.ReferenceEquals(entry, null) then None else Some entry
        with _ ->
            None

    static member private Reads (model: VersionedModel) (entry: ExtensionEntry) =
        entry.Capabilities
        |> Seq.exists (fun capability ->
            match capability with
            | ExtensionCapability.Validator claim -> claim.Reads model.SourceVersion.Version
            | ExtensionCapability.Backend _ -> false
        )

    static member private Run
        (model: VersionedModel)
        (id: ExtensionId)
        (analyzer: IModelAnalyzer)
        (ct: CancellationToken)
        =
        task {
            try
                let! diagnostics = analyzer.AnalyzeAsync(model.Distribution, ct)

                // A C# analyzer can return null, or a list with null elements, despite the signature.
                if obj.ReferenceEquals(diagnostics, null) then
                    return AnalyzerOutcome.Failed "The analyzer returned no diagnostic list.", [||]
                elif diagnostics |> Seq.exists (fun diagnostic -> obj.ReferenceEquals(diagnostic, null)) then
                    return AnalyzerOutcome.Failed "The analyzer returned a null diagnostic.", [||]
                else
                    return
                        AnalyzerOutcome.Ran,
                        [|
                            for diagnostic in diagnostics -> { extension = id; diagnostic = diagnostic }
                        |]
            with
            // A cancellation of this run is not a failure of the analyzer: an unmatched exception propagates.
            | error when
                not (
                    error :? OperationCanceledException
                    && ct.IsCancellationRequested
                )
                ->
                return AnalyzerOutcome.Failed $"{error.GetType().Name}: {error.Message}", [||]
        }

    /// <summary>
    /// The merged result for the model. <paramref name="turnedOff"/> holds the ids that the configuration turns off.
    /// Cancellation of <paramref name="ct"/> throws <c>OperationCanceledException</c>; it is not an analyzer failure.
    /// </summary>
    static member RunAsync
        (
            analyzers: IEnumerable<IModelAnalyzer>,
            model: VersionedModel,
            turnedOff: IEnumerable<ExtensionId>,
            ct: CancellationToken
        ) : Task<AnalysisResult> =
        task {
            let turnedOff = Set.ofSeq turnedOff

            let read =
                analyzers
                |> Seq.map (fun analyzer -> analyzer, AnalyzerRunner.TryEntry analyzer)
                |> Array.ofSeq

            let ordered =
                read
                |> Array.choose (fun (analyzer, entry) -> entry |> Option.map (fun entry -> entry, analyzer))
                |> Array.sortWith (fun (a, _) (b, _) -> compare a.Identity.Id b.Identity.Id)

            let runs = ResizeArray<AnalyzerRun>()
            let diagnostics = ResizeArray<AnalyzerDiagnostic>()

            for entry, analyzer in ordered do
                ct.ThrowIfCancellationRequested()
                let id = entry.Identity.Id

                if turnedOff.Contains id then
                    runs.Add { analyzer = id; outcome = AnalyzerOutcome.NotRun AnalyzerSkipReason.TurnedOff }
                elif not (AnalyzerRunner.Reads model entry) then
                    runs.Add { analyzer = id; outcome = AnalyzerOutcome.NotRun AnalyzerSkipReason.UnsupportedModel }
                else
                    let! outcome, reported = AnalyzerRunner.Run model id analyzer ct
                    runs.Add { analyzer = id; outcome = outcome }
                    diagnostics.AddRange reported

            return {
                runs = runs.ToArray()
                diagnostics = diagnostics.ToArray()
                brokenEntries = read.Length - ordered.Length
                family = model.Family
                version = model.SourceVersion
            }
        }
