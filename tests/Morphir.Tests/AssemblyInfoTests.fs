namespace Morphir.Tests

open System.Reflection
open Expecto
open Morphir

module AssemblyInfoTests =
    [<Tests>]
    let tests =
        testList "Morphir.AssemblyInfo" [
            testCase "mkInfoString should have at least four segments"
            <| fun () ->
                let assembly = typeof<Main.CLIArguments>.Assembly
                let actual = AssemblyInfo.mkInfoString (assembly)

                let length =
                    actual.Split('-')
                    |> Array.length

                Expect.isGreaterThanOrEqual
                    length
                    4
                    $"String \"{actual}\" should have at least four segments"
        ]