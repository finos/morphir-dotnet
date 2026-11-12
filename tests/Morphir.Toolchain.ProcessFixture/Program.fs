// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Toolchain.ProcessFixture.Program

open System
open System.ComponentModel
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open System.Text
open System.Threading
open Morphirup

[<Struct; StructLayout(LayoutKind.Sequential)>]
type StartupInfo =
    val mutable Size: int
    val mutable Reserved: nativeint
    val mutable Desktop: nativeint
    val mutable Title: nativeint
    val mutable X: int
    val mutable Y: int
    val mutable XSize: int
    val mutable YSize: int
    val mutable XChars: int
    val mutable YChars: int
    val mutable Fill: int
    val mutable Flags: int
    val mutable Show: int16
    val mutable ReservedBytes: int16
    val mutable ReservedPointer: nativeint
    val mutable Input: nativeint
    val mutable Output: nativeint
    val mutable Error: nativeint

[<Struct; StructLayout(LayoutKind.Sequential)>]
type ProcessInfo =
    val mutable Process: nativeint
    val mutable Thread: nativeint
    val mutable Pid: int
    val mutable Tid: int

[<DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)>]
extern bool CreateProcessW(
    string application,
    StringBuilder command,
    nativeint processSecurity,
    nativeint threadSecurity,
    bool inheritHandles,
    uint32 flags,
    nativeint environment,
    string directory,
    StartupInfo& startup,
    ProcessInfo& result
)

[<DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)>]
extern bool CloseHandle(nativeint handle)

let private startWithoutInheritedHandles dotnet fixture pidFile ready release =
    // Test-only launch: every generated argument is a local path/name without quotes.
    // bInheritHandles=false ensures this descendant cannot retain the owner's output writers.
    let args = [ dotnet; "exec"; fixture; "quiet"; pidFile; ready; release ]

    if
        args
        |> List.exists (fun (arg: string) ->
            arg.Contains('"')
            || arg.EndsWith('\\')
        )
    then
        invalidArg "args" "The fixture accepts only its generated path/name arguments."

    let command =
        StringBuilder(
            args
            |> List.map (fun arg ->
                "\""
                + arg
                + "\""
            )
            |> String.concat " "
        )

    let mutable startup = Unchecked.defaultof<StartupInfo>
    startup.Size <- Marshal.SizeOf<StartupInfo>()
    let mutable result = Unchecked.defaultof<ProcessInfo>

    if
        not (
            CreateProcessW(
                dotnet,
                command,
                0n,
                0n,
                false,
                0x08000400u,
                0n,
                Environment.CurrentDirectory,
                &startup,
                &result
            )
        )
    then
        raise (Win32Exception(Marshal.GetLastPInvokeError()))
    // Only successful out-values are owned. Closing these handles does not terminate the process.
    if not (CloseHandle result.Thread) then
        raise (Win32Exception(Marshal.GetLastPInvokeError()))

    if not (CloseHandle result.Process) then
        raise (Win32Exception(Marshal.GetLastPInvokeError()))

[<EntryPoint>]
let main args =
    match args with
    | [| "quiet"; pidFile; readyName; releaseName |] ->
        use ready = EventWaitHandle.OpenExisting readyName
        use release = EventWaitHandle.OpenExisting releaseName
        File.WriteAllText(pidFile, string Environment.ProcessId)

        ready.Set()
        |> ignore
        // A final fixture expiry prevents an interrupted test from leaving an indefinite process.
        release.WaitOne(TimeSpan.FromSeconds 25.0)
        |> ignore

        0
    | [| "root"; mode; dotnet; fixture; rootPid; childPid; readyName; releaseName |] ->
        File.WriteAllText(rootPid, string Environment.ProcessId)
        use ready = EventWaitHandle.OpenExisting readyName
        use release = EventWaitHandle.OpenExisting releaseName
        startWithoutInheritedHandles dotnet fixture childPid readyName releaseName

        if not (ready.WaitOne(TimeSpan.FromSeconds 10.0)) then
            failwith "Descendant did not reach its barrier."

        if mode = "output-failure" then
            Console.Out.Write "fixture output"
            Console.Out.Flush()

            release.WaitOne(TimeSpan.FromSeconds 20.0)
            |> ignore
        elif mode = "hold" then
            release.WaitOne(TimeSpan.FromSeconds 20.0)
            |> ignore
        elif
            mode
            <> "exit"
        then
            invalidArg "mode" "Unknown fixture mode."

        0
    | [| "owner"; dotnet; fixture; home; rootPid; childPid; readyName; releaseName |] ->
        use input = new MemoryStream()
        use output = Console.OpenStandardOutput()
        use errors = new StreamWriter(Console.OpenStandardError(), AutoFlush = true)
        use deadline = new CancellationTokenSource(TimeSpan.FromSeconds 20.0)

        LocalProcess.runAsync
            dotnet
            [
                "exec"
                fixture
                "root"
                "hold"
                dotnet
                fixture
                rootPid
                childPid
                readyName
                releaseName
            ]
            Environment.CurrentDirectory
            home
            (LocalProcess.Input.Redirect input)
            output
            errors
            deadline.Token
        |> fun operation -> operation.GetAwaiter().GetResult()
    | [| "owner-input"; dotnet; fixture; home; destination |] ->
        use output = Console.OpenStandardOutput()
        use errors = new StreamWriter(Console.OpenStandardError(), AutoFlush = true)
        use deadline = new CancellationTokenSource(TimeSpan.FromSeconds 15.0)

        LocalProcess.runAsync
            dotnet
            [ "exec"; fixture; "read-input"; destination ]
            Environment.CurrentDirectory
            home
            LocalProcess.Input.Inherit
            output
            errors
            deadline.Token
        |> fun operation -> operation.GetAwaiter().GetResult()
    | [| "read-input"; destination |] ->
        use source = Console.OpenStandardInput()
        use output = File.Create destination
        source.CopyTo output
        0
    | values when
        values.Length
        >= 2
        && values[0] = "echo"
        ->
        File.WriteAllText(
            values[1],
            System.Text.Json.JsonSerializer.Serialize<string array>(values[2..])
        )

        0
    | _ -> invalidArg "args" "Unknown test fixture invocation."
