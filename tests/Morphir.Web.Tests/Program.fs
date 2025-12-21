module Morphir.Web.Tests.Program

open Expecto

[<EntryPoint>]
let main args =
    runTestsWithCLIArgs [] args Morphir.Web.Tests.Pages.IndexTests.indexTests
