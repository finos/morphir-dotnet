// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Conformance.Tests.CommandTests

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open Expecto
open Morphir.Conformance
open Morphir.Conformance.WasmHost
open Morphir.Conformance.Tests.Repository
open Morphir.Conformance.Tests.FakeWorker
open Morphir.TestSupport

/// A sandbox checkout whose corpus holds only these cases, with their modules, and the suite generated
/// from it.
let private kitOf (caseIds: string list) =
    let sandbox =
        sandboxWith (fun corpus ->
            let cases = corpus["cases"].AsArray()

            for case in List.ofSeq cases do
                if not (List.contains (case["caseId"].GetValue<string>()) caseIds) then
                    cases.Remove case
                    |> ignore
        )

    for caseId in caseIds do
        let moduleRef = (suiteCase caseId).Case.Module
        let target = HostRunner.moduleFileIn sandbox.Root (suite.Force().Pin) moduleRef

        Directory.CreateDirectory(Path.GetDirectoryName target)
        |> ignore

        File.Copy(moduleFile moduleRef, target, true)

    Generation.writeRepository sandbox.Root
    |> requireOk "The reduced suite"
    |> ignore

    sandbox, sandbox.PathOf(Generation.SuiteDirectory.Split '/')

let private run (kit: string) (fake: FakeHost) (report: string) (profiles: string list) =
    use gate = (ProcessGate.Shared.EnterAsync()).GetAwaiter().GetResult()

    (WasmHostCommands.RunAsync(
        kit,
        dotnet,
        (fake.Command.Arguments
         |> List.toArray),
        (profiles
         |> List.toArray),
        "test",
        "test-platform",
        "fake",
        report,
        null,
        null,
        null,
        null,
        Action<string>(ignore),
        CancellationToken.None
    ))
        .GetAwaiter()
        .GetResult()

let private document (result: WasmHostCommandResult) =
    try
        JsonNode.Parse(result.Output).AsObject()
    with :? JsonException as error ->
        failtest $"stdout is not one JSON document: {error.Message}\n{result.Output}"

[<Tests>]
let tests =
    // PlatformSpecific: these tests use the real file system, the checkout or worker processes, so the platform
    // scope of the Test target runs them on every operating system (docs/contributing/ci-os-matrix.md).
    testLabel "PlatformSpecific"
    <| testList "WASM host commands: exit codes and output (task 1.5)" [
        for otherExecutable in [ false; true ] do
            testCase (if otherExecutable then "citation admission is unavailable even for another executable" else "well-shaped citation metadata cannot publish without the generic checker") (fun () ->
                let sandbox, kit = kitOf [ "extism/echo" ]
                use _ = sandbox
                let fake = host [ "extism-echo", [ started []; echo 1; read 1; exitWith 0 ] ]
                use _ = fake.Sandbox
                let citationPath = sandbox.PathOf "guest-report.json"
                // A pass-shaped JSON document and valid digest strings are not checked guest proof.
                File.WriteAllText(citationPath, "{\"verdict\":{\"qualified\":true,\"reasons\":[]}}")
                let outputPath = sandbox.PathOf("out", "must-not-exist.json")
                let hostSha = if otherExecutable then String.replicate 64 "2" else HostRunner.fileSha256 dotnet
                let pending = WasmHostCommands.RunAsync(
                    kit, dotnet, List.toArray fake.Command.Arguments, [| "extism-kernel-v1" |],
                    "test", "test-platform", "fake", outputPath, null,
                    citationPath, hostSha, String.replicate 64 "4", Action<string>(ignore), CancellationToken.None)
                let result = pending.GetAwaiter().GetResult()
                Expect.equal result.ExitCode 1 "operational failure, not a verdict or invalid guest report"
                let output = document result
                Expect.equal (output["error"].["code"].GetValue<string>()) "mck.guest_kit_check_unavailable" "primary cause retained"
                Expect.stringContains (output["error"].["message"].GetValue<string>()) "checker is unavailable" "dependency explained"
                Expect.isNull output["verdict"] "no fabricated normative reason"
                Expect.isFalse (File.Exists outputPath) "no report published"
                Expect.isEmpty (invocations fake) "no worker admitted")

        test "run exits 0 when every applicable case passed, and writes the report" {
            let sandbox, kit = kitOf [ "classification/unknown-namespace" ]
            use _ = sandbox

            let fake =
                host [
                    "env-open-socket",
                    [ refused "extension.abi_unsupported" "env.open_socket"; exitWith 3 ]
                ]

            use _ = fake.Sandbox
            let report = sandbox.PathOf("out", "wasm-host.json")
            let result = run kit fake report []
            Expect.equal result.ExitCode 0 (String.Join("\n", result.Diagnostics))
            let output = document result
            Expect.isTrue (output["experimental"].GetValue<bool>()) "experimental"
            Expect.equal (output["totals"].["passed"].GetValue<int>()) 1 "one passed"
            Expect.isTrue (File.Exists report) "the report"
            let recorded = JsonNode.Parse(File.ReadAllText report)
            Expect.equal
                (recorded["host"].["profiles"].AsArray() |> Seq.map (fun item -> item.GetValue<string>()) |> Seq.toList)
                [ "extism-kernel-v1" ]
                "the command's default profile is explicit in the report"

            let checkResult = WasmHostCommands.CheckReport(kit, report, null, null)
            Expect.equal checkResult.ExitCode 0 (String.Join("\n", checkResult.Diagnostics))
            Expect.isTrue ((document checkResult).["valid"].GetValue<bool>()) "valid"
        }

        test "run exits 1 when a case failed, and report-check agrees" {
            let sandbox, kit = kitOf [ "classification/unknown-namespace"; "extism/echo" ]
            use _ = sandbox

            let fake =
                host [
                    "env-open-socket",
                    [ refused "extension.abi_unsupported" "env.open_socket"; exitWith 3 ]
                    "extism-echo", [ started []; read 2; exitWith 0 ]
                ]

            use _ = fake.Sandbox
            let report = sandbox.PathOf "wasm-host.json"
            let result = run kit fake report []
            Expect.equal result.ExitCode 1 "a failed case"

            Expect.exists
                result.Diagnostics
                (fun line -> line.Contains "extism/echo: failed")
                "the failure on stderr"

            Expect.equal
                (WasmHostCommands.CheckReport(kit, report, null, null).ExitCode)
                1
                "report-check recomputes the failure"
        }

        test "run exits 1 when the host cannot give its build identity" {
            let sandbox, kit = kitOf [ "classification/unknown-namespace" ]
            use _ = sandbox
            let fake = host []
            use _ = fake.Sandbox

            let broken =
                { fake with
                    Command =
                        { fake.Command with
                            Arguments = [
                                "exec"
                                assembly
                                "--script"
                                fake.Sandbox.PathOf "missing.json"
                            ]
                        }
                }

            let result = run kit broken (sandbox.PathOf "r.json") []
            Expect.equal result.ExitCode 1 "an operational error"

            Expect.equal
                ((document result).["error"].["code"].GetValue<string>())
                "mck.host_unavailable"
                "the code"
        }

        test "run exits 2 for an invalid invocation" {
            let fake = host []
            use _ = fake.Sandbox
            let kit = Path.Combine(root, Generation.SuiteDirectory)
            let report = fake.Sandbox.PathOf "r.json"
            Expect.equal ((run null fake report []).ExitCode) 2 "no kit"

            Expect.equal
                ((run (fake.Sandbox.PathOf "nowhere") fake report []).ExitCode)
                2
                "a kit that is not a directory"

            Expect.equal ((run kit fake report [ "wasm-gc-v9" ]).ExitCode) 2 "an unknown profile"
            Expect.equal ((run kit fake report [ "morphir-bytes-v1" ]).ExitCode) 2 "required profile omitted"
            Expect.equal ((run kit fake report [ "extism-kernel-v1"; "extism-kernel-v1" ]).ExitCode) 2 "duplicate declaration"
            Expect.equal ((run kit fake null []).ExitCode) 2 "no report"

            let missingHost =
                WasmHostCommands
                    .RunAsync(
                        kit,
                        fake.Sandbox.PathOf "no-host",
                        [||],
                        [||],
                        "test",
                        "test-platform",
                        "fake",
                        report,
                        null,
                        null,
                        null,
                        null,
                        Action<string>(ignore),
                        CancellationToken.None
                    )
                    .GetAwaiter()
                    .GetResult()

            Expect.equal missingHost.ExitCode 2 "a host that is not a file"

            let partialCitation =
                WasmHostCommands
                    .RunAsync(
                        kit,
                        dotnet,
                        [||],
                        [||],
                        "test",
                        "test-platform",
                        "fake",
                        report,
                        null,
                        report,
                        null,
                        null,
                        Action<string>(ignore),
                        CancellationToken.None
                    )
                    .GetAwaiter()
                    .GetResult()

            Expect.equal partialCitation.ExitCode 2 "a guest kit citation without its digests"
            Expect.isEmpty (invocations fake) "no worker started"
        }

        // Spec "Report identity"; corpus convention identity at 48d10706f.
        test
            "run without the host's offering, platform or engine is an invalid invocation before any case" {
            let fake = host [ "*", [ exitWith 3 ] ]
            use _ = fake.Sandbox
            let kit = Path.Combine(root, Generation.SuiteDirectory)

            let runWithout (offering: string) (platform: string) (engine: string) =
                WasmHostCommands
                    .RunAsync(
                        kit,
                        dotnet,
                        (fake.Command.Arguments
                         |> List.toArray),
                        [||],
                        offering,
                        platform,
                        engine,
                        fake.Sandbox.PathOf "r.json",
                        null,
                        null,
                        null,
                        null,
                        Action<string>(ignore),
                        CancellationToken.None
                    )
                    .GetAwaiter()
                    .GetResult()

            for name, result in
                [
                    "--offering", runWithout null "test-platform" "fake"
                    "--platform", runWithout "test" null "fake"
                    "--engine", runWithout "test" "test-platform" " "
                ] do
                Expect.equal result.ExitCode 2 $"no {name}"
                let error = (document result).["error"]
                Expect.equal (error["code"].GetValue<string>()) "mck.usage" $"no {name}: the code"

                Expect.stringContains
                    (error["message"].GetValue<string>())
                    name
                    $"no {name}: the option is named"

                Expect.stringContains
                    (error["message"].GetValue<string>())
                    "a worker reports none of them"
                    $"no {name}: the convention's reason"

            Expect.isEmpty (invocations fake) "no worker started, not even --help"
            Expect.isFalse (File.Exists(fake.Sandbox.PathOf "r.json")) "no report"
        }

        // Spec "Module digests are checked before a case runs": Corpus version the runner does not know.
        test
            "A kit of another version stops the run with an invalid-invocation result before any case" {
            use sandbox = sandboxWith (fun corpus -> corpus["version"] <- "0.2.0-draft.1")
            let fake = host [ "*", [ exitWith 3 ] ]
            use _ = fake.Sandbox

            let result =
                run
                    (sandbox.PathOf(Generation.SuiteDirectory.Split '/'))
                    fake
                    (sandbox.PathOf "r.json")
                    []

            Expect.equal result.ExitCode 2 "invalid invocation"

            Expect.equal
                ((document result).["error"].["code"].GetValue<string>())
                "mck.kit_version"
                "the kit version"

            Expect.isEmpty (invocations fake) "no case ran"

            Expect.equal
                (WasmHostCommands
                    .CheckReport(
                        sandbox.PathOf(Generation.SuiteDirectory.Split '/'),
                        sandbox.PathOf "r.json",
                        null,
                        null
                    )
                    .ExitCode)
                2
                "report-check too"
        }

        test
            "report-check exits 2 for an invalid invocation and 1 for a report that is not the kit's" {
            let kit = Path.Combine(root, Generation.SuiteDirectory)

            Expect.equal
                (WasmHostCommands.CheckReport(kit, null, null, null).ExitCode)
                2
                "no report"

            Expect.equal
                (WasmHostCommands.CheckReport(null, "r.json", null, null).ExitCode)
                2
                "no kit"

            Expect.equal
                (WasmHostCommands
                    .CheckReport(kit, Path.Combine(root, "no-such-report.json"), null, null)
                    .ExitCode)
                2
                "a missing report"

            use sandbox = TestSandbox.Create "morphir-wasm-report-"
            let report = sandbox.PathOf "r.json"
            File.WriteAllText(report, "{\"report\":\"something else\"}")
            let result = WasmHostCommands.CheckReport(kit, report, null, null)
            Expect.equal result.ExitCode 1 "an invalid report"
            Expect.isFalse ((document result).["valid"].GetValue<bool>()) "not valid"
        }

        // Spec "Hosts are driven through a stdio worker": Two hosts, one command line.
        test "Two hosts run through the same code path and arguments, only the host differs" {
            let first =
                host [
                    "env-open-socket",
                    [ refused "extension.abi_unsupported" "env.open_socket"; exitWith 3 ]
                ]

            use _ = first.Sandbox

            let second =
                host [
                    "env-open-socket",
                    [
                        refused "extension.abi_unsupported" "env.open_socket (other host)"
                        exitWith 3
                    ]
                ]

            use _ = second.Sandbox
            let subset = suiteOf [ "classification/unknown-namespace" ]
            let one = runSuite (options first [ Profile.ExtismKernelV1 ]) subset
            let other = runSuite (options second [ Profile.ExtismKernelV1 ]) subset

            let workerCalls fake =
                invocations fake
                |> List.filter (fun call -> not (List.contains "--help" call))

            Expect.equal (workerCalls first) (workerCalls second) "the same worker arguments"
            Expect.equal (onlyOutcome one) (onlyOutcome other) "the same outcome"
        }
    ]
