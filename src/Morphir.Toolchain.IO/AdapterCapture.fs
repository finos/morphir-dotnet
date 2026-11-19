// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain.IO

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks

// Internal development capture, reusing creation-time native ownership. No legacy fallback.
type private CaptureChild = {
    Input: Stream
    Output: Stream
    Errors: Stream
    Wait: CancellationToken -> Task
    Settle: unit -> Task
    Close: unit -> unit
    Owner: IAsyncDisposable
}

/// One native adapter invocation, with finite byte budgets and a deadline across feeding, exit and drains.
[<AbstractClass; Sealed>]
type internal AdapterCapture =
    static member private Read(stream: Stream, limit: int, stop: CancellationTokenSource) = task {
        use bytes = new MemoryStream()
        let chunk = Array.zeroCreate<byte> 4096
        try
            let mutable reading = true
            while reading do
                let! count = stream.ReadAsync(chunk.AsMemory(), stop.Token)
                if count = 0 then reading <- false
                elif bytes.Length + int64 count > int64 limit then
                    raise (InvalidDataException "adapter.output_limit")
                else bytes.Write(chunk, 0, count)
            return bytes.ToArray()
        with error ->
            stop.Cancel()
            return raise error
    }

    /// Recognizes owner timeouts retained alongside a capture error.
    static member SettlementTimedOut(error: Exception) =
        match error with
        | :? OperationCanceledException -> false
        | :? TimeoutException -> true
        | _ ->
            let disposalTimedOut = error.Data["Morphir.Launch.DisposalFailure"] :? TimeoutException

            let cleanupTimedOut =
                match error with
                | :? IOException as capture ->
                    match capture.InnerException with
                    | NonNull primary -> primary.Data["AdapterCleanup"] :? TimeoutException
                    | Null -> false
                | _ -> false

            disposalTimedOut || cleanupTimedOut

    static member RunAsync(program: string, arguments: IReadOnlyList<string>, directory: string,
                           request: byte array, deadline: TimeSpan, maxBytes: int, cancellation: CancellationToken) = task {
        if not (Path.IsPathFullyQualified program) || request.Length > 16384 || maxBytes < 1 || maxBytes > 65536
           || deadline <= TimeSpan.Zero || deadline > TimeSpan.FromMinutes 1. then
            invalidArg "program" "An explicit absolute adapter and bounded request are required."
        use stop = CancellationTokenSource.CreateLinkedTokenSource cancellation
        stop.CancelAfter deadline
        let start = ProcessStartInfo(program)
        start.UseShellExecute <- false
        start.CreateNoWindow <- true
        start.RedirectStandardInput <- true
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true
        start.WorkingDirectory <- directory
        for argument in arguments do start.ArgumentList.Add argument
        let settlement = TimeSpan.FromSeconds 2.
        let! child = task {
            if OperatingSystem.IsWindows() then
                let! child = WindowsChild.StartAsync(start, stop.Token)
                let input = child.StandardInput |> Option.defaultWith (fun () -> raise (IOException "No adapter input."))
                return {
                    Input = input; Output = child.StandardOutput; Errors = child.StandardError
                    Wait = fun ct -> child.WaitForExitAsync().WaitAsync(ct) :> Task
                    Settle = fun () -> child.SettleAsync()
                    Close = fun () -> input.Dispose(); child.StandardOutput.Dispose(); child.StandardError.Dispose()
                    Owner = child :> IAsyncDisposable
                }
            elif LinuxChild.IsSupported then
                let child = LinuxChild.Start(start, settlement, stop.Token)
                return {
                    Input = child.StandardInput.BaseStream; Output = child.StandardOutput.BaseStream; Errors = child.StandardError.BaseStream
                    Wait = child.WaitForExitAsync; Settle = (fun () -> child.SettleAsync() :> Task); Close = child.ClosePipes
                    Owner = child :> IAsyncDisposable
                }
            elif DarwinChild.IsSupported then
                let child = DarwinChild.Start(start, settlement, stop.Token)
                return {
                    Input = child.StandardInput.BaseStream; Output = child.StandardOutput.BaseStream; Errors = child.StandardError.BaseStream
                    Wait = child.WaitForExitAsync; Settle = (fun () -> child.SettleAsync() :> Task); Close = child.ClosePipes
                    Owner = child :> IAsyncDisposable
                }
            else return raise (PlatformNotSupportedException "No owned adapter launcher on this platform.")
        }
        return! AsyncOwnership.runAsync child.Owner (fun () -> task {
            let output = AdapterCapture.Read(child.Output, maxBytes, stop)
            let errors = AdapterCapture.Read(child.Errors, 8192, stop)
            let feed = task {
                try
                    do! child.Input.WriteAsync(request.AsMemory(), stop.Token)
                    do! child.Input.FlushAsync(stop.Token)
                    child.Input.Dispose()
                with error -> stop.Cancel(); return raise error
            }
            let exited = child.Wait stop.Token
            let all = Task.WhenAll([| output :> Task; errors :> Task; feed :> Task; exited |])
            try
                do! all.WaitAsync(stop.Token)
                // Root exit and bounded EOF are both required; disposal settles descendants too.
                return! output
            with primary ->
                stop.Cancel()
                try do! child.Settle() with cleanup -> primary.Data["AdapterCleanup"] <- cleanup
                child.Close()
                try do! all.WaitAsync(settlement) with _ -> ()
                cancellation.ThrowIfCancellationRequested()
                return raise (IOException("adapter.capture_failed_or_deadline", primary))
        })
    }
