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
    use stderr = new StreamWriter(Console.OpenStandardError(), UTF8Encoding(false), AutoFlush = true)

    Cli.run
        { Out = stdout
          Error = stderr
          CurrentDirectory = Environment.CurrentDirectory
          Platform = Platform.current () }
        (List.ofArray args)
