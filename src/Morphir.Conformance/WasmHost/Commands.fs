// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.WasmHost

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Morphir.Conformance

/// Reads a kit directory of the wasm-host suite.
module Kit =
    /// The checkout that holds a kit directory: the nearest directory at or above it that holds the pinned
    /// corpus.
    let repositoryRoot (kitDirectory: string) =
        let rec find (directory: DirectoryInfo | null) =
            match directory with
            | null -> None
            | directory when
                File.Exists(Path.Combine(directory.FullName, CorpusJson.DefaultCorpusPath))
                ->
                Some directory.FullName
            | directory -> find directory.Parent

        find (DirectoryInfo(Path.GetFullPath kitDirectory))

    /// Reads the features and id map of a kit directory and joins them to the pinned corpus of the checkout,
    /// refusing a kit or corpus version the runner does not implement, a stale or hand-edited feature and
    /// a step outside the vocabulary. Ok gives the suite and the checkout's root.
    let load (kitDirectory: string) : Result<WasmHostSuite * string, CorpusIssue list> =
        let issue code message = [
            {
                Code = code
                Path = kitDirectory
                Line = 0
                Message = message
            }
        ]

        if not (Directory.Exists kitDirectory) then
            Error(issue "mck.invalid_kit" $"The kit {kitDirectory} is not a directory")
        else
            match repositoryRoot kitDirectory with
            | None ->
                Error(
                    issue
                        "mck.invalid_kit"
                        $"No directory at or above {kitDirectory} holds the pinned corpus {CorpusJson.DefaultCorpusPath}"
                )
            | Some root ->
                let artifacts =
                    Directory.GetFiles(
                        Path.GetFullPath kitDirectory,
                        "*",
                        SearchOption.TopDirectoryOnly
                    )
                    |> Array.map (fun file -> {
                        Path = Path.GetRelativePath(root, file).Replace('\\', '/')
                        Bytes = File.ReadAllBytes file
                    })
                    |> Array.sortWith (fun left right ->
                        StringComparer.Ordinal.Compare(left.Path, right.Path)
                    )
                    |> Array.toList

                CorpusJson.load root
                |> Result.bind (fun corpus -> FeatureReader.read corpus artifacts)
                |> Result.bind (fun suite ->
                    if
                        suite.Kit.Kit
                        <> Suite.Kit
                        || suite.Kit.Version
                           <> Suite.KitVersion
                    then
                        Error(
                            issue
                                "mck.kit_version"
                                $"The runner implements kit {Suite.Kit} {Suite.KitVersion}; the kit is {suite.Kit.Kit} {suite.Kit.Version}"
                        )
                    else
                        Ok(suite, root)
                )

/// The result of an experimental `morphir mck wasm-host` command, for the C# command that prints it.
[<Sealed>]
type WasmHostCommandResult internal (exitCode: int, output: string, diagnostics: string list) =
    /// The exit status: 0 when every applicable case passed, 1 for a failed case or an operational
    /// error, 2 for an invalid invocation.
    member _.ExitCode = exitCode

    /// The one JSON document that the command writes on stdout.
    member _.Output = output

    /// The diagnostic lines that the command writes on stderr.
    member _.Diagnostics: IReadOnlyList<string> =
        List.toArray diagnostics :> IReadOnlyList<string>

    override _.ToString() = $"exit {exitCode}"

/// The experimental `morphir mck wasm-host run` and `report-check` commands. The C# commands bind the
/// arguments and print the result; every decision is made here.
[<AbstractClass; Sealed>]
type WasmHostCommands =
    static member private Document(command: string, exitCode: int, write: Utf8JsonWriter -> unit) =
        use output = new MemoryStream()
        use writer = new Utf8JsonWriter(output, JsonWriterOptions(Indented = false))
        writer.WriteStartObject()
        writer.WriteString("command", command)
        writer.WriteBoolean("experimental", true)
        writer.WriteNumber("exitCode", exitCode)
        write writer
        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(output.ToArray())

    static member private Invalid(command: string, code: string, messages: string list) =
        let document =
            WasmHostCommands.Document(
                command,
                2,
                fun writer ->
                    writer.WriteStartObject "error"
                    writer.WriteString("code", code)
                    writer.WriteString("message", String.Join(" ", messages))
                    writer.WriteEndObject()
            )

        WasmHostCommandResult(
            2,
            document,
            messages
            |> List.map (fun message -> $"Error: {code}: {message}")
        )

    /// <summary>The result of an invocation that the command-line parser refused: exit status 2.</summary>
    static member UsageError(command: string, message: string) : WasmHostCommandResult =
        WasmHostCommands.Invalid(command, "mck.usage", [ message ])

    static member private Text(value: string | null) =
        match value with
        | null -> None
        | text when String.IsNullOrWhiteSpace text -> None
        | text -> Some text

    static member private Deviations(path: string option) : Result<Deviation list, string> =
        match path with
        | None -> Ok []
        | Some path when not (File.Exists path) ->
            Error $"The deviations file {path} does not exist"
        | Some path ->
            try
                use document = JsonDocument.Parse(File.ReadAllBytes path)

                match document.RootElement.TryGetProperty "deviations" with
                | true, items when items.ValueKind = JsonValueKind.Array ->
                    let read (item: JsonElement) name =
                        match item.TryGetProperty(name: string) with
                        | true, value when value.ValueKind = JsonValueKind.String ->
                            Some(nonNull (value.GetString()))
                        | _ -> None

                    items.EnumerateArray()
                    |> Seq.map (fun item ->
                        match read item "caseId", read item "tracking", read item "note" with
                        | Some caseId, Some tracking, Some note ->
                            Ok
                                {
                                    CaseId = caseId
                                    Tracking = tracking
                                    Note = note
                                }
                        | _ ->
                            Error
                                $"Each deviation in {path} has the strings caseId, tracking and note"
                    )
                    |> List.ofSeq
                    |> ResultSyntax.all
                | _ -> Error $"The deviations file {path} is an object with a deviations array"
            with :? JsonException as error ->
                Error $"The deviations file {path} is not JSON: {error.Message}"

    static member private Failures(report: WasmHostReport) =
        report.Cases
        |> List.filter (fun case -> not (Judge.acceptable case.Outcome))
        |> List.map (fun case ->
            let detail =
                match case.Outcome with
                | CaseOutcome.Failed failures ->
                    failures
                    |> List.map (fun failure ->
                        $"run {failure.Run} {failure.Matcher}: {failure.Message}"
                    )
                    |> fun parts -> String.Join(" | ", parts)
                | CaseOutcome.Error reason -> reason
                | CaseOutcome.Refused(expected, actual) ->
                    $"the module's digest is {actual}, and the corpus records {expected}"
                | CaseOutcome.NotEnforced bounds ->
                    let shown =
                        String.Join(
                            ", ",
                            bounds
                            |> List.map (fun bound -> bound.WireValue)
                        )

                    $"the host does not enforce {shown}, which qualification requires"
                | CaseOutcome.Passed
                | CaseOutcome.NotApplicable _ -> ""

            let deviation =
                match case.Deviation with
                | Some deviation -> $" (known deviation, {deviation.Tracking})"
                | None -> ""

            $"{case.Case.Id} {case.Case.Case.CaseId}: {case.Outcome.WireValue}{deviation}: {detail}"
        )

    /// <summary>
    /// <c>morphir mck wasm-host run</c>: runs every case of the kit against the host's worker mode and writes the
    /// report. The host is an executable path and the arguments before <c>extension worker</c>. Profiles are the
    /// profiles the host declares it runs; offering, platform and engine are what the host declares about itself
    /// (null platform means this process's platform). The guest kit citation is all three of its values or none.
    /// </summary>
    static member RunAsync
        (kit: string | null, host: string | null,
                                            hostArguments: IReadOnlyList<string>,
                                            profiles: IReadOnlyList<string>,
                                            offering: string | null, platform: string | null,
                                                                                        engine:
                                                                                            string | null,
                                                                                                     report:
                                                                                                         string | null,
                                                                                                                  deviations:
                                                                                                                      string | null,
                                                                                                                               guestKitReport:
                                                                                                                                   string | null,
                                                                                                                                            guestKitHostSha256:
                                                                                                                                                string | null,
                                                                                                                                                         guestKitModuleSha256:
                                                                                                                                                             string | null,
                                                                                                                                                                      progress:
                                                                                                                                                                          Action<string>,
                                                                                                                                                                      cancellationToken:
                                                                                                                                                                          CancellationToken)
        : Task<WasmHostCommandResult> =
        task {
            let command = "mck wasm-host run"

            let parsedProfiles =
                profiles
                |> Seq.map Profile.Parse
                |> List.ofSeq

            let invalid code message =
                WasmHostCommands.Invalid(command, code, [ message ])

            match
                WasmHostCommands.Text kit, WasmHostCommands.Text host, WasmHostCommands.Text report
            with
            | None, _, _ -> return invalid "mck.usage" "--kit is required"
            | _, None, _ -> return invalid "mck.usage" "--host is required"
            | _, _, None -> return invalid "mck.usage" "--report is required"
            | Some kit, Some host, Some reportPath ->
                let hostPath = Path.GetFullPath host

                match
                    parsedProfiles
                    |> List.tryPick (
                        function
                        | Error message -> Some message
                        | Ok _ -> None
                    )
                with
                | Some message -> return invalid "mck.usage" message
                | None when not (File.Exists hostPath) ->
                    return invalid "mck.usage" $"The host {hostPath} is not a file"
                | None ->
                    match WasmHostCommands.Deviations(WasmHostCommands.Text deviations) with
                    | Error message -> return invalid "mck.usage" message
                    | Ok deviationList ->
                        let citation =
                            match
                                WasmHostCommands.Text guestKitReport,
                                WasmHostCommands.Text guestKitHostSha256,
                                WasmHostCommands.Text guestKitModuleSha256
                            with
                            | None, None, None -> Ok None
                            | Some path, Some hostSha, Some moduleSha ->
                                if not (File.Exists path) then
                                    Error $"The guest kit report {path} is not a file"
                                else
                                    Ok(
                                        Some
                                            {
                                                Report = Path.GetFullPath path
                                                ReportSha256 = HostRunner.fileSha256 path
                                                HostSha256 = hostSha.ToLowerInvariant()
                                                GuestModuleSha256 = moduleSha.ToLowerInvariant()
                                            }
                                    )
                            | _ ->
                                Error
                                    "--guest-kit-report, --guest-kit-host-sha256 and --guest-kit-module-sha256 are given together or not at all"

                        let hostSha = HostRunner.fileSha256 hostPath

                        match citation with
                        | Error message -> return invalid "mck.usage" message
                        | Ok(Some cited) when
                            cited.HostSha256
                            <> hostSha
                            ->
                            return
                                invalid
                                    "mck.guest_kit_other_executable"
                                    $"The cited generic guest kit run records the host executable digest {cited.HostSha256}, and --host has the digest {hostSha}"
                        | Ok citation ->
                            match Kit.load kit with
                            | Error issues ->
                                let code =
                                    issues
                                    |> List.tryHead
                                    |> Option.map (fun issue -> issue.Code)
                                    |> Option.defaultValue "mck.invalid_kit"

                                return
                                    WasmHostCommands.Invalid(
                                        command,
                                        code,
                                        issues
                                        |> List.map (fun issue ->
                                            $"{issue.Path}:{issue.Line}: {issue.Code}: {issue.Message}"
                                        )
                                    )
                            | Ok(suite, root) ->
                                let options = {
                                    Host = {
                                        Executable = hostPath
                                        Arguments = List.ofSeq hostArguments
                                    }
                                    Declared = {
                                        Profiles =
                                            parsedProfiles
                                            |> List.choose Result.toOption
                                            |> List.distinct
                                        Offering = WasmHostCommands.Text offering
                                        Platform =
                                            WasmHostCommands.Text platform
                                            |> Option.orElse (Some(HostRunner.currentPlatform ()))
                                        Engine = WasmHostCommands.Text engine
                                    }
                                    Limits = RunLimits.ofKit suite.Kit
                                    ModuleFile = HostRunner.moduleFileIn root suite.Pin
                                    Deviations = deviationList
                                    GuestKit = citation
                                    Runner = HostRunner.runnerIdentity ()
                                    Progress = progress.Invoke
                                }

                                let! result = HostRunner.run options suite cancellationToken

                                match result with
                                | Error message ->
                                    let document =
                                        WasmHostCommands.Document(
                                            command,
                                            1,
                                            fun writer ->
                                                writer.WriteStartObject "error"
                                                writer.WriteString("code", "mck.host_unavailable")
                                                writer.WriteString("message", message)
                                                writer.WriteEndObject()
                                        )

                                    return
                                        WasmHostCommandResult(
                                            1,
                                            document,
                                            [ $"Error: mck.host_unavailable: {message}" ]
                                        )
                                | Ok hostReport ->
                                    let bytes = HostReport.toUtf8 hostReport
                                    let fullReport = Path.GetFullPath reportPath

                                    match Path.GetDirectoryName fullReport with
                                    | null -> ()
                                    | directory ->
                                        Directory.CreateDirectory directory
                                        |> ignore

                                    File.WriteAllBytes(fullReport, bytes)
                                    let exitCode = HostRunner.exitCode hostReport

                                    let document =
                                        WasmHostCommands.Document(
                                            command,
                                            exitCode,
                                            fun writer ->
                                                writer.WriteString("report", fullReport)

                                                writer.WriteString(
                                                    "reportSha256",
                                                    Convert.ToHexStringLower(
                                                        Security.Cryptography.SHA256.HashData bytes
                                                    )
                                                )

                                                writer.WriteString(
                                                    "host",
                                                    hostReport.Host.Executable
                                                )

                                                writer.WriteString(
                                                    "hostSha256",
                                                    hostReport.Host.Sha256
                                                )

                                                HostReport.writeTotalsObject
                                                    writer
                                                    "totals"
                                                    (HostReport.totals (
                                                        hostReport.Cases
                                                        |> List.map (fun case -> case.Outcome)
                                                    ))

                                                writer.WriteString(
                                                    "qualificationNotice",
                                                    HostReport.QualificationNotice
                                                )
                                        )

                                    return
                                        WasmHostCommandResult(
                                            exitCode,
                                            document,
                                            WasmHostCommands.Failures hostReport
                                        )
        }

    /// <summary>
    /// <c>morphir mck wasm-host report-check</c>: checks a report against the kit and recomputes every outcome from
    /// the recorded observations. expectOffering and expectPlatform, when given, are the offering and platform the
    /// report is cited for; a report of another offering or platform is refused.
    /// </summary>
    static member CheckReport
        (kit: string | null, report: string | null, expectOffering: string | null,
                                                                             expectPlatform: string | null)
        : WasmHostCommandResult =
        let command = "mck wasm-host report-check"

        match WasmHostCommands.Text kit, WasmHostCommands.Text report with
        | None, _ -> WasmHostCommands.Invalid(command, "mck.usage", [ "--kit is required" ])
        | _, None -> WasmHostCommands.Invalid(command, "mck.usage", [ "--report is required" ])
        | Some _, Some path when not (File.Exists path) ->
            WasmHostCommands.Invalid(command, "mck.usage", [ $"The report {path} is not a file" ])
        | Some kit, Some path ->
            match Kit.load kit with
            | Error issues ->
                let code =
                    issues
                    |> List.tryHead
                    |> Option.map (fun issue -> issue.Code)
                    |> Option.defaultValue "mck.invalid_kit"

                WasmHostCommands.Invalid(
                    command,
                    code,
                    issues
                    |> List.map (fun issue ->
                        $"{issue.Path}:{issue.Line}: {issue.Code}: {issue.Message}"
                    )
                )
            | Ok(suite, root) ->
                let fullPath = Path.GetFullPath path

                let expectations: HostReport.Expectations = {
                    Offering = WasmHostCommands.Text expectOffering
                    Platform = WasmHostCommands.Text expectPlatform
                }

                match
                    HostReport.check
                        suite
                        (HostRunner.moduleFileIn root suite.Pin)
                        expectations
                        (File.ReadAllBytes fullPath)
                with
                | Error problems ->
                    let document =
                        WasmHostCommands.Document(
                            command,
                            1,
                            fun writer ->
                                writer.WriteString("report", fullPath)
                                writer.WriteBoolean("valid", false)
                                writer.WriteStartArray "problems"

                                for problem in problems do
                                    writer.WriteStringValue problem

                                writer.WriteEndArray()
                        )

                    WasmHostCommandResult(
                        1,
                        document,
                        problems
                        |> List.map (fun problem -> $"Error: mck.invalid_report: {problem}")
                    )
                | Ok checkedReport ->
                    let exitCode = HostRunner.exitCode checkedReport

                    let document =
                        WasmHostCommands.Document(
                            command,
                            exitCode,
                            fun writer ->
                                writer.WriteString("report", fullPath)
                                writer.WriteBoolean("valid", true)

                                HostReport.writeTotalsObject
                                    writer
                                    "totals"
                                    (HostReport.totals (
                                        checkedReport.Cases
                                        |> List.map (fun case -> case.Outcome)
                                    ))

                                writer.WriteString(
                                    "qualificationNotice",
                                    HostReport.QualificationNotice
                                )
                        )

                    WasmHostCommandResult(
                        exitCode,
                        document,
                        WasmHostCommands.Failures checkedReport
                    )
