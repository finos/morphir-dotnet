// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.WasmHost

open System
open System.Globalization
open System.Text

/// A JSON value with its object members in the order written and each number as its literal
/// text. Frames are sent as the compact text of their value, so member order is part of a frame.
[<RequireQualifiedAccess>]
type JsonValue =
    | Null
    | Bool of bool
    /// A number, as its JSON literal text, for example 1 or 0.5.
    | Number of literal: string
    | String of string
    | Array of JsonValue list
    /// An object, with its members in the order written. Member names are unique.
    | Object of (string * JsonValue) list

/// The JSON type names of a pointer check: `null`, `boolean`, `number`, `string`, `array`
/// and `object`.
[<RequireQualifiedAccess>]
type JsonType =
    | Null
    | Boolean
    | Number
    | String
    | Array
    | Object

    /// The name of the type, as the corpus writes it.
    member this.WireValue =
        match this with
        | Null -> "null"
        | Boolean -> "boolean"
        | Number -> "number"
        | String -> "string"
        | Array -> "array"
        | Object -> "object"

    /// Reads a JSON type name.
    static member Parse(text: string) : Result<JsonType, string> =
        match text with
        | "null" -> Ok Null
        | "boolean" -> Ok Boolean
        | "number" -> Ok Number
        | "string" -> Ok String
        | "array" -> Ok Array
        | "object" -> Ok Object
        | other -> Error $"Unknown JSON type name \"{other}\""

    override this.ToString() = this.WireValue

/// An execution profile of the contract. A host runs `extism-kernel-v1` always and
/// `morphir-bytes-v1` only when it declares it.
[<RequireQualifiedAccess>]
type Profile =
    | ExtismKernelV1
    | MorphirBytesV1

    /// The name of the profile, as the contract writes it.
    member this.WireValue =
        match this with
        | ExtismKernelV1 -> "extism-kernel-v1"
        | MorphirBytesV1 -> "morphir-bytes-v1"

    /// Reads a profile name.
    static member Parse(text: string) : Result<Profile, string> =
        match text with
        | "extism-kernel-v1" -> Ok ExtismKernelV1
        | "morphir-bytes-v1" -> Ok MorphirBytesV1
        | other -> Error $"Unknown profile \"{other}\""

    /// The profile every host runs.
    static member Required = ExtismKernelV1

    override this.ToString() = this.WireValue

/// A bound of the contract that a host may declare enforced or not enforced.
[<RequireQualifiedAccess>]
type Bound =
    | MemoryCeiling
    | InstructionBudget
    | CallDeadline

    /// The name of the bound, as the started event writes it.
    member this.WireValue =
        match this with
        | MemoryCeiling -> "memoryCeiling"
        | InstructionBudget -> "instructionBudget"
        | CallDeadline -> "callDeadline"

    /// Reads a bound name.
    static member Parse(text: string) : Result<Bound, string> =
        match text with
        | "memoryCeiling" -> Ok MemoryCeiling
        | "instructionBudget" -> Ok InstructionBudget
        | "callDeadline" -> Ok CallDeadline
        | other -> Error $"Unknown bound \"{other}\""

    override this.ToString() = this.WireValue

/// The kind of a fault event.
[<RequireQualifiedAccess>]
type FaultKind =
    | Failure
    | Trap
    | MemoryLimit
    | BudgetExhausted
    | CallDeadline

    /// The kind, as the fault event writes it.
    member this.WireValue =
        match this with
        | Failure -> "failure"
        | Trap -> "trap"
        | MemoryLimit -> "memory-limit"
        | BudgetExhausted -> "budget-exhausted"
        | CallDeadline -> "call-deadline"

    /// Reads a fault kind.
    static member Parse(text: string) : Result<FaultKind, string> =
        match text with
        | "failure" -> Ok Failure
        | "trap" -> Ok Trap
        | "memory-limit" -> Ok MemoryLimit
        | "budget-exhausted" -> Ok BudgetExhausted
        | "call-deadline" -> Ok CallDeadline
        | other -> Error $"Unknown fault kind \"{other}\""

    override this.ToString() = this.WireValue

/// An event that the worker writes on stderr as a JSON line.
[<RequireQualifiedAccess>]
type WorkerEvent =
    | Started
    | Refused
    | Fault
    | Output

    /// The event name, as the worker writes it.
    member this.WireValue =
        match this with
        | Started -> "started"
        | Refused -> "refused"
        | Fault -> "fault"
        | Output -> "output"

    /// Reads an event name.
    static member Parse(text: string) : Result<WorkerEvent, string> =
        match text with
        | "started" -> Ok Started
        | "refused" -> Ok Refused
        | "fault" -> Ok Fault
        | "output" -> Ok Output
        | other -> Error $"Unknown event \"{other}\""

    override this.ToString() = this.WireValue

/// A stderr event that must appear. Each event can fix only the members the corpus's
/// conventions give it.
[<RequireQualifiedAccess>]
type EventMatcher =
    /// A started event, with the profile it must declare, if fixed.
    | Started of abi: Profile option
    /// A refused event, with its code, if fixed, and the strings that must each occur in its
    /// error message.
    | Refused of code: string option * messageIncludes: string list
    /// A fault event, with its kind, if fixed.
    | Fault of kind: FaultKind option
    /// An output event.
    | Output

    /// The event this matcher names.
    member this.Event =
        match this with
        | Started _ -> WorkerEvent.Started
        | Refused _ -> WorkerEvent.Refused
        | Fault _ -> WorkerEvent.Fault
        | Output -> WorkerEvent.Output

/// What a pointer check requires of the value at its pointer.
[<RequireQualifiedAccess>]
type PointerTest =
    /// The value equals this JSON value.
    | Equals of JsonValue
    /// The value has this JSON type.
    | HasType of JsonType

/// One check of a frame: an RFC 6901 pointer and what the value there must be.
type PointerCheck = { Pointer: string; Test: PointerTest }

/// How one stdout frame must look.
[<RequireQualifiedAccess>]
type FrameMatcher =
    /// The frame's body parses to a JSON value equal to this one.
    | Json of JsonValue
    /// The frame's body parses to a JSON value equal to the body of the run's frame with this
    /// index, counted from 0.
    | SameAsFrame of index: int
    /// The frame's body satisfies every check. The list is not empty.
    | Pointers of PointerCheck list

/// Where a timing range ends: at the first fault event or at the worker's exit.
[<RequireQualifiedAccess>]
type Until =
    | Fault
    | Exit

    /// The value, as the corpus writes it.
    member this.WireValue =
        match this with
        | Fault -> "fault"
        | Exit -> "exit"

    /// Reads the end of a timing range.
    static member Parse(text: string) : Result<Until, string> =
        match text with
        | "fault" -> Ok Fault
        | "exit" -> Ok Exit
        | other -> Error $"Unknown timing end \"{other}\""

    override this.ToString() = this.WireValue

/// A timing range in seconds, measured from the end of the write of the run's last frame.
type Timing = {
    Until: Until
    /// The least number of seconds. Not negative, and not more than To.
    From: decimal
    /// The most number of seconds.
    To: decimal
}

/// The observation a run must give. An absent member, or an empty list, constrains nothing.
type Expectation = {
    /// The worker's exit status.
    ExitCode: int option
    /// The byte count of all of stdout.
    StdoutBytes: int option
    /// Every frame on stdout, in order. Some [] means that stdout has no frame.
    StdoutFrames: FrameMatcher list option
    /// Stderr events that must appear in this order, possibly among others.
    Events: EventMatcher list
    /// Events that must not appear.
    NoEvents: WorkerEvent list
    /// Strings that must occur, in order, in the text of the output events.
    GuestOutput: string list
    /// Strings that must each occur in that text, in any order.
    GuestOutputIncludes: string list
    /// Strings that must not occur in that text.
    NoGuestOutput: string list
    /// The time from the last frame to the first fault or to the exit.
    WithinSeconds: Timing option
}

/// What a host declares that selects one alternative of a run.
[<RequireQualifiedAccess>]
type Condition =
    /// The started event's bounds declare this bound enforced (true) or not enforced (false).
    | Bound of bound: Bound * enforced: bool
    /// The host declares (true) or does not declare (false) that it runs this profile.
    | HostRuns of profile: Profile * runs: bool

/// One alternative of a run: its condition and its expectation.
type Alternative = { When: Condition; Expect: Expectation }

/// The expectation of a run: one for every host, or one for each alternative.
[<RequireQualifiedAccess>]
type RunExpectation =
    | Always of Expectation
    /// The alternatives, in corpus order. The list is not empty and its conditions differ.
    | Alternatives of Alternative list

/// A frame to send, by its name in the corpus's frame table and its value. The body sent is
/// the UTF-8 of the compact text of the value.
type Frame = { Name: string; Value: JsonValue }

/// One worker process of a case: the frames written in order, and what it must give.
type Run = {
    Frames: Frame list
    Expect: RunExpectation
}

/// The build identity the worker is given.
[<RequireQualifiedAccess>]
type BuildIdentity =
    /// The host's own identity, from `extension worker --help --json`.
    | Host
    /// The host's identity with its last hexadecimal digit changed.
    | Other

/// The worker arguments that a case sets beyond the module and its ABI.
type WorkerSettings = {
    RequestBytes: int option
    ResponseBytes: int option
    /// Arguments that the runs leave out, in corpus order.
    Omit: string list
    BuildIdentity: BuildIdentity
}

/// The worker arguments of a case that sets none.
module WorkerSettings =
    /// No limit, no omitted argument and the host's own build identity.
    let defaults = {
        RequestBytes = None
        ResponseBytes = None
        Omit = []
        BuildIdentity = BuildIdentity.Host
    }

/// A module of the corpus: its name, its source and binary paths relative to the corpus file,
/// and the SHA-256 of the binary.
type ModuleRef = {
    Name: string
    Wat: string
    Wasm: string
    /// Lowercase hexadecimal SHA-256 of the binary.
    Sha256: string
}

/// What a case traces to: requirements and scenarios of the specs, and contract elements.
type CaseTests = {
    Requirements: string list
    Scenarios: string list
    Contract: string list
}

/// One crafted case: a module that does one thing, the runs to make and what each must give.
type WasmHostCase = {
    /// The upstream case id, for example classification/unknown-namespace.
    CaseId: string
    /// The profile a host must declare for the case to apply. Every host runs extism-kernel-v1.
    AppliesTo: Profile
    Module: ModuleRef
    /// The ABI given to the worker with --abi, if any.
    DeclaredAbi: Profile option
    Worker: WorkerSettings
    /// The bounds the case exercises. When the host declares one not enforced, the case is
    /// not_enforced.
    Bounds: Bound list
    Tests: CaseTests
    /// The runs, in order. The list is not empty.
    Runs: Run list
    /// What the contract leaves open about the observation.
    Gaps: string list
}

/// The identity of the kit and of the contract it checks.
type KitIdentity = {
    Kit: string
    Version: string
    ContractId: string
    ContractVersion: string
    /// The contract's path in finos/morphir.
    ContractPath: string
    /// The fixed margin, in seconds, that the timing ranges add to a call deadline.
    DeadlineMarginSeconds: decimal
}

/// The pinned corpus a suite is generated from: its path in this repository, the finos/morphir
/// commit it was copied from and the SHA-256 of its bytes.
type Pin = {
    /// Repository-relative path, for example tests/Fixtures/MepWasm/wasm-host.cases.json.
    Corpus: string
    /// The 40-digit finos/morphir commit.
    Commit: string
    /// Lowercase hexadecimal SHA-256 of the corpus bytes.
    Sha256: string
}

/// The cases of the pinned JSON corpus, in corpus order.
type WasmHostCorpus = {
    Pin: Pin
    Kit: KitIdentity
    Cases: WasmHostCase list
}

/// One case of the generated suite, with its stable id and where its first scenario is.
type SuiteCase = {
    /// The stable id, wasm-host-NNNN.
    Id: string
    File: string
    Line: int
    Case: WasmHostCase
}

/// The cases that the generated features give, the runner's input.
type WasmHostSuite = {
    Pin: Pin
    Kit: KitIdentity
    /// The cases, by file in ordinal order and by id within a file.
    Cases: SuiteCase list
}

/// The suite's constants.
module Suite =
    /// The suite name, the prefix of every id.
    [<Literal>]
    let Name = "wasm-host"

    /// The step vocabulary version of the suite.
    [<Literal>]
    let VocabularyVersion = "1.2"

    /// The kit this runner implements.
    [<Literal>]
    let Kit = "morphir/mep-wasm-host-kit"

    /// The kit version this runner implements.
    [<Literal>]
    let KitVersion = "0.1.0-draft.1"

    /// The file, beside the features, that assigns each upstream case id its number.
    [<Literal>]
    let IdMapFile = "case-ids.json"

    /// The command that generates the features again.
    [<Literal>]
    let RegenerateCommand = "dotnet fsi scripts/generate-wasm-host-cases.fsx"

    /// The stable id of a number, for example wasm-host-0001.
    let id (number: int) =
        Name
        + "-"
        + number.ToString("D4", CultureInfo.InvariantCulture)

/// Compact JSON text, as the frames are sent.
module Json =
    let private escape (builder: StringBuilder) (text: string) =
        builder.Append '"'
        |> ignore

        for index in
            0 .. text.Length
                 - 1 do
            let character = text[index]

            match character with
            | '"' ->
                builder.Append "\\\""
                |> ignore
            | '\\' ->
                builder.Append "\\\\"
                |> ignore
            | '\b' ->
                builder.Append "\\b"
                |> ignore
            | '\f' ->
                builder.Append "\\f"
                |> ignore
            | '\n' ->
                builder.Append "\\n"
                |> ignore
            | '\r' ->
                builder.Append "\\r"
                |> ignore
            | '\t' ->
                builder.Append "\\t"
                |> ignore
            | _ when character < ' ' ->
                builder
                    .Append("\\u")
                    .Append((int character).ToString("x4", CultureInfo.InvariantCulture))
                |> ignore
            | _ when Char.IsHighSurrogate character ->
                if
                    index + 1 < text.Length
                    && Char.IsLowSurrogate text[index + 1]
                then
                    builder.Append character
                    |> ignore
                else
                    builder
                        .Append("\\u")
                        .Append((int character).ToString("x4", CultureInfo.InvariantCulture))
                    |> ignore
            | _ when Char.IsLowSurrogate character ->
                if
                    index > 0
                    && Char.IsHighSurrogate text[index - 1]
                then
                    builder.Append character
                    |> ignore
                else
                    builder
                        .Append("\\u")
                        .Append((int character).ToString("x4", CultureInfo.InvariantCulture))
                    |> ignore
            | _ ->
                builder.Append character
                |> ignore

        builder.Append '"'
        |> ignore

    let rec private write (builder: StringBuilder) (value: JsonValue) =
        match value with
        | JsonValue.Null ->
            builder.Append "null"
            |> ignore
        | JsonValue.Bool true ->
            builder.Append "true"
            |> ignore
        | JsonValue.Bool false ->
            builder.Append "false"
            |> ignore
        | JsonValue.Number literal ->
            builder.Append literal
            |> ignore
        | JsonValue.String text -> escape builder text
        | JsonValue.Array items ->
            builder.Append '['
            |> ignore

            items
            |> List.iteri (fun index item ->
                if index > 0 then
                    builder.Append ','
                    |> ignore

                write builder item
            )

            builder.Append ']'
            |> ignore
        | JsonValue.Object members ->
            builder.Append '{'
            |> ignore

            members
            |> List.iteri (fun index (name, item) ->
                if index > 0 then
                    builder.Append ','
                    |> ignore

                escape builder name

                builder.Append ':'
                |> ignore

                write builder item
            )

            builder.Append '}'
            |> ignore

    /// The compact text of a value: no whitespace, members in the order written, strings
    /// escaped as JSON.stringify escapes them.
    let compact (value: JsonValue) =
        let builder = StringBuilder()
        write builder value
        builder.ToString()

    /// A string as a JSON string literal.
    let quote (text: string) =
        let builder = StringBuilder()
        escape builder text
        builder.ToString()

    let rec private ofElementAt
        (path: string)
        (element: System.Text.Json.JsonElement)
        : Result<JsonValue, string> =
        match element.ValueKind with
        | System.Text.Json.JsonValueKind.Null -> Ok JsonValue.Null
        | System.Text.Json.JsonValueKind.True -> Ok(JsonValue.Bool true)
        | System.Text.Json.JsonValueKind.False -> Ok(JsonValue.Bool false)
        | System.Text.Json.JsonValueKind.Number -> Ok(JsonValue.Number(element.GetRawText()))
        | System.Text.Json.JsonValueKind.String ->
            try
                Ok(JsonValue.String(element.GetString()))
            with :? InvalidOperationException as error ->
                Error $"{path}: {error.Message}"
        | System.Text.Json.JsonValueKind.Array ->
            let items =
                element.EnumerateArray()
                |> Seq.mapi (fun index item -> ofElementAt $"{path}/{index}" item)
                |> Seq.toList

            match
                items
                |> List.tryPick (
                    function
                    | Error message -> Some message
                    | Ok _ -> None
                )
            with
            | Some message -> Error message
            | None ->
                Ok(
                    JsonValue.Array(
                        items
                        |> List.choose Result.toOption
                    )
                )
        | System.Text.Json.JsonValueKind.Object ->
            let members =
                element.EnumerateObject()
                |> Seq.map (fun property -> property.Name, property.Value)
                |> Seq.toList

            let names =
                members
                |> List.map fst

            match
                names
                |> List.countBy id
                |> List.tryFind (fun (_, count) -> count > 1)
            with
            | Some(name, _) -> Error $"{path}: the member \"{name}\" is repeated"
            | None ->
                let values =
                    members
                    |> List.map (fun (name, value) ->
                        ofElementAt
                            (path
                             + "/"
                             + name.Replace("~", "~0").Replace("/", "~1"))
                            value
                        |> Result.map (fun value -> name, value)
                    )

                match
                    values
                    |> List.tryPick (
                        function
                        | Error message -> Some message
                        | Ok _ -> None
                    )
                with
                | Some message -> Error message
                | None ->
                    Ok(
                        JsonValue.Object(
                            values
                            |> List.choose Result.toOption
                        )
                    )
        | kind -> Error $"{path}: unexpected JSON value kind {kind}"

    /// Reads a parsed JSON element into a value. A repeated member name is an error.
    let ofElement (path: string) (element: System.Text.Json.JsonElement) = ofElementAt path element

    /// Parses JSON text into a value.
    let parse (text: string) : Result<JsonValue, string> =
        try
            use document = System.Text.Json.JsonDocument.Parse text
            ofElementAt "" document.RootElement
        with :? System.Text.Json.JsonException as error ->
            Error error.Message

/// Selects what a case expects of a host from what the host declares.
module Selection =
    /// Whether a case applies to a host that declares these profiles. Every host runs
    /// extism-kernel-v1, so its cases apply to every host. A case that does not apply is
    /// recorded as not_applicable.
    let applies (declared: Profile list) (case: WasmHostCase) =
        case.AppliesTo = Profile.Required
        || List.contains case.AppliesTo declared

    /// Whether a condition holds for a host that declares these profiles and these bounds.
    /// enforced gives, for each bound, whether the started event declares it enforced.
    let holds (declared: Profile list) (enforced: Bound -> bool) (condition: Condition) =
        match condition with
        | Condition.HostRuns(profile, runs) ->
            (profile = Profile.Required
             || List.contains profile declared) = runs
        | Condition.Bound(bound, value) -> enforced bound = value

    /// The expectation of a run for a host: the run's only expectation, or the one alternative
    /// whose condition holds. It is an error when no alternative or more than one holds.
    let expectation
        (declared: Profile list)
        (enforced: Bound -> bool)
        (run: Run)
        : Result<Expectation, string> =
        match run.Expect with
        | RunExpectation.Always expectation -> Ok expectation
        | RunExpectation.Alternatives alternatives ->
            match
                alternatives
                |> List.filter (fun alternative -> holds declared enforced alternative.When)
            with
            | [ alternative ] -> Ok alternative.Expect
            | [] -> Error "No alternative of the run holds for the host"
            | _ -> Error "More than one alternative of the run holds for the host"

    /// Whether the host declares one of the case's bounds not enforced, so that the case is
    /// recorded as not_enforced.
    let notEnforced (enforced: Bound -> bool) (case: WasmHostCase) =
        case.Bounds
        |> List.exists (
            enforced
            >> not
        )
