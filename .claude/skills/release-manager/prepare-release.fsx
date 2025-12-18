#!/usr/bin/env dotnet fsi
// Release preparation and pre-flight validation
// Usage: dotnet fsi prepare-release.fsx [--version VERSION] [--json] [--dry-run] [--skip-local-check]

#r "nuget: Spectre.Console, 0.53.0"
#r "nuget: System.Text.Json, 9.0.0"

open System
open System.IO
open System.Diagnostics
open System.Text.Json
open System.Text.Json.Serialization
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

let projectRoot =
    let scriptDir = __SOURCE_DIRECTORY__
    Path.GetFullPath(Path.Combine(scriptDir, "..", "..", ".."))

let historyFile = Path.Combine(projectRoot, ".claude", "skills", "release-manager", ".release-history.json")

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
// Types
// ============================================================================

type VersionType = Major | Minor | Patch | Prerelease

type VersionInfo = {
    Suggested: string
    Specified: string option
    Type: string
    Rationale: string
}

type RemoteState = {
    CiPassing: bool
    LatestRun: int64 option
    LatestCommit: string
    CommitMessage: string
}

type ChangelogInfo = {
    HasUnreleased: bool
    ChangeCount: int
    Added: int
    Changed: int
    FixedCount: int
    BreakingChanges: int
}

type VersionValidation = {
    NugetAvailable: bool
    TagAvailable: bool
    Conflicts: string list
}

type LocalState = {
    Branch: string
    Clean: bool
    ModifiedFiles: int
    Blocking: bool
}

type ProcessChanges = {
    HasReleaseProcessChanges: bool
    ChangedFiles: string list
    ShouldPromptForPlaybookUpdate: bool
}

type PrepareResult = {
    Ready: bool
    Version: VersionInfo
    RemoteState: RemoteState
    Changelog: ChangelogInfo
    VersionValidation: VersionValidation
    LocalState: LocalState option
    ProcessChanges: ProcessChanges option
    Warnings: string list
    Errors: string list
    ExitCode: int
}

// ============================================================================
// Configuration (projectRoot already defined above for historyFile)
// ============================================================================

let changelogPath = Path.Combine(projectRoot, "CHANGELOG.md")

// ============================================================================
// Command Line Parsing
// ============================================================================

let args = fsi.CommandLineArgs |> Array.skip 1

let jsonOutput = args |> Array.contains "--json"
let dryRun = args |> Array.contains "--dry-run"
let skipLocalCheck = args |> Array.contains "--skip-local-check"

let specifiedVersion =
    args
    |> Array.tryFindIndex ((=) "--version")
    |> Option.bind (fun i ->
        if i + 1 < args.Length then Some args.[i + 1]
        else None
    )

// ============================================================================
// Logging (respects CLI standards)
// ============================================================================

let logInfo msg =
    if not jsonOutput then
        eprintfn "[INFO] %s" msg

let logWarning msg =
    eprintfn "[WARN] %s" msg

let logError msg =
    eprintfn "[ERROR] %s" msg

let logVerbose msg =
    if not jsonOutput then
        eprintfn "[VERBOSE] %s" msg

// ============================================================================
// Shell Execution
// ============================================================================

let runCommand (command: string) (args: string) : int * string * string =
    let psi = ProcessStartInfo(
        FileName = command,
        Arguments = args,
        WorkingDirectory = projectRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    )

    use proc = Process.Start(psi)
    let output = proc.StandardOutput.ReadToEnd()
    let error = proc.StandardError.ReadToEnd()
    proc.WaitForExit()

    (proc.ExitCode, output.Trim(), error.Trim())

// ============================================================================
// Remote State Validation
// ============================================================================

let checkRemoteCI () : Result<RemoteState, string> =
    logInfo "Checking remote CI status on main branch..."

    // Check latest GitHub Actions run on main
    let (code, output, error) = runCommand "gh" "run list --branch main --limit 1 --json conclusion,databaseId,headSha"

    if code <> 0 then
        Error $"Failed to query GitHub Actions: {error}"
    else
        try
            logInfo $"Parsing GitHub response (length: {output.Length})"
            let doc = JsonDocument.Parse(output)
            let runs = doc.RootElement.EnumerateArray() |> Seq.toList

            if runs.IsEmpty then
                Error "No workflow runs found on main branch"
            else
                let root = runs.[0]

                let conclusionProp = root.GetProperty("conclusion")
                let conclusion = if conclusionProp.ValueKind = JsonValueKind.Null then "in_progress" else conclusionProp.GetString()
                let runId = root.GetProperty("databaseId").GetInt64()
                let sha = root.GetProperty("headSha").GetString()

                // Get commit message from git log
                let shortSha = if sha.Length > 7 then sha.Substring(0, 7) else sha
                let commitMsg =
                    try
                        let (gitCode, gitOutput, _) = runCommand "git" (sprintf "log -1 --pretty=format:%%s %s" shortSha)
                        if gitCode = 0 then gitOutput.Trim() else ""
                    with _ -> ""

                let ciPassing = conclusion = "success"

                if not ciPassing then
                    logWarning $"Latest CI run #{runId} has conclusion: {conclusion}"

                Ok {
                    CiPassing = ciPassing
                    LatestRun = Some runId
                    LatestCommit = shortSha
                    CommitMessage = commitMsg
                }
        with ex ->
            logError $"Exception details: {ex}"
            Error $"Failed to parse CI response: {ex.Message}"

// ============================================================================
// Changelog Parsing
// ============================================================================

let parseChangelog () : Result<ChangelogInfo, string> =
    logInfo "Parsing CHANGELOG.md..."

    if not (File.Exists changelogPath) then
        Error "CHANGELOG.md not found"
    else
        let lines = File.ReadAllLines(changelogPath)

        let rec findUnreleased lineNum =
            if lineNum >= lines.Length then None
            elif lines.[lineNum].StartsWith("## [Unreleased]") then Some lineNum
            else findUnreleased (lineNum + 1)

        let rec findNextVersion lineNum =
            if lineNum >= lines.Length then lines.Length
            elif lines.[lineNum].StartsWith("## [") && not (lines.[lineNum].StartsWith("## [Unreleased]")) then lineNum
            else findNextVersion (lineNum + 1)

        match findUnreleased 0 with
        | None ->
            Error "No [Unreleased] section found in CHANGELOG.md"
        | Some startLine ->
            let endLine = findNextVersion (startLine + 1)
            let unreleasedLines = lines.[startLine..endLine-1]

            let countCategory (prefix: string) =
                unreleasedLines
                |> Array.filter (fun line -> line.StartsWith($"- {prefix}"))
                |> Array.length

            let added = countCategory "Added" + countCategory "**Added**"
            let changed = countCategory "Changed" + countCategory "**Changed**"
            let fixedCount = countCategory "Fixed" + countCategory "**Fixed**"
            let breaking =
                unreleasedLines
                |> Array.filter (fun line -> line.Contains("BREAKING") || line.Contains("**BREAKING**"))
                |> Array.length

            let totalChanges = added + changed + fixedCount

            if totalChanges = 0 then
                logWarning "No changes found in [Unreleased] section"

            Ok {
                HasUnreleased = true
                ChangeCount = totalChanges
                Added = added
                Changed = changed
                FixedCount = fixedCount
                BreakingChanges = breaking
            }

// ============================================================================
// Version Suggestion
// ============================================================================

let suggestVersion (changelog: ChangelogInfo) : VersionInfo =
    logInfo "Analyzing changes to suggest version..."

    let versionType, rationale =
        if changelog.BreakingChanges > 0 then
            Major, "Breaking changes detected"
        elif changelog.Added > 0 then
            Minor, "New features added, no breaking changes"
        elif changelog.Changed > 0 then
            Minor, "Improvements made"
        elif changelog.FixedCount > 0 then
            Patch, "Bug fixes only"
        else
            Patch, "No significant changes"

    // Get current version from latest tag
    let (code, output, _) = runCommand "git" "describe --tags --abbrev=0"
    let currentVersion =
        if code = 0 && output <> "" then output
        else "0.1.0"

    let parts = currentVersion.TrimStart('v').Split([|'-'|]).[0].Split('.')
    let major = if parts.Length > 0 then int parts.[0] else 0
    let minor = if parts.Length > 1 then int parts.[1] else 0
    let patch = if parts.Length > 2 then int parts.[2] else 0

    let suggested =
        match versionType with
        | Major -> $"{major + 1}.0.0"
        | Minor -> $"{major}.{minor + 1}.0"
        | Patch -> $"{major}.{minor}.{patch + 1}"
        | Prerelease -> $"{major}.{minor}.{patch + 1}-alpha.1"

    {
        Suggested = suggested
        Specified = specifiedVersion
        Type = string versionType
        Rationale = rationale
    }

// ============================================================================
// Version Validation
// ============================================================================

let validateVersion (version: string) : Result<VersionValidation, string> =
    logInfo $"Validating version {version}..."

    // Check if version exists on NuGet
    let (nugetCode, _, _) = runCommand "dotnet" $"nuget search Morphir.Core --exact-match --version {version}"
    let nugetAvailable = nugetCode <> 0 // Non-zero means not found, which is good

    // Check if git tag exists
    let (tagCode, _, _) = runCommand "git" $"rev-parse v{version}"
    let tagAvailable = tagCode <> 0 // Non-zero means tag doesn't exist, which is good

    let conflicts = [
        if not nugetAvailable then yield "Version already exists on NuGet"
        if not tagAvailable then yield $"Git tag v{version} already exists"
    ]

    if conflicts.IsEmpty then
        Ok {
            NugetAvailable = nugetAvailable
            TagAvailable = tagAvailable
            Conflicts = []
        }
    else
        let conflictMsg = String.concat ", " conflicts
        logWarning (sprintf "Version conflicts detected: %s" conflictMsg)
        Ok {
            NugetAvailable = nugetAvailable
            TagAvailable = tagAvailable
            Conflicts = conflicts
        }

// ============================================================================
// Local State Check
// ============================================================================

let checkLocalState () : LocalState =
    logInfo "Checking local state..."

    // Get current branch
    let (_, branch, _) = runCommand "git" "branch --show-current"

    // Check if working tree is clean
    let (_, status, _) = runCommand "git" "status --porcelain"
    let clean = String.IsNullOrWhiteSpace(status)
    let modifiedFiles = if clean then 0 else status.Split('\n').Length

    if not clean then
        logWarning $"Working tree has {modifiedFiles} modified files"
        logWarning "Consider stashing: git stash save \"WIP before release\""

    {
        Branch = branch
        Clean = clean
        ModifiedFiles = modifiedFiles
        Blocking = false // Never blocking for releases
    }

// ============================================================================
// Process Change Detection
// ============================================================================

let checkForProcessChanges () : ProcessChanges =
    logInfo "Checking for release process changes since last release..."
    
    // Get the last release tag
    let (tagCode, lastTag, _) = runCommand "git" "describe --tags --abbrev=0"
    
    if tagCode <> 0 || String.IsNullOrWhiteSpace(lastTag) then
        logInfo "No previous release tag found, skipping process change detection"
        {
            HasReleaseProcessChanges = false
            ChangedFiles = []
            ShouldPromptForPlaybookUpdate = false
        }
    else
        // Check for changes to release-related files
        let releaseProcessPaths = [
            ".github/workflows/deployment.yml"
            ".github/workflows/"
            ".claude/skills/release-manager/"
            "AGENTS.md"
            ".agents/release-management.md"
        ]
        
        let changedFiles = 
            releaseProcessPaths
            |> List.collect (fun path ->
                let (code, output, _) = runCommand "git" (sprintf "diff --name-only %s HEAD -- %s" lastTag path)
                if code = 0 && not (String.IsNullOrWhiteSpace(output)) then
                    output.Split('\n') |> Array.filter (fun s -> not (String.IsNullOrWhiteSpace(s))) |> Array.toList
                else
                    []
            )
        
        let hasChanges = not (List.isEmpty changedFiles)
        
        if hasChanges then
            logInfo (sprintf "Detected %d release process files changed since %s" changedFiles.Length lastTag)
        
        {
            HasReleaseProcessChanges = hasChanges
            ChangedFiles = changedFiles
            ShouldPromptForPlaybookUpdate = hasChanges
        }

// ============================================================================
// Main Logic
// ============================================================================

let prepare () : PrepareResult =
    let mutable warnings = []
    let mutable errors = []

    // 1. Check remote CI
    let remoteState =
        match checkRemoteCI() with
        | Ok state ->
            if not state.CiPassing then
                warnings <- "CI not passing on main branch" :: warnings
            state
        | Error err ->
            errors <- err :: errors
            {
                CiPassing = false
                LatestRun = None
                LatestCommit = ""
                CommitMessage = ""
            }

    // 2. Parse changelog
    let changelog =
        match parseChangelog() with
        | Ok info ->
            if info.ChangeCount = 0 then
                warnings <- "No changes in [Unreleased] section" :: warnings
            info
        | Error err ->
            errors <- err :: errors
            {
                HasUnreleased = false
                ChangeCount = 0
                Added = 0
                Changed = 0
                FixedCount = 0
                BreakingChanges = 0
            }

    // 3. Suggest version
    let versionInfo = suggestVersion changelog
    let versionToValidate = specifiedVersion |> Option.defaultValue versionInfo.Suggested

    // 4. Validate version
    let versionValidation =
        match validateVersion versionToValidate with
        | Ok validation ->
            if not validation.Conflicts.IsEmpty then
                errors <- validation.Conflicts @ errors
            validation
        | Error err ->
            errors <- err :: errors
            {
                NugetAvailable = false
                TagAvailable = false
                Conflicts = [err]
            }

    // 5. Check local state (optional)
    let localState =
        if skipLocalCheck then
            logInfo "Skipping local state check (--skip-local-check)"
            None
        else
            Some (checkLocalState())

    // 6. Check for process changes
    let processChanges = Some (checkForProcessChanges())
    
    // Prompt for playbook update if process changes detected
    if not jsonOutput && processChanges.IsSome && processChanges.Value.ShouldPromptForPlaybookUpdate then
        let promptText = sprintf "We see changes to %d release process files. Would you like to update or add to our release playbooks based on these changes?" processChanges.Value.ChangedFiles.Length
        let feedback = promptForFeedback promptText
        
        match feedback with
        | Some fb when not (String.IsNullOrWhiteSpace(fb)) ->
            logInfo "Process change feedback captured"
            eprintfn ""
            eprintfn "📝 Feedback for process changes:"
            eprintfn "%s" fb
            eprintfn ""
            eprintfn "Consider updating:"
            eprintfn "  - .claude/skills/release-manager/skill.md"
            eprintfn "  - .agents/release-management.md"
            eprintfn "  - .claude/skills/release-manager/README.md"
            eprintfn ""
        | _ -> ()

    // Determine readiness
    let ready = errors.IsEmpty
    let exitCode =
        if ready && warnings.IsEmpty then 0
        elif ready then 2 // Ready with warnings
        else 1 // Not ready

    {
        Ready = ready
        Version = versionInfo
        RemoteState = remoteState
        Changelog = changelog
        VersionValidation = versionValidation
        LocalState = localState
        ProcessChanges = processChanges
        Warnings = List.rev warnings
        Errors = List.rev errors
        ExitCode = exitCode
    }

// ============================================================================
// Output Formatting
// ============================================================================

let outputJson (result: PrepareResult) =
    let options = JsonSerializerOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    )

    let json = JsonSerializer.Serialize(result, options)
    printfn "%s" json

let outputHuman (result: PrepareResult) =
    AnsiConsole.Write(
        FigletText("Prepare Release")
            .Centered()
            .Color(if result.Ready then Color.Green else Color.Red)
    )

    AnsiConsole.MarkupLine($"[dim]Project root: {projectRoot}[/]")
    AnsiConsole.WriteLine()

    // Remote State
    AnsiConsole.MarkupLine("[bold]Remote State:[/]")
    if result.RemoteState.CiPassing then
        let runNum = result.RemoteState.LatestRun |> Option.map string |> Option.defaultValue "N/A"
        let ciMsg = sprintf "[green]✅ CI passing on main branch (run #%s)[/]" runNum
        AnsiConsole.MarkupLine(ciMsg)
    else
        AnsiConsole.MarkupLine("[red]❌ CI not passing on main branch[/]")
    let commitMsg = sprintf "[dim]   Latest commit: %s \"%s\"[/]" result.RemoteState.LatestCommit result.RemoteState.CommitMessage
    AnsiConsole.MarkupLine(commitMsg)
    AnsiConsole.WriteLine()

    // Changelog Analysis
    AnsiConsole.MarkupLine("[bold]Changelog Analysis:[/]")
    if result.Changelog.HasUnreleased then
        let changelogMsg = sprintf "[green]📝 [[Unreleased]] section found with %d changes[/]" result.Changelog.ChangeCount
        AnsiConsole.MarkupLine(changelogMsg)
        if result.Changelog.Added > 0 then
            let addedMsg = sprintf "[dim]   - Added: %d features[/]" result.Changelog.Added
            AnsiConsole.MarkupLine(addedMsg)
        if result.Changelog.Changed > 0 then
            let changedMsg = sprintf "[dim]   - Changed: %d improvements[/]" result.Changelog.Changed
            AnsiConsole.MarkupLine(changedMsg)
        if result.Changelog.FixedCount > 0 then
            let fixedMsg = sprintf "[dim]   - Fixed: %d bug fixes[/]" result.Changelog.FixedCount
            AnsiConsole.MarkupLine(fixedMsg)
        if result.Changelog.BreakingChanges > 0 then
            let breakingMsg = sprintf "[red]   - Breaking changes: %d[/]" result.Changelog.BreakingChanges
            AnsiConsole.MarkupLine(breakingMsg)
    else
        AnsiConsole.MarkupLine("[red]❌ No [[Unreleased]] section found[/]")
    AnsiConsole.WriteLine()

    // Version Suggestion
    AnsiConsole.MarkupLine("[bold]Version Suggestion:[/]")
    let version = result.Version.Specified |> Option.defaultValue result.Version.Suggested
    let suggestedMsg = sprintf "[yellow]📊 Suggested version: %s (%s)[/]" result.Version.Suggested result.Version.Type
    AnsiConsole.MarkupLine(suggestedMsg)
    let rationaleMsg = sprintf "[dim]   Rationale: %s[/]" result.Version.Rationale
    AnsiConsole.MarkupLine(rationaleMsg)
    if result.Version.Specified.IsSome then
        let specifiedMsg = sprintf "[cyan]   User specified: %s[/]" result.Version.Specified.Value
        AnsiConsole.MarkupLine(specifiedMsg)
    AnsiConsole.WriteLine()

    // Version Validation
    AnsiConsole.MarkupLine("[bold]Version Validation:[/]")
    if result.VersionValidation.NugetAvailable then
        let nugetMsg = sprintf "[green]✅ Version %s available on NuGet[/]" version
        AnsiConsole.MarkupLine(nugetMsg)
    else
        let nugetMsg = sprintf "[red]❌ Version %s already exists on NuGet[/]" version
        AnsiConsole.MarkupLine(nugetMsg)
    if result.VersionValidation.TagAvailable then
        let tagMsg = sprintf "[green]✅ Tag v%s does not exist[/]" version
        AnsiConsole.MarkupLine(tagMsg)
    else
        let tagMsg = sprintf "[red]❌ Tag v%s already exists[/]" version
        AnsiConsole.MarkupLine(tagMsg)
    AnsiConsole.WriteLine()

    // Local State (if checked)
    match result.LocalState with
    | Some state ->
        AnsiConsole.MarkupLine("[bold]Local State (Advisory):[/]")
        let branchMsg = sprintf "[dim]ℹ️  On branch: %s[/]" state.Branch
        AnsiConsole.MarkupLine(branchMsg)
        if state.Clean then
            AnsiConsole.MarkupLine("[green]✅ Working tree clean[/]")
        else
            let changesMsg = sprintf "[yellow]⚠️  Local changes detected (%d modified files)[/]" state.ModifiedFiles
            AnsiConsole.MarkupLine(changesMsg)
            AnsiConsole.MarkupLine("[dim]   You can:[/]")
            AnsiConsole.MarkupLine("[dim]   - Stash: git stash save \"WIP before release\"[/]")
            AnsiConsole.MarkupLine("[dim]   - Commit: git add . && git commit -m \"WIP\"[/]")
            AnsiConsole.MarkupLine("[dim]   - Continue anyway (workflow runs on remote main)[/]")
        AnsiConsole.WriteLine()
    | None ->
        AnsiConsole.MarkupLine("[dim]Local state check skipped[/]")
        AnsiConsole.WriteLine()

    // Process Changes (if checked)
    match result.ProcessChanges with
    | Some changes when changes.HasReleaseProcessChanges ->
        AnsiConsole.MarkupLine("[bold]Release Process Changes Detected:[/]")
        AnsiConsole.MarkupLine(sprintf "[yellow]📋 %d release process files changed since last release[/]" changes.ChangedFiles.Length)
        if changes.ChangedFiles.Length <= 10 then
            for file in changes.ChangedFiles do
                AnsiConsole.MarkupLine(sprintf "[dim]   - %s[/]" file)
        if changes.ShouldPromptForPlaybookUpdate then
            AnsiConsole.MarkupLine("[yellow]💡 Consider updating release playbooks and documentation[/]")
        AnsiConsole.WriteLine()
    | _ -> ()

    // Warnings
    if not result.Warnings.IsEmpty then
        AnsiConsole.MarkupLine("[bold yellow]Warnings:[/]")
        for warning in result.Warnings do
            AnsiConsole.MarkupLine($"[yellow]⚠️  {warning}[/]")
        AnsiConsole.WriteLine()

    // Errors
    if not result.Errors.IsEmpty then
        AnsiConsole.MarkupLine("[bold red]Errors:[/]")
        for error in result.Errors do
            AnsiConsole.MarkupLine($"[red]❌ {error}[/]")
        AnsiConsole.WriteLine()

    // Result
    AnsiConsole.WriteLine()
    if result.Ready then
        let message =
            if result.Warnings.IsEmpty then
                sprintf "[green]✅ Ready to release v%s[/]" version
            else
                sprintf "[yellow]⚠️  Ready to release v%s (with warnings)[/]" version
        AnsiConsole.MarkupLine(message)
    else
        AnsiConsole.MarkupLine("[red]❌ Not ready for release - please fix the errors above[/]")

// ============================================================================
// Entry Point
// ============================================================================

let main () =
    try
        logInfo "Starting release preparation..."
        if dryRun then
            logInfo "Dry run mode enabled (no side effects)"

        let result = prepare()

        if jsonOutput then
            outputJson result
        else
            outputHuman result

        result.ExitCode
    with ex ->
        logError $"Unhandled exception: {ex.Message}"
        logError $"Stack trace: {ex.StackTrace}"
        1

exit (main())
