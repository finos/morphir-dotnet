namespace Morphir.IR.Tests.Features

open Json
open LightBDD.Framework
open LightBDD.Framework.Parameters
open LightBDD.XUnit2
open Morphir.IR.Tests.Features.Contexts
open Morphir.IR.Tests.Features.Contexts.StringFormattingContext
open Morphir.IR.Type
open Xunit
open Morphir.IR.Tests
open Morphir.Codecs

[<FeatureDescription("""In order to test the Morphir IR
As a user of the Morphir IR
I want to be able to format the Morphir IR in an easy to read manner""")>]
[<Label("Formatting")>]
[<Trait("Category", "Formatting")>]
[<Trait("Category", "IR")>]
type StringFormattingFeature() as self =
    inherit FeatureFixture()

    [<Scenario>]
    member __.``Formatting Type Nodes``() =
        let typeDecoder:Decode.Decoder<Type<unit>> = Default.decodeType Default.decodeUnit
        self.Runner.RunScenarioWithContext<StringFormattingContext>(
            <@ fun c -> c.``Given a set of JSON encoded Type nodes``(Table.For(
                    Inputs.row "Unit" """["Unit", {}]""",
                    Inputs.row "Variable" """["Variable", {}, ["hello","world"]]""",
                    Inputs.row "Variable-02" """["Variable", {}, ["a"]]""",
                    Inputs.row "Tuple-01" """["Tuple", {}, [["Unit", {}],["Unit", {}]]]""",
                    Inputs.row "Tuple-02" """["Tuple", {}, [["Variable", {}, ["a"]],["Variable", {}, ["b"], ["Variable", {}, ["c"]]]]]"""
                ))  @>,
            <@ fun c -> c.``When I decode the Type nodes``(typeDecoder) @>,
            <@ fun c -> c.``When I format the Type nodes``() @>,
            <@ fun c -> c.``Then the formatted Type nodes should match the expected output``(
                Table.ExpectData(
                    Outputs.row "Unit" "()",
                    Outputs.row "Variable" "helloWorld",
                    Outputs.row "Variable-02" "a",
                    Outputs.row "Tuple-01" "((), ())",
                    Outputs.row "Tuple-02" "(a, b, c)"

                )) @>
            )


