#!/usr/bin/env dotnet fsi
// Monitor a GitHub Actions workflow run for morphir-dotnet release
// Usage: dotnet fsi monitor-release.fsx --version 1.2.0 [--run-id <id>] [--issue <number>] [--update-issue] [--json] [--use-gh-watch] [--timeout <minutes>]

#r "nuget: Spectre.Console, 0.53.0"
#r "nuget: System.Text.Json, 9.0.0"
#r "nuget: Argu, 6.2.4"

open System
open System.IO
open System.Diagnostics
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading
open Argu
open Spectre.Console

// ============================================================================
// Release History Tracking (inlined for simplicity)
// ============================================================================

[<JsonConverter(typeof<JsonStringEnumConverter>)>]
type ReleaseStatus =
    | Success
    | Failure
    | Cancelled

type ReleaseRecord = {
    Version: string
    Date: DateTime
    Status: ReleaseStatus
    IssueNumber: int option
    Notes: string option
}

type ReleaseHistory = {
    Releases: ReleaseRecord list
    ConsecutiveSuccesses: int
    ConsecutiveFailures: int
    LastUpdated: DateTime
}

let historyFile =
    let scriptDir = __SOURCE_DIRECTORY__
    let projectRoot = Path.GetFullPath(Path.Combine(scriptDir, "..", "..", ".."))
    Path.Combine(projectRoot, ".claude", "skills", "release-manager", ".release-history.json")

let serializerOptions =
    let options = JsonSerializerOptions(WriteIndented = true)
    options.Converters.Add(JsonStringEnumConverter())
    options

let loadHistory () : ReleaseHistory =
    if File.Exists(historyFile) then
        try
            let json = File.ReadAllText(historyFile)
            JsonSerializer.Deserialize<ReleaseHistory>(json, serializerOptions)
        with _ ->
            {
                Releases = []
                ConsecutiveSuccesses = 0
                ConsecutiveFailures = 0
                LastUpdated = DateTime.UtcNow
            }
    else
        {
            Releases = []
            ConsecutiveSuccesses = 0
            ConsecutiveFailures = 0
            LastUpdated = DateTime.UtcNow
        }

let saveHistory (history: ReleaseHistory) : unit =
    try
        let dir = Path.GetDirectoryName(historyFile)
        if not (Directory.Exists(dir)) then
            Directory.CreateDirectory(dir) |> ignore
        
        let json = JsonSerializer.Serialize(history, serializerOptions)
        File.WriteAllText(historyFile, json)
    with ex ->
        eprintfn "[ERROR] Failed to save release history: %s" ex.Message

let addRelease (version: string) (status: ReleaseStatus) (issueNumber: int option) (notes: string option) : ReleaseHistory =
    let history = loadHistory()
    
    let newRecord = {
        Version = version
        Date = DateTime.UtcNow
        Status = status
        IssueNumber = issueNumber
        Notes = notes
    }
    
    let consecutiveSuccesses =
        match status with
        | Success -> history.ConsecutiveSuccesses + 1
        | _ -> 0
    
    let consecutiveFailures =
        match status with
        | Failure -> history.ConsecutiveFailures + 1
        | _ -> 0
    
    let updatedHistory = {
        Releases = newRecord :: history.Releases
        ConsecutiveSuccesses = consecutiveSuccesses
        ConsecutiveFailures = consecutiveFailures
        LastUpdated = DateTime.UtcNow
    }
    
    saveHistory updatedHistory
    updatedHistory

let promptForFeedback (question: string) : string option =
    eprintfn ""
    eprintfn "[FEEDBACK REQUEST]"
    eprintfn "%s" question
    eprintfn ""
    eprintfn "Enter your feedback (or press Enter to skip):"
    eprintfn "> "
    
    let response = Console.ReadLine()
    if String.IsNullOrWhiteSpace(response) then
        None
    else
        Some response

// ============================================================================
// CLI Arguments
// ============================================================================

type MonitorArguments =
    | [<Mandatory; AltCommandLine("-v")>] Version of string
    | [<AltCommandLine("-r")>] Run_Id of int
    | [<AltCommandLine("-i")>] Issue of int
    | Update_Issue
    | Use_Gh_Watch
    | Json
    | [<AltCommandLine("-t")>] Timeout of int

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Version _ -> "Release version to monitor (e.g., 1.2.0)"
            | Run_Id _ -> "Specific workflow run ID to monitor"
            | Issue _ -> "GitHub issue number to update with progress"
            | Update_Issue -> "Update the specified issue with workflow status"
            | Use_Gh_Watch -> "Use 'gh run watch' for real-time monitoring"
            | Json -> "Output results as JSON"
            | Timeout _ -> "Maximum time to wait in minutes"

// ============================================================================
// Types
// ============================================================================

type WorkflowJob = {
    Name: string
    Status: string
    Conclusion: string option
}

type WorkflowRun = {
    Id: int
    Status: string
    Conclusion: string option
    StartedAt: DateTime
    UpdatedAt: DateTime
    Jobs: WorkflowJob list
}

type MonitorResult = {
    Success: bool
    Version: string
    Run: WorkflowRun option
    IssueUpdated: bool
    Duration: TimeSpan option
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
// GitHub CLI Interactions
// ============================================================================

let findLatestDeploymentRunAsync (version: string) (ct: CancellationToken) : Async<Result<int, string>> =
    async {
        logInfo "Finding latest deployment workflow run..."

        let! result = runCommandAsync "gh" "run list --workflow=deployment.yml --limit 10 --json databaseId,status,conclusion,headBranch" ct

        match result with
        | Error err -> return Error (sprintf "Failed to list workflow runs: %s" err)
        | Ok output ->
            try
                use doc = JsonDocument.Parse(output)
                let runs = doc.RootElement.EnumerateArray() |> Seq.toList

                match runs |> List.tryHead with
                | Some run ->
                    let runId = run.GetProperty("databaseId").GetInt32()
                    return Ok runId
                | None ->
                    return Error "No deployment workflow runs found"
            with ex ->
                return Error (sprintf "Failed to parse workflow runs: %s" ex.Message)
    }

let getWorkflowRunAsync (runId: int) (ct: CancellationToken) : Async<Result<WorkflowRun, string>> =
    async {
        let! result = runCommandAsync "gh" (sprintf "run view %d --json databaseId,status,conclusion,startedAt,updatedAt" runId) ct

        match result with
        | Error err -> return Error (sprintf "Failed to get workflow run: %s" err)
        | Ok output ->
            try
                use doc = JsonDocument.Parse(output)
                let root = doc.RootElement

                let status = root.GetProperty("status").GetString()
                let conclusionProp = root.GetProperty("conclusion")
                let conclusion = if conclusionProp.ValueKind = JsonValueKind.Null then None else Some (conclusionProp.GetString())
                let startedAt = DateTime.Parse(root.GetProperty("startedAt").GetString())
                let updatedAt = DateTime.Parse(root.GetProperty("updatedAt").GetString())

                return Ok {
                    Id = runId
                    Status = status
                    Conclusion = conclusion
                    StartedAt = startedAt
                    UpdatedAt = updatedAt
                    Jobs = [] // Will be populated separately if needed
                }
            with ex ->
                return Error (sprintf "Failed to parse workflow run: %s" ex.Message)
    }

let getWorkflowJobsAsync (runId: int) (ct: CancellationToken) : Async<Result<WorkflowJob list, string>> =
    async {
        let! result = runCommandAsync "gh" (sprintf "run view %d --json jobs" runId) ct

        match result with
        | Error err -> return Error (sprintf "Failed to get workflow jobs: %s" err)
        | Ok output ->
            try
                use doc = JsonDocument.Parse(output)
                let jobsArray = doc.RootElement.GetProperty("jobs").EnumerateArray()

                let jobs =
                    jobsArray
                    |> Seq.map (fun job ->
                        let name = job.GetProperty("name").GetString()
                        let status = job.GetProperty("status").GetString()
                        let conclusionProp = job.GetProperty("conclusion")
                        let conclusion = if conclusionProp.ValueKind = JsonValueKind.Null then None else Some (conclusionProp.GetString())

                        { Name = name; Status = status; Conclusion = conclusion }
                    )
                    |> Seq.toList

                return Ok jobs
            with ex ->
                return Error (sprintf "Failed to parse workflow jobs: %s" ex.Message)
    }

let watchWorkflowRunAsync (runId: int) (ct: CancellationToken) : Async<Result<unit, string>> =
    async {
        logInfo (sprintf "Watching workflow run %d using gh CLI..." runId)

        let psi = ProcessStartInfo(
            FileName = "gh",
            Arguments = sprintf "run watch %d" runId,
            WorkingDirectory = projectRoot,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false
        )

        try
            use proc = new Process()
            proc.StartInfo <- psi
            proc.Start() |> ignore

            // Register cancellation
            use _ = ct.Register(fun () ->
                try
                    if not proc.HasExited then
                        logWarn "Cancelling gh run watch..."
                        proc.Kill()
                with _ -> ()
            )

            proc.WaitForExit()

            if ct.IsCancellationRequested then
                return Error "Watch cancelled"
            elif proc.ExitCode = 0 then
                return Ok ()
            else
                return Error (sprintf "gh run watch failed with exit code %d" proc.ExitCode)
        with
        | :? OperationCanceledException ->
            return Error "Watch cancelled"
        | ex ->
            return Error (sprintf "Failed to watch workflow: %s" ex.Message)
    }

let updateIssueAsync (issueNumber: int) (version: string) (run: WorkflowRun) (ct: CancellationToken) : Async<Result<unit, string>> =
    async {
        logInfo (sprintf "Updating issue #%d with workflow status..." issueNumber)

        let statusIcon =
            match run.Status, run.Conclusion with
            | "completed", Some "success" -> "✅"
            | "completed", Some "failure" -> "❌"
            | "in_progress", _ -> "🔄"
            | "queued", _ -> "⏳"
            | _ -> "❓"

        let duration = run.UpdatedAt - run.StartedAt
        let durationStr = sprintf "%02d:%02d:%02d" (int duration.TotalHours) duration.Minutes duration.Seconds

        let comment = sprintf """## Workflow Update

**Status**: %s %s
**Run ID**: %d
**Duration**: %s
**Updated**: %s

[View Workflow Run](https://github.com/finos/morphir-dotnet/actions/runs/%d)

---
*Updated by monitor-release.fsx*
""" statusIcon run.Status run.Id durationStr (run.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")) run.Id

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

let displayRunStatus (run: WorkflowRun) =
    let statusColor =
        match run.Status, run.Conclusion with
        | "completed", Some "success" -> "green"
        | "completed", Some "failure" -> "red"
        | "in_progress", _ -> "yellow"
        | "queued", _ -> "blue"
        | _ -> "grey"

    let statusText =
        match run.Conclusion with
        | Some conclusion -> sprintf "%s (%s)" run.Status conclusion
        | None -> run.Status

    let duration = run.UpdatedAt - run.StartedAt
    let durationStr = sprintf "%02d:%02d:%02d" (int duration.TotalHours) duration.Minutes duration.Seconds

    AnsiConsole.MarkupLine(sprintf "[bold]Workflow Run #%d[/]" run.Id)
    AnsiConsole.MarkupLine(sprintf "[%s]Status: %s[/]" statusColor statusText)
    AnsiConsole.MarkupLine(sprintf "Started: %s" (run.StartedAt.ToString("yyyy-MM-dd HH:mm:ss")))
    AnsiConsole.MarkupLine(sprintf "Updated: %s" (run.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")))
    AnsiConsole.MarkupLine(sprintf "Duration: %s" durationStr)
    AnsiConsole.WriteLine()

let displayJobs (jobs: WorkflowJob list) =
    let table = Table()
    table.AddColumn("Job Name") |> ignore
    table.AddColumn("Status") |> ignore
    table.AddColumn("Conclusion") |> ignore

    for job in jobs do
        let statusMarkup =
            match job.Status with
            | "completed" -> "[green]completed[/]"
            | "in_progress" -> "[yellow]in_progress[/]"
            | "queued" -> "[blue]queued[/]"
            | _ -> job.Status

        let conclusionMarkup =
            match job.Conclusion with
            | Some "success" -> "[green]success[/]"
            | Some "failure" -> "[red]failure[/]"
            | Some c -> c
            | None -> "-"

        table.AddRow(job.Name, statusMarkup, conclusionMarkup) |> ignore

    AnsiConsole.Write(table)
    AnsiConsole.WriteLine()

// ============================================================================
// Main Logic
// ============================================================================

let monitorAsync (results: ParseResults<MonitorArguments>) (ct: CancellationToken) : Async<MonitorResult> =
    async {
        let version = results.GetResult Version
        let runId = results.TryGetResult Run_Id
        let issueNumber = results.TryGetResult Issue
        let updateIssue = results.Contains Update_Issue
        let useGhWatch = results.Contains Use_Gh_Watch
        let jsonOutput = results.Contains Json

        let mutable warnings = []
        let mutable errors = []
        let mutable timedOut = false
        let mutable cancelled = false

        if not jsonOutput then
            AnsiConsole.Write(
                FigletText("Release Monitor")
                    .Centered()
                    .Color(Color.Blue)
            )
            AnsiConsole.MarkupLine(sprintf "[bold]Version:[/] %s" version)
            match results.TryGetResult Timeout with
            | Some mins -> AnsiConsole.MarkupLine(sprintf "[dim]Timeout: %d minutes[/]" mins)
            | None -> ()
            AnsiConsole.WriteLine()

        try
            // Find or use provided run ID
            let! runIdResult =
                match runId with
                | Some id ->
                    logInfo (sprintf "Using provided run ID: %d" id)
                    async { return Ok id }
                | None ->
                    logInfo "No run ID provided, finding latest deployment run..."
                    findLatestDeploymentRunAsync version ct

            match runIdResult with
            | Error err ->
                errors <- err :: errors
                return {
                    Success = false
                    Version = version
                    Run = None
                    IssueUpdated = false
                    Duration = None
                    TimedOut = timedOut
                    Cancelled = cancelled
                    Warnings = warnings
                    Errors = errors
                    ExitCode = 1
                }
            | Ok runId ->
                // Use gh watch if requested
                if useGhWatch then
                    let! watchResult = watchWorkflowRunAsync runId ct
                    match watchResult with
                    | Error err ->
                        if err.Contains("cancelled") then
                            cancelled <- true
                        errors <- err :: errors
                    | Ok () ->
                        logInfo "gh run watch completed"

                // Get final status
                let! runResult = getWorkflowRunAsync runId ct
                match runResult with
                | Error err ->
                    errors <- err :: errors
                    return {
                        Success = false
                        Version = version
                        Run = None
                        IssueUpdated = false
                        Duration = None
                        TimedOut = timedOut
                        Cancelled = cancelled
                        Warnings = warnings
                        Errors = errors
                        ExitCode = 1
                    }
                | Ok run ->
                    // Get jobs for detailed status
                    let! jobsResult = getWorkflowJobsAsync runId ct
                    let jobs =
                        match jobsResult with
                        | Ok jobs -> jobs
                        | Error err ->
                            warnings <- (sprintf "Could not fetch jobs: %s" err) :: warnings
                            []

                    let runWithJobs = { run with Jobs = jobs }

                    if not jsonOutput then
                        displayRunStatus runWithJobs
                        if not (List.isEmpty jobs) then
                            AnsiConsole.MarkupLine("[bold]Jobs:[/]")
                            displayJobs jobs

                    // Update issue if requested
                    let! issueUpdated =
                        async {
                            match issueNumber, updateIssue with
                            | Some issueNum, true ->
                                let! updateResult = updateIssueAsync issueNum version runWithJobs ct
                                match updateResult with
                                | Ok () -> return true
                                | Error err ->
                                    warnings <- (sprintf "Failed to update issue: %s" err) :: warnings
                                    return false
                            | _ -> return false
                        }

                    let success = run.Conclusion = Some "success"
                    let duration = Some (run.UpdatedAt - run.StartedAt)

                    // Track release in history and prompt for retrospective feedback on failure
                    let releaseStatus = if success then Success else Failure
                    let _ = addRelease version releaseStatus issueNumber None
                    
                    // Prompt for retrospective feedback if failed
                    let feedbackGiven =
                        if not success && not jsonOutput && not ct.IsCancellationRequested then
                            let feedback = promptForFeedback "We noticed the release failed. Are there any changes we could make to the release process to ensure future success?"
                            
                            match feedback, issueNumber with
                            | Some fb, Some issueNum when not (String.IsNullOrWhiteSpace(fb)) ->
                                // Add feedback to issue
                                let feedbackComment = sprintf """## 📝 Release Failure Retrospective

**Feedback from Release Manager:**

%s

---
*Collected by monitor-release.fsx after release failure*
""" fb
                                let escapedFeedback = feedbackComment.Replace("\"", "\\\"").Replace("\n", "\\n")
                                let feedbackResult = 
                                    Async.RunSynchronously(
                                        runCommandAsync "gh" (sprintf "issue comment %d --body \"%s\"" issueNum escapedFeedback) ct,
                                        cancellationToken = ct
                                    )
                                match feedbackResult with
                                | Ok _ -> 
                                    logInfo "Retrospective feedback added to issue"
                                    true
                                | Error err ->
                                    logWarn (sprintf "Failed to add feedback to issue: %s" err)
                                    false
                            | _ -> false
                        else
                            false

                    if not jsonOutput then
                        if success then
                            AnsiConsole.MarkupLine("[green]✅ Workflow completed successfully[/]")
                        else
                            AnsiConsole.MarkupLine(sprintf "[red]❌ Workflow %s[/]" (run.Conclusion |> Option.defaultValue "incomplete"))
                            if feedbackGiven then
                                AnsiConsole.MarkupLine("[green]📝 Retrospective feedback recorded[/]")

                    return {
                        Success = success
                        Version = version
                        Run = Some runWithJobs
                        IssueUpdated = issueUpdated
                        Duration = duration
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
                Run = None
                IssueUpdated = false
                Duration = None
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
                Run = None
                IssueUpdated = false
                Duration = None
                TimedOut = timedOut
                Cancelled = cancelled
                Warnings = warnings
                Errors = ["Operation timed out"] @ errors
                ExitCode = 3
            }
    }

let outputJson (result: MonitorResult) =
    let options = JsonSerializerOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    )
    let json = JsonSerializer.Serialize(result, options)
    printfn "%s" json

let main (args: string array) =
    let parser = ArgumentParser.Create<MonitorArguments>(programName = "monitor-release.fsx")

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

        let result = Async.RunSynchronously(monitorAsync results cts.Token)

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
