#!/usr/bin/env -S dotnet fsi

(*
verify-compatibility.fsx

Purpose: Verify behavioral equivalence between Elm and F# implementations by comparing JSON outputs.

Usage:
    dotnet fsi verify-compatibility.fsx <test-data-dir>

Example:
    dotnet fsi verify-compatibility.fsx tests/fixtures/

Output:
    - Comparison of JSON outputs
    - Differences highlighted
    - Pass/fail status
*)

#r "nuget: Spectre.Console, 0.49.1"
#r "nuget: System.Text.Json, 9.0.0"

open System
open System.IO
open System.Text.Json
open Spectre.Console

type ComparisonResult =
    | Match
    | Mismatch of expected: string * actual: string
    | MissingFile of which: string

let compareJsonFiles (elmFile: string) (fsharpFile: string) : ComparisonResult =
    if not (File.Exists(elmFile)) then
        MissingFile "Elm output"
    elif not (File.Exists(fsharpFile)) then
        MissingFile "F# output"
    else
        try
            let elmJson = File.ReadAllText(elmFile)
            let fsharpJson = File.ReadAllText(fsharpFile)
            
            // Parse and re-serialize to normalize formatting
            let elmDoc = JsonDocument.Parse(elmJson)
            let fsharpDoc = JsonDocument.Parse(fsharpJson)
            
            let options = JsonSerializerOptions(WriteIndented = false)
            let elmNormalized = JsonSerializer.Serialize(elmDoc, options)
            let fsharpNormalized = JsonSerializer.Serialize(fsharpDoc, options)
            
            if elmNormalized = fsharpNormalized then
                Match
            else
                Mismatch (elmNormalized, fsharpNormalized)
        with
        | ex -> Mismatch ($"Error: {ex.Message}", "")

let runCompatibilityTests (testDataDir: string) : (string * ComparisonResult) list =
    if not (Directory.Exists(testDataDir)) then
        []
    else
        let elmDir = Path.Combine(testDataDir, "elm-output")
        let fsharpDir = Path.Combine(testDataDir, "fsharp-output")
        
        if not (Directory.Exists(elmDir)) || not (Directory.Exists(fsharpDir)) then
            []
        else
            let elmFiles = Directory.GetFiles(elmDir, "*.json")
            
            elmFiles
            |> Array.map (fun elmFile ->
                let fileName = Path.GetFileName(elmFile)
                let fsharpFile = Path.Combine(fsharpDir, fileName)
                let result = compareJsonFiles elmFile fsharpFile
                (fileName, result)
            )
            |> Array.toList

// Main
let args = fsi.CommandLineArgs |> Array.skip 1

if args.Length < 1 then
    AnsiConsole.MarkupLine("[red]Error:[/] Missing test data directory")
    AnsiConsole.MarkupLine("Usage: dotnet fsi verify-compatibility.fsx <test-data-dir>")
    AnsiConsole.MarkupLine()
    AnsiConsole.MarkupLine("Expected structure:")
    AnsiConsole.MarkupLine("  <test-data-dir>/")
    AnsiConsole.MarkupLine("    elm-output/")
    AnsiConsole.MarkupLine("      test1.json")
    AnsiConsole.MarkupLine("    fsharp-output/")
    AnsiConsole.MarkupLine("      test1.json")
    exit 1

let testDataDir = args.[0]

AnsiConsole.Status()
    .Start("Running compatibility tests...", fun ctx ->
        ctx.Spinner <- Spinner.Known.Dots
        ctx.SpinnerStyle <- Style.Parse("green")
        
        let results = runCompatibilityTests testDataDir
        
        if results.IsEmpty then
            AnsiConsole.MarkupLine("[yellow]Warning:[/] No test files found")
            AnsiConsole.MarkupLine($"Checked directory: {testDataDir}")
            exit 0
        
        AnsiConsole.Clear()
        let panel = Panel($"[bold]Compatibility Test Results[/]")
        panel.Border <- BoxBorder.Rounded
        AnsiConsole.Write(panel)
        
        AnsiConsole.WriteLine()
        
        let table = Table()
        table.Border <- TableBorder.Rounded
        table.AddColumn("[bold]Test File[/]") |> ignore
        table.AddColumn("[bold]Status[/]") |> ignore
        table.AddColumn("[bold]Details[/]") |> ignore
        
        let mutable passCount = 0
        let mutable failCount = 0
        
        for (fileName, result) in results do
            match result with
            | Match ->
                table.AddRow(fileName, "[green]✓ PASS[/]", "[dim]Outputs match[/]") |> ignore
                passCount <- passCount + 1
            | Mismatch (expected, actual) ->
                let details = if expected.Length > 50 then "Differences found" else $"{expected} != {actual}"
                table.AddRow(fileName, "[red]✗ FAIL[/]", $"[red]{details}[/]") |> ignore
                failCount <- failCount + 1
            | MissingFile which ->
                table.AddRow(fileName, "[yellow]⚠ SKIP[/]", $"[yellow]{which} missing[/]") |> ignore
        
        AnsiConsole.Write(table)
        
        AnsiConsole.WriteLine()
        let totalTests = passCount + failCount
        let passRate = if totalTests > 0 then (float passCount / float totalTests) * 100.0 else 0.0
        
        AnsiConsole.MarkupLine($"[bold]Summary:[/]")
        AnsiConsole.MarkupLine($"  Total: {totalTests}")
        AnsiConsole.MarkupLine($"  Passed: [green]{passCount}[/]")
        AnsiConsole.MarkupLine($"  Failed: [red]{failCount}[/]")
        AnsiConsole.MarkupLine($"  Pass Rate: [yellow]{passRate:F1}%%[/]")
        
        if failCount > 0 then
            exit 1
    )
