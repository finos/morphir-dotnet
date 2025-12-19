#!/usr/bin/env dotnet fsi
// AOT Test Runner Script
// Usage: dotnet fsi aot-test-runner.fsx [--runtime linux-x64] [--project <path>] [--json]
//
// Runs comprehensive AOT build tests and measures sizes
// Tests: Framework-dependent, Self-contained, Trimmed, Native AOT

#r "nuget: System.Text.Json, 9.0.0"
#r "nuget: Argu, 6.2.4"

open System
open System.IO
open System.Diagnostics
open System.Text.Json
open System.Text.Json.Serialization
open Argu

// ============================================================================
// Types
// ============================================================================

type BuildConfiguration =
    | FrameworkDependent
    | SelfContained
    | Trimmed
    | NativeAot
    | NativeAotOptimized

type BuildResult = {
    Configuration: BuildConfiguration
    Success: bool
    BuildTime: TimeSpan
    ExecutablePath: string option
    ExecutableSize: int64 option
    Errors: string list
}

type SmokeTestResult = {
    TestName: string
    Success: bool
    Output: string
    ExitCode: int
}

type TestRunResult = {
    Timestamp: DateTime
    Runtime: string
    ProjectPath: string
    BuildResults: BuildResult list
    SmokeTests: Map<BuildConfiguration, SmokeTestResult list>
    Summary: string
}

type Arguments =
    | [<AltCommandLine("-r")>] Runtime of string
    | [<AltCommandLine("-p")>] Project of string
    | Json
    | [<AltCommandLine("-s")>] Skip_Smoke_Tests

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Runtime _ -> "Target runtime (e.g., linux-x64, win-x64, osx-x64)"
            | Project _ -> "Path to .csproj file"
            | Json -> "Output results as JSON"
            | Skip_Smoke_Tests -> "Skip smoke tests after builds"

// ============================================================================
// Utilities
// ============================================================================

let jsonOutput = ref false

let logInfo msg =
    if not !jsonOutput then
        eprintfn "[INFO] %s" msg

let logError msg =
    eprintfn "[ERROR] %s" msg

let runCommand (command: string) (args: string) (workingDir: string) : int * string =
    let startInfo = ProcessStartInfo()
    startInfo.FileName <- command
    startInfo.Arguments <- args
    startInfo.WorkingDirectory <- workingDir
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true
    startInfo.UseShellExecute <- false
    startInfo.CreateNoWindow <- true
    
    use proc = new Process()
    proc.StartInfo <- startInfo
    
    let output = System.Text.StringBuilder()
    proc.OutputDataReceived.Add(fun e -> if not (isNull e.Data) then output.AppendLine(e.Data) |> ignore)
    proc.ErrorDataReceived.Add(fun e -> if not (isNull e.Data) then output.AppendLine(e.Data) |> ignore)
    
    proc.Start() |> ignore
    proc.BeginOutputReadLine()
    proc.BeginErrorReadLine()
    proc.WaitForExit()
    
    (proc.ExitCode, output.ToString())

let formatSize (bytes: int64) : string =
    let kb = float bytes / 1024.0
    let mb = kb / 1024.0
    
    if mb >= 1.0 then
        sprintf "%.2f MB" mb
    elif kb >= 1.0 then
        sprintf "%.2f KB" kb
    else
        sprintf "%d bytes" bytes

// ============================================================================
// Build Functions
// ============================================================================

let findExecutable (outputDir: string) (projectName: string) (runtime: string) : string option =
    let exeName = 
        if runtime.StartsWith("win") then
            projectName + ".exe"
        else
            projectName
    
    let possiblePaths = [
        Path.Combine(outputDir, exeName)
        Path.Combine(outputDir, "publish", exeName)
    ]
    
    possiblePaths |> List.tryFind File.Exists

let buildFrameworkDependent (projectPath: string) : BuildResult =
    logInfo "Building framework-dependent..."
    let startTime = DateTime.Now
    
    let projectDir = Path.GetDirectoryName(projectPath)
    let (exitCode, output) = runCommand "dotnet" "build -c Release" projectDir
    
    let endTime = DateTime.Now
    
    {
        Configuration = FrameworkDependent
        Success = exitCode = 0
        BuildTime = endTime - startTime
        ExecutablePath = None
        ExecutableSize = None
        Errors = if exitCode = 0 then [] else [output]
    }

let buildSelfContained (projectPath: string) (runtime: string) : BuildResult =
    logInfo "Building self-contained..."
    let startTime = DateTime.Now
    
    let projectDir = Path.GetDirectoryName(projectPath)
    let projectName = Path.GetFileNameWithoutExtension(projectPath)
    let args = sprintf "publish -c Release -r %s --self-contained" runtime
    let (exitCode, output) = runCommand "dotnet" args projectDir
    
    let endTime = DateTime.Now
    
    let outputDir = Path.Combine(projectDir, "bin", "Release", "net10.0", runtime, "publish")
    let exePath = findExecutable outputDir projectName runtime
    let size = exePath |> Option.map (fun p -> FileInfo(p).Length)
    
    {
        Configuration = SelfContained
        Success = exitCode = 0
        BuildTime = endTime - startTime
        ExecutablePath = exePath
        ExecutableSize = size
        Errors = if exitCode = 0 then [] else [output]
    }

let buildTrimmed (projectPath: string) (runtime: string) : BuildResult =
    logInfo "Building trimmed..."
    let startTime = DateTime.Now
    
    let projectDir = Path.GetDirectoryName(projectPath)
    let projectName = Path.GetFileNameWithoutExtension(projectPath)
    let args = sprintf "publish -c Release -r %s --self-contained /p:PublishTrimmed=true" runtime
    let (exitCode, output) = runCommand "dotnet" args projectDir
    
    let endTime = DateTime.Now
    
    let outputDir = Path.Combine(projectDir, "bin", "Release", "net10.0", runtime, "publish")
    let exePath = findExecutable outputDir projectName runtime
    let size = exePath |> Option.map (fun p -> FileInfo(p).Length)
    
    {
        Configuration = Trimmed
        Success = exitCode = 0
        BuildTime = endTime - startTime
        ExecutablePath = exePath
        ExecutableSize = size
        Errors = if exitCode = 0 then [] else [output]
    }

let buildNativeAot (projectPath: string) (runtime: string) (optimized: bool) : BuildResult =
    let configName = if optimized then "Native AOT (optimized)" else "Native AOT"
    logInfo $"Building {configName}..."
    let startTime = DateTime.Now
    
    let projectDir = Path.GetDirectoryName(projectPath)
    let projectName = Path.GetFileNameWithoutExtension(projectPath)
    let optimizeArgs = if optimized then " /p:IlcOptimizationPreference=Size" else ""
    let args = sprintf "publish -c Release -r %s /p:PublishAot=true%s" runtime optimizeArgs
    let (exitCode, output) = runCommand "dotnet" args projectDir
    
    let endTime = DateTime.Now
    
    let outputDir = Path.Combine(projectDir, "bin", "Release", "net10.0", runtime, "publish")
    let exePath = findExecutable outputDir projectName runtime
    let size = exePath |> Option.map (fun p -> FileInfo(p).Length)
    
    let config = if optimized then NativeAotOptimized else NativeAot
    
    {
        Configuration = config
        Success = exitCode = 0
        BuildTime = endTime - startTime
        ExecutablePath = exePath
        ExecutableSize = size
        Errors = if exitCode = 0 then [] else [output]
    }

// ============================================================================
// Smoke Tests
// ============================================================================

let runSmokeTests (exePath: string) : SmokeTestResult list =
    let results = ResizeArray<SmokeTestResult>()
    
    // Test 1: --version
    logInfo "  Running smoke test: --version"
    let (exitCode1, output1) = runCommand exePath "--version" (Path.GetDirectoryName(exePath))
    results.Add({
        TestName = "--version"
        Success = exitCode1 = 0
        Output = output1.Trim()
        ExitCode = exitCode1
    })
    
    // Test 2: --help
    logInfo "  Running smoke test: --help"
    let (exitCode2, output2) = runCommand exePath "--help" (Path.GetDirectoryName(exePath))
    results.Add({
        TestName = "--help"
        Success = exitCode2 = 0
        Output = output2.Trim()
        ExitCode = exitCode2
    })
    
    results |> Seq.toList

// ============================================================================
// Main Test Runner
// ============================================================================

let runTests (projectPath: string) (runtime: string) (skipSmokeTests: bool) : TestRunResult =
    let buildResults = ResizeArray<BuildResult>()
    let smokeTests = System.Collections.Generic.Dictionary<BuildConfiguration, SmokeTestResult list>()
    
    // Build framework-dependent
    buildResults.Add(buildFrameworkDependent projectPath)
    
    // Build self-contained
    let scResult = buildSelfContained projectPath runtime
    buildResults.Add(scResult)
    if not skipSmokeTests && scResult.Success && scResult.ExecutablePath.IsSome then
        smokeTests.[SelfContained] <- runSmokeTests scResult.ExecutablePath.Value
    
    // Build trimmed
    let trimResult = buildTrimmed projectPath runtime
    buildResults.Add(trimResult)
    if not skipSmokeTests && trimResult.Success && trimResult.ExecutablePath.IsSome then
        smokeTests.[Trimmed] <- runSmokeTests trimResult.ExecutablePath.Value
    
    // Build Native AOT
    let aotResult = buildNativeAot projectPath runtime false
    buildResults.Add(aotResult)
    if not skipSmokeTests && aotResult.Success && aotResult.ExecutablePath.IsSome then
        smokeTests.[NativeAot] <- runSmokeTests aotResult.ExecutablePath.Value
    
    // Build Native AOT (optimized)
    let aotOptResult = buildNativeAot projectPath runtime true
    buildResults.Add(aotOptResult)
    if not skipSmokeTests && aotOptResult.Success && aotOptResult.ExecutablePath.IsSome then
        smokeTests.[NativeAotOptimized] <- runSmokeTests aotOptResult.ExecutablePath.Value
    
    let successCount = buildResults |> Seq.filter (fun r -> r.Success) |> Seq.length
    let summary = sprintf "%d of %d builds succeeded" successCount (Seq.length buildResults)
    
    {
        Timestamp = DateTime.UtcNow
        Runtime = runtime
        ProjectPath = projectPath
        BuildResults = buildResults |> Seq.toList
        SmokeTests = smokeTests |> Seq.map (fun kvp -> (kvp.Key, kvp.Value)) |> Map.ofSeq
        Summary = summary
    }

// ============================================================================
// Output
// ============================================================================

let outputHuman (result: TestRunResult) =
    printfn "=== AOT Test Runner Results ==="
    printfn "Project: %s" result.ProjectPath
    printfn "Runtime: %s" result.Runtime
    printfn "Timestamp: %s" (result.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"))
    printfn ""
    printfn "Summary: %s" result.Summary
    printfn ""
    
    printfn "Build Results:"
    printfn "%-25s %-10s %-15s %-20s" "Configuration" "Status" "Build Time" "Size"
    printfn "%s" (String.replicate 70 "-")
    
    for br in result.BuildResults do
        let status = if br.Success then "✓ Pass" else "✗ Fail"
        let buildTime = sprintf "%.2fs" br.BuildTime.TotalSeconds
        let size = br.ExecutableSize |> Option.map formatSize |> Option.defaultValue "N/A"
        printfn "%-25s %-10s %-15s %-20s" (sprintf "%A" br.Configuration) status buildTime size
    
    printfn ""
    
    if not (Map.isEmpty result.SmokeTests) then
        printfn "Smoke Test Results:"
        for KeyValue(config, tests) in result.SmokeTests do
            printfn "  %A:" config
            for test in tests do
                let status = if test.Success then "✓" else "✗"
                printfn "    %s %s (exit code: %d)" status test.TestName test.ExitCode

let outputJson (result: TestRunResult) =
    let options = JsonSerializerOptions()
    options.WriteIndented <- true
    options.Converters.Add(JsonFSharpConverter())
    
    let json = JsonSerializer.Serialize(result, options)
    printfn "%s" json

// ============================================================================
// CLI Entry Point
// ============================================================================

let main (args: string array) =
    try
        let parser = ArgumentParser.Create<Arguments>(programName = "aot-test-runner.fsx")
        let results = parser.Parse(args)
        
        jsonOutput := results.Contains Json
        
        let runtime = results.GetResult(Runtime, defaultValue = "linux-x64")
        let skipSmokeTests = results.Contains Skip_Smoke_Tests
        
        let projectPath =
            match results.TryGetResult Project with
            | Some path -> path
            | None ->
                // Try to find .csproj in current directory
                let currentDir = Directory.GetCurrentDirectory()
                let csprojFiles = Directory.GetFiles(currentDir, "*.csproj")
                if csprojFiles.Length = 0 then
                    logError "No .csproj file found in current directory. Use --project to specify."
                    exit 2
                elif csprojFiles.Length > 1 then
                    logError "Multiple .csproj files found. Use --project to specify which one."
                    exit 2
                else
                    csprojFiles.[0]
        
        if not (File.Exists projectPath) then
            logError $"Project file not found: {projectPath}"
            2
        else
            logInfo $"Running AOT test matrix for: {projectPath}"
            logInfo $"Target runtime: {runtime}"
            
            let result = runTests projectPath runtime skipSmokeTests
            
            if !jsonOutput then
                outputJson result
            else
                outputHuman result
            
            let allSuccess = result.BuildResults |> List.forall (fun r -> r.Success)
            if allSuccess then 0 else 1
    
    with
    | :? ArguParseException as ex ->
        eprintfn "%s" ex.Message
        1
    | ex ->
        logError $"Unexpected error: {ex.Message}"
        eprintfn "%s" ex.StackTrace
        2

exit (main fsi.CommandLineArgs.[1..])
