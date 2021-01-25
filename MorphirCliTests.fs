namespace Finos.Morphir.Tests

open Expecto
open Finos.Morphir

module MorphirCliTests =
    [<Tests>]
    let tests =
        testList "samples"
            [ testCase "Version should be non empty" <| fun _ ->
                let toolsVersion = ToolInfo.toolsVersion
                Expect.isNotNull toolsVersion "The toolsVersion should be provided" ]
