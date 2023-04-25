module Morphir.IR.Tests.BuilderTests
open Morphir.SDK.Testing

[<Tests>]
let tests =
    let libraryCETests = describe "LibraryCETests" []
    describe "BuilderTests" [
        libraryCETests
    ]


