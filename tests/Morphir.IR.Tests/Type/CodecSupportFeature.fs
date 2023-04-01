namespace Morphir.IR.Tests.Type

open System
open LightBDD.Framework.Parameters
open Morphir
open Morphir.IR
open Morphir.IR.Tests
open LightBDD.Framework.Scenarios
open LightBDD.Framework
open LightBDD.XUnit2
open Morphir.IR.Tests.Type.RoundtripEncodingContext
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
            RoundtripEncodingContext.Factory(Codecs.Default.decodeUnit),
            <@
                fun c ->
                    c.``Given I am provided Morphir IR nodes``(
                        Table.For(
                            encodedIRRow """["Unit", {}]""" Type "()",
                            encodedIRRow """["Variable", {}, ["name", "x"]]""" Type "name-x"
                        )
                    )
            @>,
            <@ fun c -> c.``When I decode the nodes``() @>,
            <@
                fun c ->
                    c.``Then I should get back the expected nodes``(
                        Table.ExpectData<Expression<_>>(
                            Type.unit (),
                            Type.variable () (Name.fromString "NameX")
                        )
                    )
            @>
        )

    member val RoundtripTestingInputData =
        Table.For(
            encodedIRRow """["Unit", {}]""" Type "()",
            encodedIRRow """["Variable", {}, ["name", "x"]]""" Type "name-x"
        )
