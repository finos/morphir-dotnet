#!/usr/bin/env dotnet fsi
// Resume a failed morphir-dotnet release
// Usage: dotnet fsi resume-release.fsx --version 1.2.0 --issue <number> [--json] [--timeout <minutes>]

#r "nuget: Spectre.Console, 0.53.0"
#r "nuget: System.Text.Json, 9.0.0"
#r "nuget: Argu, 6.2.4"

open System
open System.IO
open System.Diagnostics
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.RegularExpressions
open System.Threading
open Argu
open Spectre.Console

// ============================================================================
// CLI Arguments
// ============================================================================

type ResumeArguments =
    | [<Mandatory; AltCommandLine("-v")>] Version of string
    | [<Mandatory; AltCommandLine("-i")>] Issue of int
    | Json
    | [<AltCommandLine("-t")>] Timeout of int
    | Force

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Version _ -> "Release version to resume (e.g., 1.2.0)"
            | Issue _ -> "GitHub issue number tracking the release"
            | Json -> "Output results as JSON"
            | Timeout _ -> "Maximum time to wait in minutes"
            | Force -> "Force resume even if release appears successful"

// ============================================================================
// Types
// ============================================================================

type ReleasePhase =
    | Preparation
    | Execution
    | Verification
    | Documentation
    | PostRelease
    | Unknown

type ChecklistItem = {
    Phase: ReleasePhase
    Description: string
    Completed: bool
}

type IssueAnalysis = {
    Version: string
    IssueNumber: int
    CurrentPhase: ReleasePhase
    CompletedItems: ChecklistItem list
    PendingItems: ChecklistItem list
    FailurePoint: string option
    CanResume: bool
    ResumeStrategy: string option
}

type WorkflowRun = {
    Id: int
    Status: string
    Conclusion: string option
}

type ResumeResult = {
    Success: bool
    Version: string
    Issue: int
    Analysis: IssueAnalysis
    WorkflowTriggered: bool
    WorkflowRunId: int option
    IssueUpdated: bool
    TimedOut: bool
    Cancelled: bool
    Warnings: string list
    Errors: string list
    ExitCode: int
}

// ============================================================================
// Utilities
// ============================================================================

let projectRoot =
    let scriptDir = __SOURCE_DIRECTORY__
    Path.GetFullPath(Path.Combine(scriptDir, "..", "..", ".."))

let logInfo msg =
    eprintfn "[INFO] %s" msg

let logWarn msg =
    eprintfn "[WARN] %s" msg

let logError msg =
    eprintfn "[ERROR] %s" msg

let runCommandAsync (command: string) (args: string) (cancellationToken: CancellationToken) : Async<Result<string, string>> =
    async {
        try
            let psi = ProcessStartInfo(
                FileName = command,
                Arguments = args,
                WorkingDirectory = projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            )

            use proc = new Process()
            proc.StartInfo <- psi
            proc.Start() |> ignore

            // Register cancellation
            use _ = cancellationToken.Register(fun () ->
                try
                    if not proc.HasExited then
                        proc.Kill()
                with _ -> ()
            )

            let! output = proc.StandardOutput.ReadToEndAsync() |> Async.AwaitTask
            let! error = proc.StandardError.ReadToEndAsync() |> Async.AwaitTask
            proc.WaitForExit()

            if cancellationToken.IsCancellationRequested then
                return Error "Command cancelled"
            elif proc.ExitCode = 0 then
                return Ok output
            else
                return Error (if String.IsNullOrWhiteSpace(error) then output else error)
        with
        | :? OperationCanceledException ->
            return Error "Command cancelled"
        | ex ->
            return Error ex.Message
    }

// ============================================================================
// Issue Analysis
// ============================================================================

let parsePhase (text: string) : ReleasePhase =
    if text.Contains("Preparation") then Preparation
    elif text.Contains("Execution") then Execution
    elif text.Contains("Verification") then Verification
    elif text.Contains("Documentation") then Documentation
    elif text.Contains("Post-Release") then PostRelease
    else Unknown

let getIssueBodyAsync (issueNumber: int) (ct: CancellationToken) : Async<Result<string, string>> =
    async {
        logInfo (sprintf "Fetching issue #%d..." issueNumber)

        let! result = runCommandAsync "gh" (sprintf "issue view %d --json body" issueNumber) ct

        match result with
        | Error err -> return Error (sprintf "Failed to fetch issue: %s" err)
        | Ok output ->
            try
                use doc = JsonDocument.Parse(output)
                let body = doc.RootElement.GetProperty("body").GetString()
                return Ok body
            with ex ->
                return Error (sprintf "Failed to parse issue body: %s" ex.Message)
    }

let analyzeIssue (issueNumber: int) (version: string) (issueBody: string) : IssueAnalysis =
    logInfo "Analyzing issue for release state..."

    // Parse checklist items
    let checklistPattern = @"- \[([ x])\] (.+?)(?:\n|$)"
    let matches = Regex.Matches(issueBody, checklistPattern)

    let items =
        matches
        |> Seq.cast<Match>
        |> Seq.map (fun m ->
            let completed = m.Groups.[1].Value = "x"
            let description = m.Groups.[2].Value.Trim()

            // Try to determine phase from context
            let phase =
                if description.Contains("Pre-flight") || description.Contains("CHANGELOG") then Preparation
                elif description.Contains("Deployment workflow") || description.Contains("Build executable") then Execution
                elif description.Contains("Package validation") || description.Contains("QA Tester") then Verification
                elif description.Contains("GitHub release") || description.Contains("What's New") then Documentation
                elif description.Contains("Release playbook") || description.Contains("Retrospective") then PostRelease
                else Unknown

            {
                Phase = phase
                Description = description
                Completed = completed
            }
        )
        |> Seq.toList

    let completedItems = items |> List.filter (fun i -> i.Completed)
    let pendingItems = items |> List.filter (fun i -> not i.Completed)

    // Determine current phase
    let currentPhase =
        if pendingItems |> List.isEmpty then
            PostRelease // All done
        else
            pendingItems
            |> List.tryHead
            |> Option.map (fun i -> i.Phase)
            |> Option.defaultValue Unknown

    // Look for failure point in issue body
    let failurePoint =
        let failurePattern = @"\*\*Failure Point\*\*: (.+?)(?:\n|$)"
        let m = Regex.Match(issueBody, failurePattern)
        if m.Success then Some (m.Groups.[1].Value.Trim())
        else None

    // Determine resume strategy
    let resumeStrategy =
        match currentPhase with
        | Preparation ->
            Some "Complete pre-flight checks and update CHANGELOG, then trigger deployment workflow"
        | Execution ->
            Some "Re-trigger deployment workflow or monitor existing run"
        | Verification ->
            Some "Run validation tests and update issue with results"
        | Documentation ->
            Some "Create GitHub release and update documentation"
        | PostRelease ->
            Some "Complete retrospective and close issue"
        | Unknown ->
            None

    let canResume =
        not (List.isEmpty pendingItems) && resumeStrategy.IsSome

    {
        Version = version
        IssueNumber = issueNumber
        CurrentPhase = currentPhase
        CompletedItems = completedItems
        PendingItems = pendingItems
        FailurePoint = failurePoint
        CanResume = canResume
        ResumeStrategy = resumeStrategy
    }

// ============================================================================
// Resume Actions
// ============================================================================

let checkExistingWorkflowAsync (version: string) (ct: CancellationToken) : Async<Result<WorkflowRun option, string>> =
    async {
        logInfo "Checking for existing workflow runs..."

        let! result = runCommandAsync "gh" "run list --workflow=deployment.yml --limit 5 --json databaseId,status,conclusion" ct

        match result with
        | Error err -> return Error (sprintf "Failed to list workflows: %s" err)
        | Ok output ->
            try
                use doc = JsonDocument.Parse(output)
                let runs = doc.RootElement.EnumerateArray() |> Seq.toList

                let latestRun =
                    runs
                    |> List.tryHead
                    |> Option.map (fun run ->
                        let id = run.GetProperty("databaseId").GetInt32()
                        let status = run.GetProperty("status").GetString()
                        let conclusionProp = run.GetProperty("conclusion")
                        let conclusion = if conclusionProp.ValueKind = JsonValueKind.Null then None else Some (conclusionProp.GetString())

                        { Id = id; Status = status; Conclusion = conclusion }
                    )

                return Ok latestRun
            with ex ->
                return Error (sprintf "Failed to parse workflow runs: %s" ex.Message)
    }

let triggerWorkflowAsync (version: string) (ct: CancellationToken) : Async<Result<int, string>> =
    async {
        logInfo (sprintf "Triggering deployment workflow for v%s..." version)

        let! result = runCommandAsync "gh" (sprintf "workflow run deployment.yml --ref main --field release-version=%s --field configuration=Release" version) ct

        match result with
        | Error err -> return Error (sprintf "Failed to trigger workflow: %s" err)
        | Ok _ ->
            // Wait a moment for workflow to register
            do! Async.Sleep(2000)

            // Get the run ID
            let! listResult = runCommandAsync "gh" "run list --workflow=deployment.yml --limit 1 --json databaseId" ct
            match listResult with
            | Ok output ->
                try
                    use doc = JsonDocument.Parse(output)
                    let runs = doc.RootElement.EnumerateArray() |> Seq.toList
                    match runs |> List.tryHead with
                    | Some run ->
                        let runId = run.GetProperty("databaseId").GetInt32()
                        logInfo (sprintf "Workflow triggered with run ID: %d" runId)
                        return Ok runId
                    | None ->
                        return Error "Could not find triggered workflow run"
                with ex ->
                    return Error (sprintf "Failed to get run ID: %s" ex.Message)
            | Error err ->
                return Error (sprintf "Failed to get run ID: %s" err)
    }

let updateIssueAsync (issueNumber: int) (version: string) (analysis: IssueAnalysis) (workflowRunId: int option) (ct: CancellationToken) : Async<Result<unit, string>> =
    async {
        logInfo (sprintf "Updating issue #%d with resume information..." issueNumber)

        let phaseStr =
            match analysis.CurrentPhase with
            | Preparation -> "Preparation"
            | Execution -> "Execution"
            | Verification -> "Verification"
            | Documentation -> "Documentation"
            | PostRelease -> "Post-Release"
            | Unknown -> "Unknown"

        let workflowInfo =
            match workflowRunId with
            | Some runId ->
                sprintf """
**New Workflow Run**: [#%d](https://github.com/finos/morphir-dotnet/actions/runs/%d)
**Status**: 🔄 Running
""" runId runId
            | None -> ""

        let comment = sprintf """## Release Resumed

**Version**: %s
**Current Phase**: %s
**Strategy**: %s
%s
---
*Resumed by resume-release.fsx*
""" version phaseStr (analysis.ResumeStrategy |> Option.defaultValue "Manual intervention required") workflowInfo

        let escapedComment = comment.Replace("\"", "\\\"").Replace("\n", "\\n")
        let! result = runCommandAsync "gh" (sprintf "issue comment %d --body \"%s\"" issueNumber escapedComment) ct

        match result with
        | Ok _ ->
            logInfo "Issue updated successfully"
            return Ok ()
        | Error err ->
            return Error (sprintf "Failed to update issue: %s" err)
    }

// ============================================================================
// Display Helpers
// ============================================================================

let displayAnalysis (analysis: IssueAnalysis) =
    let phaseColor =
        match analysis.CurrentPhase with
        | Preparation -> "blue"
        | Execution -> "yellow"
        | Verification -> "cyan"
        | Documentation -> "magenta"
        | PostRelease -> "green"
        | Unknown -> "grey"

    let phaseStr =
        match analysis.CurrentPhase with
        | Preparation -> "Preparation"
        | Execution -> "Execution"
        | Verification -> "Verification"
        | Documentation -> "Documentation"
        | PostRelease -> "Post-Release"
        | Unknown -> "Unknown"

    AnsiConsole.MarkupLine(sprintf "[bold]Current Phase:[/] [%s]%s[/]" phaseColor phaseStr)
    AnsiConsole.MarkupLine(sprintf "[bold]Progress:[/] %d / %d items completed" analysis.CompletedItems.Length (analysis.CompletedItems.Length + analysis.PendingItems.Length))

    match analysis.FailurePoint with
    | Some point -> AnsiConsole.MarkupLine(sprintf "[red]Failure Point:[/] %s" point)
    | None -> ()

    AnsiConsole.WriteLine()

    if analysis.CanResume then
        AnsiConsole.MarkupLine("[green]✓ Release can be resumed[/]")
        match analysis.ResumeStrategy with
        | Some strategy ->
            AnsiConsole.MarkupLine(sprintf "[bold]Resume Strategy:[/] %s" strategy)
        | None -> ()
    else
        AnsiConsole.MarkupLine("[red]✗ Release cannot be automatically resumed[/]")

    AnsiConsole.WriteLine()

// ============================================================================
// Main Logic
// ============================================================================

let resumeAsync (results: ParseResults<ResumeArguments>) (ct: CancellationToken) : Async<ResumeResult> =
    async {
        let version = results.GetResult Version
        let issueNumber = results.GetResult Issue
        let jsonOutput = results.Contains Json
        let force = results.Contains Force

        let mutable warnings = []
        let mutable errors = []
        let mutable timedOut = false
        let mutable cancelled = false

        if not jsonOutput then
            AnsiConsole.Write(
                FigletText("Resume Release")
                    .Centered()
                    .Color(Color.Blue)
            )
            AnsiConsole.MarkupLine(sprintf "[bold]Version:[/] %s" version)
            AnsiConsole.MarkupLine(sprintf "[bold]Issue:[/] #%d" issueNumber)
            match results.TryGetResult Timeout with
            | Some mins -> AnsiConsole.MarkupLine(sprintf "[dim]Timeout: %d minutes[/]" mins)
            | None -> ()
            AnsiConsole.WriteLine()

        try
            // Fetch and analyze issue
            let! issueBodyResult = getIssueBodyAsync issueNumber ct

            match issueBodyResult with
            | Error err ->
                errors <- err :: errors
                return {
                    Success = false
                    Version = version
                    Issue = issueNumber
                    Analysis = {
                        Version = version
                        IssueNumber = issueNumber
                        CurrentPhase = Unknown
                        CompletedItems = []
                        PendingItems = []
                        FailurePoint = None
                        CanResume = false
                        ResumeStrategy = None
                    }
                    WorkflowTriggered = false
                    WorkflowRunId = None
                    IssueUpdated = false
                    TimedOut = timedOut
                    Cancelled = cancelled
                    Warnings = warnings
                    Errors = errors
                    ExitCode = 1
                }
            | Ok issueBody ->
                let analysis = analyzeIssue issueNumber version issueBody

                if not jsonOutput then
                    displayAnalysis analysis

                if not analysis.CanResume && not force then
                    errors <- "Release cannot be automatically resumed. Use --force to override." :: errors
                    return {
                        Success = false
                        Version = version
                        Issue = issueNumber
                        Analysis = analysis
                        WorkflowTriggered = false
                        WorkflowRunId = None
                        IssueUpdated = false
                        TimedOut = timedOut
                        Cancelled = cancelled
                        Warnings = warnings
                        Errors = errors
                        ExitCode = 1
                    }
                else
                    // Check for existing workflow
                    let! existingWorkflow = checkExistingWorkflowAsync version ct

                    let! (workflowTriggered, workflowRunId) =
                        async {
                            match existingWorkflow with
                            | Ok (Some workflow) when workflow.Status = "in_progress" || workflow.Status = "queued" ->
                                logInfo (sprintf "Found existing workflow run #%d in progress" workflow.Id)
                                warnings <- (sprintf "Workflow #%d already in progress" workflow.Id) :: warnings
                                return (false, Some workflow.Id)
                            | _ ->
                                // Trigger new workflow if we're in execution phase
                                if analysis.CurrentPhase = Execution || force then
                                    let! triggerResult = triggerWorkflowAsync version ct
                                    match triggerResult with
                                    | Ok runId ->
                                        return (true, Some runId)
                                    | Error err ->
                                        errors <- err :: errors
                                        return (false, None)
                                else
                                    logInfo "Not in execution phase, skipping workflow trigger"
                                    return (false, None)
                        }

                    // Update issue
                    let! issueUpdateResult = updateIssueAsync issueNumber version analysis workflowRunId ct
                    let issueUpdated =
                        match issueUpdateResult with
                        | Ok () -> true
                        | Error err ->
                            warnings <- err :: warnings
                            false

                    let success = List.isEmpty errors

                    if not jsonOutput then
                        if success then
                            AnsiConsole.MarkupLine("[green]✅ Release resume initiated[/]")
                        else
                            AnsiConsole.MarkupLine("[red]❌ Failed to resume release[/]")

                    return {
                        Success = success
                        Version = version
                        Issue = issueNumber
                        Analysis = analysis
                        WorkflowTriggered = workflowTriggered
                        WorkflowRunId = workflowRunId
                        IssueUpdated = issueUpdated
                        TimedOut = timedOut
                        Cancelled = cancelled
                        Warnings = warnings
                        Errors = errors
                        ExitCode = if success then 0 else 1
                    }

        with
        | :? OperationCanceledException ->
            if ct.IsCancellationRequested then
                cancelled <- true
                logWarn "Operation cancelled"
            return {
                Success = false
                Version = version
                Issue = issueNumber
                Analysis = {
                    Version = version
                    IssueNumber = issueNumber
                    CurrentPhase = Unknown
                    CompletedItems = []
                    PendingItems = []
                    FailurePoint = None
                    CanResume = false
                    ResumeStrategy = None
                }
                WorkflowTriggered = false
                WorkflowRunId = None
                IssueUpdated = false
                TimedOut = timedOut
                Cancelled = cancelled
                Warnings = warnings
                Errors = ["Operation cancelled"] @ errors
                ExitCode = 2
            }
        | :? TimeoutException ->
            timedOut <- true
            logError "Operation timed out"
            return {
                Success = false
                Version = version
                Issue = issueNumber
                Analysis = {
                    Version = version
                    IssueNumber = issueNumber
                    CurrentPhase = Unknown
                    CompletedItems = []
                    PendingItems = []
                    FailurePoint = None
                    CanResume = false
                    ResumeStrategy = None
                }
                WorkflowTriggered = false
                WorkflowRunId = None
                IssueUpdated = false
                TimedOut = timedOut
                Cancelled = cancelled
                Warnings = warnings
                Errors = ["Operation timed out"] @ errors
                ExitCode = 3
            }
    }

let outputJson (result: ResumeResult) =
    let options = JsonSerializerOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    )
    let json = JsonSerializer.Serialize(result, options)
    printfn "%s" json

let main (args: string array) =
    let parser = ArgumentParser.Create<ResumeArguments>(programName = "resume-release.fsx")

    try
        let results = parser.Parse(args)

        use cts = new CancellationTokenSource()

        // Set timeout if specified
        match results.TryGetResult Timeout with
        | Some minutes ->
            cts.CancelAfter(TimeSpan.FromMinutes(float minutes))
        | None -> ()

        // Handle Ctrl+C
        Console.CancelKeyPress.Add(fun args ->
            logWarn "Cancellation requested..."
            cts.Cancel()
            args.Cancel <- true
        )

        let result = Async.RunSynchronously(resumeAsync results cts.Token)

        if results.Contains Json then
            outputJson result

        result.ExitCode
    with
    | :? ArguParseException as ex ->
        logError ex.Message
        eprintfn "%s" (parser.PrintUsage())
        1
    | ex ->
        logError (sprintf "Unexpected error: %s" ex.Message)
        1

exit (main fsi.CommandLineArgs.[1..])
