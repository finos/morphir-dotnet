namespace Morphir.IR.Tests.Features

open System
open LightBDD.Framework.Parameters
open Morphir
open Morphir.IR
open Morphir.IR.Tests
open LightBDD.Framework.Scenarios
open LightBDD.Framework
open LightBDD.XUnit2
open Morphir.IR.Tests.Features.Contexts
open Morphir.IR.Tests.Features.Contexts.RoundtripEncodingContext
open Morphir.Codecs
open Xunit

[<FeatureDescription("""In order to store and retrieve IR node trees
As a user of the Morphir IR
I want to be able to encode and decode Morphir IR nodes in a canonical manner""")>]
[<Label("Codec-Support")>]
[<Trait("Category", "Codecs")>]
[<Trait("Category", "IR")>]
type ``Codec Support Feature``() as self =
    inherit FeatureFixture()

    [<Scenario>]
    member this.``The Codecs module should support round-tripping``() =
        self.Runner.RunScenarioWithProvidedContext(
            RoundtripEncodingContext.Factory(decodeUnit),
            <@
                fun c ->
                    c.``Given I am provided Morphir IR nodes``(
                        Table.For(
                            scenarioInput """["Unit", {}]""" Type "()",
                            scenarioInput """["Variable", {}, ["type", "var"]]""" Type "typeVar"
                        )
                    )
            @>,
            <@ fun c -> c.``When I decode the nodes``() @>,
            <@
                fun c ->
                    c.``Then I should get back the expected nodes``(
                        Table.ExpectData<ResultsRow<unit>>(
                            ResultsRow.Success<unit>(Type.unit (), "()" ),
                            ResultsRow.Success<unit>(Type.variable () (Name.fromString "typeVar"), "typeVar")
                        )
                    )
            @>
        )
