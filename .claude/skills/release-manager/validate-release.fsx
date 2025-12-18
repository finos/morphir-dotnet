#!/usr/bin/env dotnet fsi
// Validate a morphir-dotnet release on NuGet
// Usage: dotnet fsi validate-release.fsx --version 1.2.0 [--smoke-tests] [--issue <number>] [--update-issue] [--json] [--timeout <minutes>]

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

let historyFile =
    let scriptDir = __SOURCE_DIRECTORY__
    let projectRoot = Path.GetFullPath(Path.Combine(scriptDir, "..", "..", ".."))
    Path.Combine(projectRoot, ".claude", "skills", "release-manager", ".release-history.json")

let getConsecutiveSuccesses () : int =
    if File.Exists(historyFile) then
        try
            let json = File.ReadAllText(historyFile)
            use doc = JsonDocument.Parse(json)
            let root = doc.RootElement
            if root.TryGetProperty("ConsecutiveSuccesses", &_) then
                root.GetProperty("ConsecutiveSuccesses").GetInt32()
            else
                0
        with _ -> 0
    else
        0

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

let shouldPromptForSuccessFeedback () : bool =
    getConsecutiveSuccesses() >= 3

// ============================================================================
// CLI Arguments
// ============================================================================

type ValidateArguments =
    | [<Mandatory; AltCommandLine("-v")>] Version of string
    | [<AltCommandLine("-i")>] Issue of int
    | Update_Issue
    | Smoke_Tests
    | Json
    | [<AltCommandLine("-t")>] Timeout of int

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Version _ -> "Release version to validate (e.g., 1.2.0)"
            | Issue _ -> "GitHub issue number to update with validation results"
            | Update_Issue -> "Update the specified issue with validation status"
            | Smoke_Tests -> "Run smoke tests after validation"
            | Json -> "Output results as JSON"
            | Timeout _ -> "Maximum time to wait in minutes"

// ============================================================================
// Types
// ============================================================================

type PackageInfo = {
    Name: string
    Version: string
    Available: bool
    DownloadUrl: string option
    PublishedAt: DateTime option
}

type SmokeTestResult = {
    TestName: string
    Passed: bool
    Duration: TimeSpan
    Error: string option
}

type ValidationResult = {
    Success: bool
    Version: string
    Packages: PackageInfo list
    AllPackagesAvailable: bool
    ToolInstallTest: bool option
    SmokeTests: SmokeTestResult list
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
// Package Validation
// ============================================================================

let expectedPackages = [
    "Morphir.Core"
    "Morphir.Tooling"
    "Morphir"
    "Morphir.Tool"
]

let checkPackageAsync (packageName: string) (version: string) (ct: CancellationToken) : Async<PackageInfo> =
    async {
        logInfo (sprintf "Checking %s v%s on NuGet..." packageName version)

        let! result = runCommandAsync "dotnet" (sprintf "package search %s --exact-match --format json" packageName) ct

        match result with
        | Error err ->
            logWarn (sprintf "Failed to search for %s: %s" packageName err)
            return {
                Name = packageName
                Version = version
                Available = false
                DownloadUrl = None
                PublishedAt = None
            }
        | Ok output ->
            try
                use doc = JsonDocument.Parse(output)
                let searchResults = doc.RootElement.GetProperty("searchResult")

                let packageElement =
                    searchResults.EnumerateArray()
                    |> Seq.tryFind (fun p ->
                        let packages = p.GetProperty("packages")
                        packages.EnumerateArray()
                        |> Seq.exists (fun pkg ->
                            pkg.GetProperty("id").GetString() = packageName &&
                            pkg.GetProperty("version").GetString() = version
                        )
                    )

                match packageElement with
                | Some elem ->
                    let pkg =
                        elem.GetProperty("packages").EnumerateArray()
                        |> Seq.find (fun pkg ->
                            pkg.GetProperty("id").GetString() = packageName &&
                            pkg.GetProperty("version").GetString() = version
                        )

                    let downloadUrl = sprintf "https://www.nuget.org/packages/%s/%s" packageName version

                    logInfo (sprintf "✓ %s v%s found on NuGet" packageName version)

                    return {
                        Name = packageName
                        Version = version
                        Available = true
                        DownloadUrl = Some downloadUrl
                        PublishedAt = None // NuGet API doesn't provide this easily via dotnet CLI
                    }
                | None ->
                    logWarn (sprintf "✗ %s v%s not found on NuGet" packageName version)
                    return {
                        Name = packageName
                        Version = version
                        Available = false
                        DownloadUrl = None
                        PublishedAt = None
                    }
            with ex ->
                logError (sprintf "Failed to parse package search results: %s" ex.Message)
                return {
                    Name = packageName
                    Version = version
                    Available = false
                    DownloadUrl = None
                    PublishedAt = None
                }
    }

// ============================================================================
// Tool Installation Test
// ============================================================================

let testToolInstallAsync (version: string) (ct: CancellationToken) : Async<Result<unit, string>> =
    async {
        logInfo "Testing tool installation..."

        // Uninstall first (ignore errors)
        let! _ = runCommandAsync "dotnet" "tool uninstall -g Morphir.Tool" ct

        // Try to install the specific version
        let! installResult = runCommandAsync "dotnet" (sprintf "tool install -g Morphir.Tool --version %s" version) ct

        match installResult with
        | Error err -> return Error (sprintf "Tool installation failed: %s" err)
        | Ok _ ->
            logInfo "Tool installed successfully, testing execution..."

            // Test that the tool runs
            let! versionResult = runCommandAsync "dotnet-morphir" "--version" ct

            match versionResult with
            | Ok output ->
                if output.Contains(version) then
                    logInfo "✓ Tool executes and reports correct version"
                    return Ok ()
                else
                    return Error (sprintf "Tool version mismatch. Expected %s, got: %s" version output)
            | Error err ->
                return Error (sprintf "Tool execution failed: %s" err)
    }

// ============================================================================
// Smoke Tests
// ============================================================================

let runSmokeTestAsync (testName: string) (testAction: CancellationToken -> Async<Result<unit, string>>) (ct: CancellationToken) : Async<SmokeTestResult> =
    async {
        let sw = Stopwatch.StartNew()
        let! result = testAction ct
        sw.Stop()

        match result with
        | Ok () ->
            return {
                TestName = testName
                Passed = true
                Duration = sw.Elapsed
                Error = None
            }
        | Error err ->
            return {
                TestName = testName
                Passed = false
                Duration = sw.Elapsed
                Error = Some err
            }
    }

let runAllSmokeTestsAsync (version: string) (ct: CancellationToken) : Async<SmokeTestResult list> =
    async {
        logInfo "Running smoke tests..."

        // For now, we'll use the existing smoke-test.fsx if it exists
        let smokeTestPath = Path.Combine(projectRoot, ".claude", "skills", "qa-tester", "smoke-test.fsx")

        if File.Exists(smokeTestPath) then
            let! result = runSmokeTestAsync "QA Smoke Tests" (fun ct ->
                async {
                    let! cmdResult = runCommandAsync "dotnet" (sprintf "fsi %s" smokeTestPath) ct
                    match cmdResult with
                    | Ok _ -> return Ok ()
                    | Error err -> return Error err
                }
            ) ct

            return [result]
        else
            logWarn "smoke-test.fsx not found, skipping smoke tests"
            return []
    }

// ============================================================================
// GitHub Issue Update
// ============================================================================

let updateIssueAsync (issueNumber: int) (version: string) (result: ValidationResult) (ct: CancellationToken) : Async<Result<unit, string>> =
    async {
        logInfo (sprintf "Updating issue #%d with validation results..." issueNumber)

        let packageStatus =
            result.Packages
            |> List.map (fun pkg ->
                let icon = if pkg.Available then "✅" else "❌"
                sprintf "- [%s] [%s v%s](%s)" icon pkg.Name pkg.Version (pkg.DownloadUrl |> Option.defaultValue "N/A")
            )
            |> String.concat "\n"

        let toolStatus =
            match result.ToolInstallTest with
            | Some true -> "✅ Passed"
            | Some false -> "❌ Failed"
            | None -> "⏭️ Skipped"

        let smokeTestStatus =
            if List.isEmpty result.SmokeTests then
                "⏭️ Skipped"
            elif result.SmokeTests |> List.forall (fun t -> t.Passed) then
                sprintf "✅ All %d tests passed" result.SmokeTests.Length
            else
                let failed = result.SmokeTests |> List.filter (fun t -> not t.Passed) |> List.length
                sprintf "❌ %d of %d tests failed" failed result.SmokeTests.Length

        let comment = sprintf """## Validation Results

**Version**: %s
**Status**: %s

### Package Availability

%s

### Tool Installation

%s

### Smoke Tests

%s

---
*Updated by validate-release.fsx*
""" version (if result.Success then "✅ Passed" else "❌ Failed") packageStatus toolStatus smokeTestStatus

        let escapedComment = comment.Replace("\"", "\\\"").Replace("\n", "\\n")
        let! cmdResult = runCommandAsync "gh" (sprintf "issue comment %d --body \"%s\"" issueNumber escapedComment) ct

        match cmdResult with
        | Ok _ ->
            logInfo "Issue updated successfully"
            return Ok ()
        | Error err ->
            return Error (sprintf "Failed to update issue: %s" err)
    }

// ============================================================================
// Display Helpers
// ============================================================================

let displayPackages (packages: PackageInfo list) =
    let table = Table()
    table.AddColumn("Package") |> ignore
    table.AddColumn("Version") |> ignore
    table.AddColumn("Status") |> ignore

    for pkg in packages do
        let statusMarkup =
            if pkg.Available then "[green]Available[/]"
            else "[red]Not Found[/]"

        table.AddRow(pkg.Name, pkg.Version, statusMarkup) |> ignore

    AnsiConsole.Write(table)
    AnsiConsole.WriteLine()

let displaySmokeTests (tests: SmokeTestResult list) =
    if List.isEmpty tests then
        AnsiConsole.MarkupLine("[dim]No smoke tests run[/]")
    else
        let table = Table()
        table.AddColumn("Test") |> ignore
        table.AddColumn("Status") |> ignore
        table.AddColumn("Duration") |> ignore

        for test in tests do
            let statusMarkup =
                if test.Passed then "[green]✓ Passed[/]"
                else "[red]✗ Failed[/]"

            let durationStr = sprintf "%.2fs" test.Duration.TotalSeconds

            table.AddRow(test.TestName, statusMarkup, durationStr) |> ignore

        AnsiConsole.Write(table)
        AnsiConsole.WriteLine()

// ============================================================================
// Main Logic
// ============================================================================

let validateAsync (results: ParseResults<ValidateArguments>) (ct: CancellationToken) : Async<ValidationResult> =
    async {
        let version = results.GetResult Version
        let issueNumber = results.TryGetResult Issue
        let updateIssue = results.Contains Update_Issue
        let runSmokeTests = results.Contains Smoke_Tests
        let jsonOutput = results.Contains Json

        let mutable warnings = []
        let mutable errors = []
        let mutable timedOut = false
        let mutable cancelled = false

        if not jsonOutput then
            AnsiConsole.Write(
                FigletText("Release Validator")
                    .Centered()
                    .Color(Color.Blue)
            )
            AnsiConsole.MarkupLine(sprintf "[bold]Version:[/] %s" version)
            match results.TryGetResult Timeout with
            | Some mins -> AnsiConsole.MarkupLine(sprintf "[dim]Timeout: %d minutes[/]" mins)
            | None -> ()
            AnsiConsole.WriteLine()

        try
            // Check all packages
            if not jsonOutput then
                AnsiConsole.MarkupLine("[bold]Checking packages on NuGet...[/]")

            let! packages =
                expectedPackages
                |> List.map (fun pkg -> checkPackageAsync pkg version ct)
                |> Async.Parallel

            let packageList = packages |> Array.toList
            let allAvailable = packageList |> List.forall (fun p -> p.Available)

            if not jsonOutput then
                displayPackages packageList

            if not allAvailable then
                let missing = packageList |> List.filter (fun p -> not p.Available) |> List.map (fun p -> p.Name)
                errors <- (sprintf "Missing packages: %s" (String.concat ", " missing)) :: errors

            // Test tool installation
            let! toolTest =
                async {
                    if not jsonOutput then
                        AnsiConsole.MarkupLine("[bold]Testing tool installation...[/]")

                    let! result = testToolInstallAsync version ct
                    match result with
                    | Ok () ->
                        if not jsonOutput then
                            AnsiConsole.MarkupLine("[green]✓ Tool installation test passed[/]")
                        return Some true
                    | Error err ->
                        if not jsonOutput then
                            AnsiConsole.MarkupLine(sprintf "[red]✗ Tool installation test failed: %s[/]" err)
                        errors <- (sprintf "Tool installation failed: %s" err) :: errors
                        return Some false
                }

            // Run smoke tests if requested
            let! smokeTests =
                if runSmokeTests then
                    async {
                        if not jsonOutput then
                            AnsiConsole.MarkupLine("[bold]Running smoke tests...[/]")

                        let! tests = runAllSmokeTestsAsync version ct

                        if not jsonOutput then
                            displaySmokeTests tests

                        let failedTests = tests |> List.filter (fun t -> not t.Passed)
                        if not (List.isEmpty failedTests) then
                            for test in failedTests do
                                errors <- (sprintf "Smoke test '%s' failed: %s" test.TestName (test.Error |> Option.defaultValue "Unknown error")) :: errors

                        return tests
                    }
                else
                    async { return [] }

            let success = allAvailable && (toolTest = Some true) && (smokeTests |> List.forall (fun t -> t.Passed))

            let validationResult = {
                Success = success
                Version = version
                Packages = packageList
                AllPackagesAvailable = allAvailable
                ToolInstallTest = toolTest
                SmokeTests = smokeTests
                IssueUpdated = false
                TimedOut = timedOut
                Cancelled = cancelled
                Warnings = warnings
                Errors = errors
                ExitCode = if success then 0 else 1
            }

            // Update issue if requested
            let! issueUpdated =
                async {
                    match issueNumber, updateIssue with
                    | Some issueNum, true ->
                        let! updateResult = updateIssueAsync issueNum version validationResult ct
                        match updateResult with
                        | Ok () -> return true
                        | Error err ->
                            warnings <- (sprintf "Failed to update issue: %s" err) :: warnings
                            return false
                    | _ -> return false
                }

            // Prompt for continuous improvement feedback after consecutive successes
            let feedbackGiven =
                if success && not jsonOutput && not ct.IsCancellationRequested && shouldPromptForSuccessFeedback() then
                    let consecutiveSuccesses = getConsecutiveSuccesses()
                    let promptText = sprintf "You've had %d successful releases in a row! 🎉 Would you like to provide feedback on how we can further improve the release process?" consecutiveSuccesses
                    let feedback = promptForFeedback promptText
                    
                    match feedback, issueNumber with
                    | Some fb, Some issueNum when not (String.IsNullOrWhiteSpace(fb)) ->
                        // Add feedback to issue
                        let feedbackComment = sprintf """## 📈 Release Success Feedback

**After %d consecutive successful releases:**

%s

---
*Collected by validate-release.fsx after consecutive successful releases*
""" consecutiveSuccesses fb
                        let escapedFeedback = feedbackComment.Replace("\"", "\\\"").Replace("\n", "\\n")
                        let feedbackResult = 
                            Async.RunSynchronously(
                                runCommandAsync "gh" (sprintf "issue comment %d --body \"%s\"" issueNum escapedFeedback) ct,
                                cancellationToken = ct
                            )
                        match feedbackResult with
                        | Ok _ -> 
                            logInfo "Success feedback added to issue"
                            true
                        | Error err ->
                            logWarn (sprintf "Failed to add feedback to issue: %s" err)
                            false
                    | _ -> false
                else
                    false

            if not jsonOutput then
                AnsiConsole.WriteLine()
                if success then
                    AnsiConsole.MarkupLine("[green]✅ All validations passed[/]")
                    if feedbackGiven then
                        AnsiConsole.MarkupLine("[green]📈 Continuous improvement feedback recorded[/]")
                else
                    AnsiConsole.MarkupLine("[red]❌ Some validations failed[/]")

            return { validationResult with IssueUpdated = issueUpdated }

        with
        | :? OperationCanceledException ->
            if ct.IsCancellationRequested then
                cancelled <- true
                logWarn "Operation cancelled"
            return {
                Success = false
                Version = version
                Packages = []
                AllPackagesAvailable = false
                ToolInstallTest = None
                SmokeTests = []
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
                Packages = []
                AllPackagesAvailable = false
                ToolInstallTest = None
                SmokeTests = []
                IssueUpdated = false
                TimedOut = timedOut
                Cancelled = cancelled
                Warnings = warnings
                Errors = ["Operation timed out"] @ errors
                ExitCode = 3
            }
    }

let outputJson (result: ValidationResult) =
    let options = JsonSerializerOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    )
    let json = JsonSerializer.Serialize(result, options)
    printfn "%s" json

let main (args: string array) =
    let parser = ArgumentParser.Create<ValidateArguments>(programName = "validate-release.fsx")

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

        let result = Async.RunSynchronously(validateAsync results cts.Token)

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
