// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.WasmHost

open System
open System.IO
open System.Reflection
open System.Security.Cryptography
open System.Threading
open System.Threading.Tasks
open Morphir.Conformance

/// What the host declares about itself, given to the runner as input. The worker mode reports none of it:
/// the profiles a host runs are in its runtime report (corpus withoutCase hostSession:profiles-reported),
/// and the offering, platform and engine are what the report must identify (spec "Report identity").
type HostDeclaration = {
    /// The profiles the host declares it runs. extism-kernel-v1 is always run.
    Profiles: Profile list
    /// The offering, for example native-aot or dotnet-tool.
    Offering: string option
    /// The platform, for example linux-x64.
    Platform: string option
    /// The engine that enforces the bounds, for example wasmtime 50.0.1.
    Engine: string option
}

/// A known difference of the host from the contract. It explains a case's failure and links its
/// tracking issue; it never changes the outcome.
type Deviation = {
    /// The upstream case id it explains.
    CaseId: string
    /// The issue that tracks it.
    Tracking: string
    Note: string
}

/// A passing run of the generic guest kit through the same host executable, cited by the report (spec "A
/// host does not qualify on crafted cases alone").
type GuestKitCitation = {
    /// The execution profile covered by this guest run.
    Profile: Profile
    /// The path of the cited report.
    Report: string
    /// The SHA-256 of the cited report's bytes.
    ReportSha256: string
    /// The host executable digest that the cited report records.
    HostSha256: string
    /// The digest of the published guest module it ran.
    GuestModuleSha256: string
}

/// The identity of this runner.
type RunnerIdentity = {
    Name: string
    Version: string
    /// The executable that ran the runner.
    Executable: string
    Sha256: string
}

/// The one host executable a report qualifies.
type HostIdentity = {
    Executable: string
    /// The arguments before `extension worker`.
    Arguments: string list
    Sha256: string
    /// The build identity from `extension worker --help --json`.
    BuildIdentity: string
    Declared: HostDeclaration
}

/// The result of one case.
type HostCaseResult = {
    Case: SuiteCase
    /// The module file the case used.
    ModulePath: string
    /// The SHA-256 of that file, None when it is missing.
    ObservedSha256: string option
    /// The observations of the runs, in order. Empty when the case did not run.
    Runs: RunObservation list
    Outcome: CaseOutcome
    Deviation: Deviation option
}

/// Everything a run of the suite against one host saw, and the identities that make it checkable.
type WasmHostReport = {
    Pin: Pin
    Kit: KitIdentity
    Runner: RunnerIdentity
    Host: HostIdentity
    Limits: RunLimits
    /// What the started events declared about each bound.
    Bounds: Map<Bound, BoundDeclaration>
    GuestKitCitations: GuestKitCitation list
    Cases: HostCaseResult list
}

/// How the runner drives a host.
type RunnerOptions = {
    Host: HostCommand
    Declared: HostDeclaration
    Limits: RunLimits
    /// The file of a module of the corpus.
    ModuleFile: ModuleRef -> string
    Deviations: Deviation list
    GuestKit: GuestKitCitation option
    Runner: RunnerIdentity
    /// Receives one line for each case as it ends. The command writes it to stderr.
    Progress: string -> unit
}

/// Runs the suite against one host through its worker mode, and judges the cases.
module HostRunner =
    /// The lowercase hexadecimal SHA-256 of a file.
    let fileSha256 (path: string) =
        use stream = File.OpenRead path
        Convert.ToHexStringLower(SHA256.HashData stream)

    /// The SHA-256 of a file, None when it does not exist.
    let tryFileSha256 (path: string) =
        if File.Exists path then Some(fileSha256 path) else None

    /// The module file of a corpus module in a checkout: the module's path relative to the pinned corpus.
    let moduleFileIn (repositoryRoot: string) (pin: Pin) (moduleRef: ModuleRef) =
        let corpusDirectory =
            match pin.Corpus.LastIndexOf '/' with
            | -1 -> ""
            | index -> pin.Corpus.Substring(0, index)

        Path.GetFullPath(
            Path.Combine(
                Array.concat [
                    [| repositoryRoot |]
                    corpusDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries)
                    moduleRef.Wasm.Split('/', StringSplitOptions.RemoveEmptyEntries)
                ]
            )
        )

    /// This runner: Morphir.Conformance's version and the executable of the process.
    let runnerIdentity () =
        let assembly = typeof<HostCommand>.Assembly

        let version =
            match assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
            | null ->
                match assembly.GetName().Version with
                | null -> "0.0.0"
                | version -> version.ToString()
            | attribute -> attribute.InformationalVersion

        let executable =
            match Environment.ProcessPath with
            | null -> ""
            | path -> path

        {
            Name = "morphir-dotnet mck wasm-host"
            Version = version
            Executable = executable
            Sha256 =
                tryFileSha256 executable
                |> Option.defaultValue ""
        }

    let private deviationOf (deviations: Deviation list) (case: SuiteCase) =
        deviations
        |> List.tryFind (fun deviation -> deviation.CaseId = case.Case.CaseId)

    /// Runs every case of the suite against the host and judges it. A case that does not apply to the
    /// declared profiles is not run, and a case whose module differs from its digest is refused before any
    /// worker starts. Error means no case ran: the build identity could not be read.
    let run
        (options: RunnerOptions)
        (suite: WasmHostSuite)
        (cancellationToken: CancellationToken)
        : Task<Result<WasmHostReport, string>> =
        task {
            let! identity =
                Worker.buildIdentity options.Host options.Limits.HelpTimeout cancellationToken

            match identity with
            | Error message ->
                return Error $"The build identity of the host could not be read: {message}"
            | Ok identity ->
                let observed =
                    ResizeArray<SuiteCase * string * string option * RunObservation list>()

                for case in suite.Cases do
                    let modulePath = options.ModuleFile case.Case.Module
                    let sha = tryFileSha256 modulePath

                    let runnable =
                        Selection.applies options.Declared.Profiles case.Case
                        && sha = Some case.Case.Module.Sha256

                    let runs = ResizeArray<RunObservation>()

                    if runnable then
                        for run in case.Case.Runs do
                            let! observation =
                                Worker.run
                                    options.Host
                                    (Worker.arguments identity modulePath case.Case)
                                    run.Frames
                                    (RunLimits.runLimit options.Limits case.Case run)
                                    options.Limits.Margin
                                    cancellationToken

                            runs.Add observation

                    observed.Add(case, modulePath, sha, List.ofSeq runs)

                    let state = if not runnable then "not run" else $"{runs.Count} runs"

                    options.Progress $"{case.Id} {case.Case.CaseId}: {state}"

                let observations =
                    observed
                    |> Seq.collect (fun (_, _, _, runs) -> runs)
                    |> List.ofSeq

                let bounds = Judge.declaration observations

                let cases =
                    observed
                    |> Seq.map (fun (case, modulePath, sha, runs) ->
                        let outcome =
                            Judge.outcome options.Declared.Profiles bounds case.Case sha runs

                        let satisfied =
                            match outcome with
                            | CaseOutcome.Passed(_ :: _ as alternatives) ->
                                let shown =
                                    alternatives
                                    |> List.map (fun alternative ->
                                        $"run {alternative.Run} alternative {alternative.Alternative}"
                                    )

                                $""" ({String.Join(", ", shown)})"""
                            | _ -> ""

                        options.Progress
                            $"{case.Id} {case.Case.CaseId}: {outcome.WireValue}{satisfied}"

                        {
                            Case = case
                            ModulePath = modulePath
                            ObservedSha256 = sha
                            Runs = runs
                            Outcome = outcome
                            Deviation = deviationOf options.Deviations case
                        }
                    )
                    |> List.ofSeq

                return
                    Ok
                        {
                            Pin = suite.Pin
                            Kit = suite.Kit
                            Runner = options.Runner
                            Host = {
                                Executable = options.Host.Executable
                                Arguments = options.Host.Arguments
                                Sha256 =
                                    tryFileSha256 options.Host.Executable
                                    |> Option.defaultValue ""
                                BuildIdentity = identity
                                Declared = options.Declared
                            }
                            Limits = options.Limits
                            Bounds = bounds
                            GuestKitCitations = Option.toList options.GuestKit
                            Cases = cases
                        }
        }

    /// The exit status of a report: 0 when every applicable case passed, 1 otherwise. A case recorded as
    /// not_enforced for the memory ceiling or the call deadline, which qualification requires, is not a
    /// pass.
    let exitCode (report: WasmHostReport) =
        if
            report.Cases
            |> List.forall (fun case -> Judge.acceptable case.Outcome)
        then
            0
        else
            1
