#!/usr/bin/env dotnet fsi

(*
   Monitor PR Status Script

   Part of the Release Manager skill for morphir-dotnet.
   Monitors a GitHub pull request until all checks complete.

   Usage:
     dotnet fsi monitor-pr.fsx --pr 223
     dotnet fsi monitor-pr.fsx --pr 223 --auto-merge
     dotnet fsi monitor-pr.fsx --pr 223 --interval 15 --timeout 30
     dotnet fsi monitor-pr.fsx --help

   Features:
   - Polls PR status at configurable intervals
   - Displays live progress of checks with colorized output
   - Exits when all checks complete (success or failure)
   - Optionally auto-merges PR when all checks pass
   - Follows CLI standards (stdout = data, stderr = diagnostics)
*)

#r "nuget: Argu, 6.2.4"
#r "nuget: Spectre.Console, 0.53.0"
#r "nuget: System.Text.Json, 9.0.0"

open System
open System.Diagnostics
open System.Text.Json
open Argu
open Spectre.Console

// ============================================================================
// CLI Arguments (Argu)
// ============================================================================

type CliArguments =
    | [<Mandatory; AltCommandLine("-p")>] Pr of int
    | [<AltCommandLine("-i")>] Interval of seconds:int
    | [<AltCommandLine("-t")>] Timeout of minutes:int
    | [<AltCommandLine("-m")>] Auto_Merge
    | [<AltCommandLine("-v")>] Verbose
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Pr _ -> "PR number to monitor (required)"
            | Interval _ -> "polling interval in seconds (default: 30)"
            | Timeout _ -> "timeout in minutes (default: 30)"
            | Auto_Merge -> "automatically merge PR when all checks pass"
            | Verbose -> "enable verbose diagnostic output"

// ============================================================================
// Types
// ============================================================================

type CheckStatus =
    | Pending
    | InProgress
    | Completed

type CheckConclusion =
    | Success
    | Failure
    | Cancelled
    | Skipped
    | Neutral
    | TimedOut
    | ActionRequired
    | Unknown

type Check = {
    Name: string
    Status: CheckStatus
    Conclusion: CheckConclusion option
    StartedAt: DateTime option
    CompletedAt: DateTime option
    DetailsUrl: string option
}

type PrStatus = {
    Number: int
    Title: string
    State: string
    Checks: Check list
    Mergeable: string
}

// ============================================================================
// GitHub CLI Integration
// ============================================================================

let runGhCommand (args: string) (verbose: bool) : Result<string, string> =
    try
        if verbose then
            eprintfn "[VERBOSE] Running: gh %s" args

        let psi = ProcessStartInfo()
        psi.FileName <- "gh"
        psi.Arguments <- args
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false

        use proc = Process.Start(psi)
        let output = proc.StandardOutput.ReadToEnd()
        let error = proc.StandardError.ReadToEnd()
        proc.WaitForExit()

        if proc.ExitCode = 0 then
            Ok output
        else
            Error error
    with ex ->
        Error ex.Message

let parseCheckStatus (status: string) : CheckStatus =
    match status.ToUpperInvariant() with
    | "PENDING" | "QUEUED" -> Pending
    | "IN_PROGRESS" -> InProgress
    | "COMPLETED" -> Completed
    | _ -> Pending

let parseCheckConclusion (conclusion: string option) : CheckConclusion option =
    match conclusion with
    | None | Some "" -> None
    | Some c ->
        match c.ToUpperInvariant() with
        | "SUCCESS" -> Some Success
        | "FAILURE" -> Some Failure
        | "CANCELLED" -> Some Cancelled
        | "SKIPPED" -> Some Skipped
        | "NEUTRAL" -> Some Neutral
        | "TIMED_OUT" -> Some TimedOut
        | "ACTION_REQUIRED" -> Some ActionRequired
        | _ -> Some Unknown

let getPrStatus (prNumber: int) (verbose: bool) : Result<PrStatus, string> =
    let query = $"pr view {prNumber} --json number,title,state,statusCheckRollup,mergeable"

    match runGhCommand query verbose with
    | Error err -> Error err
    | Ok json ->
        try
            if verbose then
                eprintfn "[VERBOSE] Parsing JSON response (length: %d)" json.Length

            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            let checks =
                match root.TryGetProperty("statusCheckRollup") with
                | true, checksArray ->
                    checksArray.EnumerateArray()
                    |> Seq.filter (fun check ->
                        // Skip EasyCLA and StatusContext (use CheckRun only)
                        match check.TryGetProperty("__typename") with
                        | true, typename when typename.GetString() = "StatusContext" -> false
                        | _ ->
                            match check.TryGetProperty("name") with
                            | true, nameProp ->
                                let name = nameProp.GetString()
                                name <> "EasyCLA"
                            | _ -> false
                    )
                    |> Seq.map (fun check ->
                        let name = check.GetProperty("name").GetString()
                        let status = check.GetProperty("status").GetString()
                        let conclusion =
                            match check.TryGetProperty("conclusion") with
                            | true, prop when prop.ValueKind <> JsonValueKind.Null ->
                                Some (prop.GetString())
                            | _ -> None

                        let startedAt =
                            match check.TryGetProperty("startedAt") with
                            | true, prop when prop.ValueKind <> JsonValueKind.Null ->
                                DateTime.TryParse(prop.GetString()) |> function
                                | true, dt -> Some dt
                                | false, _ -> None
                            | _ -> None

                        let completedAt =
                            match check.TryGetProperty("completedAt") with
                            | true, prop when prop.ValueKind <> JsonValueKind.Null ->
                                DateTime.TryParse(prop.GetString()) |> function
                                | true, dt when dt.Year > 1 -> Some dt
                                | _ -> None
                            | _ -> None

                        let detailsUrl =
                            match check.TryGetProperty("detailsUrl") with
                            | true, prop -> Some (prop.GetString())
                            | _ -> None

                        {
                            Name = name
                            Status = parseCheckStatus status
                            Conclusion = parseCheckConclusion conclusion
                            StartedAt = startedAt
                            CompletedAt = completedAt
                            DetailsUrl = detailsUrl
                        }
                    )
                    |> Seq.toList
                | false, _ -> []

            let title = root.GetProperty("title").GetString()
            let state = root.GetProperty("state").GetString()
            let mergeable = root.GetProperty("mergeable").GetString()

            Ok {
                Number = prNumber
                Title = title
                State = state
                Checks = checks
                Mergeable = mergeable
            }
        with ex ->
            Error $"Failed to parse PR status: {ex.Message}"

let mergePr (prNumber: int) (verbose: bool) : Result<unit, string> =
    eprintfn "Attempting to merge PR #%d..." prNumber
    match runGhCommand $"pr merge {prNumber} --auto --squash" verbose with
    | Ok _ -> Ok ()
    | Error err -> Error err

// ============================================================================
// Display
// ============================================================================

let getCheckStatusIcon (check: Check) : string =
    match check.Status, check.Conclusion with
    | Completed, Some Success -> "[green]✅[/]"
    | Completed, Some Failure -> "[red]❌[/]"
    | Completed, Some Cancelled -> "[yellow]⚠️[/]"
    | Completed, Some Skipped -> "[gray]⏭️[/]"
    | InProgress, _ -> "[blue]⏳[/]"
    | Pending, _ -> "[gray]⏸️[/]"
    | _ -> "[gray]❓[/]"

let getCheckStatusText (check: Check) : string =
    match check.Status, check.Conclusion with
    | Completed, Some Success -> "[green]Success[/]"
    | Completed, Some Failure -> "[red]Failure[/]"
    | Completed, Some Cancelled -> "[yellow]Cancelled[/]"
    | Completed, Some Skipped -> "[dim]Skipped[/]"
    | InProgress, _ -> "[blue]Running[/]"
    | Pending, _ -> "[dim]Queued[/]"
    | _ -> "[dim]Unknown[/]"

let getElapsedTime (check: Check) : string =
    match check.StartedAt, check.CompletedAt with
    | Some started, Some completed ->
        let elapsed = completed - started
        $"{elapsed.TotalSeconds:F0}s"
    | Some started, None ->
        let elapsed = DateTime.UtcNow - started
        $"{elapsed.TotalSeconds:F0}s"
    | _ -> "-"

let displayPrStatus (status: PrStatus) (verbose: bool) =
    AnsiConsole.Clear()

    let rule = Rule($"[bold]PR #{status.Number}: {status.Title}[/]")
    rule.Style <- Style.Parse("blue")
    AnsiConsole.Write(rule)
    AnsiConsole.WriteLine()

    if status.Checks.IsEmpty then
        AnsiConsole.MarkupLine("[yellow]No checks found for this PR[/]")
    else
        let table = Table()
        table.Border <- TableBorder.Rounded
        table.AddColumn(TableColumn("[bold]Check[/]").LeftAligned()) |> ignore
        table.AddColumn(TableColumn("[bold]Status[/]").Centered()) |> ignore
        table.AddColumn(TableColumn("[bold]Elapsed[/]").RightAligned()) |> ignore

        for check in status.Checks do
            let icon = getCheckStatusIcon check
            let statusText = getCheckStatusText check
            let elapsed = getElapsedTime check

            table.AddRow(
                $"{icon} {check.Name}",
                statusText,
                elapsed
            )
            |> ignore

        AnsiConsole.Write(table)
        AnsiConsole.WriteLine()

        // Summary
        let total = status.Checks.Length
        let completed = status.Checks |> List.filter (fun c -> c.Status = Completed) |> List.length
        let inProgress = status.Checks |> List.filter (fun c -> c.Status = InProgress) |> List.length
        let pending = status.Checks |> List.filter (fun c -> c.Status = Pending) |> List.length

        AnsiConsole.MarkupLine($"[dim]Total: {total} | Completed: {completed} | Running: {inProgress} | Queued: {pending}[/]")
        AnsiConsole.WriteLine()

let allChecksCompleted (status: PrStatus) : bool =
    not status.Checks.IsEmpty &&
    status.Checks |> List.forall (fun c -> c.Status = Completed)

let allChecksPassed (status: PrStatus) : bool =
    not status.Checks.IsEmpty &&
    status.Checks
    |> List.forall (fun c ->
        match c.Conclusion with
        | Some Success | Some Skipped -> true
        | _ -> false
    )

let anyCheckFailed (status: PrStatus) : bool =
    status.Checks
    |> List.exists (fun c ->
        match c.Conclusion with
        | Some Failure -> true
        | _ -> false
    )

// ============================================================================
// Main Monitoring Loop
// ============================================================================

let monitor (args: ParseResults<CliArguments>) : int =
    let prNumber = args.GetResult Pr
    let interval = args.GetResult(Interval, defaultValue = 30)
    let timeoutMinutes = args.GetResult(Timeout, defaultValue = 30)
    let autoMerge = args.Contains Auto_Merge
    let verbose = args.Contains Verbose

    let startTime = DateTime.UtcNow

    eprintfn "Monitoring PR #%d" prNumber
    eprintfn "Polling every %d seconds (timeout: %d minutes)" interval timeoutMinutes
    if autoMerge then
        eprintfn "Auto-merge: ENABLED"
    eprintfn ""

    let rec loop iteration =
        if verbose then
            eprintfn "[VERBOSE] Iteration %d - fetching PR status..." iteration

        match getPrStatus prNumber verbose with
        | Error err ->
            eprintfn "Error fetching PR status: %s" err
            1
        | Ok status ->
            displayPrStatus status verbose

            let elapsed = DateTime.UtcNow - startTime
            if elapsed.TotalMinutes > float timeoutMinutes then
                eprintfn "Timeout after %d minutes" timeoutMinutes
                2
            elif allChecksCompleted status then
                if allChecksPassed status then
                    eprintfn "✅ All checks passed!"

                    if autoMerge then
                        match mergePr prNumber verbose with
                        | Ok () ->
                            eprintfn "✅ PR merged successfully"
                            printfn "merged" // stdout output
                            0
                        | Error err ->
                            eprintfn "⚠️  Auto-merge failed: %s" err
                            eprintfn "You may need to merge manually"
                            printfn "checks-passed" // stdout output
                            0
                    else
                        eprintfn "Run with --auto-merge to merge automatically"
                        printfn "checks-passed" // stdout output
                        0
                elif anyCheckFailed status then
                    eprintfn "❌ Some checks failed"
                    eprintfn ""

                    // Show failed checks with details
                    let failedChecks =
                        status.Checks
                        |> List.filter (fun c -> c.Conclusion = Some Failure)

                    for check in failedChecks do
                        eprintfn "Failed: %s" check.Name
                        match check.DetailsUrl with
                        | Some url -> eprintfn "  %s" url
                        | None -> ()

                    printfn "checks-failed" // stdout output
                    1
                else
                    eprintfn "All checks completed but some were cancelled/skipped"
                    printfn "checks-completed" // stdout output
                    0
            else
                // Still running, wait and check again
                eprintfn "Waiting %d seconds before next check..." interval
                System.Threading.Thread.Sleep(interval * 1000)
                loop (iteration + 1)

    loop 1

// ============================================================================
// Entry Point
// ============================================================================

let parser = ArgumentParser.Create<CliArguments>(
    programName = "monitor-pr.fsx",
    helpTextMessage = "Monitor GitHub PR checks until completion"
)

try
    if fsi.CommandLineArgs.Length > 1 then
        let args = parser.ParseCommandLine(inputs = (fsi.CommandLineArgs |> Array.skip 1), raiseOnUsage = true)
        let exitCode = monitor args
        exit exitCode
    else
        eprintfn "Usage: dotnet fsi monitor-pr.fsx --pr <number> [options]"
        eprintfn "Run with --help for more information"
        exit 1
with
| :? ArguParseException as ex ->
    eprintfn "%s" ex.Message
    exit 1
| ex ->
    eprintfn "Error: %s" ex.Message
    exit 1
