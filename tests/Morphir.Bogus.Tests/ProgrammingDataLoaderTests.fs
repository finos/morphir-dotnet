module Morphir.Bogus.Tests.ProgrammingDataLoaderTests

open Expecto
open Morphir.Bogus
open Morphir.SDK.Testing
open Morphir.SDK
open Morphir.Bogus.ProgrammingLibDataLoader

[<Tests>]
let tests =
    describe "Morphir.Bogus.Tests" [
        testCase "Can load programming data"
        <| fun _ ->
            let data = ProgrammingLibDataLoader.TypeData

            for row in data do
                printfn "Row: %A" row

            Expect.isNonEmpty data "Expected data to be non empty"
    ]
