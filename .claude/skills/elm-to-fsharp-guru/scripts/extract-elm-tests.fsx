#!/usr/bin/env -S dotnet fsi

(*
extract-elm-tests.fsx

Purpose: Extract test cases from Elm documentation comments and generate BDD scenarios.

Usage:
    dotnet fsi extract-elm-tests.fsx <elm-file> <output-feature-file>

Example:
    dotnet fsi extract-elm-tests.fsx src/Morphir/IR/Type.elm tests/Morphir.Core.Tests/IR/TypeTests.feature

Output:
    - BDD scenarios (Reqnroll .feature file)
    - Test cases extracted from doc comments
    - Example inputs and expected outputs
*)

#r "nuget: Spectre.Console, 0.49.1"

open System
open System.IO
open System.Text
open System.Text.RegularExpressions
open Spectre.Console

type TestCase = {
    Expression: string
    Expected: string
}

type FunctionDoc = {
    FunctionName: string
    Description: string
    TestCases: TestCase list
}

let extractDocComments (content: string) : (string * string) list =
    // Match {-| ... -} followed by function name
    let docRegex = Regex(@"{-\|([^}]+?)-}\s*(\w+)\s*:", RegexOptions.Singleline)
    docRegex.Matches(content)
    |> Seq.cast<Match>
    |> Seq.map (fun m ->
        let docText = m.Groups.[1].Value.Trim()
        let funcName = m.Groups.[2].Value
        (funcName, docText)
    )
    |> Seq.toList

let parseTestCases (docText: string) : TestCase list =
    // Look for patterns like: expression == expected
    let testRegex = Regex(@"^\s*(.+?)\s*==\s*(.+?)$", RegexOptions.Multiline)
    testRegex.Matches(docText)
    |> Seq.cast<Match>
    |> Seq.map (fun m ->
        {
            Expression = m.Groups.[1].Value.Trim()
            Expected = m.Groups.[2].Value.Trim()
        }
    )
    |> Seq.toList

let extractFunctionDocs (content: string) : FunctionDoc list =
    let docComments = extractDocComments content
    
    docComments
    |> List.map (fun (funcName, docText) ->
        let lines = docText.Split('\n') |> Array.map (fun l -> l.Trim())
        let description = lines |> Array.tryHead |> Option.defaultValue ""
        let testCases = parseTestCases docText
        
        {
            FunctionName = funcName
            Description = description
            TestCases = testCases
        }
    )
    |> List.filter (fun doc -> not doc.TestCases.IsEmpty)

let generateFeatureFile (moduleName: string) (functionDocs: FunctionDoc list) : string =
    let sb = StringBuilder()
    
    sb.AppendLine($"Feature: {moduleName}") |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine($"  Tests extracted from Elm documentation") |> ignore
    sb.AppendLine() |> ignore
    
    for funcDoc in functionDocs do
        sb.AppendLine($"  # {funcDoc.Description}") |> ignore
        
        for i, testCase in funcDoc.TestCases |> List.indexed do
            sb.AppendLine($"  Scenario: {funcDoc.FunctionName} case {i + 1}") |> ignore
            sb.AppendLine($"    Given the expression \"{testCase.Expression}\"") |> ignore
            sb.AppendLine($"    When I evaluate it") |> ignore
            sb.AppendLine($"    Then the result should be \"{testCase.Expected}\"") |> ignore
            sb.AppendLine() |> ignore
    
    sb.ToString()

let extractModuleName (filePath: string) : string =
    let fileName = Path.GetFileNameWithoutExtension(filePath)
    fileName

// Main
let args = fsi.CommandLineArgs |> Array.skip 1

if args.Length < 2 then
    AnsiConsole.MarkupLine("[red]Error:[/] Missing arguments")
    AnsiConsole.MarkupLine("Usage: dotnet fsi extract-elm-tests.fsx <elm-file> <output-feature-file>")
    exit 1

let elmFile = args.[0]
let outputFile = args.[1]

if not (File.Exists(elmFile)) then
    AnsiConsole.MarkupLine($"[red]Error:[/] File not found: {elmFile}")
    exit 1

AnsiConsole.Status()
    .Start("Extracting test cases from Elm docs...", fun ctx ->
        ctx.Spinner <- Spinner.Known.Dots
        ctx.SpinnerStyle <- Style.Parse("green")
        
        let content = File.ReadAllText(elmFile)
        let moduleName = extractModuleName elmFile
        let functionDocs = extractFunctionDocs content
        
        if functionDocs.IsEmpty then
            AnsiConsole.MarkupLine("[yellow]Warning:[/] No test cases found in documentation")
            exit 0
        
        let featureContent = generateFeatureFile moduleName functionDocs
        
        // Ensure output directory exists
        let outputDir = Path.GetDirectoryName(outputFile)
        if not (String.IsNullOrEmpty(outputDir)) && not (Directory.Exists(outputDir)) then
            Directory.CreateDirectory(outputDir) |> ignore
        
        File.WriteAllText(outputFile, featureContent)
        
        AnsiConsole.Clear()
        let panel = Panel($"[bold green]✓[/] Test Extraction Complete")
        panel.Border <- BoxBorder.Rounded
        AnsiConsole.Write(panel)
        
        AnsiConsole.WriteLine()
        AnsiConsole.MarkupLine($"[bold]Elm Module:[/] {moduleName}")
        AnsiConsole.MarkupLine($"[bold]Functions with tests:[/] {functionDocs.Length}")
        
        let totalTests = functionDocs |> List.sumBy (fun d -> d.TestCases.Length)
        AnsiConsole.MarkupLine($"[bold]Total test cases:[/] {totalTests}")
        AnsiConsole.MarkupLine($"[bold]Output file:[/] [cyan]{outputFile}[/]")
        
        AnsiConsole.WriteLine()
        AnsiConsole.MarkupLine("[bold]Test Cases Extracted:[/]")
        for funcDoc in functionDocs do
            AnsiConsole.MarkupLine($"  [cyan]{funcDoc.FunctionName}[/]: {funcDoc.TestCases.Length} tests")
    )
