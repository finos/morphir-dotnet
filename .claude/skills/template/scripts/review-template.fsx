#!/usr/bin/env -S dotnet fsi

/// Review Script Template: {Review Purpose}
/// Scans {domain} for {issues to detect}
/// Usage: dotnet fsi review-template.fsx [--scope=area] [--format=text|json|markdown]
///
/// Template Instructions:
/// 1. Replace all {placeholder} text with actual content
/// 2. Define review scope and detection logic
/// 3. Implement findings categorization
/// 4. Add trend analysis (vs previous reviews)
/// 5. Generate actionable recommendations
/// 6. Test with real project data

#r "nuget: Spectre.Console, 0.49.1"

open System
open System.IO
open Spectre.Console

/// Severity level for findings
type Severity =
    | Critical
    | High
    | Medium
    | Low

/// A single finding from the review
type Finding = {
    Category: string
    Description: string
    Location: string
    Severity: Severity
    Recommendation: string
}

/// Review results
type ReviewResult = {
    ReviewDate: DateTime
    Scope: string
    Findings: Finding list
    Metrics: Map<string, obj>
    Trends: string list
    Recommendations: string list
    AutomationOpportunities: string list
}

/// Configuration for review
type ReviewConfig = {
    Scope: string option
    OutputFormat: string
    CompareWithPrevious: bool
}

/// Parse command line arguments
let parseArgs (args: string array) : ReviewConfig =
    let mutable scope = None
    let mutable format = "text"
    let mutable compare = false
    
    for arg in args do
        if arg.StartsWith("--scope=") then
            scope <- Some (arg.Substring("--scope=".Length))
        elif arg.StartsWith("--format=") then
            format <- arg.Substring("--format=".Length)
        elif arg = "--compare" then
            compare <- true
    
    { Scope = scope; OutputFormat = format; CompareWithPrevious = compare }

/// Display status message
let status msg = AnsiConsole.MarkupLine($"[bold blue]►[/] {msg}")
let success msg = AnsiConsole.MarkupLine($"[bold green]✓[/] {msg}")
let error msg = AnsiConsole.MarkupLine($"[bold red]✗[/] {msg}")
let warning msg = AnsiConsole.MarkupLine($"[bold yellow]⚠[/] {msg}")

/// Scan {domain} for {issue category 1}
let scan_{category1} (scope: string option) : Finding list =
    // TODO: Implement scanning logic for {category 1}
    // Example:
    // - Find files in scope
    // - Parse/analyze files
    // - Detect issues
    // - Create findings
    []

/// Scan {domain} for {issue category 2}
let scan_{category2} (scope: string option) : Finding list =
    // TODO: Implement scanning logic for {category 2}
    []

/// Scan {domain} for {issue category 3}
let scan_{category3} (scope: string option) : Finding list =
    // TODO: Implement scanning logic for {category 3}
    []

/// Analyze trends (compare with previous review if available)
let analyzeTrends (current: ReviewResult) : string list =
    // TODO: Load previous review results
    // TODO: Compare metrics
    // TODO: Identify trends (improving, worsening, stable)
    [
        "{Metric 1}: {trend description}"
        "{Metric 2}: {trend description}"
    ]

/// Generate recommendations based on findings
let generateRecommendations (findings: Finding list) : string list =
    let criticalCount = findings |> List.filter (fun f -> f.Severity = Critical) |> List.length
    let highCount = findings |> List.filter (fun f -> f.Severity = High) |> List.length
    
    [
        if criticalCount > 0 then
            $"Address {criticalCount} critical findings immediately"
        if highCount > 0 then
            $"Plan to address {highCount} high-priority findings this quarter"
        // TODO: Add domain-specific recommendations
    ]

/// Identify automation opportunities (patterns appearing 3+ times)
let identifyAutomationOpportunities (findings: Finding list) : string list =
    findings
    |> List.groupBy (fun f -> f.Category)
    |> List.filter (fun (_, items) -> List.length items >= 3)
    |> List.map (fun (category, items) ->
        $"{category}: Appears {List.length items} times → Consider automation")

/// Perform the review
let performReview (config: ReviewConfig) : ReviewResult =
    status $"Starting {config.Scope |> Option.defaultValue \"full\"} review..."
    
    // Scan for different issue categories
    let findings = [
        yield! scan_{category1} config.Scope
        yield! scan_{category2} config.Scope
        yield! scan_{category3} config.Scope
    ]
    
    success $"Found {findings.Length} findings"
    
    // Collect metrics
    let metrics = Map [
        "{Metric 1}", box 0
        "{Metric 2}", box 0
        "{Metric 3}", box 0
        "Total Findings", box findings.Length
        "Critical", box (findings |> List.filter (fun f -> f.Severity = Critical) |> List.length)
        "High", box (findings |> List.filter (fun f -> f.Severity = High) |> List.length)
        "Medium", box (findings |> List.filter (fun f -> f.Severity = Medium) |> List.length)
        "Low", box (findings |> List.filter (fun f -> f.Severity = Low) |> List.length)
    ]
    
    let result = {
        ReviewDate = DateTime.UtcNow
        Scope = config.Scope |> Option.defaultValue "full"
        Findings = findings
        Metrics = metrics
        Trends = []
        Recommendations = []
        AutomationOpportunities = []
    }
    
    // Analyze trends
    let trends =
        if config.CompareWithPrevious then
            analyzeTrends result
        else
            []
    
    // Generate recommendations
    let recommendations = generateRecommendations findings
    
    // Identify automation opportunities
    let automationOpps = identifyAutomationOpportunities findings
    
    { result with
        Trends = trends
        Recommendations = recommendations
        AutomationOpportunities = automationOpps }

/// Format severity for display
let formatSeverity = function
    | Critical -> "[red]Critical[/]"
    | High -> "[orange1]High[/]"
    | Medium -> "[yellow]Medium[/]"
    | Low -> "[grey]Low[/]"

/// Display review results as text
let displayText (result: ReviewResult) =
    AnsiConsole.WriteLine()
    AnsiConsole.Rule($"[bold]{Guru Name} Review Report[/]")
    AnsiConsole.WriteLine()
    
    // Summary
    let table = Table()
    table.AddColumn("Metric") |> ignore
    table.AddColumn("Value") |> ignore
    
    for kvp in result.Metrics do
        table.AddRow(kvp.Key, kvp.Value.ToString()) |> ignore
    
    AnsiConsole.Write(table)
    AnsiConsole.WriteLine()
    
    // Findings by category
    if result.Findings.Length > 0 then
        AnsiConsole.MarkupLine("[bold]Findings:[/]")
        AnsiConsole.WriteLine()
        
        for (category, items) in result.Findings |> List.groupBy (fun f -> f.Category) do
            AnsiConsole.MarkupLine($"[bold underline]{category}:[/]")
            for finding in items do
                AnsiConsole.MarkupLine($"  {formatSeverity finding.Severity} {finding.Description}")
                AnsiConsole.MarkupLine($"    Location: [grey]{finding.Location}[/]")
                AnsiConsole.MarkupLine($"    → {finding.Recommendation}")
                AnsiConsole.WriteLine()
    else
        success "No issues found!"
    
    // Trends
    if result.Trends.Length > 0 then
        AnsiConsole.MarkupLine("[bold]Trends:[/]")
        for trend in result.Trends do
            AnsiConsole.MarkupLine($"  • {trend}")
        AnsiConsole.WriteLine()
    
    // Recommendations
    if result.Recommendations.Length > 0 then
        AnsiConsole.MarkupLine("[bold]Recommendations:[/]")
        for i, rec in result.Recommendations |> List.indexed do
            AnsiConsole.MarkupLine($"  {i+1}. {rec}")
        AnsiConsole.WriteLine()
    
    // Automation opportunities
    if result.AutomationOpportunities.Length > 0 then
        AnsiConsole.MarkupLine("[bold]Automation Opportunities:[/]")
        for opp in result.AutomationOpportunities do
            AnsiConsole.MarkupLine($"  • {opp}")

/// Display review results as JSON
let displayJson (result: ReviewResult) =
    // TODO: Implement proper JSON serialization
    printfn "{\"reviewDate\": \"%s\", \"findingsCount\": %d}" 
        (result.ReviewDate.ToString("o"))
        result.Findings.Length

/// Display review results as Markdown
let displayMarkdown (result: ReviewResult) =
    printfn "# %s Review Report" "{Guru Name}"
    printfn ""
    printfn "**Date:** %s" (result.ReviewDate.ToString("yyyy-MM-dd"))
    printfn "**Scope:** %s" result.Scope
    printfn ""
    printfn "## Summary"
    printfn ""
    for kvp in result.Metrics do
        printfn "- **%s:** %s" kvp.Key (kvp.Value.ToString())
    printfn ""
    
    if result.Findings.Length > 0 then
        printfn "## Findings"
        printfn ""
        for (category, items) in result.Findings |> List.groupBy (fun f -> f.Category) do
            printfn "### %s" category
            printfn ""
            for finding in items do
                printfn "- **[%A]** %s" finding.Severity finding.Description
                printfn "  - **Location:** %s" finding.Location
                printfn "  - **Recommendation:** %s" finding.Recommendation
                printfn ""

/// Entry point
let main (args: string array) =
    try
        AnsiConsole.MarkupLine("[bold]{Guru Name} - Review Script[/]")
        AnsiConsole.WriteLine()
        
        let config = parseArgs args
        let result = performReview config
        
        match config.OutputFormat.ToLower() with
        | "json" -> displayJson result
        | "markdown" | "md" -> displayMarkdown result
        | _ -> displayText result
        
        // Return exit code based on critical/high findings
        let criticalCount = result.Findings |> List.filter (fun f -> f.Severity = Critical) |> List.length
        let highCount = result.Findings |> List.filter (fun f -> f.Severity = High) |> List.length
        
        if criticalCount > 0 then 2
        elif highCount > 0 then 1
        else 0
    with
    | ex ->
        error $"Review failed: {ex.Message}"
        error $"Stack trace: {ex.StackTrace}"
        2

// Execute
exit (main fsi.CommandLineArgs.[1..])
