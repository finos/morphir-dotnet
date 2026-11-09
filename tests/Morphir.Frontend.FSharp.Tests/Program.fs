module Morphir.Frontend.FSharp.Tests.Program

open Expecto

[<EntryPoint>]
let main args =
    match args with
    | [| argument |] when argument = CompilerEdgeTests.WorkingDirectoryProbe.Argument ->
        CompilerEdgeTests.WorkingDirectoryProbe.Run()
    | args when args.Length > 0 && args[0] = FakeMorphir.Argument -> FakeMorphir.run args[1..]
    | _ -> runTestsInAssemblyWithCLIArgs [] args
