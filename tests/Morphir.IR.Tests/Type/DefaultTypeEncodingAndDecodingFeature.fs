namespace Morphir.IR.Tests.Type

open Morphir.IR.Tests
open Json
open Morphir.Codecs.Default
open LightBDD.Framework
open LightBDD.XUnit2
open Microsoft.FSharp.Core
open Morphir.IR
open Xunit

[<FeatureDescription("""In order to work with the IR
As a developer
I want to be able to encode and decode types using the default Morphir IR format""")>]
[<Label("Type-Codec")>]
[<Trait("Category", "Codecs")>]
[<Trait("Category", "IR")>]
type ``Type Encoding And Decoding Feature``() =
    inherit FeatureFixture()


    [<Scenario>]
    member this.``Unit encoding and decoding``() =
        let createContext () =
            EncodingAndDecodingContext(Type.unit (()), encodeUnit)

        let expectedIRJson = """["Unit",{}]"""

        this.Runner.RunScenarioWithProvidedContext<EncodingAndDecodingContext<_>>(
            createContext,
            <@ fun c -> c.``Given a type``() @>,
            <@ fun c -> c.``When I encode it to JSON``() @>,
            <@
                fun c ->
                    c.``Then the encoded value should be equivalent to the expectedJSON``(
                        expectedIRJson
                    )
            @>
        )

    // [<Scenario>]
    // [<InlineData("""["Unit",{}]""")>]
    // member this.``Encoders and decoders support round-tripping for Types``() =


    [<Scenario>]
    member this.``Variable encoding and decoding``() =
        let createContext () =
            EncodingAndDecodingContext(
                Type.variable "aVar"
                <| Name.fromString "aVar",
                Encode.string
            )

        let expectedIRJson = """["Variable", "aVar", ["a", "var"]]"""

        this.Runner.RunScenarioWithProvidedContext<EncodingAndDecodingContext<_>>(
            createContext,
            <@ fun c -> c.``Given a type``() @>,
            <@ fun c -> c.``When I encode it to JSON``() @>,
            <@
                fun c ->
                    c.``Then the encoded value should be equivalent to the expectedJSON``(
                        expectedIRJson
                    )
            @>
        )
