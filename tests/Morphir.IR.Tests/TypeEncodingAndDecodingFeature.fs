namespace Morphir.IR.Tests

open Json
open LightBDD.Framework.Expectations
open Morphir.Codec
open LightBDD.Framework
open LightBDD.Framework.Scenarios
open LightBDD.XUnit2
open Microsoft.FSharp.Core
open Morphir.IR
open Xunit
open ExpressionHelpers
open Type.Codec

[<FeatureDescription("""In order to work with the IR
As a developer
I want to be able to encode and decode types""")>]
[<Label("Type-Codec")>]
[<Trait("Category", "Codecs")>]
[<Trait("Category", "IR")>]
type TypeEncodingAndDecodingFeature() =
    inherit FeatureFixture()

    static member UnitData with get() = seq {
        yield [| ("""["Unit",{}]""" |> Json.Value.parse) :> obj |]
    }

    [<Scenario>]
    [<MemberData("UnitData")>]
    member this.``Unit encoding and decoding``(expectedEncodedValue:Value) =
        this.Runner.RunScenarioWithContext<Ctx>(
            <@ fun c -> c.``Given a typeExpr``(Type.unit (())) @>,
            <@ fun c -> c.``When I encode it to JSON``() @>,
            <@ fun c -> c.``Then the encoded value should be as expected``(expectedEncodedValue) @>
        )

and private Ctx() as self =
    member val TypeExpr: Type.Type<_> option = None with get, set
    member val EncodedValue: Json.Encode.Value option = None with get, set

    member __.``Given a typeExpr``(typeExpr: Type.Type<_>) = self.TypeExpr <- Some typeExpr

    member __.``When I encode it to JSON``() =
        self.EncodedValue <- encodeType encodeUnit self.TypeExpr.Value |> Some
    member __.``Then the encoded value should be as expected``(expected:Encode.Value) =
        printfn "Expected: %O" expected
        Assert.Equal<Encode.Value>(expected, self.EncodedValue.Value)
