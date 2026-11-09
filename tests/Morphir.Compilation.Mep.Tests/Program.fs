// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Compilation.Mep.Tests.Program

open Expecto

[<EntryPoint>]
let main args =
    match args with
    | [| "--fake-guest"; scenario |] -> FakeGuests.run scenario
    | _ -> runTestsInAssemblyWithCLIArgs [] args
