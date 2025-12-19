#!/usr/bin/env dotnet fsi

(*
   Validate CHANGELOG.md Format Script

   Validates CHANGELOG.md against KeepAChangelog format requirements.
   This script can be run as a pre-commit hook to catch format errors early.

   Usage:
     dotnet fsi validate-changelog.fsx
     dotnet fsi validate-changelog.fsx --file CHANGELOG.md
     dotnet fsi validate-changelog.fsx --help

   Features:
   - Validates KeepAChangelog format compliance
   - Fast feedback (runs in ~1s vs full build)
   - Clear error messages with line numbers
   - Exit code 0 on success, 1 on failure

   Validation Rules:
   - File must exist
   - Must start with "# Changelog"
   - Version headers must be in format: ## [version] - YYYY-MM-DD
   - Subsections must start with ### (Added, Changed, Deprecated, Removed, Fixed, Security)
   - All items in subsections must start with "-" (list items)
   - No text between version header and first subsection (except blank lines)
*)

#r "nuget: Argu, 6.2.4"

open System
open System.IO
open System.Text.RegularExpressions
open Argu

// ============================================================================
// CLI Arguments (Argu)
// ============================================================================

type CliArguments =
    | [<AltCommandLine("-f")>] File of string
    | [<AltCommandLine("-v")>] Verbose
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | File _ -> "path to CHANGELOG.md (default: ./CHANGELOG.md)"
            | Verbose -> "enable verbose diagnostic output"

// ============================================================================
// Validation Types
// ============================================================================

type ValidationError = {
    Line: int
    Message: string
}

type ValidationResult =
    | Valid
    | Invalid of ValidationError list

// ============================================================================
// Validation Logic
// ============================================================================

let versionHeaderPattern = Regex(@"^## \[.+\]( - \d{4}-\d{2}-\d{2})?$")
let subsectionPattern = Regex(@"^### (Added|Changed|Deprecated|Removed|Fixed|Security)$")
let listItemPattern = Regex(@"^- ")

let validateChangelog (filePath: string) (verbose: bool) : Result<unit, ValidationError list> =
    try
        if verbose then
            eprintfn "[VERBOSE] Validating changelog: %s" filePath

        if not (File.Exists(filePath)) then
            Error [{ Line = 0; Message = $"Changelog file not found: {filePath}" }]
        else
            let lines = File.ReadAllLines(filePath)
            let errors = ResizeArray<ValidationError>()

            if verbose then
                eprintfn "[VERBOSE] File has %d lines" lines.Length

            // Check first line
            if lines.Length = 0 || not (lines.[0].StartsWith("# Changelog")) then
                errors.Add({ Line = 1; Message = "First line must be '# Changelog'" })

            // Track state
            let mutable inVersionSection = false
            let mutable inSubsection = false
            let mutable currentVersion = None
            let mutable linesSinceVersionHeader = 0

            for i = 1 to lines.Length - 1 do
                let line = lines.[i]
                let lineNum = i + 1

                if verbose && i % 50 = 0 then
                    eprintfn "[VERBOSE] Processed %d lines..." lineNum

                // Skip blank lines
                if String.IsNullOrWhiteSpace(line) then
                    ()
                // Version header
                elif versionHeaderPattern.IsMatch(line) then
                    inVersionSection <- true
                    inSubsection <- false
                    currentVersion <- Some line
                    linesSinceVersionHeader <- 0
                    if verbose then
                        eprintfn "[VERBOSE] Found version: %s" line
                // Subsection header
                elif subsectionPattern.IsMatch(line) then
                    inSubsection <- true
                    linesSinceVersionHeader <- 0
                    if verbose then
                        eprintfn "[VERBOSE] Found subsection: %s" line
                // List item (must be in a subsection)
                elif line.StartsWith("-") then
                    if not inSubsection then
                        errors.Add({ Line = lineNum; Message = "List items must be inside a subsection (### Added, Changed, etc.)" })
                    linesSinceVersionHeader <- 0
                // Link definition (e.g., [Unreleased]: https://...)
                elif line.StartsWith("[") && line.Contains("]:") then
                    inVersionSection <- false
                    inSubsection <- false
                // Comment or other allowed content
                elif line.StartsWith("<!--") || line.StartsWith("All notable changes") || line.StartsWith("The format is based") || line.StartsWith("and this project") then
                    ()
                // Section headers (# or ##)
                elif line.StartsWith("#") then
                    // Could be the main title or a version, already handled above
                    ()
                // Content between version and subsection
                elif inVersionSection && not inSubsection then
                    linesSinceVersionHeader <- linesSinceVersionHeader + 1
                    // Allow one line of content (like a note), but it must be a list item
                    if linesSinceVersionHeader = 1 && not (line.StartsWith("-")) then
                        errors.Add({
                            Line = lineNum
                            Message = $"Text between version header and subsection must be a list item (start with '-'). Found: {line.Substring(0, min 50 line.Length)}..."
                        })
                // Content in subsection but not a list item
                elif inSubsection && not (line.StartsWith("-") || line.StartsWith("  ")) then
                    // Allow indented content (continuation of list item)
                    errors.Add({
                        Line = lineNum
                        Message = $"Content in subsection must be a list item (start with '-'). Found: {line.Substring(0, min 50 line.Length)}..."
                    })

            if errors.Count = 0 then
                Ok ()
            else
                Error (errors |> Seq.toList)
    with ex ->
        Error [{ Line = 0; Message = $"Exception while validating changelog: {ex.Message}" }]

// ============================================================================
// Entry Point
// ============================================================================

let parser = ArgumentParser.Create<CliArguments>(
    programName = "validate-changelog.fsx",
    helpTextMessage = "Validate CHANGELOG.md format"
)

// Filter out dotnet-injected arguments (e.g., --preferreduilang from DOTNET_CLI_UI_LANGUAGE)
let filterDotnetArgs (args: string[]) =
    args |> Array.filter (fun arg ->
        not (arg.StartsWith("--preferreduilang", StringComparison.OrdinalIgnoreCase))
    )

try
    let args =
        let filteredArgs = fsi.CommandLineArgs |> Array.skip 1 |> filterDotnetArgs
        if filteredArgs.Length > 0 then
            parser.ParseCommandLine(inputs = filteredArgs, raiseOnUsage = true)
        else
            parser.ParseCommandLine(inputs = [||], raiseOnUsage = false)

    let changelogPath = args.GetResult(File, defaultValue = "CHANGELOG.md")
    let verbose = args.Contains Verbose

    match validateChangelog changelogPath verbose with
    | Ok () ->
        eprintfn "✅ CHANGELOG.md is valid"
        exit 0
    | Error errors ->
        eprintfn "❌ CHANGELOG validation failed with %d error(s):" errors.Length
        eprintfn ""
        for error in errors do
            if error.Line > 0 then
                eprintfn "  Line %d: %s" error.Line error.Message
            else
                eprintfn "  %s" error.Message
        eprintfn ""
        eprintfn "Please fix the CHANGELOG.md format before committing."
        eprintfn "See https://keepachangelog.com/ for format guidelines."
        exit 1
with
| :? ArguParseException as ex ->
    eprintfn "%s" ex.Message
    exit 1
| ex ->
    eprintfn "Error: %s" ex.Message
    exit 1
