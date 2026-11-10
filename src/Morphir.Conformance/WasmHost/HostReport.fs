// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.WasmHost

open System
open System.Globalization
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open Morphir.Conformance

/// The totals of a report, by outcome.
type ReportTotals = {
    Cases: int
    Passed: int
    Failed: int
    Error: int
    Refused: int
    NotApplicable: int
    NotEnforced: int
}

/// The JSON report of a run of the wasm-host suite, and its check. The report holds per-case
/// observations and outcomes and the identities of the run. It holds no qualification verdict: parent task
/// 1.4 defines the verdict, and until it does a report says so instead.
module HostReport =
    /// The report format.
    [<Literal>]
    let Format = "morphir/mck-wasm-host-report"

    /// The report format version.
    [<Literal>]
    let FormatVersion = "0.1.0-draft.1"

    /// What the report says in place of a verdict.
    [<Literal>]
    let QualificationNotice =
        "This report holds no qualification verdict. Parent task 1.4 of the finos/morphir change add-dotnet-wasm-extension-host defines the host qualification verdict; until it does, a report holds per-case observations and outcomes only, and no tool may read one as a qualification."

    let private invariant = CultureInfo.InvariantCulture

    /// The totals of a list of outcomes.
    let totals (outcomes: CaseOutcome list) =
        let count predicate =
            outcomes
            |> List.filter predicate
            |> List.length

        {
            Cases = outcomes.Length
            Passed = count (fun outcome -> outcome = CaseOutcome.Passed)
            Failed = count (fun outcome -> outcome.WireValue = "failed")
            Error = count (fun outcome -> outcome.WireValue = "error")
            Refused = count (fun outcome -> outcome.WireValue = "refused")
            NotApplicable = count (fun outcome -> outcome.WireValue = "not_applicable")
            NotEnforced = count (fun outcome -> outcome.WireValue = "not_enforced")
        }

    let private writeTotals (writer: Utf8JsonWriter) (totals: ReportTotals) =
        writer.WriteNumber("cases", totals.Cases)
        writer.WriteNumber("passed", totals.Passed)
        writer.WriteNumber("failed", totals.Failed)
        writer.WriteNumber("error", totals.Error)
        writer.WriteNumber("refused", totals.Refused)
        writer.WriteNumber("notApplicable", totals.NotApplicable)
        writer.WriteNumber("notEnforced", totals.NotEnforced)

    /// Writes the totals as one JSON object, for a command's stdout.
    let writeTotalsObject (writer: Utf8JsonWriter) (name: string) (totals: ReportTotals) =
        writer.WriteStartObject name
        writeTotals writer totals
        writer.WriteEndObject()

    let private optionalString (writer: Utf8JsonWriter) (name: string) (value: string option) =
        match value with
        | Some text -> writer.WriteString(name, text)
        | None -> writer.WriteNull name

    let private strings (writer: Utf8JsonWriter) (name: string) (values: string list) =
        writer.WriteStartArray name

        for value in values do
            writer.WriteStringValue value

        writer.WriteEndArray()

    let private milliseconds (span: TimeSpan) = int64 span.TotalMilliseconds

    let private writeRun (writer: Utf8JsonWriter) (run: RunObservation) =
        writer.WriteStartObject()
        strings writer "arguments" run.Arguments
        writer.WriteNumber("framesWritten", run.FramesWritten)

        match run.LastFrameWrittenMs with
        | Some value -> writer.WriteNumber("lastFrameWrittenMs", value)
        | None -> writer.WriteNull "lastFrameWrittenMs"

        writer.WriteString("end", run.End.WireValue)

        match run.End with
        | WorkerEnd.Exited code -> writer.WriteNumber("exitCode", code)
        | WorkerEnd.NotStarted reason -> writer.WriteString("reason", reason)
        | WorkerEnd.StoppedByRunner -> writer.WriteBoolean("processTreeStopped", true)

        writer.WriteNumber("endMs", run.EndMs)
        writer.WriteNumber("stdoutBytes", run.Stdout.Length)
        writer.WriteString("stdoutBase64", Convert.ToBase64String run.Stdout)
        writer.WriteStartArray "stderr"

        for line in run.Stderr do
            writer.WriteStartObject()
            writer.WriteNumber("atMs", line.AtMs)
            writer.WriteString("line", line.Text)
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteEndObject()

    let private writeCase (writer: Utf8JsonWriter) (result: HostCaseResult) =
        let case = result.Case
        writer.WriteStartObject()
        writer.WriteString("id", case.Id)
        writer.WriteString("caseId", case.Case.CaseId)
        writer.WriteString("feature", case.File)
        writer.WriteNumber("line", case.Line)
        writer.WriteString("appliesTo", case.Case.AppliesTo.WireValue)

        strings
            writer
            "bounds"
            (case.Case.Bounds
             |> List.map (fun bound -> bound.WireValue))

        writer.WriteStartObject "module"
        writer.WriteString("name", case.Case.Module.Name)
        writer.WriteString("wasm", case.Case.Module.Wasm)
        writer.WriteString("sha256", case.Case.Module.Sha256)
        writer.WriteString("path", result.ModulePath)
        optionalString writer "observedSha256" result.ObservedSha256
        writer.WriteEndObject()
        writer.WriteString("outcome", result.Outcome.WireValue)

        match result.Outcome with
        | CaseOutcome.Passed -> ()
        | CaseOutcome.Failed failures ->
            writer.WriteStartArray "failures"

            for failure in failures do
                writer.WriteStartObject()
                writer.WriteNumber("run", failure.Run)
                writer.WriteString("matcher", failure.Matcher)
                writer.WriteString("message", failure.Message)
                writer.WriteEndObject()

            writer.WriteEndArray()
        | CaseOutcome.Error reason
        | CaseOutcome.NotApplicable reason -> writer.WriteString("reason", reason)
        | CaseOutcome.NotEnforced bounds ->
            strings
                writer
                "notEnforced"
                (bounds
                 |> List.map (fun bound -> bound.WireValue))
        | CaseOutcome.Refused(expected, actual) ->
            writer.WriteStartObject "refused"
            writer.WriteString("expectedSha256", expected)
            writer.WriteString("actualSha256", actual)
            writer.WriteEndObject()

        match result.Deviation with
        | Some deviation ->
            writer.WriteStartObject "deviation"
            writer.WriteString("tracking", deviation.Tracking)
            writer.WriteString("note", deviation.Note)
            writer.WriteEndObject()
        | None -> ()

        writer.WriteStartArray "runs"

        for run in result.Runs do
            writeRun writer run

        writer.WriteEndArray()
        writer.WriteEndObject()

    /// The report as UTF-8 JSON: indented, LF line endings, members in a fixed order. The same report gives
    /// the same bytes.
    let toUtf8 (report: WasmHostReport) =
        use output = new MemoryStream()

        use writer =
            new Utf8JsonWriter(
                output,
                JsonWriterOptions(
                    Indented = true,
                    NewLine = "\n",
                    Encoder = Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                )
            )

        writer.WriteStartObject()
        writer.WriteString("report", Format)
        writer.WriteString("reportVersion", FormatVersion)
        writer.WriteBoolean("experimental", true)
        writer.WriteString("qualificationNotice", QualificationNotice)
        writer.WriteStartObject "suite"
        writer.WriteString("name", Suite.Name)
        writer.WriteString("vocabularyVersion", Suite.VocabularyVersion)
        writer.WriteString("kit", report.Kit.Kit)
        writer.WriteString("kitVersion", report.Kit.Version)
        writer.WriteStartObject "contract"
        writer.WriteString("id", report.Kit.ContractId)
        writer.WriteString("version", report.Kit.ContractVersion)
        writer.WriteString("path", report.Kit.ContractPath)
        writer.WriteEndObject()

        writer.WriteString(
            "deadlineMarginSeconds",
            report.Kit.DeadlineMarginSeconds.ToString(invariant)
        )

        writer.WriteStartObject "corpus"
        writer.WriteString("path", report.Pin.Corpus)
        writer.WriteString("commit", report.Pin.Commit)
        writer.WriteString("sha256", report.Pin.Sha256)
        writer.WriteEndObject()
        writer.WriteEndObject()
        writer.WriteStartObject "runner"
        writer.WriteString("name", report.Runner.Name)
        writer.WriteString("version", report.Runner.Version)
        writer.WriteString("executable", report.Runner.Executable)
        writer.WriteString("sha256", report.Runner.Sha256)
        writer.WriteEndObject()
        writer.WriteStartObject "host"
        writer.WriteString("executable", report.Host.Executable)
        strings writer "arguments" report.Host.Arguments
        writer.WriteString("sha256", report.Host.Sha256)
        writer.WriteString("buildIdentity", report.Host.BuildIdentity)

        strings
            writer
            "profiles"
            (report.Host.Declared.Profiles
             |> List.map (fun profile -> profile.WireValue))

        optionalString writer "offering" report.Host.Declared.Offering
        optionalString writer "platform" report.Host.Declared.Platform
        optionalString writer "engine" report.Host.Declared.Engine
        writer.WriteEndObject()
        writer.WriteStartObject "limits"
        writer.WriteNumber("extismCallDeadlineMs", milliseconds report.Limits.ExtismCallDeadline)
        writer.WriteNumber("bytesCallDeadlineMs", milliseconds report.Limits.BytesCallDeadline)
        writer.WriteNumber("marginMs", milliseconds report.Limits.Margin)
        writer.WriteNumber("helpTimeoutMs", milliseconds report.Limits.HelpTimeout)
        writer.WriteEndObject()
        writer.WriteStartArray "bounds"

        for bound in Contract.bounds do
            writer.WriteStartObject()
            writer.WriteString("bound", bound.WireValue)

            match Map.tryFind bound report.Bounds with
            | Some(BoundDeclaration.Declared BoundState.Enforced) ->
                writer.WriteString("declaration", "enforced")
            | Some(BoundDeclaration.Declared(BoundState.NotEnforced reason)) ->
                writer.WriteString("declaration", "not-enforced")
                writer.WriteString("reason", reason)
            | Some(BoundDeclaration.Inconsistent states) ->
                writer.WriteString("declaration", "inconsistent")

                strings
                    writer
                    "declared"
                    (states
                     |> List.map (fun state -> state.WireValue))
            | Some BoundDeclaration.Undeclared
            | None -> writer.WriteString("declaration", "undeclared")

            writer.WriteBoolean(
                "requiredForQualification",
                List.contains bound Contract.requiredForQualification
            )

            writer.WriteEndObject()

        writer.WriteEndArray()

        match report.GuestKit with
        | Some citation ->
            writer.WriteStartObject "guestKitCitation"
            writer.WriteString("report", citation.Report)
            writer.WriteString("reportSha256", citation.ReportSha256)
            writer.WriteString("hostSha256", citation.HostSha256)
            writer.WriteString("guestModuleSha256", citation.GuestModuleSha256)
            writer.WriteEndObject()
        | None -> writer.WriteNull "guestKitCitation"

        writer.WriteStartObject "totals"

        writeTotals
            writer
            (totals (
                report.Cases
                |> List.map (fun case -> case.Outcome)
            ))

        writer.WriteEndObject()
        writer.WriteStartArray "cases"

        for case in report.Cases do
            writeCase writer case

        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()
        Array.append (output.ToArray()) [| 10uy |]

    // ---- Reading -------------------------------------------------------------------------------------

    type private Reader(problems: ResizeArray<string>) =
        member _.Problem(message: string) = problems.Add message

        member this.Member
            (element: JsonElement)
            (path: string)
            (name: string)
            : JsonElement option =
            if
                element.ValueKind
                <> JsonValueKind.Object
            then
                this.Problem $"{path} is not an object"
                None
            else
                match element.TryGetProperty name with
                | true, value -> Some value
                | _ ->
                    this.Problem $"{path} has no member {name}"
                    None

        member this.Text (element: JsonElement) (path: string) (name: string) =
            match this.Member element path name with
            | Some value when value.ValueKind = JsonValueKind.String -> nonNull (value.GetString())
            | Some _ ->
                this.Problem $"{path}.{name} is not a string"
                ""
            | None -> ""

        member this.OptionalText (element: JsonElement) (path: string) (name: string) =
            match this.Member element path name with
            | Some value when value.ValueKind = JsonValueKind.String ->
                Some(nonNull (value.GetString()))
            | Some value when value.ValueKind = JsonValueKind.Null -> None
            | Some _ ->
                this.Problem $"{path}.{name} is not a string or null"
                None
            | None -> None

        member this.Int64 (element: JsonElement) (path: string) (name: string) =
            match this.Member element path name with
            | Some value when value.ValueKind = JsonValueKind.Number ->
                match value.TryGetInt64() with
                | true, number -> number
                | _ ->
                    this.Problem $"{path}.{name} is not an integer"
                    0L
            | Some _ ->
                this.Problem $"{path}.{name} is not a number"
                0L
            | None -> 0L

        member this.Array (element: JsonElement) (path: string) (name: string) : JsonElement list =
            match this.Member element path name with
            | Some value when value.ValueKind = JsonValueKind.Array ->
                value.EnumerateArray()
                |> Seq.map (fun item -> item.Clone())
                |> List.ofSeq
            | Some _ ->
                this.Problem $"{path}.{name} is not an array"
                []
            | None -> []

        member this.Strings (element: JsonElement) (path: string) (name: string) =
            this.Array element path name
            |> List.mapi (fun index item ->
                if item.ValueKind = JsonValueKind.String then
                    nonNull (item.GetString())
                else
                    this.Problem $"{path}.{name}[{index}] is not a string"
                    ""
            )

    /// A report as read back: the identities and observations it records, the outcomes it claims and the
    /// bytes it was read from.
    type ParsedReport = {
        Report: WasmHostReport
        /// The outcome that each case claims, by id, as written.
        Claimed: (string * string) list
    }

    let private profileOf (reader: Reader) (path: string) (text: string) =
        match Profile.Parse text with
        | Ok profile -> Some profile
        | Error message ->
            reader.Problem $"{path}: {message}"
            None

    let private boundOf (reader: Reader) (path: string) (text: string) =
        match Bound.Parse text with
        | Ok bound -> Some bound
        | Error message ->
            reader.Problem $"{path}: {message}"
            None

    let private stateOf (reader: Reader) (path: string) (text: string) =
        if text = "enforced" then
            Some BoundState.Enforced
        elif text.StartsWith("not-enforced:", StringComparison.Ordinal) then
            Some(BoundState.NotEnforced(text.Substring "not-enforced:".Length))
        else
            reader.Problem $"{path}: \"{text}\" is not a bound state"
            None

    let private readRun (reader: Reader) (path: string) (element: JsonElement) : RunObservation =
        let ending =
            match reader.Text element path "end" with
            | "exited" -> WorkerEnd.Exited(int (reader.Int64 element path "exitCode"))
            | "stopped-by-runner" -> WorkerEnd.StoppedByRunner
            | "not-started" -> WorkerEnd.NotStarted(reader.Text element path "reason")
            | other ->
                reader.Problem $"{path}.end \"{other}\" is not a worker end"
                WorkerEnd.StoppedByRunner

        let stdout =
            try
                Convert.FromBase64String(reader.Text element path "stdoutBase64")
            with :? FormatException ->
                reader.Problem $"{path}.stdoutBase64 is not base64"
                [||]

        let last =
            match reader.Member element path "lastFrameWrittenMs" with
            | Some value when value.ValueKind = JsonValueKind.Null -> None
            | Some _ -> Some(reader.Int64 element path "lastFrameWrittenMs")
            | None -> None

        {
            Arguments = reader.Strings element path "arguments"
            FramesWritten = int (reader.Int64 element path "framesWritten")
            LastFrameWrittenMs = last
            Stdout = stdout
            Stderr =
                reader.Array element path "stderr"
                |> List.mapi (fun index line ->
                    let linePath = $"{path}.stderr[{index}]"

                    {
                        AtMs = reader.Int64 line linePath "atMs"
                        Text = reader.Text line linePath "line"
                    }
                )
            End = ending
            EndMs = reader.Int64 element path "endMs"
        }

    /// Reads a report into its identities, observations and claimed outcomes. Each case is joined to the
    /// suite case with its id; the claimed outcome is kept as written, and the outcome in Report is the one
    /// the observations give. Error lists every structural problem, a case of another kit among them.
    let parse (suite: WasmHostSuite) (bytes: byte array) : Result<ParsedReport, string list> =
        let problems = ResizeArray<string>()
        let reader = Reader problems

        let parsed =
            try
                Ok(JsonDocument.Parse(ReadOnlyMemory bytes))
            with :? JsonException as error ->
                Error [ $"The report is not JSON: {error.Message}" ]

        match parsed with
        | Error problems -> Error problems
        | Ok document ->
            use document = document
            let root = document.RootElement

            if
                reader.Text root "$" "report"
                <> Format
            then
                reader.Problem $"The report is not a {Format} document"

            if
                reader.Text root "$" "reportVersion"
                <> FormatVersion
            then
                reader.Problem $"The report version is not {FormatVersion}"

            let suiteElement =
                reader.Member root "$" "suite"
                |> Option.defaultValue root

            let contract =
                reader.Member suiteElement "suite" "contract"
                |> Option.defaultValue suiteElement

            let corpus =
                reader.Member suiteElement "suite" "corpus"
                |> Option.defaultValue suiteElement

            let margin =
                match
                    Decimal.TryParse(
                        reader.Text suiteElement "suite" "deadlineMarginSeconds",
                        NumberStyles.Number,
                        invariant
                    )
                with
                | true, value -> value
                | _ ->
                    reader.Problem "suite.deadlineMarginSeconds is not a decimal number"
                    0m

            let kit = {
                Kit = reader.Text suiteElement "suite" "kit"
                Version = reader.Text suiteElement "suite" "kitVersion"
                ContractId = reader.Text contract "suite.contract" "id"
                ContractVersion = reader.Text contract "suite.contract" "version"
                ContractPath = reader.Text contract "suite.contract" "path"
                DeadlineMarginSeconds = margin
            }

            let pin = {
                Corpus = reader.Text corpus "suite.corpus" "path"
                Commit = reader.Text corpus "suite.corpus" "commit"
                Sha256 = reader.Text corpus "suite.corpus" "sha256"
            }

            let runnerElement =
                reader.Member root "$" "runner"
                |> Option.defaultValue root

            let runner = {
                Name = reader.Text runnerElement "runner" "name"
                Version = reader.Text runnerElement "runner" "version"
                Executable = reader.Text runnerElement "runner" "executable"
                Sha256 = reader.Text runnerElement "runner" "sha256"
            }

            let hostElement =
                reader.Member root "$" "host"
                |> Option.defaultValue root

            let host = {
                Executable = reader.Text hostElement "host" "executable"
                Arguments = reader.Strings hostElement "host" "arguments"
                Sha256 = reader.Text hostElement "host" "sha256"
                BuildIdentity = reader.Text hostElement "host" "buildIdentity"
                Declared = {
                    Profiles =
                        reader.Strings hostElement "host" "profiles"
                        |> List.choose (profileOf reader "host.profiles")
                    Offering = reader.OptionalText hostElement "host" "offering"
                    Platform = reader.OptionalText hostElement "host" "platform"
                    Engine = reader.OptionalText hostElement "host" "engine"
                }
            }

            let limitsElement =
                reader.Member root "$" "limits"
                |> Option.defaultValue root

            let limits = {
                ExtismCallDeadline =
                    TimeSpan.FromMilliseconds(
                        float (reader.Int64 limitsElement "limits" "extismCallDeadlineMs")
                    )
                BytesCallDeadline =
                    TimeSpan.FromMilliseconds(
                        float (reader.Int64 limitsElement "limits" "bytesCallDeadlineMs")
                    )
                Margin =
                    TimeSpan.FromMilliseconds(
                        float (reader.Int64 limitsElement "limits" "marginMs")
                    )
                HelpTimeout =
                    TimeSpan.FromMilliseconds(
                        float (reader.Int64 limitsElement "limits" "helpTimeoutMs")
                    )
            }

            let citation =
                match reader.Member root "$" "guestKitCitation" with
                | Some value when value.ValueKind = JsonValueKind.Null -> None
                | Some value ->
                    Some
                        {
                            Report = reader.Text value "guestKitCitation" "report"
                            ReportSha256 = reader.Text value "guestKitCitation" "reportSha256"
                            HostSha256 = reader.Text value "guestKitCitation" "hostSha256"
                            GuestModuleSha256 =
                                reader.Text value "guestKitCitation" "guestModuleSha256"
                        }
                | None -> None

            let byId =
                suite.Cases
                |> List.map (fun case -> case.Id, case)
                |> Map.ofList

            let recorded =
                reader.Array root "$" "cases"
                |> List.mapi (fun index element ->
                    let path = $"cases[{index}]"
                    let id = reader.Text element path "id"
                    let caseId = reader.Text element path "caseId"

                    match Map.tryFind id byId with
                    | None ->
                        reader.Problem
                            $"{path}: the case {id} {caseId} is not a case of the kit {suite.Kit.Kit} {suite.Kit.Version} at corpus {suite.Pin.Sha256}"

                        None
                    | Some case when
                        case.Case.CaseId
                        <> caseId
                        ->
                        reader.Problem
                            $"{path}: the kit's case {id} is {case.Case.CaseId}, not {caseId}"

                        None
                    | Some case ->
                        let moduleElement =
                            reader.Member element path "module"
                            |> Option.defaultValue element

                        let modulePath = $"{path}.module"

                        let recordedSha = reader.Text moduleElement modulePath "sha256"

                        if
                            recordedSha
                            <> case.Case.Module.Sha256
                        then
                            reader.Problem
                                $"{path}: the report records the module digest {recordedSha} for {id}, and the kit records {case.Case.Module.Sha256}"

                        let deviation =
                            match element.TryGetProperty "deviation" with
                            | true, value ->
                                Some
                                    {
                                        CaseId = case.Case.CaseId
                                        Tracking = reader.Text value $"{path}.deviation" "tracking"
                                        Note = reader.Text value $"{path}.deviation" "note"
                                    }
                            | _ -> None

                        let runs =
                            reader.Array element path "runs"
                            |> List.mapi (fun runIndex run ->
                                readRun reader $"{path}.runs[{runIndex}]" run
                            )

                        Some(
                            id,
                            reader.Text element path "outcome",
                            case,
                            reader.Text moduleElement modulePath "path",
                            reader.OptionalText moduleElement modulePath "observedSha256",
                            runs,
                            deviation
                        )
                )
                |> List.choose id

            let bounds =
                recorded
                |> List.collect (fun (_, _, _, _, _, runs, _) -> runs)
                |> Judge.declaration

            let cases =
                recorded
                |> List.map (fun (_, _, case, modulePath, observed, runs, deviation) -> {
                    Case = case
                    ModulePath = modulePath
                    ObservedSha256 = observed
                    Runs = runs
                    Outcome = Judge.outcome host.Declared.Profiles bounds case.Case observed runs
                    Deviation = deviation
                })

            if problems.Count > 0 then
                Error(List.ofSeq problems)
            else
                Ok
                    {
                        Report = {
                            Pin = pin
                            Kit = kit
                            Runner = runner
                            Host = host
                            Limits = limits
                            Bounds = bounds
                            GuestKit = citation
                            Cases = cases
                        }
                        Claimed =
                            recorded
                            |> List.map (fun (id, outcome, _, _, _, _, _) -> id, outcome)
                    }

    let private hex64 = Regex("^[0-9a-f]{64}$")

    let private firstDifferentLine (expected: byte array) (actual: byte array) =
        let left = Encoding.UTF8.GetString(expected).Split('\n')
        let right = Encoding.UTF8.GetString(actual).Split('\n')

        Seq.zip left right
        |> Seq.tryFindIndex (fun (a, b) -> a <> b)
        |> Option.defaultValue (min left.Length right.Length)
        |> (+) 1

    /// What a report check expects of the citation of a report, for example by release metadata.
    type Expectations = {
        Offering: string option
        Platform: string option
    }

    /// Checks a report against the kit: every case of the kit accounted for once and no other, the corpus,
    /// kit and contract identities of the kit, each module digest the kit's and the module files
    /// unchanged, the host executable unchanged, the identities present, a guest kit citation for the same
    /// executable, and every outcome recomputed from the recorded observations. A stored outcome is never
    /// trusted: the report must be byte for byte what the runner writes for its observations, so an edited
    /// outcome, a verdict member or any other edit is refused. moduleFile gives the module files of the
    /// checkout. Ok gives the recomputed report.
    let check
        (suite: WasmHostSuite)
        (moduleFile: ModuleRef -> string)
        (expectations: Expectations)
        (bytes: byte array)
        : Result<WasmHostReport, string list> =
        match parse suite bytes with
        | Error problems -> Error problems
        | Ok parsed ->
            let report = parsed.Report

            let problems = [
                if
                    report.Kit
                    <> suite.Kit
                then
                    yield
                        $"The report is of the kit {report.Kit.Kit} {report.Kit.Version} (contract {report.Kit.ContractVersion}), and the kit given is {suite.Kit.Kit} {suite.Kit.Version} (contract {suite.Kit.ContractVersion})"

                if
                    report.Pin
                    <> suite.Pin
                then
                    yield
                        $"The report is of the corpus {report.Pin.Corpus} at {report.Pin.Commit} with SHA-256 {report.Pin.Sha256}, and the kit's pin is {suite.Pin.Corpus} at {suite.Pin.Commit} with SHA-256 {suite.Pin.Sha256}"

                let ids =
                    report.Cases
                    |> List.map (fun case -> case.Case.Id)

                for case in suite.Cases do
                    match
                        ids
                        |> List.filter (fun id -> id = case.Id)
                        |> List.length
                    with
                    | 0 ->
                        yield $"The report has no result for the case {case.Id} {case.Case.CaseId}"
                    | 1 -> ()
                    | count ->
                        yield
                            $"The report has {count} results for the case {case.Id} {case.Case.CaseId}"

                if
                    ids
                    <> (suite.Cases
                        |> List.map (fun case -> case.Id)
                        |> List.filter (fun id -> List.contains id ids))
                then
                    yield "The report's cases are not in the kit's order"

                for result in report.Cases do
                    let expectedPath = moduleFile result.Case.Case.Module

                    if
                        result.ModulePath
                        <> expectedPath
                    then
                        yield
                            $"{result.Case.Id}: the report used the module file {result.ModulePath}, and the kit's module is {expectedPath}"

                    match result.ObservedSha256, HostRunner.tryFileSha256 expectedPath with
                    | Some recorded, Some current when
                        recorded
                        <> current
                        ->
                        yield
                            $"{result.Case.Id}: the module file's digest is now {current}, and the report recorded {recorded}"
                    | Some _, None ->
                        yield $"{result.Case.Id}: the module file {expectedPath} no longer exists"
                    | None, Some current ->
                        yield
                            $"{result.Case.Id}: the report recorded no module file, and it now has the digest {current}"
                    | _ -> ()

                if not (hex64.IsMatch report.Host.Sha256) then
                    yield "The report does not record the host executable's SHA-256"

                match HostRunner.tryFileSha256 report.Host.Executable with
                | None ->
                    yield
                        $"The host executable {report.Host.Executable} no longer exists, so its digest cannot be checked"
                | Some current when
                    current
                    <> report.Host.Sha256
                    ->
                    yield
                        $"The host executable's digest is now {current}, and the report recorded {report.Host.Sha256}"
                | Some _ -> ()

                if not (Regex.IsMatch(report.Host.BuildIdentity, "^[0-9a-fA-F]{64}$")) then
                    yield "The report does not record the host's build identity"

                for name, value in
                    [
                        "offering", report.Host.Declared.Offering
                        "platform", report.Host.Declared.Platform
                        "engine", report.Host.Declared.Engine
                    ] do
                    if value.IsNone then
                        yield $"The report does not identify the host's {name}"

                match expectations.Offering, report.Host.Declared.Offering with
                | Some expected, Some recorded when
                    expected
                    <> recorded
                    ->
                    yield
                        $"The report qualifies the offering {recorded}, and it is cited for {expected}"
                | _ -> ()

                match expectations.Platform, report.Host.Declared.Platform with
                | Some expected, Some recorded when
                    expected
                    <> recorded
                    ->
                    yield
                        $"The report qualifies the platform {recorded}, and it is cited for {expected}"
                | _ -> ()

                if
                    report.Runner.Name = ""
                    || report.Runner.Version = ""
                then
                    yield "The report does not identify its runner"

                match report.GuestKit with
                | Some citation ->
                    if
                        citation.HostSha256
                        <> report.Host.Sha256
                    then
                        yield
                            $"The cited generic guest kit run records the host executable digest {citation.HostSha256}, and the crafted cases ran against {report.Host.Sha256}"

                    match HostRunner.tryFileSha256 citation.Report with
                    | Some current when
                        current
                        <> citation.ReportSha256
                        ->
                        yield
                            $"The cited guest kit report's digest is now {current}, and the report recorded {citation.ReportSha256}"
                    | _ -> ()
                | None -> ()

                for (id, claimed), result in List.zip parsed.Claimed report.Cases do
                    if
                        claimed
                        <> result.Outcome.WireValue
                    then
                        yield
                            $"{id}: the report claims the outcome {claimed}, and its observations give {result.Outcome.WireValue}"

                let rebuilt = toUtf8 report

                if
                    rebuilt
                    <> bytes
                then
                    yield
                        $"The report is not what the runner writes for its recorded observations, from line {firstDifferentLine rebuilt bytes}: an outcome, a total or a member was changed or added"
            ]

            match problems with
            | [] -> Ok report
            | problems -> Error problems
