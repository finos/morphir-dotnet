// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.WasmHost

open System

/// What a host declares about one bound across the started events of a run of the suite.
[<RequireQualifiedAccess>]
type BoundDeclaration =
    /// Every started event that declares the bound declares it this way.
    | Declared of BoundState
    /// No started event declares the bound, for example because every case was refused.
    | Undeclared
    /// The started events disagree. The states, in the order first seen.
    | Inconsistent of BoundState list

    override this.ToString() =
        match this with
        | Declared state -> state.WireValue
        | Undeclared -> "undeclared"
        | Inconsistent states ->
            let shown =
                String.Join(
                    ", ",
                    states
                    |> List.map (fun state -> state.WireValue)
                )

            $"inconsistent ({shown})"

/// The alternative that a run with alternatives satisfied. For a runaway case it records which bound
/// ended the call.
type SatisfiedAlternative = {
    /// The run, counted from 1.
    Run: int
    /// The alternative, counted from 1 in corpus order.
    Alternative: int
    /// The alternative's condition.
    Condition: Condition
}

/// The outcome of one case.
[<RequireQualifiedAccess>]
type CaseOutcome =
    /// Every run satisfied its expectation: its only one, or one alternative that applies to the host.
    /// The list gives, for each run with alternatives, the first alternative it satisfied.
    | Passed of satisfied: SatisfiedAlternative list
    /// A run satisfied no expectation that applies to the host. The failures of every applicable
    /// alternative of such a run are listed, each with its alternative. Never empty.
    | Failed of failures: MatcherFailure list
    /// The runner could not judge the case: the outcome is neither a pass nor a failure of the host.
    | Error of reason: string
    /// The case needs a profile that the host does not declare.
    | NotApplicable of reason: string
    /// The host declares a bound of the case not enforced.
    | NotEnforced of bounds: Bound list
    /// The module's bytes differ from the recorded digest, so the case did not run.
    | Refused of expectedSha256: string * actualSha256: string

    /// The outcome, as the report writes it.
    member this.WireValue =
        match this with
        | Passed _ -> "passed"
        | Failed _ -> "failed"
        | Error _ -> "error"
        | NotApplicable _ -> "not_applicable"
        | NotEnforced _ -> "not_enforced"
        | Refused _ -> "refused"

    override this.ToString() = this.WireValue

/// Judges a case from its observations and from what the host declares. It is a pure function of its
/// inputs, so a report check computes the same outcome from the recorded observations.
module Judge =
    /// What the started events of these observations declare about each bound. A started event whose
    /// bounds do not have the contract's shape declares nothing; the convention check fails its case.
    let declaration (observations: RunObservation list) : Map<Bound, BoundDeclaration> =
        let declared =
            observations
            |> List.collect (fun observation -> Comparer.events observation.Stderr)
            |> List.filter (fun event -> event.Name = "started")
            |> List.choose (fun event ->
                match Comparer.startedBounds event with
                | Ok bounds -> Some bounds
                | Error _ -> None
            )
            |> List.concat

        Contract.bounds
        |> List.map (fun bound ->
            let states =
                declared
                |> List.filter (fun (name, _) -> name = bound)
                |> List.map snd
                |> List.distinct

            bound,
            match states with
            | [] -> BoundDeclaration.Undeclared
            | [ state ] -> BoundDeclaration.Declared state
            | states -> BoundDeclaration.Inconsistent states
        )
        |> Map.ofList

    /// Whether a declaration says enforced, or why it cannot say.
    let enforced (declaration: Map<Bound, BoundDeclaration>) (bound: Bound) : Result<bool, string> =
        match Map.tryFind bound declaration with
        | Some(BoundDeclaration.Declared BoundState.Enforced) -> Ok true
        | Some(BoundDeclaration.Declared(BoundState.NotEnforced _)) -> Ok false
        | Some(BoundDeclaration.Inconsistent _ as found) ->
            Error $"The host's started events declare {bound.WireValue} {found}"
        | Some BoundDeclaration.Undeclared
        | None -> Error $"No started event of the host declares {bound.WireValue}"

    let private conditionBounds (run: Run) =
        match run.Expect with
        | RunExpectation.Always _ -> []
        | RunExpectation.Alternatives alternatives ->
            alternatives
            |> List.choose (fun alternative ->
                match alternative.When with
                | Condition.Bound(bound, _) -> Some bound
                | Condition.HostRuns _ -> None
            )

    /// Judges one run against the expectations that apply to the host (convention alternatives): the run
    /// passes when its observation satisfies one of them. Choice1Of2 is a pass, with the first alternative
    /// satisfied when the run has alternatives; Choice2Of2 the failures of every applicable expectation,
    /// each with its alternative.
    let private judgeRun
        (case: WasmHostCase)
        (runNumber: int)
        (run: Run)
        (observation: RunObservation)
        (applicable: ApplicableExpectation list)
        : Choice<SatisfiedAlternative option, MatcherFailure list> =
        let compared =
            applicable
            |> List.map (fun expectation ->
                expectation, Comparer.compare case run.Frames expectation.Expect observation
            )

        match
            compared
            |> List.tryFind (fun (_, failures) -> failures.IsEmpty)
        with
        | Some(expectation, _) ->
            Choice1Of2(
                expectation.Alternative
                |> Option.map (fun (number, condition) ->
                    ({
                        Run = runNumber
                        Alternative = number
                        Condition = condition
                    }
                    : SatisfiedAlternative)
                )
            )
        | None ->
            Choice2Of2(
                compared
                |> List.collect (fun (expectation, failures) ->
                    failures
                    |> List.map (fun (matcher, message) ->
                        ({
                            Run = runNumber
                            Alternative =
                                expectation.Alternative
                                |> Option.map fst
                            Matcher = matcher
                            Message = message
                        }
                        : MatcherFailure)
                    )
                )
            )

    /// The outcome of a case for a host that declares these profiles and bounds. observedSha256 is the
    /// SHA-256 of the module file the case used, None when the file is missing, and runs are the
    /// observations of its runs in order. A case that does not apply is not_applicable; a module that
    /// differs from its digest is refused; a case for a bound the host does not enforce is not_enforced.
    let outcome
        (profiles: Profile list)
        (declaration: Map<Bound, BoundDeclaration>)
        (case: WasmHostCase)
        (observedSha256: string option)
        (runs: RunObservation list)
        : CaseOutcome =
        if not (Selection.applies profiles case) then
            CaseOutcome.NotApplicable
                $"The host does not declare the profile {case.AppliesTo.WireValue}"
        else
            match observedSha256 with
            | None -> CaseOutcome.Error $"The module file {case.Module.Wasm} does not exist"
            | Some actual when
                actual
                <> case.Module.Sha256
                ->
                CaseOutcome.Refused(case.Module.Sha256, actual)
            | Some _ ->
                let needed =
                    case.Bounds
                    @ (case.Runs
                       |> List.collect conditionBounds)
                    |> List.distinct

                match
                    needed
                    |> List.tryPick (fun bound ->
                        match enforced declaration bound with
                        | Error message -> Some message
                        | Ok _ -> None
                    )
                with
                | Some message -> CaseOutcome.Error message
                | None ->
                    let isEnforced bound = enforced declaration bound = Ok true

                    match
                        case.Bounds
                        |> List.filter (
                            isEnforced
                            >> not
                        )
                    with
                    | _ :: _ as bounds -> CaseOutcome.NotEnforced bounds
                    | [] ->
                        if
                            runs.Length
                            <> case.Runs.Length
                        then
                            CaseOutcome.Error
                                $"The case has {case.Runs.Length} runs and {runs.Length} were observed"
                        else
                            match
                                runs
                                |> List.tryPick (fun observation ->
                                    match observation.End with
                                    | WorkerEnd.NotStarted reason -> Some reason
                                    | _ -> None
                                )
                            with
                            | Some reason -> CaseOutcome.Error $"The worker did not start: {reason}"
                            | None ->
                                let judged =
                                    List.zip case.Runs runs
                                    |> List.mapi (fun index (run, observation) ->
                                        Selection.applicable profiles isEnforced run
                                        |> Result.map (judgeRun case (index + 1) run observation)
                                    )

                                match
                                    judged
                                    |> List.tryPick (
                                        function
                                        | Error message -> Some message
                                        | Ok _ -> None
                                    )
                                with
                                | Some message -> CaseOutcome.Error message
                                | None ->
                                    let verdicts =
                                        judged
                                        |> List.choose Result.toOption

                                    match
                                        verdicts
                                        |> List.collect (
                                            function
                                            | Choice2Of2 failures -> failures
                                            | Choice1Of2 _ -> []
                                        )
                                    with
                                    | [] ->
                                        CaseOutcome.Passed(
                                            verdicts
                                            |> List.choose (
                                                function
                                                | Choice1Of2 satisfied -> satisfied
                                                | Choice2Of2 _ -> None
                                            )
                                        )
                                    | failures -> CaseOutcome.Failed failures

    /// Whether an outcome counts as a pass for the exit status: passed, not_applicable, or not_enforced for
    /// bounds that qualification does not require.
    let acceptable (outcome: CaseOutcome) =
        match outcome with
        | CaseOutcome.Passed _
        | CaseOutcome.NotApplicable _ -> true
        | CaseOutcome.NotEnforced bounds ->
            bounds
            |> List.forall (fun bound ->
                not (List.contains bound Contract.requiredForQualification)
            )
        | CaseOutcome.Failed _
        | CaseOutcome.Error _
        | CaseOutcome.Refused _ -> false
