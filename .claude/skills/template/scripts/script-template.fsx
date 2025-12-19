#!/usr/bin/env -S dotnet fsi

/// Automation Script Template: {Script Purpose}
/// Saves ~{N} tokens per use by automating {high-token-cost task}
/// Usage: dotnet fsi {script-name}.fsx [args]
///
/// Template Instructions:
/// 1. Replace all {placeholder} text with actual content
/// 2. Remove template instructions when done
/// 3. Update the purpose and token savings estimate above
/// 4. Add proper argument parsing
/// 5. Implement the main logic
/// 6. Add comprehensive error handling
/// 7. Test with real data

#r "nuget: Spectre.Console, 0.49.1"
#r "nuget: System.CommandLine, 2.0.0-beta4.22272.1"

open System
open System.IO
open System.CommandLine
open System.CommandLine.Invocation
open Spectre.Console

/// Configuration for this script
type Config = {
    /// {Configuration field 1 description}
    Field1: string
    /// {Configuration field 2 description}
    Field2: int
    /// {Configuration field 3 description}
    Field3: bool
    /// Output format (text, json, markdown)
    OutputFormat: string
}

/// Result of running the script
type Result = {
    /// {Result field 1 description}
    Field1: string
    /// {Result field 2 description}
    Field2: string list
    /// {Result field 3 description}
    Field3: int
    /// Whether the operation succeeded
    Success: bool
    /// Error message if failed
    ErrorMessage: string option
}

/// Parse command line arguments
let parseArgs (args: string array) : Config =
    // TODO: Implement proper command line parsing
    // For now, using defaults
    {
        Field1 = "{default-value}"
        Field2 = 0
        Field3 = false
        OutputFormat = "text"
    }

/// Display a status message
let status (message: string) =
    AnsiConsole.MarkupLine($"[bold blue]►[/] {message}")

/// Display a success message
let success (message: string) =
    AnsiConsole.MarkupLine($"[bold green]✓[/] {message}")

/// Display an error message
let error (message: string) =
    AnsiConsole.MarkupLine($"[bold red]✗[/] {message}")

/// Display a warning message
let warning (message: string) =
    AnsiConsole.MarkupLine($"[bold yellow]⚠[/] {message}")

/// Main logic of the script
let execute (config: Config) : Result =
    try
        status "Starting {task name}..."
        
        // TODO: Implement main logic here
        // Example structure:
        // 1. Validate inputs
        // 2. Perform analysis/transformation
        // 3. Collect results
        // 4. Return structured result
        
        success "{Task} completed successfully"
        
        {
            Field1 = "{result-value}"
            Field2 = ["{item1}"; "{item2}"]
            Field3 = 0
            Success = true
            ErrorMessage = None
        }
    with
    | ex ->
        error $"Failed: {ex.Message}"
        {
            Field1 = ""
            Field2 = []
            Field3 = 0
            Success = false
            ErrorMessage = Some ex.Message
        }

/// Format result as text
let formatText (result: Result) : string =
    let sb = System.Text.StringBuilder()
    sb.AppendLine("═══════════════════════════════════") |> ignore
    sb.AppendLine(" {Script Name} Results") |> ignore
    sb.AppendLine("═══════════════════════════════════") |> ignore
    sb.AppendLine() |> ignore
    
    if result.Success then
        sb.AppendLine($"Status: ✓ Success") |> ignore
    else
        sb.AppendLine($"Status: ✗ Failed") |> ignore
        sb.AppendLine($"Error: {result.ErrorMessage.Value}") |> ignore
    
    sb.AppendLine() |> ignore
    sb.AppendLine($"{result.Field1}") |> ignore
    
    // TODO: Format additional fields
    
    sb.ToString()

/// Format result as JSON
let formatJson (result: Result) : string =
    let errorStr = result.ErrorMessage |> Option.map (sprintf "\"%s\"") |> Option.defaultValue "null"
    $"""{{
  "success": {result.Success.ToString().ToLower()},
  "field1": "{result.Field1}",
  "field2": [{String.Join(", ", result.Field2 |> List.map (sprintf "\"%s\""))}],
  "field3": {result.Field3},
  "errorMessage": {errorStr}
}}"""

/// Format result as Markdown
let formatMarkdown (result: Result) : string =
    let sb = System.Text.StringBuilder()
    sb.AppendLine("# {Script Name} Results") |> ignore
    sb.AppendLine() |> ignore
    
    if result.Success then
        sb.AppendLine("**Status:** ✓ Success") |> ignore
    else
        sb.AppendLine("**Status:** ✗ Failed") |> ignore
        sb.AppendLine($"**Error:** {result.ErrorMessage.Value}") |> ignore
    
    sb.AppendLine() |> ignore
    sb.AppendLine("## Results") |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine($"- **Field 1:** {result.Field1}") |> ignore
    
    // TODO: Format additional fields
    
    sb.ToString()

/// Display result based on output format
let displayResult (config: Config) (result: Result) =
    match config.OutputFormat.ToLower() with
    | "json" ->
        printfn "%s" (formatJson result)
    | "markdown" | "md" ->
        printfn "%s" (formatMarkdown result)
    | _ ->
        printfn "%s" (formatText result)

/// Entry point
let main (args: string array) =
    try
        AnsiConsole.MarkupLine("[bold]{Guru Name} - {Script Name}[/]")
        AnsiConsole.WriteLine()
        
        let config = parseArgs args
        let result = execute config
        
        displayResult config result
        
        if result.Success then 0 else 1
    with
    | ex ->
        error $"Unexpected error: {ex.Message}"
        error $"Stack trace: {ex.StackTrace}"
        2

// Execute
exit (main fsi.CommandLineArgs.[1..])
