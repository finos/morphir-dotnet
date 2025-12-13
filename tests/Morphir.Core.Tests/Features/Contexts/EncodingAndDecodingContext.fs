namespace Morphir.IR.Tests.Features.Contexts

open Json
open Morphir.Codecs.Default
open Microsoft.FSharp.Core
open Morphir.IR
open Xunit

type EncodingAndDecodingContext<'TA>
    (
        typeExpr: Type.Type<'TA>,
        encodeAttributes: 'TA -> Encode.Value
    ) as self =

    member val ProvidedRawJson = "" with get, set
    member val ProvidedJsonValue = Json.Value.CreateNull() with get, set
    member val GivenType = typeExpr with get, set
    member val EncodedType: Json.Encode.Value option = None with get, set
    member val DecodedType: Type.Type<'TA> option = None with get, set

    member __.``Given a type``(``type``: Type.Type<'TA>) = self.GivenType <- ``type``
    member __.``Given a type``() = ()

    member __.``Given a JSON representation of a type``(json: string) =
        self.ProvidedRawJson <- json
        self.ProvidedJsonValue <- Json.Value.parse json

    member __.``When I encode it to JSON``() =
        self.EncodedType <-
            encodeType encodeAttributes self.GivenType
            |> Some

    member __.``When I decode it from JSON``() = ()

    member __.``Then the decoded type should have a string representation of:``(expected: string) =
        let actual = self.DecodedType.Value
        let actualString = Type.toString actual
        Assert.Equal(expected, actualString)

    member __.``Then the encoded value should be as expected``(expected: Encode.Value) =
        printfn $"Expected: {expected}"
        Assert.Equal<Encode.Value>(expected, self.EncodedType.Value)

    member __.``Then the encoded value should be equivalent to the expectedJSON``
        (expectedJSON: string)
        =
        let expected = Json.Value.parse expectedJSON
        let actual = self.EncodedType.Value
        Assert.Equal(expected.ToString(), actual.ToString())
