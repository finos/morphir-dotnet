module Morphir.Frontend.FSharp.Tests.Program

open Expecto

[<EntryPoint>]
let main args =
    match args with
    | [| argument |] when argument = CompilerEdgeTests.WorkingDirectoryProbe.Argument ->
        CompilerEdgeTests.WorkingDirectoryProbe.Run()
    | _ -> runTestsInAssemblyWithCLIArgs [] args
