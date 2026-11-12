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

        start.Environment["MORPHIR_HOME"] <- home
        start.Environment["MORPHIR_ISOLATED"] <- "1"
        start.Environment["MORPHIR_DISCOVERY_BOUNDARY"] <- directory
        start.Environment["DOTNET_MULTILEVEL_LOOKUP"] <- "0"
        match overlay with
        | Some directory -> start.Environment["MORPHIR_EXTENSION_OVERLAY"] <- directory
        | None -> ()

        for name in
            [
                "DOTNET_ROOT"
                "DOTNET_ROOT_X64"
                "DOTNET_ROOT(x86)"
                "DOTNET_SHARED_STORE"
                "DOTNET_ADDITIONAL_DEPS"
                "DOTNET_STARTUP_HOOKS"
            ] do
            start.Environment.Remove name
            |> ignore

        ct.ThrowIfCancellationRequested()
        let! child = WindowsChild.StartAsync(start, ct)

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
