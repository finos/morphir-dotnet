// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// Task 9.2: the frontend-csharp suite through the MCK <c>cli</c> adapter. The adapter drives the executable of the
/// target of the run (<c>dotnet-morphir</c> by default) and the published Native AOT <c>morphir</c>, and the test
/// compares the IR and the diagnostics of each variant with the library adapter and with each other.
/// </summary>
module Morphir.Lang.CSharp.Tests.CliAdapterTests

open System
open System.Collections.Concurrent
open System.IO
open System.Reflection
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.Conformance
open Morphir.TestSupport
open Morphir.TestSupport.Drivers

/// <summary>The test assembly. It has the paths of the development build in its metadata.</summary>
let private testAssembly = Assembly.GetExecutingAssembly()

let private dotnetHost =
    match Environment.GetEnvironmentVariable "DOTNET_HOST_PATH" with
    | null -> "dotnet"
    | host -> host

/// <summary>The command, the prefix arguments and the implementation file of one CLI entry point.</summary>
type private Executable = {
    Command: string
    Prefix: string list
    Implementation: string
}

let private executableOf (fileName: string) (prefix: string seq) (artifact: string) = {
    Command = if fileName = "dotnet" then dotnetHost else fileName
    Prefix = List.ofSeq prefix
    Implementation = artifact
}

/// <summary>
/// The executable of the target of the run of the testing library (<c>MORPHIR_TEST_TARGET</c>,
/// <c>MORPHIR_TEST_HOST</c> and <c>MORPHIR_TEST_EXECUTABLE</c>). The default host is the tool host
/// <c>dotnet-morphir</c> when the target has it. Each host registers the in-process C# frontend, so no host skips.
/// </summary>
let private target =
    lazy
        (let cli = TargetCli.OfTestRun(testAssembly, MorphirHost.DotnetTool)
         executableOf cli.FileName cli.PrefixArguments cli.Artifact, cli)

/// <summary><c>dotnet-morphir</c> of the development build, the JIT reference for the Native AOT run.</summary>
let private developmentTool =
    lazy
        (let assembly =
            CliArtifacts.OfDevelopmentBuild(testAssembly).PathOf MorphirHost.DotnetTool

         executableOf "dotnet" [ "exec"; assembly ] assembly)

/// <summary>A runner that holds a slot of the shared process gate, inside the sandbox.</summary>
let private gatedRun (sandbox: TestSandbox) (invocation: CliProcess) (cancellationToken: CancellationToken) = task {
    let start =
        ChildProcess.StartInfo(invocation.FileName, Array.ofList invocation.Arguments)

    sandbox.Apply start
    start.WorkingDirectory <- invocation.WorkingDirectory
    let! result = ChildProcess.RunAsync(start, TimeSpan.FromMinutes 3.0, cancellationToken)

    return {
        ExitCode = result.ExitCode
        StandardOutput = result.StandardOutput
        StandardError = result.StandardError
    }
}

let private optionsFor (executable: Executable) (sandbox: TestSandbox) : CliAdapterOptions =
    { CliAdapter.options executable.Command executable.Prefix sandbox.Work with
        ProjectFile = CliAdapter.csharpProjectFile
        Run = gatedRun sandbox
    }

/// <summary>
/// An adapter that observes every case before the runner asks for it, in parallel. The shared process gate bounds
/// the concurrency. The runner then reads the observations in case order, so the report stays deterministic.
/// </summary>
let private prefetched (adapter: Adapter) (suite: CaseSet) = task {
    let observations =
        ConcurrentDictionary<CompileInput, Result<CompileObservation, ObservationFailure>>()

    let! _ =
        suite.Cases
        |> List.filter (fun case -> case.Exclusion.IsNone)
        |> List.map (fun case -> case.Input)
        |> List.distinct
        |> List.map (fun input -> task {
            let! observation = adapter.Observe input CancellationToken.None
            observations[input] <- observation
        })
        |> Task.WhenAll

    return
        { adapter with
            Observe = fun input _ -> Task.FromResult observations[input]
        }
}

/// <summary>The suite through the <c>cli</c> adapter on <paramref name="executable"/>.</summary>
let private runCli (executable: Executable) = task {
    use sandbox = TestSandbox.Create "morphir-mck-csharp-cli-"
    let options = optionsFor executable sandbox
    let! identity = CliAdapter.identify options executable.Implementation CancellationToken.None
    let! adapter = prefetched (CliAdapter.create identity options) ConformanceTests.suite.Value
    return! Runner.run ConformanceTests.suite.Value adapter CancellationToken.None
}

let private describe (result: CaseResult) =
    $"{result.Case.Id} ({result.Case.Input.InputKind}, {result.Case.Input.IrVersion}): {result.Verdict}"

/// <summary>Writes a report to MORPHIR_MCK_REPORT_DIR when it is set, for the PRD evidence.</summary>
let private keep (name: string) (report: RunReport) =
    match Environment.GetEnvironmentVariable "MORPHIR_MCK_REPORT_DIR" with
    | null
    | "" -> ()
    | directory ->
        Directory.CreateDirectory directory |> ignore
        File.WriteAllBytes(Path.Combine(directory, name), Report.toUtf8 report)

let private expectQualified (report: RunReport) =
    let failures =
        report.Results
        |> List.filter (fun result ->
            match result.Verdict with
            | Verdict.Failed _ -> true
            | _ -> false)

    if not failures.IsEmpty then
        failtestf
            "%d variant(s) fail through the %s adapter:\n%s"
            failures.Length
            report.Adapter
            (failures |> List.map describe |> String.concat "\n")

    Expect.isTrue report.Qualified $"{report.Implementation.Name} qualifies through the {report.Adapter} adapter"

/// <summary>
/// Each variant of <paramref name="actual"/> has the model and the diagnostics of the same variant of
/// <paramref name="expected"/>. The two reports are of the same suite, so their results are in the same order.
/// </summary>
let private expectSameOutput (what: string) (expected: RunReport) (actual: RunReport) =
    Expect.equal actual.Results.Length expected.Results.Length $"{what}: the same number of variants"

    for actualResult, expectedResult in List.zip actual.Results expected.Results do
        Expect.equal actualResult.Case.Id expectedResult.Case.Id $"{what}: the same case order"
        Expect.equal actualResult.Case.Input.InputKind expectedResult.Case.Input.InputKind $"{what}: the same input kind"

        match actualResult.Observation, expectedResult.Observation with
        | Some actualObservation, Some expectedObservation ->
            Expect.equal actualObservation.Success expectedObservation.Success $"{what}: {describe actualResult}"
            Expect.equal actualObservation.Diagnostics expectedObservation.Diagnostics $"{what}: {describe actualResult}"

            match actualObservation.ModelJson, expectedObservation.ModelJson with
            | Some actualModel, Some expectedModel ->
                match JsonComparison.firstDifference expectedModel actualModel with
                | Some difference -> failtest $"{what}: {describe actualResult}: the IR differs: {difference}"
                | None -> ()
            | None, None -> ()
            | _ -> failtest $"{what}: {describe actualResult}: only one side wrote a model"
        | None, None -> ()
        | _ -> failtest $"{what}: {describe actualResult}: only one side has an observation"

let private expectEveryInputKind (report: RunReport) =
    Expect.equal
        (report.Results |> List.map (fun result -> result.Case.Input.InputKind) |> List.distinct |> List.sort)
        [ "directory"; "files"; "project file" ]
        "every input kind runs"

[<Tests>]
let tests =
    testSequenced
    <| testList "C# corpus through the MCK cli adapter (task 9.2)" [
        test "The C# project file lists the case documents in case order and turns off the default compile items" {
            let input = {
                Package = "acme/demo"
                ExposedModules = [ "Pricing" ]
                Documents = [
                    { Uri = "Pricing.cs"; Text = "" }
                    { Uri = "Nested/Rates;1.cs"; Text = "" }
                ]
                InputKind = "project file"
                IrVersion = "4"
            }

            match CliAdapter.csharpProjectFile input with
            | Error message -> failtest message
            | Ok project ->
                Expect.stringEnds project.Path ".csproj" "a C# project file"
                Expect.stringContains project.Text "<EnableDefaultCompileItems>false</EnableDefaultCompileItems>" "no glob"

                Expect.isLessThan
                    (project.Text.IndexOf "Include=\"Pricing.cs\"")
                    (project.Text.IndexOf "Include=\"Nested/Rates%3B1.cs\"")
                    "the compile items keep the case order, with MSBuild escapes"

            Expect.isError
                (CliAdapter.csharpProjectFile
                    { input with
                        Documents = [ { Uri = "Pricing.fs"; Text = "" } ]
                    })
                "no C# project file for other languages"
        }

        testTask
            "The suite qualifies the executable of the target (dotnet-morphir by default) through the cli adapter, with the library IR" {
            let executable, cli = target.Value
            let! report = runCli executable
            let library = ConformanceTests.report.Value
            keep $"cli-{MorphirHosts.Name cli.Host}-{cli.Target.Name}.json" report
            keep "library.json" library
            expectQualified report
            expectEveryInputKind report

            Expect.equal
                report.Results.Length
                ConformanceTests.suite.Value.Cases.Length
                "every variant of the suite has a verdict"

            expectSameOutput $"{MorphirHosts.Name cli.Host} on {cli.Target.Name} and the library" library report
        }
        |> testLabel "PlatformSpecific"

        // The Native AOT morphir of a published target of the run: MORPHIR_TEST_TARGET=aot after
        // ./build.sh --target PublishExecutable, or MORPHIR_TEST_TARGET=executable with MORPHIR_TEST_EXECUTABLE.
        // MORPHIR_TEST_TARGET=native-aot runs the packaged Native AOT offering. The other targets skip this test.
        testTask "The Native AOT morphir gives the models and the diagnostics of dotnet-morphir for every variant" {
            let executable, cli = target.Value

            // A trimmed or untrimmed single file has the native host too, but it runs on the JIT.
            match cli.Target with
            | :? TestTarget.NativeAot
            | :? TestTarget.Executable -> ()
            | :? TestTarget.Packaged as packaged when packaged.Offering = PackagedOffering.NativeAot -> ()
            | other ->
                skiptest
                    $"target: this test runs a Native AOT morphir, and the run has the target {other.Name}; set MORPHIR_TEST_TARGET (aot, native-aot, or executable with MORPHIR_TEST_EXECUTABLE)"

            if cli.Host <> MorphirHost.Native then
                skiptest $"host: this test runs the native host, and the run has the {MorphirHosts.Name cli.Host} host"

            let! aot = runCli executable
            let! tool = runCli developmentTool.Value
            keep "cli-aot.json" aot
            keep "cli-tool.json" tool
            expectQualified aot
            expectQualified tool
            expectEveryInputKind aot
            expectSameOutput "Native AOT morphir and dotnet-morphir" tool aot
            expectSameOutput "Native AOT morphir and the library" ConformanceTests.report.Value aot
        }
        |> testLabel "PlatformSpecific"
    ]
