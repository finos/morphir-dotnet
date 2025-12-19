#!/usr/bin/env dotnet fsi
// AOT Analyzer Script
// Usage: dotnet fsi aot-analyzer.fsx <build-log-file> [--json]
//
// Analyzes build output for AOT/trimming warnings
// Categorizes warnings and suggests fixes

#r "nuget: System.Text.Json, 9.0.0"
#r "nuget: Argu, 6.2.4"

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.RegularExpressions
open Argu

// ============================================================================
// Types
// ============================================================================

type WarningCategory =
    | UnreferencedCode      // IL2026
    | DynamicCode           // IL3050
    | TypeCompatibility     // IL2087
    | TrimAnalysis          // IL2XXX
    | Other

type WarningEntry = {
    Code: string
    Category: WarningCategory
    Message: string
    File: string option
    Line: int option
    Suggestion: string
}

type WarningAnalysis = {
    TotalWarnings: int
    ByCategory: Map<WarningCategory, int>
    Warnings: WarningEntry list
    TopIssues: string list
    ActionItems: string list
}

type Arguments =
    | [<Mandatory; MainCommand>] Log_File of string
    | Json

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Log_File _ -> "Path to build log file"
            | Json -> "Output results as JSON"

// ============================================================================
// Utilities
// ============================================================================

let jsonOutput = ref false

let logInfo msg =
    if not !jsonOutput then
        eprintfn "[INFO] %s" msg

let logError msg =
    eprintfn "[ERROR] %s" msg

// ============================================================================
// Warning Patterns
// ============================================================================

let warningPattern = Regex(@"warning\s+(IL\d{4}):\s+(.+?)(?:\s+\[(.+?)\])?$", RegexOptions.Multiline)
let fileLinePattern = Regex(@"(.+?)\((\d+),\d+\):\s+warning", RegexOptions.Multiline)

let categorizeWarning (code: string) : WarningCategory =
    match code with
    | "IL2026" -> UnreferencedCode
    | "IL3050" -> DynamicCode
    | "IL2087" -> TypeCompatibility
    | code when code.StartsWith("IL2") -> TrimAnalysis
    | _ -> Other

let getSuggestion (code: string) (message: string) : string =
    match code with
    | "IL2026" when message.Contains("System.Text.Json") ->
        "Use source-generated JsonSerializerContext with [JsonSerializable] attributes"
    | "IL2026" ->
        "Add [DynamicDependency] attribute or refactor to avoid reflection"
    | "IL3050" when message.Contains("Expression") ->
        "Replace LINQ Expression trees with delegates"
    | "IL3050" ->
        "Remove dynamic code generation or mark method with [RequiresDynamicCode]"
    | "IL2087" ->
        "Add [DynamicallyAccessedMembers] attributes to match requirements"
    | code when code.StartsWith("IL2") ->
        "Review trimming behavior and add DynamicDependency or TrimmerRootDescriptor if needed"
    | _ ->
        "Review AOT/Trimming guide for patterns: docs/contributing/aot-trimming-guide.md"

// ============================================================================
// Warning Parsing
// ============================================================================

let parseWarnings (logContent: string) : WarningEntry list =
    let warnings = ResizeArray<WarningEntry>()
    
    for m in warningPattern.Matches(logContent) do
        let code = m.Groups.[1].Value
        let message = m.Groups.[2].Value
        
        // Try to find file and line
        let fileMatch = fileLinePattern.Match(message)
        let (file, line) =
            if fileMatch.Success then
                (Some fileMatch.Groups.[1].Value, Some (int fileMatch.Groups.[2].Value))
            else
                (None, None)
        
        warnings.Add({
            Code = code
            Category = categorizeWarning code
            Message = message
            File = file
            Line = line
            Suggestion = getSuggestion code message
        })
    
    warnings |> Seq.toList

// ============================================================================
// Analysis
// ============================================================================

let analyzeWarnings (warnings: WarningEntry list) : WarningAnalysis =
    let byCategory =
        warnings
        |> List.groupBy (fun w -> w.Category)
        |> List.map (fun (cat, ws) -> (cat, List.length ws))
        |> Map.ofList
    
    // Identify top issues (most common warning codes)
    let topIssues =
        warnings
        |> List.groupBy (fun w -> w.Code)
        |> List.sortByDescending (fun (_, ws) -> List.length ws)
        |> List.take (min 5 (warnings |> List.groupBy (fun w -> w.Code) |> List.length))
        |> List.map (fun (code, ws) -> sprintf "%s (%d occurrences)" code (List.length ws))
    
    // Generate action items
    let actionItems = ResizeArray<string>()
    
    let unreferencedCodeCount = byCategory.TryFind UnreferencedCode |> Option.defaultValue 0
    if unreferencedCodeCount > 0 then
        actionItems.Add($"Fix {unreferencedCodeCount} RequiresUnreferencedCode warnings (IL2026) - Use source generators")
    
    let dynamicCodeCount = byCategory.TryFind DynamicCode |> Option.defaultValue 0
    if dynamicCodeCount > 0 then
        actionItems.Add($"Fix {dynamicCodeCount} RequiresDynamicCode warnings (IL3050) - Remove dynamic code generation")
    
    let typeCompatCount = byCategory.TryFind TypeCompatibility |> Option.defaultValue 0
    if typeCompatCount > 0 then
        actionItems.Add($"Fix {typeCompatCount} type compatibility warnings (IL2087) - Add DynamicallyAccessedMembers")
    
    let trimCount = byCategory.TryFind TrimAnalysis |> Option.defaultValue 0
    if trimCount > 0 then
        actionItems.Add($"Review {trimCount} trim analysis warnings (IL2XXX) - Add DynamicDependency or preserve types")
    
    {
        TotalWarnings = List.length warnings
        ByCategory = byCategory
        Warnings = warnings
        TopIssues = topIssues
        ActionItems = actionItems |> Seq.toList
    }

// ============================================================================
// Output
// ============================================================================

let outputHuman (analysis: WarningAnalysis) =
    printfn "=== AOT/Trimming Warning Analysis ==="
    printfn ""
    printfn "Total Warnings: %d" analysis.TotalWarnings
    printfn ""
    
    if analysis.TotalWarnings = 0 then
        printfn "✓ No AOT/trimming warnings found!"
    else
        printfn "Warnings by Category:"
        for KeyValue(category, count) in analysis.ByCategory do
            printfn "  %A: %d" category count
        printfn ""
        
        printfn "Top Issues:"
        for issue in analysis.TopIssues do
            printfn "  - %s" issue
        printfn ""
        
        printfn "Action Items:"
        for item in analysis.ActionItems do
            printfn "  [ ] %s" item
        printfn ""
        
        printfn "Detailed Warnings:"
        let groupedWarnings = analysis.Warnings |> List.groupBy (fun w -> w.Category)
        
        for (category, warnings) in groupedWarnings do
            printfn ""
            printfn "  %A:" category
            for w in warnings |> List.take (min 10 (List.length warnings)) do
                printfn "    %s: %s" w.Code w.Message
                match w.File, w.Line with
                | Some file, Some line -> printfn "      Location: %s:%d" file line
                | Some file, None -> printfn "      Location: %s" file
                | None, _ -> ()
                printfn "      → %s" w.Suggestion
            
            if List.length warnings > 10 then
                printfn "    ... and %d more" (List.length warnings - 10)

let outputJson (analysis: WarningAnalysis) =
    let options = JsonSerializerOptions()
    options.WriteIndented <- true
    options.Converters.Add(JsonFSharpConverter())
    
    let json = JsonSerializer.Serialize(analysis, options)
    printfn "%s" json

// ============================================================================
// Main
// ============================================================================

let main (args: string array) =
    try
        let parser = ArgumentParser.Create<Arguments>(programName = "aot-analyzer.fsx")
        let results = parser.Parse(args)
        
        jsonOutput := results.Contains Json
        
        let logFile = results.GetResult Log_File
        
        if not (File.Exists logFile) then
            logError $"Log file not found: {logFile}"
            2
        else
            logInfo $"Analyzing build log: {logFile}"
            
            let logContent = File.ReadAllText(logFile)
            let warnings = parseWarnings logContent
            
            logInfo $"Found {List.length warnings} warnings"
            
            let analysis = analyzeWarnings warnings
            
            if !jsonOutput then
                outputJson analysis
            else
                outputHuman analysis
            
            if analysis.TotalWarnings = 0 then 0 else 1
    
    with
    | :? ArguParseException as ex ->
        eprintfn "%s" ex.Message
        1
    | ex ->
        logError $"Unexpected error: {ex.Message}"
        2

exit (main fsi.CommandLineArgs.[1..])
