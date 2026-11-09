namespace Morphir.Extensions

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Morphir.IR
open Morphir.IR.Model

/// <summary>
/// A diagnostic and the id of the analyzer that reported it. The diagnostic is the MEP <c>Diagnostic</c> type, not a
/// copy, so built-in and external analyzers report the same shape.
/// </summary>
type AnalyzerDiagnostic =
    {
        /// <summary>The analyzer that reported the diagnostic.</summary>
        Extension: ExtensionId
        /// <summary>The diagnostic, in the MEP shape.</summary>
        Diagnostic: Morphir.Mep.Diagnostic
    }

    /// <summary>The analyzer id and the diagnostic.</summary>
    override this.ToString() = $"{this.Extension}: {this.Diagnostic}"

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
    /// <summary>The analyzer threw an exception or returned no list. The detail is for people.</summary>
    | Failed of detail: string
    /// <summary>The runner did not run the analyzer.</summary>
    | NotRun of reason: AnalyzerSkipReason

    /// <summary>The status that the CLI writes: <c>ran</c>, <c>failed</c> or <c>not-run</c>.</summary>
    member this.WireValue =
        match this with
        | Ran -> "ran"
        | Failed _ -> "failed"
        | NotRun _ -> "not-run"

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

/// <summary>One analyzer and its outcome.</summary>
type AnalyzerRun =
    {
        /// <summary>The analyzer id.</summary>
        Analyzer: ExtensionId
        /// <summary>What happened.</summary>
        Outcome: AnalyzerOutcome
    }

    /// <summary>The id and the outcome.</summary>
    override this.ToString() = $"{this.Analyzer}: {this.Outcome}"

/// <summary>The merged result of every analyzer for one model.</summary>
type AnalysisResult =
    {
        /// <summary>Each analyzer and its outcome, ordered by id.</summary>
        Runs: IReadOnlyList<AnalyzerRun>
        /// <summary>The analyzer diagnostics: by analyzer id, then in the order in which each analyzer reported.</summary>
        Diagnostics: IReadOnlyList<AnalyzerDiagnostic>
        /// <summary>
        /// Diagnostics of the runner itself, with no analyzer id: one <c>info</c> diagnostic when no analyzer ran for
        /// the model's family and version.
        /// </summary>
        HostDiagnostics: IReadOnlyList<Morphir.Mep.Diagnostic>
    }

    /// <summary>True when a diagnostic has the severity <c>error</c>.</summary>
    member this.HasErrors =
        Seq.append (this.Diagnostics |> Seq.map _.Diagnostic) this.HostDiagnostics
        |> Seq.exists (fun diagnostic -> diagnostic.Severity = Morphir.Mep.DiagnosticSeverity.Error)

    /// <summary>True when an analyzer failed.</summary>
    member this.HasFailures =
        this.Runs
        |> Seq.exists (fun run ->
            match run.Outcome with
            | AnalyzerOutcome.Failed _ -> true
            | AnalyzerOutcome.Ran
            | AnalyzerOutcome.NotRun _ -> false
        )

    /// <summary>The number of runs and diagnostics.</summary>
    override this.ToString() =
        $"{this.Runs.Count} analyzers; {this.Diagnostics.Count + this.HostDiagnostics.Count} diagnostics"

/// <summary>
/// Runs analyzers on one model. It selects the analyzers whose validator claim reads the model's format version and
/// that the configuration does not turn off, runs them one at a time in id order, and merges their diagnostics. An
/// exception from one analyzer is a failed outcome for that analyzer only.
/// </summary>
[<AbstractClass; Sealed>]
type AnalyzerRunner =
    static member private Family(model: VersionedModel) =
        match model.Family with
        | ModelFamily.Classic -> "Classic"
        | ModelFamily.V4 -> "V4"

    static member private Reads (model: VersionedModel) (analyzer: IModelAnalyzer) =
        analyzer.Entry.Capabilities
        |> Seq.exists (fun capability ->
            match capability with
            | ExtensionCapability.Validator claim -> claim.Reads model.SourceVersion.Version
            | ExtensionCapability.Backend _ -> false
        )

    static member private Run (model: VersionedModel) (analyzer: IModelAnalyzer) (ct: CancellationToken) = task {
        let id = analyzer.Entry.Identity.Id

        try
            let! diagnostics = analyzer.AnalyzeAsync(model.Distribution, ct)

            // A C# analyzer can return null despite the signature.
            if obj.ReferenceEquals(diagnostics, null) then
                return AnalyzerOutcome.Failed "The analyzer returned no diagnostic list.", [||]
            else
                return
                    AnalyzerOutcome.Ran,
                    [|
                        for diagnostic in diagnostics -> { Extension = id; Diagnostic = diagnostic }
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

            let ordered =
                analyzers
                |> Seq.sortWith (fun a b -> compare a.Entry.Identity.Id b.Entry.Identity.Id)
                |> Array.ofSeq

            let runs = ResizeArray<AnalyzerRun>()
            let diagnostics = ResizeArray<AnalyzerDiagnostic>()

            for analyzer in ordered do
                ct.ThrowIfCancellationRequested()
                let id = analyzer.Entry.Identity.Id

                if turnedOff.Contains id then
                    runs.Add { Analyzer = id; Outcome = AnalyzerOutcome.NotRun AnalyzerSkipReason.TurnedOff }
                elif not (AnalyzerRunner.Reads model analyzer) then
                    runs.Add { Analyzer = id; Outcome = AnalyzerOutcome.NotRun AnalyzerSkipReason.UnsupportedModel }
                else
                    let! outcome, reported = AnalyzerRunner.Run model analyzer ct
                    runs.Add { Analyzer = id; Outcome = outcome }
                    diagnostics.AddRange reported

            let anyRan =
                runs
                |> Seq.exists (fun run ->
                    match run.Outcome with
                    | AnalyzerOutcome.Ran
                    | AnalyzerOutcome.Failed _ -> true
                    | AnalyzerOutcome.NotRun _ -> false
                )

            let host: Morphir.Mep.Diagnostic array =
                if anyRan then
                    [||]
                else
                    [|
                        {
                            Severity = Morphir.Mep.DiagnosticSeverity.Info
                            Code = Some "no-analyzer"
                            Message =
                                $"No enabled analyzer reads the {AnalyzerRunner.Family model} family at format version {model.SourceVersion.Text}."
                            Location = None
                            Related = [||]
                            Data = None
                        }
                    |]

            return {
                Runs = runs.ToArray()
                Diagnostics = diagnostics.ToArray()
                HostDiagnostics = host
            }
        }
