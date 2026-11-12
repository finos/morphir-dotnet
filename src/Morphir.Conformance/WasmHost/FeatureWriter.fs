// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.WasmHost

open System.Globalization
open System.Text
open Morphir.Conformance

/// Writes cases of the model as Gherkin features in step vocabulary 1.3. The output depends only
/// on its input: the same pin, kit and numbered cases give the same bytes.
module FeatureWriter =
    /// The group of a case id, the part before its first slash. Each group is one feature file.
    let group (caseId: string) = caseId.Substring(0, caseId.IndexOf '/')

    /// The feature file of a group.
    let fileName (group: string) =
        group
        + ".feature"

    let private q = Json.quote

    let private list (values: string list) =
        values
        |> List.map q
        |> String.concat ", "

    let private count (value: int) (singular: string) =
        let text = value.ToString(CultureInfo.InvariantCulture)

        if value = 1 then
            $"{text} {singular}"
        else
            $"{text} {singular}s"

    let private number (value: int) =
        value.ToString(CultureInfo.InvariantCulture)

    let private seconds (value: decimal) =
        value.ToString(CultureInfo.InvariantCulture)

    /// The tag of the profile a case applies to.
    let profileTag (profile: Profile) =
        "@requires:profile:"
        + profile.WireValue

    /// The tag of an alternative's condition.
    let conditionTag (condition: Condition) =
        match condition with
        | Condition.Bound(bound, true) ->
            "@requires:enforced:"
            + bound.WireValue
        | Condition.Bound(bound, false) ->
            "@requires:not-enforced:"
            + bound.WireValue
        | Condition.HostRuns(profile, true) ->
            "@requires:runs:"
            + profile.WireValue
        | Condition.HostRuns(profile, false) ->
            "@requires:does-not-run:"
            + profile.WireValue

    /// The tag of a bound a case exercises.
    let boundTag (bound: Bound) =
        "@bound:"
        + bound.WireValue

    let private eventStep (matcher: EventMatcher) =
        match matcher with
        | EventMatcher.Started None -> "stderr has an event \"started\""
        | EventMatcher.Started(Some abi) ->
            $"stderr has an event \"started\" with ABI {q abi.WireValue}"
        | EventMatcher.Refused(code, includes) ->
            let code =
                match code with
                | Some code -> $" with code {q code}"
                | None -> ""

            let includes =
                match includes with
                | [] -> ""
                | values -> $" whose message includes {list values}"

            "stderr has an event \"refused\""
            + code
            + includes
        | EventMatcher.Fault None -> "stderr has an event \"fault\""
        | EventMatcher.Fault(Some kind) ->
            $"stderr has an event \"fault\" of kind {q kind.WireValue}"
        | EventMatcher.Output -> "stderr has an event \"output\""

    let private frameSteps (index: int) (matcher: FrameMatcher) =
        let ordinal = number (index + 1)

        match matcher with
        | FrameMatcher.Json value -> [ $"stdout frame {ordinal} equals {Json.compact value}" ]
        | FrameMatcher.SameAsFrame sent -> [
            $"stdout frame {ordinal} equals sent frame {number (sent + 1)}"
          ]
        | FrameMatcher.Pointers checks ->
            checks
            |> List.map (fun check ->
                match check.Test with
                | PointerTest.Equals value ->
                    $"stdout frame {ordinal} has {q check.Pointer} equal to {Json.compact value}"
                | PointerTest.HasType kind ->
                    $"stdout frame {ordinal} has {q check.Pointer} of type {q kind.WireValue}"
            )

    /// The matcher steps of an expectation, in the generator's order.
    let matcherSteps (expect: Expectation) : string list = [
        match expect.ExitCode with
        | Some code -> yield $"the worker exits with status {number code}"
        | None -> ()

        match expect.StdoutBytes with
        | Some bytes ->
            yield
                "stdout has "
                + count bytes "byte"
        | None -> ()

        match expect.StdoutFrames with
        | Some frames ->
            yield
                "stdout has exactly "
                + count frames.Length "frame"

            for index, matcher in List.indexed frames do
                yield! frameSteps index matcher
        | None -> ()

        for matcher in expect.Events do
            yield eventStep matcher

        match expect.NoEvents with
        | [] -> ()
        | events ->
            yield
                $"""stderr has none of the events {list (
                                                       events
                                                       |> List.map (fun event -> event.WireValue)
                                                   )}"""

        match expect.GuestOutput with
        | [] -> ()
        | values -> yield $"the guest output includes in order {list values}"

        match expect.GuestOutputIncludes with
        | [] -> ()
        | values -> yield $"the guest output includes {list values}"

        match expect.NoGuestOutput with
        | [] -> ()
        | values -> yield $"the guest output includes none of {list values}"

        match expect.WithinSeconds with
        | Some timing ->
            let until =
                match timing.Until with
                | Until.Fault -> "the first fault"
                | Until.Exit -> "exit"

            yield
                $"the time to {until} is between {seconds timing.From} and {seconds timing.To} seconds"
        | None -> ()
    ]

    /// The steps that set a case up, before its first run, in the generator's order.
    let setupSteps (case: WasmHostCase) : string list = [
        let m = case.Module

        yield
            $"the module {q m.Name} assembled from {q m.Wat} as {q m.Wasm} with SHA-256 {q m.Sha256}"

        match case.DeclaredAbi with
        | Some abi -> yield $"the worker is given the ABI {q abi.WireValue}"
        | None -> ()

        for argument in case.Worker.Omit do
            yield $"the worker is started without the argument {q argument}"

        match case.Worker.RequestBytes with
        | Some bytes ->
            yield
                "the worker limits requests to "
                + count bytes "byte"
        | None -> ()

        match case.Worker.ResponseBytes with
        | Some bytes ->
            yield
                "the worker limits responses to "
                + count bytes "byte"
        | None -> ()

        match case.Worker.Instructions with
        | Some budget ->
            let text = budget.ToString(CultureInfo.InvariantCulture)
            let noun = if budget = 1L then "unit" else "units"
            yield $"the worker limits instructions to {text} {noun}"
        | None -> ()

        match case.Worker.BuildIdentity with
        | BuildIdentity.Other -> yield "the worker is given another build identity"
        | BuildIdentity.Host -> ()
    ]

    /// The description lines of a scenario: what the case traces to and its gaps.
    let descriptionLines (case: WasmHostCase) : string list = [
        for requirement in case.Tests.Requirements do
            yield $"Requirement: {q requirement}"

        for scenario in case.Tests.Scenarios do
            yield $"Spec scenario: {q scenario}"

        for element in case.Tests.Contract do
            yield $"Contract element: {q element}"

        for gap in case.Gaps do
            yield $"Gap: {q gap}"
    ]

    /// The text of a frame's docstring: the compact JSON that is sent.
    let frameText (frame: Frame) = Json.compact frame.Value

    let private scenario
        (builder: StringBuilder)
        (number: int)
        (case: WasmHostCase)
        (condition: Condition option)
        (runs: (Frame list * Expectation) list)
        =
        let line (text: string) =
            builder.Append(text).Append('\n')
            |> ignore

        let tags = [
            yield profileTag case.AppliesTo

            match condition with
            | Some condition -> yield conditionTag condition
            | None -> ()

            for bound in case.Bounds do
                yield boundTag bound
        ]

        line ""

        line (
            "  "
            + String.concat " " tags
        )

        line $"  Scenario: {Suite.id number} {case.CaseId}"

        match descriptionLines case with
        | [] -> ()
        | lines ->
            for text in lines do
                line (
                    "    "
                    + text
                )

            line ""

        setupSteps case
        |> List.iteri (fun index text ->
            line (
                (if index = 0 then "    Given " else "    And ")
                + text
            )
        )

        for frames, expect in runs do
            line "    When a new worker is started"

            for frame in frames do
                line $"    And the worker is sent the frame {q frame.Name}:"
                line "      \"\"\"json"

                line (
                    "      "
                    + frameText frame
                )

                line "      \"\"\""

            matcherSteps expect
            |> List.iteri (fun index text ->
                line (
                    (if index = 0 then "    Then " else "    And ")
                    + text
                )
            )

    /// The comment lines at the top of every generated feature.
    let headerLines (pin: Pin) = [
        "# Generated from the pinned WASM host corpus. Do not edit this file by hand."
        $"# Change the corpus in finos/morphir, move the pin, then run: {Suite.RegenerateCommand}"
        $"# Corpus: {pin.Corpus}"
        $"# Commit: {pin.Commit}"
        $"# SHA-256: {pin.Sha256}"
    ]

    /// The feature name of a group.
    let featureName (group: string) =
        "WASM host cases: "
        + group

    /// The description lines of a feature: the kit and the contract it checks.
    let kitLines (kit: KitIdentity) = [
        $"Kit: {q kit.Kit} {q kit.Version}"
        $"Contract: {q kit.ContractId} {q kit.ContractVersion} {q kit.ContractPath}"
        $"Deadline margin: {seconds kit.DeadlineMarginSeconds} seconds"
    ]

    /// The text of the feature of one group, with LF line endings. The cases are given with their
    /// numbers. A frame whose text a docstring cannot hold is an error.
    let feature
        (pin: Pin)
        (kit: KitIdentity)
        (group: string)
        (cases: (int * WasmHostCase) list)
        : Result<string, CorpusIssue list> =
        let multiRun =
            cases
            |> List.filter (fun (_, case) ->
                case.Runs.Length > 1
                && case.Runs
                   |> List.exists (fun run ->
                       match run.Expect with
                       | RunExpectation.Alternatives _ -> true
                       | RunExpectation.Always _ -> false
                   )
            )
            |> List.map (fun (_, case) -> {
                Code = "mck.unsupported_case"
                Path = pin.Corpus
                Line = 0
                Message =
                    $"Vocabulary {Suite.VocabularyVersion} writes alternatives only for a case with one run: {case.CaseId}"
            })

        let docstrings =
            cases
            |> List.collect (fun (_, case) ->
                case.Runs
                |> List.collect (fun run -> run.Frames)
                |> List.filter (fun frame -> (frameText frame).Contains "\\\"\\\"\\\"")
                |> List.map (fun frame -> {
                    Code = "mck.unsupported_case"
                    Path = pin.Corpus
                    Line = 0
                    Message =
                        $"The frame \"{frame.Name}\" of {case.CaseId} holds three escaped quotes, which a Gherkin docstring cannot keep"
                })
            )

        let unwritable =
            multiRun
            @ docstrings

        if not unwritable.IsEmpty then
            Error unwritable
        else
            let builder = StringBuilder()

            let line (text: string) =
                builder.Append(text).Append('\n')
                |> ignore

            for text in headerLines pin do
                line text

            line (
                "@suite:"
                + Suite.Name
            )

            line (
                "Feature: "
                + featureName group
            )

            for text in kitLines kit do
                line (
                    "  "
                    + text
                )

            for caseNumber, case in
                cases
                |> List.sortBy fst do
                match case.Runs with
                | [ {
                        Frames = frames
                        Expect = RunExpectation.Alternatives alternatives
                    } ] ->
                    for alternative in alternatives do
                        scenario builder caseNumber case (Some alternative.When) [
                            (frames, alternative.Expect)
                        ]
                | runs ->
                    runs
                    |> List.map (fun run ->
                        match run.Expect with
                        | RunExpectation.Always expect -> run.Frames, expect
                        // Refused above: alternatives are written only for a case with one run.
                        | RunExpectation.Alternatives _ ->
                            failwith "Alternatives need a case with one run"
                    )
                    |> scenario builder caseNumber case None

            Ok(builder.ToString())
