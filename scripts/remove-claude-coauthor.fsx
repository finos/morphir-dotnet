#!/usr/bin/env dotnet fsi

(*
   Remove Claude Co-Author from Commit History

   Removes "Co-Authored-By: Claude <noreply@anthropic.com>" from commit messages
   to ensure CLA (Contributor License Agreement) compliance.

   Usage:
     dotnet fsi scripts/remove-claude-coauthor.fsx --dry-run
     dotnet fsi scripts/remove-claude-coauthor.fsx --commits 5
     dotnet fsi scripts/remove-claude-coauthor.fsx --branch fix/my-branch
     dotnet fsi scripts/remove-claude-coauthor.fsx --since-main
     dotnet fsi scripts/remove-claude-coauthor.fsx --help

   Features:
   - Scans commit history for Claude co-author violations
   - Dry-run mode (default) shows preview without making changes
   - Creates backup branch before rewriting history
   - Preserves all other commit metadata and co-authors
   - Warns if commits have been pushed to remote
   - Requires confirmation before applying changes

   Safety:
   - Requires clean working directory
   - Creates automatic backup: backup/pre-coauthor-fix-{timestamp}
   - Confirmation prompt before rewriting
   - Force-push guidance for already-pushed commits

   Background:
   AI assistants cannot sign CLAs. Including them as co-authors will block
   PR merges and fail CLA verification CI checks. Attribution in commit body
   is acceptable (e.g., "🤖 Generated with [Claude Code]").

   See: AGENTS.md#commit-messages, CLAUDE.md#commit-standards
*)

#r "nuget: Argu, 6.2.4"
#r "nuget: Spectre.Console, 0.53.0"

open System
open System.IO
open System.Diagnostics
open System.Text.RegularExpressions
open Argu
open Spectre.Console

// ============================================================================
// CLI Arguments
// ============================================================================

type CliArguments =
    | [<AltCommandLine("-d")>] Dry_Run
    | [<AltCommandLine("-c")>] Commits of int
    | [<AltCommandLine("-b")>] Branch of string
    | [<AltCommandLine("-s")>] Since_Main
    | [<AltCommandLine("-y")>] Yes
    | [<AltCommandLine("-v")>] Verbose
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Dry_Run -> "preview changes without applying (default: true)"
            | Commits _ -> "number of commits to check (e.g., --commits 5)"
            | Branch _ -> "specific branch to check (e.g., --branch fix/my-branch)"
            | Since_Main -> "check all commits since divergence from main"
            | Yes -> "skip confirmation prompt and apply changes immediately"
            | Verbose -> "enable verbose diagnostic output"

// ============================================================================
// Types
// ============================================================================

type CommitInfo = {
    Hash: string
    ShortHash: string
    Subject: string
    FullMessage: string
    Branch: string
    IsPushed: bool
}

// ============================================================================
// Git Command Execution
// ============================================================================

let runGitCommand (args: string) (verbose: bool) : Result<string, string> =
    try
        let psi = ProcessStartInfo(
            FileName = "git",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        )

        use proc = Process.Start(psi)
        let output = proc.StandardOutput.ReadToEnd()
        let error = proc.StandardError.ReadToEnd()
        proc.WaitForExit()

        if verbose then
            eprintfn "[VERBOSE] git %s" args
            if not (String.IsNullOrWhiteSpace(output)) then
                let trimmedOutput = output.Trim()
                eprintfn "[VERBOSE] stdout: %s" trimmedOutput
            if not (String.IsNullOrWhiteSpace(error)) then
                let trimmedError = error.Trim()
                eprintfn "[VERBOSE] stderr: %s" trimmedError

        if proc.ExitCode = 0 then
            let trimmedOutput = output.Trim()
            Ok trimmedOutput
        else
            let trimmedOutput = output.Trim()
            let trimmedError = error.Trim()
            Error (if String.IsNullOrWhiteSpace(error) then trimmedOutput else trimmedError)
    with ex ->
        Error (sprintf "Exception running git command: %s" ex.Message)

// ============================================================================
// Git Status Checks
// ============================================================================

let isWorkingDirectoryClean (verbose: bool) : Result<bool, string> =
    match runGitCommand "status --porcelain" verbose with
    | Ok output ->
        Ok (String.IsNullOrWhiteSpace(output))
    | Error msg ->
        Error msg

let getCurrentBranch (verbose: bool) : Result<string, string> =
    runGitCommand "branch --show-current" verbose

let isCommitPushed (hash: string) (verbose: bool) : Result<bool, string> =
    // Check if commit exists in any remote branch
    match runGitCommand (sprintf "branch -r --contains %s" hash) verbose with
    | Ok output ->
        Ok (not (String.IsNullOrWhiteSpace(output)))
    | Error _ ->
        Ok false // If error, assume not pushed

// ============================================================================
// Commit Scanning
// ============================================================================

let claudeCoAuthorPatterns = [
    Regex(@"Co-Authored-By:\s*Claude\s*<noreply@anthropic\.com>", RegexOptions.IgnoreCase)
    Regex(@"Co-authored-by:\s*Claude\s*<noreply@anthropic\.com>", RegexOptions.IgnoreCase)
    Regex(@"Coauthored-by:\s*Claude\s*<noreply@anthropic\.com>", RegexOptions.IgnoreCase)
]

let containsClaudeCoAuthor (message: string) : bool =
    claudeCoAuthorPatterns |> List.exists (fun pattern -> pattern.IsMatch(message))

let removeClaudeCoAuthor (message: string) : string =
    let mutable result = message
    for pattern in claudeCoAuthorPatterns do
        result <- pattern.Replace(result, "")
    
    // Clean up extra blank lines (max 2 consecutive newlines)
    let multipleNewlines = Regex(@"\n{3,}")
    result <- multipleNewlines.Replace(result, "\n\n")
    
    // Trim trailing whitespace
    result.TrimEnd()

let getCommitRange (args: ParseResults<CliArguments>) (verbose: bool) : Result<string, string> =
    if args.Contains Since_Main then
        // Get merge-base with main
        match runGitCommand "merge-base HEAD main" verbose with
        | Ok mergeBase ->
            let trimmedBase = mergeBase.Trim()
            Ok (sprintf "%s..HEAD" trimmedBase)
        | Error msg ->
            Error (sprintf "Could not find merge-base with main: %s" msg)
    elif args.Contains Commits then
        let n = args.GetResult Commits
        Ok (sprintf "HEAD~%d..HEAD" n)
    elif args.Contains Branch then
        let branch = args.GetResult Branch
        Ok (sprintf "%s" branch)
    else
        // Default: check last 10 commits
        Ok "HEAD~10..HEAD"

let getCommitInfo (hash: string) (verbose: bool) : Result<CommitInfo, string> =
    match runGitCommand (sprintf "log -1 --format=%%B %s" hash) verbose with
    | Ok fullMessage ->
        match runGitCommand (sprintf "log -1 --format=%%s %s" hash) verbose with
        | Ok subject ->
            match getCurrentBranch verbose with
            | Ok branch ->
                match isCommitPushed hash verbose with
                | Ok isPushed ->
                    let trimmedSubject = subject.Trim()
                    let trimmedMessage = fullMessage.Trim()
                    Ok {
                        Hash = hash
                        ShortHash = hash.Substring(0, min 7 hash.Length)
                        Subject = trimmedSubject
                        FullMessage = trimmedMessage
                        Branch = branch
                        IsPushed = isPushed
                    }
                | Error msg ->
                    Error msg
            | Error msg ->
                Error msg
        | Error msg ->
            Error msg
    | Error msg ->
        Error msg

let scanForClaudeCoAuthor (commitRange: string) (verbose: bool) : Result<CommitInfo list, string> =
    if verbose then
        eprintfn "[VERBOSE] Scanning commit range: %s" commitRange

    match runGitCommand (sprintf "log --format=%%H %s" commitRange) verbose with
    | Ok output ->
        let hashes =
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun s -> s.Trim())
            |> Array.toList

        if verbose then
            eprintfn "[VERBOSE] Found %d commits to check" hashes.Length

        let commits =
            hashes
            |> List.choose (fun hash ->
                match getCommitInfo hash verbose with
                | Ok info ->
                    if containsClaudeCoAuthor info.FullMessage then
                        Some info
                    else
                        None
                | Error msg ->
                    eprintfn "[WARNING] Could not get info for commit %s: %s" hash msg
                    None
            )

        Ok commits
    | Error msg ->
        Error msg

// ============================================================================
// Backup Creation
// ============================================================================

let createBackupBranch (verbose: bool) : Result<string, string> =
    let timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss")
    let backupBranch = sprintf "backup/pre-coauthor-fix-%s" timestamp

    if verbose then
        eprintfn "[VERBOSE] Creating backup branch: %s" backupBranch

    match runGitCommand (sprintf "branch %s" backupBranch) verbose with
    | Ok _ ->
        Ok backupBranch
    | Error msg ->
        Error msg

// ============================================================================
// Commit History Rewriting
// ============================================================================

let rewriteCommitHistory (commits: CommitInfo list) (verbose: bool) : Result<unit, string> =
    // Use git filter-branch to rewrite commit messages
    // We'll create a sed script that removes the Claude co-author line
    let sedScript = claudeCoAuthorPatterns
                    |> List.map (fun _ -> "/Co-[Aa]uthored-[Bb]y:.*Claude.*<noreply@anthropic\\.com>/d")
                    |> String.concat "; "

    let oldestCommit = commits |> List.last
    
    // Get the parent of the oldest commit to rewrite
    match runGitCommand (sprintf "rev-parse %s^" oldestCommit.Hash) verbose with
    | Ok parent ->
        let commitRange = sprintf "%s..HEAD" (parent.Trim())

        if verbose then
            eprintfn "[VERBOSE] Rewriting commits from %s to HEAD" parent
            eprintfn "[VERBOSE] Using sed script: %s" sedScript

        // Use git filter-branch with msg-filter
        let filterCommand = sprintf "filter-branch -f --msg-filter 'sed \"%s\"' %s" sedScript commitRange

        match runGitCommand filterCommand verbose with
        | Ok _ ->
            Ok ()
        | Error msg ->
            Error msg
    | Error msg ->
        Error (sprintf "Could not find parent commit: %s" msg)

// ============================================================================
// Display Functions
// ============================================================================

let displayCommitPreview (commit: CommitInfo) =
    let rule = Rule(sprintf "[bold]%s[/] - %s" commit.ShortHash commit.Subject)
    rule.Style <- Style.Parse("dim")
    AnsiConsole.Write(rule)

    AnsiConsole.MarkupLine(sprintf "  Branch: [cyan]%s[/]" commit.Branch)
    AnsiConsole.MarkupLine(sprintf "  Status: %s"
        (if commit.IsPushed then "[yellow]PUSHED to remote[/]" else "[green]local only[/]"))
    AnsiConsole.WriteLine()

    // Before
    let beforePanel = Panel(commit.FullMessage)
    beforePanel.Header <- PanelHeader("Before", Justify.Left)
    beforePanel.Border <- BoxBorder.Rounded
    beforePanel.BorderStyle <- Style.Parse("yellow")
    AnsiConsole.Write(beforePanel)
    AnsiConsole.WriteLine()

    // After
    let cleanedMessage = removeClaudeCoAuthor commit.FullMessage
    let afterPanel = Panel(cleanedMessage)
    afterPanel.Header <- PanelHeader("After", Justify.Left)
    afterPanel.Border <- BoxBorder.Rounded
    afterPanel.BorderStyle <- Style.Parse("green")
    AnsiConsole.Write(afterPanel)
    AnsiConsole.WriteLine()

let displaySummary (commits: CommitInfo list) (dryRun: bool) =
    let header = FigletText("Claude Co-Author Remover")
    header.Color <- Color.Blue
    AnsiConsole.Write(header)
    AnsiConsole.WriteLine()

    AnsiConsole.MarkupLine("[bold]🔍 Scanning commit history for Claude co-author violations...[/]")
    AnsiConsole.WriteLine()

    if commits.IsEmpty then
        AnsiConsole.MarkupLine("[green]✅ No commits found with 'Co-Authored-By: Claude'[/]")
        AnsiConsole.WriteLine()
        AnsiConsole.MarkupLine("[dim]All commits are CLA compliant![/]")
    else
        AnsiConsole.MarkupLine(sprintf "[yellow]Found %d commit(s) with \"Co-Authored-By: Claude\":[/]" commits.Length)
        AnsiConsole.WriteLine()

        commits |> List.iteri (fun i commit ->
            AnsiConsole.MarkupLine(sprintf "[bold]%d.[/]" (i + 1))
            displayCommitPreview commit
        )

        // Check if any commits are pushed
        let anyPushed = commits |> List.exists (fun c -> c.IsPushed)
        if anyPushed then
            let warning = Panel("[yellow]⚠️  WARNING[/]: Some commits have been pushed to remote.\n" +
                               "Rewriting history will require [bold]force-push[/].")
            warning.Border <- BoxBorder.Heavy
            warning.BorderStyle <- Style.Parse("yellow")
            AnsiConsole.Write(warning)
            AnsiConsole.WriteLine()

// ============================================================================
// Main Logic
// ============================================================================

let confirmRewrite () : bool =
    AnsiConsole.WriteLine()
    let confirm = AnsiConsole.Prompt(
        TextPrompt<string>("Proceed with rewrite?")
            .AddChoice("yes")
            .AddChoice("no")
            .DefaultValue("no")
    )
    confirm.ToLower() = "yes"

let main (args: ParseResults<CliArguments>) =
    let verbose = args.Contains Verbose
    let dryRun = not (args.Contains Yes) // Dry-run unless --yes is specified
    
    try
        // 1. Check working directory is clean
        match isWorkingDirectoryClean verbose with
        | Ok true -> ()
        | Ok false ->
            AnsiConsole.MarkupLine("[red]✗ Working directory is not clean![/]")
            AnsiConsole.MarkupLine("[dim]Please commit or stash your changes before running this script.[/]")
            exit 1
        | Error msg ->
            AnsiConsole.MarkupLine(sprintf "[red]✗ Error checking working directory: %s[/]" msg)
            exit 1

        // 2. Get commit range
        let commitRange =
            match getCommitRange args verbose with
            | Ok range -> range
            | Error msg ->
                AnsiConsole.MarkupLine(sprintf "[red]✗ Error determining commit range: %s[/]" msg)
                exit 1

        // 3. Scan for commits with Claude co-author
        let commits =
            match scanForClaudeCoAuthor commitRange verbose with
            | Ok cmts -> cmts
            | Error msg ->
                AnsiConsole.MarkupLine(sprintf "[red]✗ Error scanning commits: %s[/]" msg)
                exit 1

        // 4. Display summary
        displaySummary commits dryRun

        // 5. If no commits found, exit successfully
        if commits.IsEmpty then
            exit 0

        // 6. If dry-run, explain and exit
        if dryRun then
            AnsiConsole.WriteLine()
            AnsiConsole.MarkupLine("[dim]This was a [bold]dry-run[/]. No changes were made.[/]")
            AnsiConsole.MarkupLine("[dim]To apply changes, run with [bold]--yes[/] flag:[/]")
            AnsiConsole.WriteLine()
            AnsiConsole.MarkupLine("  [blue]dotnet fsi scripts/remove-claude-coauthor.fsx --yes[/]")
            AnsiConsole.WriteLine()
            exit 0

        // 7. Create backup branch
        let backupBranch =
            match createBackupBranch verbose with
            | Ok branch ->
                AnsiConsole.MarkupLine(sprintf "[green]✓[/] Created backup branch: [cyan]%s[/]" branch)
                branch
            | Error msg ->
                AnsiConsole.MarkupLine(sprintf "[red]✗ Error creating backup branch: %s[/]" msg)
                exit 1

        // 8. Confirm with user (unless --yes flag provided)
        let shouldProceed =
            if args.Contains Yes then
                true
            else
                confirmRewrite()
        
        if not shouldProceed then
            AnsiConsole.MarkupLine("[yellow]Cancelled by user.[/]")
            exit 0

        // 9. Rewrite commit history
        AnsiConsole.WriteLine()
        AnsiConsole.Status()
            .Start("Rewriting commit history...", fun ctx ->
                ctx.Spinner <- Spinner.Known.Dots
                ctx.SpinnerStyle <- Style.Parse("blue")

                match rewriteCommitHistory commits verbose with
                | Ok () ->
                    AnsiConsole.MarkupLine("[green]✅ Commits rewritten successfully![/]")
                | Error msg ->
                    AnsiConsole.MarkupLine(sprintf "[red]✗ Error rewriting commits: %s[/]" msg)
                    AnsiConsole.MarkupLine(sprintf "[yellow]You can restore from backup branch: %s[/]" backupBranch)
                    exit 1
            )

        // 10. Display next steps
        AnsiConsole.WriteLine()
        let anyPushed = commits |> List.exists (fun c -> c.IsPushed)
        
        let nextStepsPanel = Panel(
            if anyPushed then
                sprintf "1. Review changes: [blue]git log --oneline -5[/]\n" +
                sprintf "2. Force push: [blue]git push --force-with-lease origin %s[/]\n" commits.[0].Branch +
                "3. Verify on GitHub that CLA checks pass\n\n" +
                sprintf "[dim]Backup branch: %s[/]\n" backupBranch +
                "[dim]Documentation: See AGENTS.md#commit-messages[/]"
            else
                sprintf "1. Review changes: [blue]git log --oneline -5[/]\n" +
                sprintf "2. Push changes: [blue]git push origin %s[/]\n" commits.[0].Branch +
                "3. Verify on GitHub that CLA checks pass\n\n" +
                sprintf "[dim]Backup branch: %s[/]\n" backupBranch +
                "[dim]Documentation: See AGENTS.md#commit-messages[/]"
        )
        nextStepsPanel.Header <- PanelHeader("Next Steps", Justify.Left)
        nextStepsPanel.Border <- BoxBorder.Rounded
        nextStepsPanel.BorderStyle <- Style.Parse("green")
        AnsiConsole.Write(nextStepsPanel)

        exit 0

    with ex ->
        AnsiConsole.MarkupLine(sprintf "[red]✗ Unexpected error: %s[/]" ex.Message)
        if verbose then
            AnsiConsole.WriteException(ex)
        exit 1

// ============================================================================
// Entry Point
// ============================================================================

let parser = ArgumentParser.Create<CliArguments>(
    programName = "remove-claude-coauthor.fsx",
    helpTextMessage = "Remove Claude co-author from commit history for CLA compliance"
)

try
    let args =
        if fsi.CommandLineArgs.Length > 1 then
            parser.ParseCommandLine(inputs = (fsi.CommandLineArgs |> Array.skip 1), raiseOnUsage = true)
        else
            // Default: dry-run mode
            parser.ParseCommandLine(inputs = [| "--dry-run" |], raiseOnUsage = false)

    main args

with
| :? ArguParseException as ex ->
    eprintfn "%s" ex.Message
    exit 1
| ex ->
    eprintfn "Error: %s" ex.Message
    exit 1
