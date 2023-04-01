namespace Morphir.IR.Tests.Type

open LightBDD.Framework.Parameters
open Morphir
open Morphir.IR
open Morphir.Codecs

module RoundtripEncodingContext =
    type NodeKind =
        | Value
        | Type

    type EncodedIRNode = {
        Json: string
        NodeKind: NodeKind
        StringRepr: string
    }

    let (|DecodedRow|_|) attributesDecoder (row: EncodedIRNode) =
        match row.NodeKind with
        | Type ->
            let r  =
                row.Json
                |> Json.Decode.fromString (Default.decodeType attributesDecoder)
                |> Result.toOption
            r
        | Value -> None

    let encodedIRRow json nodeKind stringRepr = {
        Json = json
        NodeKind = nodeKind
        StringRepr = stringRepr
    }

open RoundtripEncodingContext

type RoundtripEncodingContext<'a>(attributesDecoder:Json.Decode.Decoder<'a>) as self =
    static member Create(attributesDecoder:Json.Decode.Decoder<'a>) = RoundtripEncodingContext(attributesDecoder)
    static member Factory(attributesDecoder:Json.Decode.Decoder<'a>) () = RoundtripEncodingContext.Create(attributesDecoder)

    member val GivenJson = "" with get, set
    member val GivenIRNodes: EncodedIRNode list = [] with get, set
    member val DecodedIRNodes: Expression<_> list = [] with get, set

    member __.``Given a JSON string``(json) = self.GivenJson <- json
    member __.``that JSON string represents a node of kind``(kind: NodeKind) = ()

    member __.``Given I am provided Morphir IR nodes``(table: InputTable<EncodedIRNode>) =
        self.GivenIRNodes <-
            table
            |> List.ofSeq
    member __.``When I decode the nodes``() =
        self.DecodedIRNodes <-
            self.GivenIRNodes
            |> List.choose (function |DecodedRow attributesDecoder node -> Some node | _ -> None)

    member __.``Then I should get back the expected nodes``(nodes:VerifiableTable<Expression<_>>) =
        nodes.SetActual(self.DecodedIRNodes)
