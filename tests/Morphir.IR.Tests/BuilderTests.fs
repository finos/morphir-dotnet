module Morphir.IR.Tests.BuilderTests

open Morphir.IR.Package
open Morphir.SDK.Testing
open Morphir.IR.Distribution
open Morphir.IR


[<Tests>]
let tests =
    let libraryCETests =
        describe "LibraryCETests" [
            test "It should be possible to define an empty library distribution" {
                let actual = library { packageName "My.SuperPackage" }

                let (Library(packageName, _, _)) = actual

                Expect.equal (PackageName.fromString "My.SuperPackage") packageName
            }
        ]

    describe "BuilderTests" [ libraryCETests ]
