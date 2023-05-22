namespace Morphir.IR.Tests.Features.Contexts

open LightBDD.Framework.Parameters
open Morphir
open Morphir.IR
open Morphir.Codecs

module RoundtripEncodingContext =
    type NodeKind =
        | Value
        | Type

    type ScenarioInput = {
        Json: string
        NodeKind: NodeKind
        StringRepr: string
    }

    type ResultsRow<'a> = {
        Node: Expression<'a> option
        StringRepr: string
        Error: string option } with
        static member Success(node:Expression<'b>, stringRepr):ResultsRow<'b> = { Node = Some node; StringRepr = stringRepr; Error = None}
        static member Failure(error, repr):ResultsRow<'b> = { Node = None; StringRepr = repr; Error = error}


    let decodeInputs attributesDecoder (row: ScenarioInput):ResultsRow<'a> =
        match row.NodeKind with
        | Type ->
            row.Json
            |> Json.Decode.fromString (Default.decodeType attributesDecoder)
            |> Result.map (fun node ->  ResultsRow.Success<'a>(node, (Type.toString node)))
            |> Result.defaultWith (fun error -> ResultsRow.Failure<'a>(Some error, row.StringRepr) )
        | Value -> ResultsRow.Failure<'a> (Some "DecodeInputs for Value Not implemented", row.StringRepr)

//    let (|DecodeNode|_|) (input:EncodedIRNode)

    let scenarioInput json nodeKind stringRepr = {
        Json = json
        NodeKind = nodeKind
        StringRepr = stringRepr
    }

open RoundtripEncodingContext

type RoundtripEncodingContext<'a>(attributesDecoder:Json.Decode.Decoder<'a>) as self =
    static member Create(attributesDecoder:Json.Decode.Decoder<'a>) = RoundtripEncodingContext(attributesDecoder)
    static member Factory(attributesDecoder:Json.Decode.Decoder<'a>) () = RoundtripEncodingContext.Create(attributesDecoder)

    member val GivenJson = "" with get, set
    member val GivenIRNodes: ScenarioInput list = [] with get, set
    member val Results: ResultsRow<_> list = [] with get, set
    member val DecodedNodes: {| Index:int; Node:Expression<_> |} list = [] with get, set

    member __.``Given a JSON string``(json) = self.GivenJson <- json
    member __.``that JSON string represents a node of kind``(kind: NodeKind) = ()

    member __.``Given I am provided Morphir IR nodes``(table: InputTable<ScenarioInput>) =
        self.GivenIRNodes <-
            table
            |> List.ofSeq
    member __.``When I decode the nodes``() =
        self.Results <-
            self.GivenIRNodes
            |> List.map (decodeInputs attributesDecoder)

        //self.DecodedNodes

    member __.``Then I should get back the expected nodes``(nodes:VerifiableTable<ResultsRow<_>>) =
        nodes.SetActual(self.Results)
