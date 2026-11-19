// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphirup.Program

open System
open System.IO
open System.Text
open Morphir.Toolchain

[<EntryPoint>]
let main args =
    use stdout = Console.OpenStandardOutput()
    // UTF-8 on every platform: Console.Error follows the console code page on Windows, which mangles paths and names.
    use stderr =
        new StreamWriter(Console.OpenStandardError(), UTF8Encoding(false), AutoFlush = true)

    use cancellation = new Threading.CancellationTokenSource()

    Console.CancelKeyPress.Add(fun event ->
        event.Cancel <- true
        cancellation.Cancel()
    )
    // Only the executable entry point waits for its asynchronous lifetime. No transport or reader blocks on async I/O.
    Cli.runWithInheritedInputAsync
        {
            Out = stdout
            Error = stderr
            CurrentDirectory = Environment.CurrentDirectory
            Platform =
                Cli.executionPlatform
                    (if OperatingSystem.IsWindows() then
                         Runtime.InteropServices.OSPlatform.Windows
                     elif OperatingSystem.IsMacOS() then
                         Runtime.InteropServices.OSPlatform.OSX
                     else
                         Runtime.InteropServices.OSPlatform.Linux)
                    Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                    (Platform.current ())
                    (List.ofArray args)
        }
        (List.ofArray args)
        cancellation.Token
    |> fun operation -> operation.GetAwaiter().GetResult()
