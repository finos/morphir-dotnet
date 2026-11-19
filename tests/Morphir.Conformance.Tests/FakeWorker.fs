// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The scripted fake worker (tests/Morphir.Conformance.FakeWorker), run with `dotnet exec`, and cases of the
/// committed suite to drive through it.
module Morphir.Conformance.Tests.FakeWorker

open System
open System.Diagnostics
open System.IO
open System.Text.Json.Nodes
open System.Threading.Tasks
open Expecto
open Morphir.Conformance.WasmHost
open Morphir.Conformance.Tests.Repository
open Morphir.TestSupport

/// The fake's assembly, copied beside the tests by the project reference.
let assembly =
    let path =
        Path.Combine(AppContext.BaseDirectory, "Morphir.Conformance.FakeWorker.dll")

    if not (File.Exists path) then
        failwith $"The fake worker is not built at {path}"

    path

/// The dotnet host that runs the tests.
let dotnet =
    match Environment.ProcessPath with
    | null -> "dotnet"
    | path when
        Path
            .GetFileNameWithoutExtension(path)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        ->
        path
    | _ -> "dotnet"

/// A build identity for the fake.
[<Literal>]
let Identity = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"

/// An action of the fake's script.
let action (name: string) (value: JsonNode) =
    let node = JsonObject()
    node[name] <- value
    node :> JsonNode

let stderr (line: string) = action "stderr" (JsonValue.Create line)

let event (members: (string * JsonNode) list) =
    let node = JsonObject()

    for name, value in members do
        node[name] <- value

    action "event" node

let text (value: string) : JsonNode = JsonValue.Create value
let number (value: int) : JsonNode = JsonValue.Create value

let started (notEnforced: string list) =
    let node = JsonObject()

    for bound in notEnforced do
        node[bound] <- JsonValue.Create false

    action "started" node

let refused (code: string) (message: string) =
    let error = JsonObject()
    error["code"] <- text code
    error["message"] <- text message
    event [ "event", text "refused"; "error", error ]

let fault (kind: string) =
    let detail = JsonObject()
    detail["kind"] <- text kind
    event [ "event", text "fault"; "fault", detail ]

let output (value: string) =
    event [ "event", text "output"; "output", text value ]

let exitWith (code: int) = action "exit" (number code)
let echo (count: int) = action "echo" (number count)
let read (count: int) = action "read" (number count)
let sleepMs (count: int) = action "sleepMs" (number count)
let hang = action "hang" (JsonValue.Create true)
let rawStdout (value: string) = action "stdout" (text value)
let spawn (pidFile: string) = action "spawn" (text pidFile)
/// A child that keeps the fake's stdin and stdout open for this long after the fake exits.
let holdStdio (milliseconds: int) = action "holdStdio" (number milliseconds)

/// A fake host in a sandbox: the script, the invocation log and the host command that runs it.
type FakeHost = {
    Sandbox: TestSandbox
    Script: string
    Log: string
    Command: HostCommand
}

/// Writes a script whose modules map module names (the file name without extension) to their actions.
let host (modules: (string * JsonNode list) list) =
    let sandbox = TestSandbox.Create "morphir-wasm-worker-"
    let script = sandbox.PathOf "script.json"
    let log = sandbox.PathOf "invocations.log"
    let root = JsonObject()
    root["buildIdentity"] <- text Identity
    let table = JsonObject()

    for name, actions in modules do
        table[name] <-
            JsonArray(
                actions
                |> List.map (fun node -> node.DeepClone())
                |> List.toArray
            )

    root["modules"] <- table
    File.WriteAllText(script, root.ToJsonString())

    {
        Sandbox = sandbox
        Script = script
        Log = log
        Command = {
            Executable = dotnet
            Arguments = [ "exec"; assembly; "--script"; script; "--log"; log ]
        }
    }

/// The command lines the fake was started with, after its own options.
let invocations (fake: FakeHost) =
    if File.Exists fake.Log then
        File.ReadAllLines fake.Log
        |> Array.filter (fun line -> line <> "")
        |> Array.map (fun line ->
            JsonNode.Parse(line).AsArray()
            |> Seq.map (fun item -> item.GetValue<string>())
            |> List.ofSeq
        )
        |> List.ofArray
    else
        []

/// The committed suite.
let suite =
    lazy
        (FeatureReader.read (corpus.Force()) (committed ())
         |> requireOk "The committed suite")

/// A case of the committed suite by its upstream id.
let suiteCase (caseId: string) =
    suite.Force().Cases
    |> List.find (fun case -> case.Case.CaseId = caseId)

/// The suite with only these cases.
let suiteOf (caseIds: string list) =
    let whole = suite.Force()

    { whole with
        Cases =
            whole.Cases
            |> List.filter (fun case -> List.contains case.Case.CaseId caseIds)
    }

/// Short limits for tests: a 2-second call deadline and a 1-second margin.
let shortLimits = {
    ExtismCallDeadline = TimeSpan.FromSeconds 2.0
    BytesCallDeadline = TimeSpan.FromSeconds 2.0
    Margin = TimeSpan.FromSeconds 1.0
    HelpTimeout = TimeSpan.FromSeconds 60.0
}

/// The module file of a corpus module in this checkout.
let moduleFile (moduleRef: ModuleRef) =
    HostRunner.moduleFileIn root (suite.Force().Pin) moduleRef

/// A host declaration.
let declared (profiles: Profile list) = {
    Profiles = profiles
    Offering = Some "test"
    Platform = Some "test-platform"
    Engine = Some "fake"
}

/// The options of a run against a fake host.
let options (fake: FakeHost) (profiles: Profile list) = {
    Host = fake.Command
    Declared = declared profiles
    Limits = shortLimits
    ModuleFile = moduleFile
    Deviations = []
    GuestKit = None
    Runner = HostRunner.runnerIdentity ()
    Progress = ignore
}

/// Runs a suite against a fake host, holding the shared process gate.
let runSuite (options: RunnerOptions) (suite: WasmHostSuite) =
    task {
        use! _gate = ProcessGate.Shared.EnterAsync()
        let! result = HostRunner.run options suite Threading.CancellationToken.None

        match result with
        | Ok report -> return report
        | Error message -> return failtest message
    }
    |> fun running -> running.GetAwaiter().GetResult()

/// The outcome of the one case of a report.
let onlyOutcome (report: WasmHostReport) =
    match report.Cases with
    | [ case ] -> case.Outcome
    | cases -> failtest $"Expected one case, got {cases.Length}"

/// The matchers that failed, in order.
let failedMatchers (outcome: CaseOutcome) =
    match outcome with
    | CaseOutcome.Failed failures ->
        failures
        |> List.map (fun failure -> failure.Matcher)
    | other -> failtest $"Expected a failed case, got {other.WireValue}: {other}"

/// Whether a process id names a running process.
let isRunning (pid: int) =
    try
        use proc = Process.GetProcessById pid
        not proc.HasExited
    with :? ArgumentException ->
        false

/// Waits a little for a condition that a stopped process tree makes true.
let eventually (condition: unit -> bool) =
    let deadline = DateTime.UtcNow.AddSeconds 10.0

    let rec wait () =
        if condition () then
            true
        elif DateTime.UtcNow > deadline then
            false
        else
            Task.Delay(100).Wait()
            wait ()

    wait ()
