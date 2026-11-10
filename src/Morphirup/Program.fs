// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphirup.Program

open System
open Morphir.Toolchain

[<EntryPoint>]
let main args =
    use stdout = Console.OpenStandardOutput()

    Cli.run
        { Out = stdout
          Error = Console.Error
          CurrentDirectory = Environment.CurrentDirectory
          Platform = Platform.current () }
        (List.ofArray args)
