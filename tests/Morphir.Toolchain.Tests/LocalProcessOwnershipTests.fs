// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Toolchain.Tests.LocalProcessOwnershipTests

open System
open System.Diagnostics
open System.IO
open System.Runtime.ExceptionServices
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.TestSupport
open Morphirup
open Morphir.Toolchain.IO

// Build copies the complete current-configuration fixture beside this test output.
let private fixturePath () =
    Path.Combine(AppContext.BaseDirectory, "ProcessFixture", "Morphir.Toolchain.ProcessFixture.dll")

let private settled (childProc: Process) = task {
    try
        do! childProc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 3.0)
        return true
    with :? TimeoutException ->
        return false
}

[<RequireQualifiedAccess>]
type private Ending =
    | RootExit
    | OwnerDeath
    | Cancelled
    | OutputFailure

type private RejectingOutput() =
    inherit MemoryStream()

    override _.Write(_, _, _) =
        raise (IOException "fixture output rejected")

    override _.WriteAsync(_: ReadOnlyMemory<byte>, _: CancellationToken) =
        ValueTask(Task.FromException(IOException "fixture output rejected"))

let private exercise ending = task {
    if not (OperatingSystem.IsWindows()) then
        skiptest "Windows job ownership control."

    use sandbox = TestSandbox.Create("morphir-launch-owner-")

    let dotnet =
        Environment.ProcessPath
        |> Option.ofObj
        |> Option.defaultWith (fun () -> failwith "No test host.")

    let fixture = fixturePath ()

    if not (File.Exists fixture) then
        failwith "Fresh childProc fixture must be built with this test project."

    let readyName =
        "Local\\morphir-ready-"
        + Guid.NewGuid().ToString("N")

    let releaseName =
        "Local\\morphir-release-"
        + Guid.NewGuid().ToString("N")

    use ready = new EventWaitHandle(false, EventResetMode.ManualReset, readyName)
    use release = new EventWaitHandle(false, EventResetMode.ManualReset, releaseName)
    let rootPid = sandbox.PathOf("root.pid")
    let childPid = sandbox.PathOf("descendant.pid")
    use timeout = new CancellationTokenSource(TimeSpan.FromSeconds 15.0)
    use input = new MemoryStream()

    use output: Stream =
        if ending = Ending.OutputFailure then
            new RejectingOutput() :> Stream
        else
            new MemoryStream() :> Stream

    use errors = new StringWriter()
    let mutable owner: Process option = None
    let mutable captures: Task<string> array = [||]
    let mutable ownedOperation: Task<int> option = None
    let handles = ResizeArray<Process>()

    let cleanup () = task {
        release.Set()
        |> ignore

        match owner with
        | Some childProc when not childProc.HasExited -> childProc.Kill(true)
        | _ -> ()

        for file in [ rootPid; childPid ] do
            if File.Exists file then
                let pid = Int32.Parse(File.ReadAllText file)

                if
                    not (
                        handles
                        |> Seq.exists (fun childProc -> childProc.Id = pid)
                    )
                then
                    try
                        handles.Add(Process.GetProcessById pid)
                    with :? ArgumentException ->
                        ()

        for childProc in handles do
            if not childProc.HasExited then
                childProc.Kill(true)

        for childProc in handles do
            do! childProc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 10.0)
            childProc.Dispose()

        match owner with
        | Some childProc ->
            do! childProc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 10.0)
            childProc.Dispose()
        | None -> ()

        match ownedOperation with
        | Some operation ->
            try
                let! _ = operation.WaitAsync(TimeSpan.FromSeconds 10.0)
                ()
            with
            | :? OperationCanceledException when timeout.IsCancellationRequested -> ()
            | :? IOException as error when
                ending = Ending.OutputFailure
                && error.Message = "fixture output rejected"
                ->
                ()
        | None -> ()

        if captures.Length > 0 then
            let! _ = Task.WhenAll(captures).WaitAsync(TimeSpan.FromSeconds 10.0)
            ()
    }

    let! outcome = task {
        try
            if ending = Ending.OwnerDeath then
                let start =
                    ChildProcess.StartInfo(
                        dotnet,
                        [|
                            "exec"
                            fixture
                            "owner"
                            dotnet
                            fixture
                            sandbox.MorphirHome
                            rootPid
                            childPid
                            readyName
                            releaseName
                        |]
                    )

                start.WorkingDirectory <- sandbox.Work
                start.UseShellExecute <- false
                start.CreateNoWindow <- true
                start.RedirectStandardOutput <- true
                start.RedirectStandardError <- true
                sandbox.Apply start

                let childProc =
                    Process.Start start
                    |> Option.ofObj
                    |> Option.defaultWith (fun () -> failwith "Owner fixture did not start.")

                owner <- Some childProc

                captures <- [|
                    childProc.StandardOutput.ReadToEndAsync()
                    childProc.StandardError.ReadToEndAsync()
                |]
            else
                let mode =
                    match ending with
                    | Ending.RootExit -> "exit"
                    | Ending.OutputFailure -> "output-failure"
                    | _ -> "hold"

                let operation =
                    LocalProcess.runAsync
                        dotnet
                        [
                            "exec"
                            fixture
                            "root"
                            mode
                            dotnet
                            fixture
                            rootPid
                            childPid
                            readyName
                            releaseName
                        ]
                        sandbox.Work
                        sandbox.MorphirHome
                        (LocalProcess.Input.Redirect input)
                        output
                        errors
                        timeout.Token

                ownedOperation <- Some operation

            let! reached = Task.Run(fun () -> ready.WaitOne(TimeSpan.FromSeconds 10.0))
            Expect.isTrue reached "actual descendant reaches its barrier"

            let pids =
                [ rootPid; childPid ]
                |> List.map (fun file -> Int32.Parse(File.ReadAllText file))

            Expect.equal
                (pids
                 |> List.distinct
                 |> List.length)
                2
                "both actual fixture identities were published"

            for pid in pids do
                try
                    handles.Add(Process.GetProcessById pid)
                with :? ArgumentException ->
                    () // An already terminated identity is settled.

            if ending = Ending.OwnerDeath then
                let childProc = owner.Value
                childProc.Kill(false) // Kill only the owner. Tree cleanup before the assertion would mask the defect.
                do! childProc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 5.0)
            elif ending = Ending.RootExit then
                let! exit = ownedOperation.Value.WaitAsync(TimeSpan.FromSeconds 10.0)
                Expect.equal exit 0 "actual root exits normally"
            elif ending = Ending.Cancelled then
                timeout.Cancel()

                try
                    let! _ = ownedOperation.Value.WaitAsync(TimeSpan.FromSeconds 10.0)
                    failtest "Actual caller cancellation must propagate."
                with :? OperationCanceledException as error ->
                    Expect.equal
                        error.CancellationToken
                        timeout.Token
                        "original token remains authoritative"
            else
                try
                    let! _ = ownedOperation.Value.WaitAsync(TimeSpan.FromSeconds 10.0)
                    failtest "The actual output writer failure must propagate."
                with :? IOException as error ->
                    Expect.equal
                        error.Message
                        "fixture output rejected"
                        "actual output cause is retained"

            let! results =
                handles
                |> Seq.map settled
                |> Seq.toArray
                |> Task.WhenAll

            Expect.isTrue
                (results
                 |> Array.forall id)
                "all owned descendants settle before normal return or after owner death"

            return None
        with error ->
            return Some error
    }

    do! cleanup ()

    match outcome with
    | Some error -> ExceptionDispatchInfo.Capture(error).Throw()
    | None -> ()
}

[<Tests>]
let tests =
    testList "morphirup launch ownership" [
        for cancellation in [ true; false ] do
            testCaseTask
                $"final disposal preserves the original failure cancellation={cancellation}"
                (fun () -> task {
                    use cancelled = new CancellationTokenSource()
                    cancelled.Cancel()

                    let primary: exn =
                        if cancellation then
                            OperationCanceledException(cancelled.Token)
                        else
                            IOException("actual output failure")

                    let cleanup = IOException("actual settlement failure")
                    let mutable disposals = 0

                    let owner =
                        { new IAsyncDisposable with
                            member _.DisposeAsync() =
                                disposals <-
                                    disposals
                                    + 1

                                ValueTask(Task.FromException(cleanup))
                        }

                    let! observed = task {
                        try
                            let! _ =
                                AsyncOwnership.runAsync
                                    owner
                                    (fun () -> Task.FromException<int>(primary))

                            return None
                        with error ->
                            return Some error
                    }

                    Expect.equal disposals 1 "the owner is disposed exactly once"

                    Expect.isTrue
                        (observed
                         |> Option.exists (fun error -> Object.ReferenceEquals(error, primary)))
                        "cleanup must not replace the actual operation cause"

                    Expect.isTrue
                        (Object.ReferenceEquals(
                            primary.Data["Morphir.Launch.DisposalFailure"],
                            cleanup
                        ))
                        "the actual settlement failure remains observable separately"
                })
        testCaseTask
            "normal root completion settles a descendant with no inherited output handles"
            (fun () -> exercise Ending.RootExit)
        testCaseTask
            "killing only the launcher owner also terminates its process tree"
            (fun () -> exercise Ending.OwnerDeath)
        testCaseTask
            "caller cancellation settles descendants and preserves its original token"
            (fun () -> exercise Ending.Cancelled)
        testCaseTask
            "an output failure settles descendants and preserves the actual cause"
            (fun () -> exercise Ending.OutputFailure)
        testCaseTask
            "the native launch preserves empty quoted Unicode and trailing-backslash arguments"
            (fun () -> task {
                if not (OperatingSystem.IsWindows()) then
                    skiptest "Windows argument control."

                use sandbox = TestSandbox.Create("morphir-launch-argv-")

                let directory =
                    Directory
                        .CreateDirectory(sandbox.PathOf("working directory with spaces"))
                        .FullName

                let destination = Path.Combine(directory, "actual arguments.json")

                let expected = [|
                    ""
                    "with spaces"
                    "embedded\"quote"
                    "ends\\"
                    "space \\"
                    "a\\\"b"
                    "中文"
                    "& not a shell"
                |]

                let dotnet =
                    Environment.ProcessPath
                    |> Option.ofObj
                    |> Option.defaultWith (fun () -> failwith "No test host.")

                use input = new MemoryStream()
                use output = new MemoryStream()
                use errors = new StringWriter()
                use timeout = new CancellationTokenSource(TimeSpan.FromSeconds 15.0)

                let! code =
                    LocalProcess.runAsync
                        dotnet
                        ([ "exec"; fixturePath (); "echo"; destination ]
                         @ List.ofArray expected)
                        directory
                        sandbox.MorphirHome
                        (LocalProcess.Input.Redirect input)
                        output
                        errors
                        timeout.Token

                Expect.equal code 0 "actual argument fixture completes"
                use actual = System.Text.Json.JsonDocument.Parse(File.ReadAllText destination)

                let values =
                    actual.RootElement.EnumerateArray()
                    |> Seq.map (fun item ->
                        item.GetString()
                        |> Option.ofObj
                        |> Option.defaultValue "<null>"
                    )
                    |> Seq.toArray

                Expect.equal
                    values
                    expected
                    "the actual native command line round-trips each argument"
            })
        testCaseTask
            "inherited standard input reaches the contained child unchanged"
            (fun () -> task {
                if not (OperatingSystem.IsWindows()) then
                    skiptest "Windows inherited input control."

                use sandbox = TestSandbox.Create("morphir-launch-stdin-")

                let dotnet =
                    Environment.ProcessPath
                    |> Option.ofObj
                    |> Option.defaultWith (fun () -> failwith "No test host.")

                let destination = sandbox.PathOf("stdin.bin")

                let start =
                    ChildProcess.StartInfo(
                        dotnet,
                        [|
                            "exec"
                            fixturePath ()
                            "owner-input"
                            dotnet
                            fixturePath ()
                            sandbox.MorphirHome
                            destination
                        |]
                    )

                start.WorkingDirectory <- sandbox.Work
                start.StandardInputEncoding <- new System.Text.UTF8Encoding(false)
                sandbox.Apply start
                let payload = "actual input with Unicode 中文 and a newline\n"

                let! result =
                    ChildProcess.RunAsync(
                        start,
                        TimeSpan.FromSeconds 20.0,
                        standardInput = payload
                    )

                Expect.equal result.ExitCode 0 "actual stdin owner and child settle"

                Expect.equal
                    (File.ReadAllBytes destination)
                    (System.Text.Encoding.UTF8.GetBytes payload)
                    "actual inherited bytes"
            })
        testCaseTask
            "a failed native start rolls back without starting a payload"
            (fun () -> task {
                if not (OperatingSystem.IsWindows()) then
                    skiptest "Windows failed-start control."

                use sandbox = TestSandbox.Create("morphir-launch-missing-")
                use input = new MemoryStream()
                use output = new MemoryStream()
                use errors = new StringWriter()

                try
                    let! _ =
                        LocalProcess.runAsync
                            (sandbox.PathOf("absent.exe"))
                            []
                            sandbox.Work
                            sandbox.MorphirHome
                            (LocalProcess.Input.Redirect input)
                            output
                            errors
                            CancellationToken.None

                    failtest "A missing executable cannot succeed."
                with :? System.ComponentModel.Win32Exception as error ->
                    Expect.equal error.NativeErrorCode 2 "actual file-not-found cause"
            })
    ]
