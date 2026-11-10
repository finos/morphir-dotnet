// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Toolchain.Tests.Program

open Expecto
open Morphir.TestSupport

/// The next slices write under Morphir Home, so the run fails when a test changed the machine folders outside its
/// sandbox.
[<EntryPoint>]
let main args =
    let tripwire = MachineTripwire.Capture()
    let result = runTestsInAssemblyWithCLIArgs [] args
    tripwire.AssertUnchanged()
    result
