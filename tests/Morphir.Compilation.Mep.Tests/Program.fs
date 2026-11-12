// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Compilation.Mep.Tests.Program

open Expecto
open Morphir.TestSupport

/// With --fake-guest the executable is a scripted guest for the host tests. Otherwise it runs the tests, and the
/// run fails when a test changed the machine folders outside its sandbox.
[<EntryPoint>]
let main args =
    match args with
    | [| "--fake-sleeper" |] ->
        use self = System.Diagnostics.Process.GetCurrentProcess()
        System.IO.File.WriteAllText(System.IO.Path.Combine(System.Environment.CurrentDirectory,"descendant.pid"),$"{self.Id}|{self.StartTime.ToUniversalTime().Ticks}")
        System.Threading.Thread.Sleep 60000
        0
    | [| "--fake-guest"; scenario |] -> FakeGuests.run scenario
    | _ ->
        let tripwire = MachineTripwire.Capture()

        let result =
            try
                runTestsInAssemblyWithCLIArgs [] args
            finally
                MepFrontendTests.disposeGuestWork ()

        tripwire.AssertUnchanged()
        result
