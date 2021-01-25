namespace Finos.Morphir.Elm.Tests

open Expecto
open Finos.Morphir.Elm

module MorphirElmJsTests =
    [<Tests>]
    let tests =
        testList "MorphirElmJs tests"
            [ testCase "The morphir-elm Javascript should not null" <| fun _ ->
                //MorphirElmJs.containingAssembly.GetManifestResourceNames() |> printfn "%A"
                let jsSource = MorphirElmJs.morphirElmCliJavascriptSource.Force()
                Expect.isNotNull jsSource "The morphir-elm Javascript source should not be null"

              testCase "The morphir-elm Javascript should not be empty" <| fun _ ->
                let jsSource = MorphirElmJs.morphirElmCliJavascriptSource.Force()
                Expect.isNotEmpty jsSource "The morphir-elm Javascript source should not be empty"]
