module Morphir.Frontend.FSharp.Tests.Program

open Expecto
open Morphir.TestSupport

/// The probe and fake modes run as child processes of the tests. Otherwise it runs the tests, and the run fails when
/// a test changed the machine folders outside its sandbox.
[<EntryPoint>]
let main args =
    match args with
    | [| argument |] when argument = CompilerEdgeTests.WorkingDirectoryProbe.Argument ->
        CompilerEdgeTests.WorkingDirectoryProbe.Run()
    | args when args.Length > 0 && args[0] = FakeMorphir.Argument -> FakeMorphir.run args[1..]
    | _ ->
        let tripwire = MachineTripwire.Capture()
        let result = runTestsInAssemblyWithCLIArgs [] args
        tripwire.AssertUnchanged()
        result
