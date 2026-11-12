// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// Tasks 6.3 and 6.4: the MCK <c>cli</c> adapter against a scripted fake executable and against
/// <c>dotnet-morphir</c>, and the complete suite through the <c>library</c> and <c>cli</c> adapters
/// (<c>compile</c>, and <c>compile --target</c> for a sample).
/// </summary>
module Morphir.Frontend.FSharp.Tests.CliAdapterTests

open System
open System.Collections.Concurrent
open System.IO
open System.Reflection
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.Conformance
open Morphir.TestSupport

let private repositoryRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

let private dotnetHost =
    match Environment.GetEnvironmentVariable "DOTNET_HOST_PATH" with
    | null -> "dotnet"
    | host -> host

/// <summary>
/// The <c>dotnet-morphir</c> under test: <c>MORPHIR_DOTNET_TOOL_CLI</c> (a published executable), else the
/// tool assembly of the solution build in the configuration of this test assembly. Returns the command, its
/// prefix arguments and the implementation file.
/// </summary>
let private dotnetMorphir =
    lazy
        (match Environment.GetEnvironmentVariable "MORPHIR_DOTNET_TOOL_CLI" with
         | null
         | "" ->
             let configuration =
                 typeof<FakeMorphir.Marker>.Assembly
                     .GetCustomAttribute<AssemblyConfigurationAttribute>()
                     .Configuration

             let assembly =
                 Path.Combine(
                     repositoryRoot,
                     "src",
                     "Morphir.Tool",
                     "bin",
                     configuration,
                     "net11.0",
                     "dotnet-morphir.dll"
                 )

             if not (File.Exists assembly) then
                 failtestf "Build the solution first: %s does not exist." assembly

             dotnetHost, [ "exec"; assembly ], assembly
         | executable -> Path.GetFullPath executable, [], Path.GetFullPath executable)

/// <summary>The fake: this test executable with its fake argument.</summary>
let private fake =
    lazy
        (let assembly = typeof<FakeMorphir.Marker>.Assembly.Location
         dotnetHost, [ "exec"; assembly; FakeMorphir.Argument ], assembly)

/// <summary>A runner that holds a slot of the shared process gate, inside the sandbox.</summary>
let private gatedRun
    (sandbox: TestSandbox)
    (environment: (string * string) list)
    (invocation: CliProcess)
    (cancellationToken: CancellationToken)
    =
    task {
        let start =
            ChildProcess.StartInfo(invocation.FileName, Array.ofList invocation.Arguments)

        sandbox.Apply start
        start.WorkingDirectory <- invocation.WorkingDirectory

        for key, value in environment do
            start.Environment[key] <- value

        let! result = ChildProcess.RunAsync(start, TimeSpan.FromMinutes 3.0, cancellationToken)

        return {
            ExitCode = result.ExitCode
            StandardOutput = result.StandardOutput
            StandardError = result.StandardError
        }
    }

let private optionsFor
    (fileName, prefix, _)
    (sandbox: TestSandbox)
    environment
    : CliAdapterOptions =
    { CliAdapter.options fileName prefix sandbox.Work with
        Run = gatedRun sandbox environment
    }

/// <summary>
/// An adapter that observes every case before the runner asks for it, in parallel. The shared process gate
/// bounds the concurrency. The runner then reads the observations in case order, so the report stays
/// deterministic.
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

let private describe (result: CaseResult) =
    $"{result.Case.Id} ({result.Case.Input.InputKind}, {result.Case.Input.IrVersion}): {result.Verdict}"

let private expectQualified (report: RunReport) =
    let failures =
        report.Results
        |> List.filter (fun result ->
            match result.Verdict with
            | Verdict.Failed _ -> true
            | _ -> false
        )

    if not failures.IsEmpty then
        failtestf
            "%d case(s) fail through the %s adapter:\n%s"
            failures.Length
            report.Adapter
            (failures
             |> List.truncate 20
             |> List.map describe
             |> String.concat "\n")

    Expect.isTrue
        report.Qualified
        $"{report.Implementation.Name} qualifies through the {report.Adapter} adapter"

/// <summary>A subset of the corpus: every row of the first cases of each kind of outcome.</summary>
let private sample (predicate: ConformanceCase -> bool) count (suite: CaseSet) =
    let ids =
        suite.Cases
        |> List.filter predicate
        |> List.map (fun case -> case.Id)
        |> List.distinct
        |> List.truncate count
        |> Set.ofList

    { suite with
        Cases =
            suite.Cases
            |> List.filter (fun case -> ids.Contains case.Id)
    }

/// <summary>Writes a report to MORPHIR_MCK_REPORT_DIR when it is set, for the PRD evidence.</summary>
let private keep (name: string) (report: RunReport) =
    match Environment.GetEnvironmentVariable "MORPHIR_MCK_REPORT_DIR" with
    | null
    | "" -> ()
    | directory ->
        Directory.CreateDirectory directory
        |> ignore

        File.WriteAllBytes(Path.Combine(directory, name), Report.toUtf8 report)

let private run adapter suite = task {
    let! adapter = prefetched adapter suite
    return! Runner.runWith (Some GeneratedCalls.checker) suite adapter CancellationToken.None
}

/// <summary>
/// True when <c>MORPHIR_MCK_COMPLETE=1</c>: the CLI tests then run every case of the suite, as for the PRD evidence.
/// Otherwise they run a sample with every input kind and IR version, because each variant starts a process and the
/// complete suite takes about 25 minutes on a 4-core CI runner.
/// </summary>
let private complete =
    Environment.GetEnvironmentVariable "MORPHIR_MCK_COMPLETE" = "1"

/// <summary>The suite, or with <see cref="complete"/> false, the first semantic and refusal cases of it.</summary>
let private suiteOrSample semanticCount refusalCount (suite: CaseSet) =
    if complete then
        suite
    else
        let semantic = sample (fun case -> case.Expected.Success) semanticCount suite
        let refusals = sample (fun case -> not case.Expected.Success) refusalCount suite

        { suite with
            Cases = semantic.Cases @ refusals.Cases
        }

let private semanticOrRefusal =
    sample (fun case -> case.Expected.Success) 2 CorpusTests.suite.Value
    |> fun semantic ->
        let refusals =
            sample (fun case -> not case.Expected.Success) 1 CorpusTests.suite.Value

        { semantic with
            Cases =
                semantic.Cases
                @ refusals.Cases
        }

[<Tests>]
let tests =
    testSequenced
    <| testList "MCK cli adapter (tasks 6.3 and 6.4)" [
        testTask "The adapter writes the documented configuration and project file" {
            let input = {
                Package = "acme/demo"
                ExposedModules = [ "Pricing" ]
                Documents = [
                    { Uri = "Pricing.fsi"; Text = "" }
                    { Uri = "Pricing.fs"; Text = "" }
                ]
                InputKind = "project file"
                IrVersion = "4"
            }

            Expect.equal
                (CliAdapter.configuration input)
                "[project]\nname = \"acme/demo\"\nexposed_modules = [\"Pricing\"]\n"
                "morphir.toml"

            match CliAdapter.fsharpProjectFile input with
            | Error message -> failtest message
            | Ok project ->
                Expect.stringEnds project.Path ".fsproj" "an F# project file"

                Expect.isLessThan
                    (project.Text.IndexOf "Include=\"Pricing.fsi\"")
                    (project.Text.IndexOf "Include=\"Pricing.fs\"")
                    "the compile items keep the case order"

            Expect.isError
                (CliAdapter.fsharpProjectFile
                    { input with
                        Documents = [ { Uri = "Pricing.cs"; Text = "" } ]
                    })
                "no default project file for other languages"
        }

        testTask
            "A scripted fake executable qualifies on a sample, so the adapter depends only on documented behavior" {
            use sandbox = TestSandbox.Create "morphir-mck-cli-fake-"
            let options = optionsFor fake.Value sandbox []

            let! identity =
                CliAdapter.identify
                    options
                    (let _, _, file = fake.Value in file)
                    CancellationToken.None

            Expect.equal
                identity.Version
                FakeMorphir.Version
                "the identity has the reported version"

            let! report = run (CliAdapter.create identity options) semanticOrRefusal
            expectQualified report

            Expect.isTrue
                (report.Results
                 |> List.exists (fun result ->
                     result.Case.Input.InputKind = "project file"
                     && result.Verdict = Verdict.Passed
                 ))
                "project-file rows run"
        }

        testTask "Broken JSON Lines output gives an operational failure with its reason" {
            use sandbox = TestSandbox.Create "morphir-mck-cli-broken-"

            let case =
                CorpusTests.suite.Value.Cases
                |> List.find (fun case ->
                    case.Expected.Success
                    && case.Exclusion.IsNone
                    && case.Input.InputKind = "files"
                )

            for mode, expected in
                [
                    "no-completed", "no completed record"
                    "two-completed", "2 completed records"
                    "completed-not-last", "not the last record"
                    "exit-mismatch", "differs from the report exit code"
                    "not-json", "is not a JSON Lines record"
                ] do
                let adapter =
                    CliAdapter.create
                        {
                            Name = "fake"
                            Version = FakeMorphir.Version
                            Sha256 = ""
                        }
                        (optionsFor fake.Value sandbox [ FakeMorphir.ModeVariable, mode ])

                let! observation = adapter.Observe case.Input CancellationToken.None

                match observation with
                | Ok _ -> failtest $"The {mode} output was accepted"
                | Error failure -> Expect.stringContains failure.Message expected mode
        }

        testTask
            "The suite (complete with MORPHIR_MCK_COMPLETE=1) qualifies dotnet-morphir through the library and cli adapters, with equal IR" {
            use sandbox = TestSandbox.Create "morphir-mck-cli-"
            let suite = suiteOrSample 3 1 CorpusTests.suite.Value
            let options = optionsFor dotnetMorphir.Value sandbox []

            let! identity =
                CliAdapter.identify
                    options
                    (let _, _, file = dotnetMorphir.Value in file)
                    CancellationToken.None

            let! cli = run (CliAdapter.create identity options) suite
            let! library = run CorpusTests.adapter suite
            keep "library.json" library
            keep "cli.json" cli
            expectQualified library
            expectQualified cli

            Expect.equal
                (cli.Results
                 |> List.length)
                (if complete then 600 else (suite.Cases |> List.length))
                "every variant of the suite has a verdict"

            Expect.equal
                (cli.Results
                 |> List.map (fun result -> result.Case.Input.InputKind)
                 |> List.distinct
                 |> List.sort)
                [ "directory"; "files"; "project file" ]
                "every input kind runs"

            Expect.equal
                (cli.Results
                 |> List.map (fun result -> result.Case.Input.IrVersion)
                 |> List.distinct
                 |> List.sort)
                [ "1"; "2"; "3"; "4" ]
                "every IR version runs"

            // morphir-dotnet check: the executable writes the same IR as the library, for every input kind.
            for cliResult, libraryResult in List.zip cli.Results library.Results do
                match cliResult.Observation, libraryResult.Observation with
                | Some { ModelJson = Some cliModel }, Some { ModelJson = Some libraryModel } ->
                    match JsonComparison.firstDifference libraryModel cliModel with
                    | Some difference ->
                        failtest
                            $"{describe cliResult}: the CLI IR differs from the library IR: {difference}"
                    | None -> ()
                | Some cliObservation, Some libraryObservation ->
                    Expect.equal
                        cliObservation.Diagnostics
                        libraryObservation.Diagnostics
                        (describe cliResult)
                | _ -> ()
        }

        testTask "A sample of the suite qualifies dotnet-morphir through compile --target" {
            use sandbox = TestSandbox.Create "morphir-mck-cli-target-"

            let suite =
                if complete then
                    let semantic = sample (fun case -> case.Expected.Success) 12 CorpusTests.suite.Value
                    let refusals = sample (fun case -> not case.Expected.Success) 4 CorpusTests.suite.Value

                    { semantic with
                        Cases = semantic.Cases @ refusals.Cases
                    }
                else
                    suiteOrSample 1 1 CorpusTests.suite.Value

            for target in [ "fsharp"; "csharp" ] do
                let options =
                    { optionsFor dotnetMorphir.Value sandbox [] with
                        ExtraArguments = [ "--target"; target; "--generate-output"; "generated" ]
                    }

                let! identity =
                    CliAdapter.identify
                        options
                        (let _, _, file = dotnetMorphir.Value in file)
                        CancellationToken.None

                let adapter =
                    { CliAdapter.create identity options with
                        Name = $"cli --target {target}"
                    }

                let! report = run adapter suite
                keep $"cli-target-{target}.json" report
                expectQualified report
        }
            // Task 9.4: the published Native AOT morphir with the installed MEP guest. Opt-in, because it needs a
        // publish and an installed tool: set MORPHIR_AOT_CLI to the published executable and MORPHIR_FSHARP_GUEST to
        // the installed morphir-fsharp-frontend command.
        testTask "The complete suite through the cli adapter against the AOT morphir with the installed guest equals the library" {
            match Environment.GetEnvironmentVariable "MORPHIR_AOT_CLI", Environment.GetEnvironmentVariable "MORPHIR_FSHARP_GUEST" with
            | null, _
            | "", _
            | _, null
            | _, "" -> skiptest "Set MORPHIR_AOT_CLI and MORPHIR_FSHARP_GUEST to run the AOT suite"
            | executable, guest ->
                use sandbox = TestSandbox.Create "morphir-mck-aot-"
                let suite = CorpusTests.suite.Value
                let morphir = Path.GetFullPath executable, [], Path.GetFullPath executable

                let options =
                    { optionsFor morphir sandbox [] with
                        ExtraArguments = [ "--extension"; guest ]
                    }

                let! identity = CliAdapter.identify options (Path.GetFullPath executable) CancellationToken.None
                let! aot = run (CliAdapter.create identity options) suite
                let! library = run CorpusTests.adapter suite
                keep "cli-aot-mep.json" aot

                let signature (result: CaseResult) =
                    result.Case.Input.Documents
                    |> List.exists (fun document -> document.Uri.EndsWith ".fsi")

                for aotResult, libraryResult in List.zip aot.Results library.Results do
                    match aotResult.Verdict, libraryResult.Verdict with
                    | Verdict.Excluded _, Verdict.Excluded _ -> ()
                    | Verdict.Failed _, Verdict.Passed when signature aotResult ->
                        // The guest claims .fs only, so no registered frontend selects the language of a .fsi
                        // document, and the host refuses the compile before dispatch.
                        let codes =
                            aotResult.Observation
                            |> Option.map (fun observation -> observation.Diagnostics |> List.map (fun diagnostic -> diagnostic.Code))
                            |> Option.defaultValue []

                        Expect.equal codes [ "compile.language_required" ] (describe aotResult)
                    | Verdict.Passed, Verdict.Passed ->
                        match aotResult.Observation, libraryResult.Observation with
                        | Some { ModelJson = Some aotModel }, Some { ModelJson = Some libraryModel } ->
                            match JsonComparison.firstDifference libraryModel aotModel with
                            | Some difference -> failtest $"{describe aotResult}: the AOT IR differs from the library IR: {difference}"
                            | None -> ()
                        | Some aotObservation, Some libraryObservation ->
                            Expect.equal aotObservation.Diagnostics libraryObservation.Diagnostics (describe aotResult)
                        | _ -> ()
                    | _ -> failtest $"{describe aotResult} / library: {libraryResult.Verdict}"

                let refused = aot.Results |> List.filter signature |> List.filter (fun result -> result.Verdict.IsFailed)
                let passed = aot.Results |> List.filter (fun result -> result.Verdict.IsPassed)
                Expect.equal (passed.Length + refused.Length + (aot.Results |> List.filter (fun r -> r.Verdict.IsExcluded)).Length) aot.Results.Length "every variant is accounted for"
        }
    ]
