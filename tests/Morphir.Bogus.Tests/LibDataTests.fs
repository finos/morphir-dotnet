module Morphir.Bogus.Tests.LibDataTests

open Expecto
open Morphir.Bogus
open Morphir.Bogus.IR
open Morphir.SDK.Testing
open Morphir.SDK
open Morphir.Bogus.LibDataLoader

[<Tests>]
let tests =
    describe "Morphir.Bogus.Tests.LibDataTests" [
        testCase "Can load programming data"
        <| fun _ ->
            let data = LibDataLoader.TypeData
            Expect.isNonEmpty data "Expected data to be non empty"

        testCase "Can load type identities"
        <| fun _ ->
            let dataSet = LibDataSet()

            let actual =
                dataSet.TypeIdentities(10)
                |> Seq.toList

            Expect.hasLength actual 10 "Expected 10 type identities"
    ]
