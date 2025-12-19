#!/usr/bin/env dotnet fsi
// AOT Diagnostics Script
// Usage: dotnet fsi aot-diagnostics.fsx <project-path> [--json]
//
// Analyzes a .NET project for Native AOT compatibility issues
// Checks: Configuration, reflection usage, dependencies, resources

#r "nuget: System.Text.Json, 9.0.0"
#r "nuget: Argu, 6.2.4"

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.RegularExpressions
open System.Xml.Linq
open Argu

// ============================================================================
// Types
// ============================================================================

type DiagnosticCategory =
    | Configuration
    | Reflection
    | DynamicCode
    | Dependencies
    | Resources
    | Trimming

type DiagnosticSeverity =
    | Critical      // Blocks AOT compilation
    | High          // Workaround needed
    | Medium        // May cause issues
    | Low           // Best practice recommendation
    | Info          // Informational

type DiagnosticIssue = {
    Category: DiagnosticCategory
    Severity: DiagnosticSeverity
    Title: string
    Description: string
    Location: string option
    Suggestion: string
}

type DiagnosticResult = {
    ProjectPath: string
    Timestamp: DateTime
    Issues: DiagnosticIssue list
    Summary: string
    IsAotReady: bool
}

type Arguments =
    | [<Mandatory; MainCommand>] Project_Path of string
    | Json

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Project_Path _ -> "Path to .csproj or .fsproj file"
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
// Project Analysis
// ============================================================================

let parseProjectFile (projectPath: string) : XDocument option =
    try
        let doc = XDocument.Load(projectPath)
        Some doc
    with ex ->
        logError $"Failed to parse project file: {ex.Message}"
        None

let getPropertyValue (doc: XDocument) (propertyName: string) : string option =
    doc.Descendants(XName.Get propertyName)
    |> Seq.tryHead
    |> Option.map (fun el -> el.Value)

let checkAotConfiguration (doc: XDocument) : DiagnosticIssue list =
    let issues = ResizeArray<DiagnosticIssue>()
    
    // Check PublishAot
    match getPropertyValue doc "PublishAot" with
    | Some "true" -> ()
    | _ ->
        issues.Add({
            Category = Configuration
            Severity = Info
            Title = "PublishAot not enabled"
            Description = "Native AOT compilation is not configured"
            Location = None
            Suggestion = "Add <PublishAot>true</PublishAot> to enable Native AOT"
        })
    
    // Check optimization preference
    match getPropertyValue doc "IlcOptimizationPreference" with
    | Some "Size" -> ()
    | _ ->
        issues.Add({
            Category = Configuration
            Severity = Low
            Title = "Size optimization not enabled"
            Description = "IlcOptimizationPreference is not set to Size"
            Location = None
            Suggestion = "Add <IlcOptimizationPreference>Size</IlcOptimizationPreference> for smaller binaries"
        })
    
    // Check invariant globalization
    match getPropertyValue doc "InvariantGlobalization" with
    | Some "true" -> ()
    | _ ->
        issues.Add({
            Category = Configuration
            Severity = Medium
            Title = "InvariantGlobalization not enabled"
            Description = "Can save ~5MB by using invariant globalization"
            Location = None
            Suggestion = "Add <InvariantGlobalization>true</InvariantGlobalization> if you don't need localization"
        })
    
    // Check analyzers
    match getPropertyValue doc "EnableAotAnalyzer" with
    | Some "true" -> ()
    | _ ->
        issues.Add({
            Category = Configuration
            Severity = High
            Title = "AOT analyzer not enabled"
            Description = "AOT analyzers help catch compatibility issues at build time"
            Location = None
            Suggestion = "Add <EnableAotAnalyzer>true</EnableAotAnalyzer> and <EnableTrimAnalyzer>true</EnableTrimAnalyzer>"
        })
    
    issues |> Seq.toList

let checkReflectionPatterns (projectDir: string) : DiagnosticIssue list =
    let issues = ResizeArray<DiagnosticIssue>()
    
    let csharpFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
    let fsharpFiles = Directory.GetFiles(projectDir, "*.fs", SearchOption.AllDirectories)
    
    let reflectionPatterns = [
        ("Type.GetType", "Type.GetType() may not work with trimming")
        ("Assembly.GetTypes", "Assembly.GetTypes() returns incomplete list with trimming")
        ("Activator.CreateInstance", "Activator.CreateInstance may fail with trimmed types")
        ("MethodInfo.Invoke", "Reflection invocation may fail in AOT")
        ("PropertyInfo.GetValue", "Reflection property access may fail in AOT")
        ("Reflection.Emit", "Reflection.Emit is not supported in Native AOT")
    ]
    
    for file in Array.append csharpFiles fsharpFiles do
        let content = File.ReadAllText(file)
        let relativePath = Path.GetRelativePath(projectDir, file)
        
        for (pattern, description) in reflectionPatterns do
            if content.Contains(pattern) then
                issues.Add({
                    Category = Reflection
                    Severity = High
                    Title = $"Reflection usage detected: {pattern}"
                    Description = description
                    Location = Some relativePath
                    Suggestion = "Use source generators or [DynamicDependency] attributes"
                })
    
    issues |> Seq.toList

let checkDynamicCodePatterns (projectDir: string) : DiagnosticIssue list =
    let issues = ResizeArray<DiagnosticIssue>()
    
    let csharpFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
    
    let dynamicPatterns = [
        ("Expression<", "LINQ Expression trees use Reflection.Emit")
        ("dynamic ", "Dynamic keyword not supported in Native AOT")
        ("DynamicObject", "DynamicObject not supported in Native AOT")
    ]
    
    for file in csharpFiles do
        let content = File.ReadAllText(file)
        let relativePath = Path.GetRelativePath(projectDir, file)
        
        for (pattern, description) in dynamicPatterns do
            if content.Contains(pattern) then
                issues.Add({
                    Category = DynamicCode
                    Severity = Critical
                    Title = $"Dynamic code detected: {pattern}"
                    Description = description
                    Location = Some relativePath
                    Suggestion = "Replace with compile-time known types or delegates"
                })
    
    issues |> Seq.toList

let checkDependencies (doc: XDocument) : DiagnosticIssue list =
    let issues = ResizeArray<DiagnosticIssue>()
    
    let knownIssues = Map.ofList [
        ("Newtonsoft.Json", "Newtonsoft.Json uses reflection. Use System.Text.Json with source generators instead")
        ("AutoMapper", "AutoMapper uses reflection. Consider manual mapping or compile-time mapping generators")
        ("Castle.Core", "Castle dynamic proxies not supported. Use source generators or compile-time proxies")
    ]
    
    let packageRefs = doc.Descendants(XName.Get "PackageReference")
    
    for pkg in packageRefs do
        let pkgName = pkg.Attribute(XName.Get "Include") |> Option.ofObj |> Option.map (fun a -> a.Value)
        
        match pkgName with
        | Some name when knownIssues.ContainsKey(name) ->
            issues.Add({
                Category = Dependencies
                Severity = High
                Title = $"Problematic dependency: {name}"
                Description = knownIssues.[name]
                Location = None
                Suggestion = "Replace with AOT-compatible alternative"
            })
        | _ -> ()
    
    issues |> Seq.toList

let checkEmbeddedResources (doc: XDocument) (projectDir: string) : DiagnosticIssue list =
    let issues = ResizeArray<DiagnosticIssue>()
    
    let embeddedResources = doc.Descendants(XName.Get "EmbeddedResource")
    
    if Seq.isEmpty embeddedResources then
        ()
    else
        issues.Add({
            Category = Resources
            Severity = Medium
            Title = "Embedded resources detected"
            Description = "Resource names may change in AOT builds"
            Location = None
            Suggestion = "Use fully qualified resource names and test carefully. Use Assembly.GetManifestResourceNames() to verify."
        })
    
    issues |> Seq.toList

let checkJsonSerialization (projectDir: string) : DiagnosticIssue list =
    let issues = ResizeArray<DiagnosticIssue>()
    
    let csharpFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
    let fsharpFiles = Directory.GetFiles(projectDir, "*.fs", SearchOption.AllDirectories)
    
    let mutable hasJsonSerializer = false
    let mutable hasJsonContext = false
    
    for file in Array.append csharpFiles fsharpFiles do
        let content = File.ReadAllText(file)
        
        if content.Contains("JsonSerializer.Serialize") || content.Contains("JsonSerializer.Deserialize") then
            hasJsonSerializer <- true
        
        if content.Contains("JsonSerializerContext") || content.Contains("[<JsonSerializable") then
            hasJsonContext <- true
    
    if hasJsonSerializer && not hasJsonContext then
        issues.Add({
            Category = Reflection
            Severity = Critical
            Title = "System.Text.Json without source generators"
            Description = "Default JSON serialization uses reflection and won't work in AOT"
            Location = None
            Suggestion = "Create a JsonSerializerContext with [JsonSerializable] attributes for all serialized types"
        })
    
    issues |> Seq.toList

// ============================================================================
// Report Generation
// ============================================================================

let generateSummary (issues: DiagnosticIssue list) : string * bool =
    let criticalCount = issues |> List.filter (fun i -> i.Severity = Critical) |> List.length
    let highCount = issues |> List.filter (fun i -> i.Severity = High) |> List.length
    let mediumCount = issues |> List.filter (fun i -> i.Severity = Medium) |> List.length
    let lowCount = issues |> List.filter (fun i -> i.Severity = Low) |> List.length
    let infoCount = issues |> List.filter (fun i -> i.Severity = Info) |> List.length
    
    let isAotReady = criticalCount = 0 && highCount = 0
    
    let summary = sprintf "Found %d issues: %d critical, %d high, %d medium, %d low, %d info" 
        (List.length issues) criticalCount highCount mediumCount lowCount infoCount
    
    (summary, isAotReady)

let outputResultHuman (result: DiagnosticResult) =
    printfn "=== AOT Diagnostics Report ==="
    printfn "Project: %s" result.ProjectPath
    printfn "Timestamp: %s" (result.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"))
    printfn ""
    printfn "Summary: %s" result.Summary
    printfn "AOT Ready: %b" result.IsAotReady
    printfn ""
    
    if result.Issues.IsEmpty then
        printfn "✓ No issues found. Project appears AOT-compatible!"
    else
        printfn "Issues:"
        printfn ""
        
        let groupedIssues = result.Issues |> List.groupBy (fun i -> i.Severity)
        
        for (severity, issues) in groupedIssues |> List.sortBy (fun (s, _) -> s) do
            printfn "  %A (%d):" severity (List.length issues)
            for issue in issues do
                printfn "    - %s" issue.Title
                printfn "      %s" issue.Description
                match issue.Location with
                | Some loc -> printfn "      Location: %s" loc
                | None -> ()
                printfn "      Suggestion: %s" issue.Suggestion
                printfn ""

let outputResultJson (result: DiagnosticResult) =
    let options = JsonSerializerOptions()
    options.WriteIndented <- true
    options.Converters.Add(JsonFSharpConverter())
    
    let json = JsonSerializer.Serialize(result, options)
    printfn "%s" json

// ============================================================================
// Main Logic
// ============================================================================

let diagnoseProject (projectPath: string) : DiagnosticResult =
    logInfo $"Analyzing project: {projectPath}"
    
    let projectDir = Path.GetDirectoryName(projectPath)
    let allIssues = ResizeArray<DiagnosticIssue>()
    
    // Parse project file
    match parseProjectFile projectPath with
    | Some doc ->
        logInfo "Checking AOT configuration..."
        allIssues.AddRange(checkAotConfiguration doc)
        
        logInfo "Checking dependencies..."
        allIssues.AddRange(checkDependencies doc)
        
        logInfo "Checking embedded resources..."
        allIssues.AddRange(checkEmbeddedResources doc projectDir)
    | None ->
        logError "Failed to parse project file"
    
    // Check source code patterns
    logInfo "Checking for reflection patterns..."
    allIssues.AddRange(checkReflectionPatterns projectDir)
    
    logInfo "Checking for dynamic code patterns..."
    allIssues.AddRange(checkDynamicCodePatterns projectDir)
    
    logInfo "Checking JSON serialization..."
    allIssues.AddRange(checkJsonSerialization projectDir)
    
    let (summary, isAotReady) = generateSummary (allIssues |> Seq.toList)
    
    {
        ProjectPath = projectPath
        Timestamp = DateTime.UtcNow
        Issues = allIssues |> Seq.toList
        Summary = summary
        IsAotReady = isAotReady
    }

// ============================================================================
// CLI Entry Point
// ============================================================================

let main (args: string array) =
    try
        let parser = ArgumentParser.Create<Arguments>(programName = "aot-diagnostics.fsx")
        let results = parser.Parse(args)
        
        jsonOutput := results.Contains Json
        
        let projectPath = results.GetResult Project_Path
        
        if not (File.Exists projectPath) then
            logError $"Project file not found: {projectPath}"
            2
        elif not (projectPath.EndsWith(".csproj") || projectPath.EndsWith(".fsproj")) then
            logError "Project file must be .csproj or .fsproj"
            2
        else
            let result = diagnoseProject projectPath
            
            if !jsonOutput then
                outputResultJson result
            else
                outputResultHuman result
            
            if result.IsAotReady then 0 else 1
    
    with
    | :? ArguParseException as ex ->
        eprintfn "%s" ex.Message
        1
    | ex ->
        logError $"Unexpected error: {ex.Message}"
        2

exit (main fsi.CommandLineArgs.[1..])
