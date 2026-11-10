// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.CodeGeneration.FSharp.Extension.Tests.Program

open Expecto
open Morphir.TestSupport

/// With --fake-backend-guest the executable is a scripted backend guest for the host adapter tests. Otherwise it
/// runs the tests, and the run fails when a test changed the machine folders outside its sandbox.
[<EntryPoint>]
let main args =
    match args with
    | [| "--fake-backend-guest"; scenario |] -> FakeBackendGuests.run scenario
    | _ ->
        let tripwire = MachineTripwire.Capture()
        let result = runTestsInAssemblyWithCLIArgs [] args
        tripwire.AssertUnchanged()
        result
