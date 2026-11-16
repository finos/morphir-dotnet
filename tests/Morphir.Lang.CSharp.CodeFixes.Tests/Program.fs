// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.CodeFixes.Tests.Program

open Expecto

[<EntryPoint>]
let main argv =
    Tests.runTestsInAssemblyWithCLIArgs [] argv
