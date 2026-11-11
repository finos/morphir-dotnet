// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.Program

open Expecto
open System
open System.Threading
open Morphir.Extensions.Installation

[<EntryPoint>]
let main args =
    match args with
    | [| "--sweep-only"; home |] ->
        RunCopies
            .SweepAsync(home, CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .Match(
                Func<RunCopySweep, int>(fun _ -> 0),
                Func<InstallationFailure, int>(fun error ->
                    eprintfn "%s" (error.ToString())
                    1
                )
            )
    | _ -> runTestsInAssemblyWithCLIArgs [] args
