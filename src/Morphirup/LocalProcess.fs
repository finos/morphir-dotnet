// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module internal Morphirup.LocalProcess

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open Morphir.Toolchain.IO

[<RequireQualifiedAccess>]
type Input =
    | Inherit
    | Redirect of Stream

/// The environment of every owned child: the Home, the isolation and discovery boundary, no global .NET lookup, and
/// none of the .NET variables that could load another runtime, store, dependency or startup hook.
let isolate (environment: System.Collections.Generic.IDictionary<string, string | null>) (home: string) (directory: string) (overlay: string option) =
    environment["MORPHIR_HOME"] <- home
    environment["MORPHIR_ISOLATED"] <- "1"
    environment["MORPHIR_DISCOVERY_BOUNDARY"] <- directory
    environment["DOTNET_MULTILEVEL_LOOKUP"] <- "0"
    match overlay with
    | Some overlay -> environment["MORPHIR_EXTENSION_OVERLAY"] <- overlay
    | None -> ()

    for name in
        [
            "DOTNET_ROOT"
            "DOTNET_ROOT_X64"
            "DOTNET_ROOT_ARM64"
            "DOTNET_ROOT(x86)"
            "DOTNET_SHARED_STORE"
            "DOTNET_ADDITIONAL_DEPS"
            "DOTNET_STARTUP_HOOKS"
        ] do
        environment.Remove name
        |> ignore

/// An owned child: on Windows in a kill-on-close job, elsewhere a process whose tree the manager terminates on
/// cancellation or failure. Off Windows there is no process group: a descendant that outlives a normal exit of the
/// host, and the whole tree when the manager itself dies, are not terminated.
type private OwnedChild =
    | Windows of WindowsChild
    | Unix of Process * Task<int>

    member this.StandardInput: Stream option =
        match this with
        | Windows child -> child.StandardInput
        | Unix(child, _) -> if child.StartInfo.RedirectStandardInput then Some child.StandardInput.BaseStream else None

    member this.StandardOutput: Stream =
        match this with
        | Windows child -> child.StandardOutput
        | Unix(child, _) -> child.StandardOutput.BaseStream

    member this.StandardError: Stream =
        match this with
        | Windows child -> child.StandardError
        | Unix(child, _) -> child.StandardError.BaseStream

    member this.WaitForExitAsync() : Task<int> =
        match this with
        | Windows child -> child.WaitForExitAsync()
        | Unix(_, exited) -> exited

    member this.RequestTermination() =
        match this with
        | Windows child -> child.RequestTermination()
        | Unix(child, _) ->
            // This runs inside a cancellation callback too, so no failure may escape. The process has exited
            // (InvalidOperationException), a member of its tree is gone or not ours (Win32Exception), or the tree walk
            // failed for some members (AggregateException). The exit wait still settles the root.
            try
                child.Kill true
            with
            | :? InvalidOperationException
            | :? System.ComponentModel.Win32Exception
            | :? AggregateException
            | :? NotSupportedException -> ()

    member this.SettleAsync() : Task =
        match this with
        | Windows child -> child.SettleAsync()
        | Unix(_, exited) -> exited :> Task

    static member StartAsync(start: ProcessStartInfo, ct: CancellationToken) = task {
        if OperatingSystem.IsWindows() then
            let! child = WindowsChild.StartAsync(start, ct)
            return Windows child
        else
            ct.ThrowIfCancellationRequested()
            if start.UseShellExecute || not start.RedirectStandardOutput || not start.RedirectStandardError then
                invalidArg "start" "A shell-free captured launch is required."
            if not (Path.IsPathFullyQualified start.FileName) then
                invalidArg "start" "An absolute executable is required."
            match Process.Start start with
            | null -> return raise (IOException "The owned child did not start.")
            | child ->
                let exited = task {
                    do! child.WaitForExitAsync()
                    return child.ExitCode
                }
                return Unix(child, exited)
    }

    interface IAsyncDisposable with
        member this.DisposeAsync() =
            match this with
            | Windows child -> (child :> IAsyncDisposable).DisposeAsync()
            | Unix(child, exited) ->
                ValueTask(task {
                    try
                        if not exited.IsCompleted then
                            this.RequestTermination()
                        let! _ = exited
                        ()
                    finally
                        child.Dispose()
                })

/// The one product launcher for probes and installed runs. Never searches PATH or evaluates a shell command.
let private runCoreAsync
    (program: string)
    (args: string list)
    directory
    home
    input
    (output: Stream)
    (errors: TextWriter)
    (overlay: string option)
    (ct: CancellationToken)
    =
    task {
        if not (Path.IsPathFullyQualified program) then
            invalidArg (nameof program) "An absolute owned program is required."

        let start = ProcessStartInfo(program)
        start.WorkingDirectory <- directory
        start.UseShellExecute <- false
        start.CreateNoWindow <- true

        start.RedirectStandardInput <-
            match input with
            | Input.Inherit -> false
            | Input.Redirect _ -> true

        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true

        for arg in args do
            start.ArgumentList.Add arg

        isolate start.Environment home directory overlay

        ct.ThrowIfCancellationRequested()
        let! child = OwnedChild.StartAsync(start, ct)

        return!
            AsyncOwnership.runAsync
                (child :> IAsyncDisposable)
                (fun () -> task {
                    use errorReader =
                        new StreamReader(
                            child.StandardError,
                            UTF8Encoding(false),
                            true,
                            4096,
                            true
                        )

                    let kill () = child.RequestTermination()

                    use cancellation = ct.Register(Action kill)
                    use feeding = CancellationTokenSource.CreateLinkedTokenSource ct

                    let stdout = task {
                        try
                            do! child.StandardOutput.CopyToAsync(output, ct)
                        with error ->
                            kill ()
                            return raise error
                    }

                    let stderr = task {
                        try
                            let buffer = Array.zeroCreate<char> 4096
                            let mutable reading = true

                            while reading do
                                let! count = errorReader.ReadAsync(buffer.AsMemory(), ct)

                                if count = 0 then
                                    reading <- false
                                else
                                    do! errors.WriteAsync(buffer.AsMemory(0, count), ct)
                        with error ->
                            kill ()
                            return raise error
                    }

                    let stdin = task {
                        match input with
                        | Input.Inherit -> ()
                        | Input.Redirect source ->
                            let target =
                                child.StandardInput
                                |> Option.defaultWith (fun () ->
                                    invalidOp "Missing owned input pipe."
                                )

                            try
                                try
                                    do! source.CopyToAsync(target, feeding.Token)
                                with
                                | :? OperationCanceledException when
                                    feeding.IsCancellationRequested
                                    ->
                                    ()
                                | :? IOException when child.WaitForExitAsync().IsCompleted -> ()
                            finally
                                target.Close()
                    }

                    try
                        let! exitCode = child.WaitForExitAsync().WaitAsync(ct)
                        feeding.Cancel()
                        do! child.SettleAsync()
                        do! Task.WhenAll([| stdout :> Task; stderr :> Task; stdin :> Task |])
                        return exitCode
                    with error ->
                        kill ()
                        feeding.Cancel()

                        try
                            do! child.SettleAsync()
                        with cleanupError ->
                            error.Data["Morphir.Launch.CleanupFailure"] <- cleanupError

                        try
                            do! Task.WhenAll([| stdout :> Task; stderr :> Task; stdin :> Task |])
                        with _ ->
                            ()

                        return raise error
                })
    }

let runAsync program args directory home input output errors ct =
    runCoreAsync program args directory home input output errors None ct

let runWithOverlayAsync program args directory home input output errors overlay ct =
    runCoreAsync program args directory home input output errors (Some overlay) ct

type LimitedCapture(maximum: int64) =
    inherit MemoryStream()

    override this.Write(buffer, offset, count) =
        if
            this.Length
            + int64 count > maximum
        then
            raise (IOException "Probe output exceeded its bound.")

        base.Write(buffer, offset, count)

    override this.WriteAsync(buffer: ReadOnlyMemory<byte>, ct) =
        if
            this.Length
            + int64 buffer.Length > maximum
        then
            raise (IOException "Probe output exceeded its bound.")

        base.WriteAsync(buffer, ct)

/// Launch verification is deliberately only the real installed --version probe, never workflow qualification.
let verifyVersionAsync program version directory home (ct: CancellationToken) = task {
    use timeout = CancellationTokenSource.CreateLinkedTokenSource ct
    timeout.CancelAfter(TimeSpan.FromSeconds 30.0)
    use input = new MemoryStream()
    use output = new LimitedCapture(65536L)
    use errorBytes = new LimitedCapture(65536L)
    use errors = new StreamWriter(errorBytes, UTF8Encoding(false), AutoFlush = true)

    let! code =
        runAsync
            program
            [ "--version" ]
            directory
            home
            (Input.Redirect input)
            output
            errors
            timeout.Token

    let actual =
        UTF8Encoding(false, true).GetString(output.ToArray()).TrimEnd('\r', '\n')

    if
        code <> 0
        || actual
           <> version
    then
        LocalCandidate.refuse
            "local.version_mismatch"
            "The actual installed executable did not report the candidate version."
}
