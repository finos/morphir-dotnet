// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.WasmHost

open System
open System.Globalization
open System.IO
open System.Text
open System.Text.RegularExpressions
open Gherkin
open Gherkin.Ast
open Morphir.Conformance

/// Reads the generated features of the suite into the case model, the runner's input. The
/// features use step vocabulary 1.2 (conformance/steps/steps-1.2.md). A step outside the
/// vocabulary is refused with its file and line, and a feature that is not exactly what the
/// generator writes for its own cases is refused.
module FeatureReader =
    /// A step of vocabulary 1.2, with its arguments read.
    [<RequireQualifiedAccess>]
    type private Step =
        | Module of ModuleRef
        | Abi of Profile
        | Omit of string
        | RequestBytes of int
        | ResponseBytes of int
        | OtherIdentity
        | StartWorker
        | SendFrame of name: string
        | ExitStatus of int
        | StdoutBytes of int
        | FrameCount of int
        | FrameJson of ordinal: int * JsonValue
        | FrameSameAs of ordinal: int * sent: int
        | FramePointer of ordinal: int * PointerCheck
        | Event of EventMatcher
        | NoEvents of WorkerEvent list
        | GuestInOrder of string list
        | GuestIncludes of string list
        | GuestNone of string list
        | Timing of Timing

    let private literal = "\"(?:[^\"\\\\\\u0000-\\u001f]|\\\\.)*\""
    let private literalPattern = Regex(literal)
    let private listOf = $"({literal}(?:, {literal})*)"
    let private integer = "(0|[1-9][0-9]*)"
    let private decimalNumber = "((?:0|[1-9][0-9]*)(?:\\.[0-9]+)?)"
    let private q = $"({literal})"

    let private pattern (text: string) =
        Regex(
            "^"
            + text
            + "$",
            RegexOptions.CultureInvariant
        )

    let private patterns = {|
        Module = pattern $"the module {q} assembled from {q} as {q} with SHA-256 {q}"
        Abi = pattern $"the worker is given the ABI {q}"
        Omit = pattern $"the worker is started without the argument {q}"
        RequestBytes = pattern $"the worker limits requests to {integer} bytes?"
        ResponseBytes = pattern $"the worker limits responses to {integer} bytes?"
        OtherIdentity = pattern "the worker is given another build identity"
        StartWorker = pattern "a new worker is started"
        SendFrame = pattern $"the worker is sent the frame {q}:"
        ExitStatus = pattern $"the worker exits with status {integer}"
        StdoutBytes = pattern $"stdout has {integer} bytes?"
        FrameCount = pattern $"stdout has exactly {integer} frames?"
        FrameSameAs = pattern $"stdout frame {integer} equals sent frame {integer}"
        FrameJson = pattern $"stdout frame {integer} equals (.+)"
        FrameType = pattern $"stdout frame {integer} has {q} of type {q}"
        FrameEquals = pattern $"stdout frame {integer} has {q} equal to (.+)"
        Started = pattern $"stderr has an event \"started\"(?: with ABI {q})?"
        Refused =
            pattern
                $"stderr has an event \"refused\"(?: with code {q})?(?: whose message includes {listOf})?"
        Fault = pattern $"stderr has an event \"fault\"(?: of kind {q})?"
        Output = pattern "stderr has an event \"output\""
        NoEvents = pattern $"stderr has none of the events {listOf}"
        GuestInOrder = pattern $"the guest output includes in order {listOf}"
        GuestNone = pattern $"the guest output includes none of {listOf}"
        GuestIncludes = pattern $"the guest output includes {listOf}"
        TimeToFault =
            pattern
                $"the time to the first fault is between {decimalNumber} and {decimalNumber} seconds"
        TimeToExit =
            pattern $"the time to exit is between {decimalNumber} and {decimalNumber} seconds"
    |}

    let private unquote (text: string) =
        match Json.parse text with
        | Ok(JsonValue.String value) -> Ok value
        | _ -> Error $"Not a JSON string: {text}"

    let private unquoteAll (text: string) =
        literalPattern.Matches text
        |> Seq.map (fun found -> unquote found.Value)
        |> Seq.toList
        |> ResultSyntax.all

    let private int32 (text: string) =
        match Int32.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture) with
        | true, value -> Ok value
        | _ -> Error $"The number {text} is too large"

    let private ordinal (text: string) =
        int32 text
        |> Result.bind (fun value ->
            if value < 1 then
                Error "Frames are counted from 1"
            else
                Ok value
        )

    let private parsedWith (parse: string -> Result<'T, string>) (text: string) =
        unquote text
        |> Result.bind parse

    /// Reads a step text. None means the text is not a step of the vocabulary.
    let private readStep (text: string) : Result<Step, string> option =
        let attempt (regex: Regex) (build: GroupCollection -> Result<Step, string>) =
            let found = regex.Match text
            if found.Success then Some(build found.Groups) else None

        let group (groups: GroupCollection) (index: int) = groups[index].Value

        let optionalGroup (groups: GroupCollection) (index: int) =
            if groups[index].Success then
                Some groups[index].Value
            else
                None

        let readers: (unit -> Result<Step, string> option) list = [
            fun () ->
                attempt
                    patterns.Module
                    (fun groups ->
                        ResultSyntax.result {
                            let! name = unquote (group groups 1)
                            let! wat = unquote (group groups 2)
                            let! wasm = unquote (group groups 3)
                            let! sha = unquote (group groups 4)

                            return
                                Step.Module
                                    {
                                        Name = name
                                        Wat = wat
                                        Wasm = wasm
                                        Sha256 = sha
                                    }
                        }
                    )
            fun () ->
                attempt
                    patterns.Abi
                    (fun groups ->
                        parsedWith Profile.Parse (group groups 1)
                        |> Result.map Step.Abi
                    )
            fun () ->
                attempt
                    patterns.Omit
                    (fun groups ->
                        unquote (group groups 1)
                        |> Result.map Step.Omit
                    )
            fun () ->
                attempt
                    patterns.RequestBytes
                    (fun groups ->
                        int32 (group groups 1)
                        |> Result.map Step.RequestBytes
                    )
            fun () ->
                attempt
                    patterns.ResponseBytes
                    (fun groups ->
                        int32 (group groups 1)
                        |> Result.map Step.ResponseBytes
                    )
            fun () -> attempt patterns.OtherIdentity (fun _ -> Ok Step.OtherIdentity)
            fun () -> attempt patterns.StartWorker (fun _ -> Ok Step.StartWorker)
            fun () ->
                attempt
                    patterns.SendFrame
                    (fun groups ->
                        unquote (group groups 1)
                        |> Result.map Step.SendFrame
                    )
            fun () ->
                attempt
                    patterns.ExitStatus
                    (fun groups ->
                        int32 (group groups 1)
                        |> Result.map Step.ExitStatus
                    )
            fun () ->
                attempt
                    patterns.StdoutBytes
                    (fun groups ->
                        int32 (group groups 1)
                        |> Result.map Step.StdoutBytes
                    )
            fun () ->
                attempt
                    patterns.FrameCount
                    (fun groups ->
                        int32 (group groups 1)
                        |> Result.map Step.FrameCount
                    )
            fun () ->
                attempt
                    patterns.FrameSameAs
                    (fun groups ->
                        ResultSyntax.result {
                            let! frame = ordinal (group groups 1)
                            let! sent = ordinal (group groups 2)
                            return Step.FrameSameAs(frame, sent - 1)
                        }
                    )
            fun () ->
                attempt
                    patterns.FrameType
                    (fun groups ->
                        ResultSyntax.result {
                            let! frame = ordinal (group groups 1)
                            let! pointer = unquote (group groups 2)
                            let! kind = parsedWith JsonType.Parse (group groups 3)

                            return
                                Step.FramePointer(
                                    frame,
                                    {
                                        Pointer = pointer
                                        Test = PointerTest.HasType kind
                                    }
                                )
                        }
                    )
            fun () ->
                attempt
                    patterns.FrameEquals
                    (fun groups ->
                        ResultSyntax.result {
                            let! frame = ordinal (group groups 1)
                            let! pointer = unquote (group groups 2)
                            let! value = Json.parse (group groups 3)

                            return
                                Step.FramePointer(
                                    frame,
                                    {
                                        Pointer = pointer
                                        Test = PointerTest.Equals value
                                    }
                                )
                        }
                    )
            fun () ->
                attempt
                    patterns.FrameJson
                    (fun groups ->
                        ResultSyntax.result {
                            let! frame = ordinal (group groups 1)
                            let! value = Json.parse (group groups 2)
                            return Step.FrameJson(frame, value)
                        }
                    )
            fun () ->
                attempt
                    patterns.Started
                    (fun groups ->
                        match optionalGroup groups 1 with
                        | None -> Ok(Step.Event(EventMatcher.Started None))
                        | Some abi ->
                            parsedWith Profile.Parse abi
                            |> Result.map (fun abi -> Step.Event(EventMatcher.Started(Some abi)))
                    )
            fun () ->
                attempt
                    patterns.Refused
                    (fun groups ->
                        ResultSyntax.result {
                            let! code =
                                match optionalGroup groups 1 with
                                | None -> Ok None
                                | Some code ->
                                    unquote code
                                    |> Result.map Some

                            let! includes =
                                match optionalGroup groups 2 with
                                | None -> Ok []
                                | Some values -> unquoteAll values

                            return Step.Event(EventMatcher.Refused(code, includes))
                        }
                    )
            fun () ->
                attempt
                    patterns.Fault
                    (fun groups ->
                        match optionalGroup groups 1 with
                        | None -> Ok(Step.Event(EventMatcher.Fault None))
                        | Some kind ->
                            parsedWith FaultKind.Parse kind
                            |> Result.map (fun kind -> Step.Event(EventMatcher.Fault(Some kind)))
                    )
            fun () -> attempt patterns.Output (fun _ -> Ok(Step.Event EventMatcher.Output))
            fun () ->
                attempt
                    patterns.NoEvents
                    (fun groups ->
                        unquoteAll (group groups 1)
                        |> Result.bind (
                            List.map WorkerEvent.Parse
                            >> ResultSyntax.all
                        )
                        |> Result.map Step.NoEvents
                    )
            fun () ->
                attempt
                    patterns.GuestInOrder
                    (fun groups ->
                        unquoteAll (group groups 1)
                        |> Result.map Step.GuestInOrder
                    )
            fun () ->
                attempt
                    patterns.GuestNone
                    (fun groups ->
                        unquoteAll (group groups 1)
                        |> Result.map Step.GuestNone
                    )
            fun () ->
                attempt
                    patterns.GuestIncludes
                    (fun groups ->
                        unquoteAll (group groups 1)
                        |> Result.map Step.GuestIncludes
                    )
            fun () ->
                attempt
                    patterns.TimeToFault
                    (fun groups ->
                        Ok(
                            Step.Timing
                                {
                                    Until = Until.Fault
                                    From =
                                        Decimal.Parse(group groups 1, CultureInfo.InvariantCulture)
                                    To =
                                        Decimal.Parse(group groups 2, CultureInfo.InvariantCulture)
                                }
                        )
                    )
            fun () ->
                attempt
                    patterns.TimeToExit
                    (fun groups ->
                        Ok(
                            Step.Timing
                                {
                                    Until = Until.Exit
                                    From =
                                        Decimal.Parse(group groups 1, CultureInfo.InvariantCulture)
                                    To =
                                        Decimal.Parse(group groups 2, CultureInfo.InvariantCulture)
                                }
                        )
                    )
        ]

        readers
        |> List.tryPick (fun read -> read ())

    /// A scenario read from a feature, before the scenarios of one case are joined.
    type private ParsedScenario = {
        Number: int
        CaseId: string
        Line: int
        Condition: Condition option
        Case: WasmHostCase
        Runs: (Frame list * Expectation) list
    }

    type private Problem = {
        Code: string
        Line: int
        Message: string
    }

    let private problem code line message =
        Error
            {
                Code = code
                Line = line
                Message = message
            }

    let private emptyExpectation = {
        ExitCode = None
        StdoutBytes = None
        StdoutFrames = None
        Events = []
        NoEvents = []
        GuestOutput = []
        GuestOutputIncludes = []
        NoGuestOutput = []
        WithinSeconds = None
    }

    /// Builds the expectation of a run from its matcher steps, in step order.
    let private expectation
        (frameCount: int)
        (steps: (int * Step) list)
        : Result<Expectation, Problem> =
        let once line name (current: 'T option) (value: 'T) =
            match current with
            | Some _ -> problem "mck.invalid_case" line $"A run has at most one {name} step"
            | None -> Ok(Some value)

        let onceList line name (current: 'T list) (value: 'T list) =
            match current with
            | [] -> Ok value
            | _ -> problem "mck.invalid_case" line $"A run has at most one {name} step"

        let folder
            (state: Result<Expectation * (int * FrameMatcher) list * int option, Problem>)
            (line: int, step: Step)
            =
            state
            |> Result.bind (fun (expect, frames, count) ->
                let frameStep ordinal (matcher: FrameMatcher) =
                    match count with
                    | None ->
                        problem
                            "mck.invalid_case"
                            line
                            "A stdout frame step needs the stdout frame count step before it"
                    | Some count when ordinal > count ->
                        problem
                            "mck.invalid_case"
                            line
                            $"stdout has {count} frames, so frame {ordinal} does not exist"
                    | Some _ ->
                        match frames with
                        | (last, _) :: _ when ordinal < last ->
                            problem "mck.invalid_case" line "Stdout frames are described in order"
                        | (last, FrameMatcher.Pointers checks) :: rest when last = ordinal ->
                            match matcher with
                            | FrameMatcher.Pointers more ->
                                Ok(
                                    expect,
                                    (ordinal,
                                     FrameMatcher.Pointers(
                                         checks
                                         @ more
                                     ))
                                    :: rest,
                                    count
                                )
                            | _ ->
                                problem
                                    "mck.invalid_case"
                                    line
                                    $"Frame {ordinal} has pointer checks and another matcher"
                        | (last, _) :: _ when last = ordinal ->
                            problem
                                "mck.invalid_case"
                                line
                                $"Frame {ordinal} has more than one matcher"
                        | _ ->
                            Ok(
                                expect,
                                (ordinal, matcher)
                                :: frames,
                                count
                            )

                match step with
                | Step.ExitStatus code ->
                    once line "exit status" expect.ExitCode code
                    |> Result.map (fun value -> { expect with ExitCode = value }, frames, count)
                | Step.StdoutBytes bytes ->
                    once line "stdout bytes" expect.StdoutBytes bytes
                    |> Result.map (fun value -> { expect with StdoutBytes = value }, frames, count)
                | Step.FrameCount value ->
                    once line "stdout frame count" count value
                    |> Result.map (fun value -> expect, frames, value)
                | Step.FrameJson(ordinal, value) -> frameStep ordinal (FrameMatcher.Json value)
                | Step.FrameSameAs(ordinal, sent) ->
                    if
                        sent
                        >= frameCount
                    then
                        problem
                            "mck.invalid_case"
                            line
                            $"The run sends {frameCount} frames, so sent frame {sent + 1} does not exist"
                    else
                        frameStep ordinal (FrameMatcher.SameAsFrame sent)
                | Step.FramePointer(ordinal, check) ->
                    frameStep ordinal (FrameMatcher.Pointers [ check ])
                | Step.Event matcher ->
                    Ok(
                        { expect with
                            Events =
                                expect.Events
                                @ [ matcher ]
                        },
                        frames,
                        count
                    )
                | Step.NoEvents events ->
                    onceList line "absent events" expect.NoEvents events
                    |> Result.map (fun value -> { expect with NoEvents = value }, frames, count)
                | Step.GuestInOrder values ->
                    onceList line "guest output in order" expect.GuestOutput values
                    |> Result.map (fun value -> { expect with GuestOutput = value }, frames, count)
                | Step.GuestIncludes values ->
                    onceList line "guest output includes" expect.GuestOutputIncludes values
                    |> Result.map (fun value ->
                        { expect with
                            GuestOutputIncludes = value
                        },
                        frames,
                        count
                    )
                | Step.GuestNone values ->
                    onceList line "guest output includes none" expect.NoGuestOutput values
                    |> Result.map (fun value ->
                        { expect with NoGuestOutput = value }, frames, count
                    )
                | Step.Timing timing ->
                    if timing.From > timing.To then
                        problem "mck.invalid_case" line "A range starts at most where it ends"
                    else
                        once line "timing" expect.WithinSeconds timing
                        |> Result.map (fun value ->
                            { expect with WithinSeconds = value }, frames, count
                        )
                | _ ->
                    problem "mck.invalid_case" line "Only matcher steps follow the frames of a run"
            )

        let lastLine =
            steps
            |> List.tryLast
            |> Option.map fst
            |> Option.defaultValue 0

        steps
        |> List.fold folder (Ok(emptyExpectation, [], None))
        |> Result.bind (fun (expect, frames, count) ->
            match count with
            | None -> Ok expect
            | Some count ->
                let frames = List.rev frames

                if
                    frames
                    |> List.map fst
                    <> [ 1..count ]
                then
                    problem
                        "mck.invalid_case"
                        lastLine
                        $"Each of the {count} stdout frames needs a matcher"
                else
                    Ok
                        { expect with
                            StdoutFrames =
                                Some(
                                    frames
                                    |> List.map snd
                                )
                        }
        )

    let private scenarioName =
        Regex("^wasm-host-([0-9]{4}) ([a-z0-9][a-z0-9._-]*(?:/[a-z0-9][a-z0-9._-]*)+)$")

    let private descriptionLine =
        Regex($"^(Requirement|Spec scenario|Contract element|Gap): ({literal})$")

    let private descriptionOf (text: string) =
        if isNull text then
            []
        else
            text.Split('\n')
            |> Array.map (fun line -> line.Trim())
            |> Array.filter (fun line -> line <> "")
            |> Array.toList

    /// Reads one scenario: its id, tags, description and steps.
    let private readScenario (scenario: Scenario) : Result<ParsedScenario, Problem> =
        let line = scenario.Location.Line
        let name = scenarioName.Match scenario.Name

        if
            scenario.Keyword.Trim()
            <> "Scenario"
            || not (Seq.isEmpty scenario.Examples)
        then
            problem "mck.invalid_case" line "The suite has plain scenarios, without Examples"
        elif not name.Success then
            problem
                "mck.invalid_case"
                line
                "A scenario is named wasm-host-NNNN and its upstream case id"
        else
            ResultSyntax.result {
                let number = Int32.Parse(name.Groups[1].Value, CultureInfo.InvariantCulture)
                let caseId = name.Groups[2].Value

                let tagFolder state (tag: Tag) =
                    state
                    |> Result.bind (fun (profile, condition, bounds) ->
                        let after (prefix: string) =
                            if tag.Name.StartsWith(prefix, StringComparison.Ordinal) then
                                Some(tag.Name.Substring prefix.Length)
                            else
                                None

                        let fromTag parse value =
                            match parse value with
                            | Ok parsed -> Ok parsed
                            | Error message ->
                                problem "mck.unsupported_tag" tag.Location.Line message

                        let setCondition (value: Condition) =
                            match condition with
                            | Some _ ->
                                problem
                                    "mck.unsupported_tag"
                                    tag.Location.Line
                                    "A scenario has at most one alternative condition"
                            | None -> Ok(profile, Some value, bounds)

                        match after "@requires:profile:" with
                        | Some value ->
                            match profile with
                            | Some _ ->
                                problem
                                    "mck.unsupported_tag"
                                    tag.Location.Line
                                    "A scenario has one @requires:profile: tag"
                            | None ->
                                fromTag Profile.Parse value
                                |> Result.map (fun parsed -> Some parsed, condition, bounds)
                        | None ->
                            match
                                after "@requires:enforced:",
                                after "@requires:not-enforced:",
                                after "@requires:runs:",
                                after "@requires:does-not-run:",
                                after "@bound:"
                            with
                            | Some value, _, _, _, _ ->
                                fromTag Bound.Parse value
                                |> Result.bind (fun bound ->
                                    setCondition (Condition.Bound(bound, true))
                                )
                            | _, Some value, _, _, _ ->
                                fromTag Bound.Parse value
                                |> Result.bind (fun bound ->
                                    setCondition (Condition.Bound(bound, false))
                                )
                            | _, _, Some value, _, _ ->
                                fromTag Profile.Parse value
                                |> Result.bind (fun profile ->
                                    setCondition (Condition.HostRuns(profile, true))
                                )
                            | _, _, _, Some value, _ ->
                                fromTag Profile.Parse value
                                |> Result.bind (fun profile ->
                                    setCondition (Condition.HostRuns(profile, false))
                                )
                            | _, _, _, _, Some value ->
                                fromTag Bound.Parse value
                                |> Result.map (fun bound ->
                                    profile,
                                    condition,
                                    bounds
                                    @ [ bound ]
                                )
                            | _ ->
                                problem
                                    "mck.unsupported_tag"
                                    tag.Location.Line
                                    $"Tag is outside vocabulary {Suite.VocabularyVersion}: {tag.Name}"
                    )

                let! profile, condition, bounds =
                    scenario.Tags
                    |> Seq.fold tagFolder (Ok(None, None, []))

                let! appliesTo =
                    match profile with
                    | Some profile -> Ok profile
                    | None ->
                        problem
                            "mck.invalid_case"
                            line
                            "A scenario needs its @requires:profile: tag"

                let descriptionFolder state (text: string) =
                    state
                    |> Result.bind (fun (tests: CaseTests, gaps: string list) ->
                        let found = descriptionLine.Match text

                        if not found.Success then
                            problem
                                "mck.invalid_case"
                                line
                                $"The description line is outside vocabulary {Suite.VocabularyVersion}: {text}"
                        else
                            match unquote found.Groups[2].Value with
                            | Error message -> problem "mck.invalid_case" line message
                            | Ok value ->
                                match found.Groups[1].Value with
                                | "Requirement" ->
                                    Ok(
                                        { tests with
                                            Requirements =
                                                tests.Requirements
                                                @ [ value ]
                                        },
                                        gaps
                                    )
                                | "Spec scenario" ->
                                    Ok(
                                        { tests with
                                            Scenarios =
                                                tests.Scenarios
                                                @ [ value ]
                                        },
                                        gaps
                                    )
                                | "Contract element" ->
                                    Ok(
                                        { tests with
                                            Contract =
                                                tests.Contract
                                                @ [ value ]
                                        },
                                        gaps
                                    )
                                | _ ->
                                    Ok(
                                        tests,
                                        gaps
                                        @ [ value ]
                                    )
                    )

                let! tests, gaps =
                    descriptionOf scenario.Description
                    |> List.fold
                        descriptionFolder
                        (Ok(
                            {
                                Requirements = []
                                Scenarios = []
                                Contract = []
                            },
                            []
                        ))

                let readOne (step: Gherkin.Ast.Step) =
                    match readStep step.Text with
                    | None ->
                        problem
                            "mck.unknown_step"
                            step.Location.Line
                            $"Step is not in vocabulary {Suite.VocabularyVersion} of the {Suite.Name} suite: {step.Text}"
                    | Some(Error message) -> problem "mck.invalid_case" step.Location.Line message
                    | Some(Ok parsed) ->
                        match parsed, step.Argument with
                        | Step.SendFrame _, (:? DocString as text) when text.ContentType = "json" ->
                            match Json.parse text.Content with
                            | Ok value -> Ok(step.Location.Line, parsed, Some value)
                            | Error message ->
                                problem
                                    "mck.invalid_case"
                                    step.Location.Line
                                    $"The frame is not JSON: {message}"
                        | Step.SendFrame _, _ ->
                            problem
                                "mck.invalid_case"
                                step.Location.Line
                                "A frame step takes a json docstring"
                        | _, null -> Ok(step.Location.Line, parsed, None)
                        | _ ->
                            problem
                                "mck.invalid_case"
                                step.Location.Line
                                "This step does not take an argument"

                let! steps =
                    scenario.Steps
                    |> Seq.map readOne
                    |> Seq.toList
                    |> ResultSyntax.all

                let setup =
                    steps
                    |> List.takeWhile (fun (_, step, _) ->
                        step
                        <> Step.StartWorker
                    )

                let rest =
                    steps
                    |> List.skipWhile (fun (_, step, _) ->
                        step
                        <> Step.StartWorker
                    )

                let setupFolder state (stepLine: int, step: Step, _) =
                    state
                    |> Result.bind (fun
                                        (moduleRef: ModuleRef option,
                                         abi: Profile option,
                                         worker: WorkerSettings) ->
                        let once (present: bool) name =
                            if present then
                                problem
                                    "mck.invalid_case"
                                    stepLine
                                    $"A scenario has at most one {name} step"
                            else
                                Ok()

                        match step with
                        | Step.Module found ->
                            once moduleRef.IsSome "module"
                            |> Result.map (fun () -> Some found, abi, worker)
                        | Step.Abi found ->
                            once abi.IsSome "ABI"
                            |> Result.map (fun () -> moduleRef, Some found, worker)
                        | Step.Omit argument ->
                            Ok(
                                moduleRef,
                                abi,
                                { worker with
                                    Omit =
                                        worker.Omit
                                        @ [ argument ]
                                }
                            )
                        | Step.RequestBytes bytes ->
                            once worker.RequestBytes.IsSome "request limit"
                            |> Result.map (fun () ->
                                moduleRef,
                                abi,
                                { worker with
                                    RequestBytes = Some bytes
                                }
                            )
                        | Step.ResponseBytes bytes ->
                            once worker.ResponseBytes.IsSome "response limit"
                            |> Result.map (fun () ->
                                moduleRef,
                                abi,
                                { worker with
                                    ResponseBytes = Some bytes
                                }
                            )
                        | Step.OtherIdentity ->
                            once (worker.BuildIdentity = BuildIdentity.Other) "build identity"
                            |> Result.map (fun () ->
                                moduleRef,
                                abi,
                                { worker with
                                    BuildIdentity = BuildIdentity.Other
                                }
                            )
                        | _ ->
                            problem
                                "mck.invalid_case"
                                stepLine
                                "A matcher or frame step needs a worker started before it"
                    )

                let! moduleRef, abi, worker =
                    setup
                    |> List.fold setupFolder (Ok(None, None, WorkerSettings.defaults))

                let! moduleRef =
                    match moduleRef with
                    | Some found -> Ok found
                    | None -> problem "mck.invalid_case" line "A scenario declares its module"

                // Split the rest into runs: each starts with the worker step, then its frames, then its matchers.
                let runs =
                    rest
                    |> List.fold
                        (fun runs (stepLine, step, argument) ->
                            match step, runs with
                            | Step.StartWorker, _ -> [] :: runs
                            | _, current :: earlier ->
                                ((stepLine, step, argument)
                                 :: current)
                                :: earlier
                            | _, [] -> runs
                        )
                        []
                    |> List.map List.rev
                    |> List.rev

                let readRun (run: (int * Step * JsonValue option) list) =
                    let frames =
                        run
                        |> List.takeWhile (fun (_, step, _) ->
                            match step with
                            | Step.SendFrame _ -> true
                            | _ -> false
                        )

                    let matchers =
                        run
                        |> List.skip frames.Length

                    match
                        matchers
                        |> List.tryFind (fun (_, step, _) ->
                            match step with
                            | Step.SendFrame _
                            | Step.Module _
                            | Step.Abi _
                            | Step.Omit _
                            | Step.RequestBytes _
                            | Step.ResponseBytes _
                            | Step.OtherIdentity -> true
                            | _ -> false
                        )
                    with
                    | Some(stepLine, _, _) ->
                        problem
                            "mck.invalid_case"
                            stepLine
                            "The frames of a run come before its matchers, and set-up steps before the first run"
                    | None ->
                        let sent =
                            frames
                            |> List.choose (fun (_, step, value) ->
                                match step, value with
                                | Step.SendFrame name, Some value ->
                                    Some { Name = name; Value = value }
                                | _ -> None
                            )

                        expectation
                            sent.Length
                            (matchers
                             |> List.map (fun (stepLine, step, _) -> stepLine, step))
                        |> Result.map (fun expect -> sent, expect)

                let! runs =
                    runs
                    |> List.map readRun
                    |> ResultSyntax.all

                if runs.IsEmpty then
                    return!
                        problem
                            "mck.invalid_case"
                            line
                            "A scenario needs a run: a new worker is started"
                else
                    return {
                        Number = number
                        CaseId = caseId
                        Line = line
                        Condition = condition
                        Runs = runs
                        Case = {
                            CaseId = caseId
                            AppliesTo = appliesTo
                            Module = moduleRef
                            DeclaredAbi = abi
                            Worker = worker
                            Bounds = bounds
                            Tests = tests
                            Runs = []
                            Gaps = gaps
                        }
                    }
            }

    /// Joins the scenarios of one case: one scenario, or one for each alternative.
    let private join (scenarios: ParsedScenario list) : Result<int * int * WasmHostCase, Problem> =
        let first = List.head scenarios

        if
            scenarios
            |> List.exists (fun scenario ->
                scenario.Case
                <> first.Case
            )
        then
            problem
                "mck.invalid_case"
                first.Line
                $"The scenarios of {first.CaseId} differ outside their alternatives"
        else
            match scenarios with
            | [ { Condition = None } ] ->
                Ok(
                    first.Number,
                    first.Line,
                    { first.Case with
                        Runs =
                            first.Runs
                            |> List.map (fun (frames, expect) -> {
                                Frames = frames
                                Expect = RunExpectation.Always expect
                            })
                    }
                )
            | _ when
                scenarios
                |> List.forall (fun scenario ->
                    scenario.Condition.IsSome
                    && scenario.Runs.Length = 1
                )
                ->
                let frames = fst first.Runs.Head

                if
                    scenarios
                    |> List.exists (fun scenario ->
                        fst scenario.Runs.Head
                        <> frames
                    )
                then
                    problem
                        "mck.invalid_case"
                        first.Line
                        $"The alternatives of {first.CaseId} send other frames"
                else
                    let alternatives =
                        scenarios
                        |> List.choose (fun scenario ->
                            scenario.Condition
                            |> Option.map (fun condition -> {
                                When = condition
                                Expect = snd scenario.Runs.Head
                            })
                        )

                    if
                        (alternatives
                         |> List.map (fun alternative -> alternative.When)
                         |> List.distinct)
                            .Length
                        <> alternatives.Length
                    then
                        problem
                            "mck.invalid_case"
                            first.Line
                            $"The alternatives of {first.CaseId} repeat a condition"
                    else
                        Ok(
                            first.Number,
                            first.Line,
                            { first.Case with
                                Runs = [
                                    {
                                        Frames = frames
                                        Expect = RunExpectation.Alternatives alternatives
                                    }
                                ]
                            }
                        )
            | _ ->
                problem
                    "mck.invalid_case"
                    first.Line
                    $"{first.CaseId} has more than one scenario, so each one is an alternative with one run and a condition tag"

    let private headerPattern = Regex("^# (Corpus|Commit|SHA-256): (\\S+)$")

    let private kitPatterns = {|
        Kit = Regex($"^Kit: ({literal}) ({literal})$")
        Contract = Regex($"^Contract: ({literal}) ({literal}) ({literal})$")
        Margin = Regex($"^Deadline margin: {decimalNumber} seconds$")
    |}

    /// Reads one feature into its pin, kit and numbered cases.
    let private readFeature
        (path: string)
        (bytes: byte array)
        : Result<Pin * KitIdentity * string * (int * int * WasmHostCase) list, CorpusIssue list> =
        let issue (found: Problem) = {
            Code = found.Code
            Path = path
            Line = found.Line
            Message = found.Message
        }

        let fail code line message =
            Error [
                issue
                    {
                        Code = code
                        Line = line
                        Message = message
                    }
            ]

        let group = Path.GetFileNameWithoutExtension path

        let parsed =
            try
                use reader = new StringReader(Encoding.UTF8.GetString bytes)
                Ok(Parser().Parse reader)
            with
            | :? CompositeParserException as error ->
                Error(
                    error.Errors
                    |> Seq.map (fun inner ->
                        issue
                            {
                                Code = "mck.invalid_gherkin"
                                Line =
                                    (if inner.Location.HasValue then
                                         inner.Location.Value.Line
                                     else
                                         1)
                                Message = inner.Message
                            }
                    )
                    |> Seq.toList
                )
            | :? ParserException as error ->
                Error [
                    issue
                        {
                            Code = "mck.invalid_gherkin"
                            Line =
                                (if error.Location.HasValue then
                                     error.Location.Value.Line
                                 else
                                     1)
                            Message = error.Message
                        }
                ]

        match parsed with
        | Error issues -> Error issues
        | Ok document when isNull document.Feature ->
            fail "mck.invalid_case" 1 "A feature is required"
        | Ok document ->
            let feature = document.Feature

            let header =
                document.Comments
                |> Seq.filter (fun comment -> comment.Location.Line < feature.Location.Line)
                |> Seq.choose (fun comment ->
                    let found = headerPattern.Match(comment.Text.Trim())

                    if found.Success then
                        Some(found.Groups[1].Value, found.Groups[2].Value)
                    else
                        None
                )
                |> Map.ofSeq

            let kitLines = descriptionOf feature.Description

            let kit =
                match kitLines with
                | [ kitLine; contractLine; marginLine ] ->
                    let kitMatch = kitPatterns.Kit.Match kitLine
                    let contractMatch = kitPatterns.Contract.Match contractLine
                    let marginMatch = kitPatterns.Margin.Match marginLine

                    if
                        kitMatch.Success
                        && contractMatch.Success
                        && marginMatch.Success
                    then
                        ResultSyntax.result {
                            let! kitName = unquote kitMatch.Groups[1].Value
                            let! version = unquote kitMatch.Groups[2].Value
                            let! contractId = unquote contractMatch.Groups[1].Value
                            let! contractVersion = unquote contractMatch.Groups[2].Value
                            let! contractPath = unquote contractMatch.Groups[3].Value

                            return {
                                Kit = kitName
                                Version = version
                                ContractId = contractId
                                ContractVersion = contractVersion
                                ContractPath = contractPath
                                DeadlineMarginSeconds =
                                    Decimal.Parse(
                                        marginMatch.Groups[1].Value,
                                        CultureInfo.InvariantCulture
                                    )
                            }
                        }
                    else
                        Error "The feature describes its kit, contract and deadline margin"
                | _ -> Error "The feature describes its kit, contract and deadline margin"

            match
                Map.tryFind "Corpus" header,
                Map.tryFind "Commit" header,
                Map.tryFind "SHA-256" header,
                kit
            with
            | None, _, _, _
            | _, None, _, _
            | _, _, None, _ ->
                fail
                    "mck.not_generated"
                    1
                    "A generated feature starts with the comments that name its corpus, commit and SHA-256"
            | _, _, _, Error message -> fail "mck.invalid_case" feature.Location.Line message
            | Some corpus, Some commit, Some sha, Ok kit ->
                let pin = {
                    Corpus = corpus
                    Commit = commit
                    Sha256 = sha
                }

                let tags =
                    feature.Tags
                    |> Seq.map (fun tag -> tag.Name)
                    |> Seq.toList

                if
                    tags
                    <> [
                        "@suite:"
                        + Suite.Name
                    ]
                then
                    fail
                        "mck.unsupported_tag"
                        feature.Location.Line
                        $"A feature of the suite has the one tag @suite:{Suite.Name}"
                elif
                    feature.Name
                    <> FeatureWriter.featureName group
                then
                    fail
                        "mck.invalid_case"
                        feature.Location.Line
                        $"The feature of the file {group}.feature is named \"{FeatureWriter.featureName group}\""
                else
                    let children =
                        feature.Children
                        |> Seq.toList

                    match
                        children
                        |> List.tryFind (fun child -> not (child :? Scenario))
                    with
                    | Some child ->
                        fail
                            "mck.invalid_case"
                            child.Location.Line
                            "The suite has scenarios directly under the feature, with no Background or Rule"
                    | None ->
                        let scenarios =
                            children
                            |> List.map (fun child -> readScenario (child :?> Scenario))

                        match
                            scenarios
                            |> List.choose (
                                function
                                | Error found -> Some(issue found)
                                | Ok _ -> None
                            )
                        with
                        | [] ->
                            let read =
                                scenarios
                                |> List.choose Result.toOption

                            // Consecutive scenarios with the same id are the alternatives of one case.
                            let grouped =
                                read
                                |> List.fold
                                    (fun groups scenario ->
                                        match groups with
                                        | (current: ParsedScenario list) :: earlier when
                                            (List.head current).Number = scenario.Number
                                            && (List.head current).CaseId = scenario.CaseId
                                            ->
                                            (scenario
                                             :: current)
                                            :: earlier
                                        | _ ->
                                            [ scenario ]
                                            :: groups
                                    )
                                    []
                                |> List.map List.rev
                                |> List.rev

                            let joined =
                                grouped
                                |> List.map join

                            match
                                joined
                                |> List.choose (
                                    function
                                    | Error found -> Some(issue found)
                                    | Ok _ -> None
                                )
                            with
                            | [] ->
                                let cases =
                                    joined
                                    |> List.choose Result.toOption

                                match
                                    cases
                                    |> List.tryFind (fun (_, _, case) ->
                                        FeatureWriter.group case.CaseId
                                        <> group
                                    )
                                with
                                | Some(_, caseLine, case) ->
                                    fail
                                        "mck.invalid_case"
                                        caseLine
                                        $"The case {case.CaseId} belongs in {FeatureWriter.group case.CaseId}.feature"
                                | None ->
                                    match
                                        FeatureWriter.feature
                                            pin
                                            kit
                                            group
                                            (cases
                                             |> List.map (fun (number, _, case) -> number, case))
                                    with
                                    | Error issues -> Error issues
                                    | Ok text ->
                                        let actual = Encoding.UTF8.GetString bytes

                                        if
                                            UTF8Encoding(false).GetBytes text
                                            <> bytes
                                        then
                                            let generated = text.Split '\n'
                                            let written = actual.Split '\n'

                                            let line =
                                                Seq.zip generated written
                                                |> Seq.tryFindIndex (fun (left, right) ->
                                                    left
                                                    <> right
                                                )
                                                |> Option.defaultValue (
                                                    min generated.Length written.Length
                                                )

                                            fail
                                                "mck.not_generated"
                                                (line + 1)
                                                $"The feature is not what the generator writes for its cases, from line {line + 1}. The features of the {Suite.Name} suite are generated, never edited by hand. Run: {Suite.RegenerateCommand}"
                                        else
                                            Ok(pin, kit, group, cases)
                            | issues -> Error issues
                        | issues -> Error issues

    /// Reads every .feature artifact into the suite's cases. A step outside vocabulary 1.2 is
    /// refused with its file and line. A feature that is not exactly what the generator writes
    /// for the cases it holds, or features of different pins, are refused.
    let parse (artifacts: Artifact list) : Result<WasmHostSuite, CorpusIssue list> =
        let features =
            artifacts
            |> List.filter (fun artifact ->
                artifact.Path.EndsWith(".feature", StringComparison.Ordinal)
            )
            |> List.sortWith (fun left right ->
                StringComparer.Ordinal.Compare(left.Path, right.Path)
            )

        let read =
            features
            |> List.map (fun artifact -> artifact.Path, readFeature artifact.Path artifact.Bytes)

        let issues =
            read
            |> List.collect (fun (_, result) ->
                match result with
                | Error issues -> issues
                | Ok _ -> []
            )

        if not issues.IsEmpty then
            Error issues
        elif features.IsEmpty then
            Error [
                {
                    Code = "mck.invalid_case"
                    Path = ""
                    Line = 0
                    Message = "The suite has no feature"
                }
            ]
        else
            let files =
                read
                |> List.choose (fun (path, result) ->
                    result
                    |> Result.toOption
                    |> Option.map (fun value -> path, value)
                )

            let (_, (pin, kit, _, _)) = List.head files

            match
                files
                |> List.tryFind (fun (_, (otherPin, otherKit, _, _)) ->
                    otherPin
                    <> pin
                    || otherKit
                       <> kit
                )
            with
            | Some(path, _) ->
                Error [
                    {
                        Code = "mck.not_generated"
                        Path = path
                        Line = 1
                        Message = "The features of the suite are generated from one pin of one kit"
                    }
                ]
            | None ->
                let cases =
                    files
                    |> List.collect (fun (path, (_, _, _, cases)) ->
                        cases
                        |> List.map (fun (number, line, case) -> {
                            Id = Suite.id number
                            File = path
                            Line = line
                            Case = case
                        })
                    )

                let repeated (key: SuiteCase -> string) (what: string) =
                    cases
                    |> List.groupBy key
                    |> List.filter (fun (_, found) -> found.Length > 1)
                    |> List.map (fun (value, found) ->
                        let second = found[1]

                        {
                            Code = "mck.duplicate_id"
                            Path = second.File
                            Line = second.Line
                            Message = $"The {what} {value} is used by more than one case"
                        }
                    )

                match
                    repeated (fun case -> case.Id) "id"
                    @ repeated (fun case -> case.Case.CaseId) "upstream case id"
                with
                | [] -> Ok { Pin = pin; Kit = kit; Cases = cases }
                | issues -> Error issues

    /// Reads the committed suite: the features and the id map. Besides what parse refuses, it
    /// refuses the suite when it is not what the pinned corpus generates with the committed id
    /// map, naming each file, line and case that differs. An expectation is accepted only when
    /// the pinned corpus holds it.
    let read
        (corpus: WasmHostCorpus)
        (artifacts: Artifact list)
        : Result<WasmHostSuite, CorpusIssue list> =
        let mapArtifact =
            artifacts
            |> List.tryFind (fun artifact -> Path.GetFileName artifact.Path = Suite.IdMapFile)

        let stale =
            match mapArtifact with
            | None -> [
                {
                    Code = "mck.stale_suite"
                    Path = Suite.IdMapFile
                    Line = 0
                    Message = $"The suite has no id map. Run: {Suite.RegenerateCommand}"
                }
              ]
            | Some map ->
                match
                    CaseIds.parse map.Path map.Bytes
                    |> Result.bind (
                        Some
                        >> Generation.generate corpus
                    )
                with
                | Error issues -> issues
                | Ok expected -> Generation.check expected artifacts

        match parse artifacts, stale with
        | Ok suite, [] -> Ok suite
        | Ok _, issues -> Error issues
        | Error issues, staleIssues ->
            Error(
                issues
                @ staleIssues
            )
