namespace Morphir.IR.Tests.Features.Contexts

open Json.Decode
open LightBDD.Framework.Parameters
open Json
open Morphir.Codecs
open Morphir.IR
open Morphir.IR.Type

module StringFormattingContext =
    [<CLIMutable>]
    type Inputs =
        {
            Label: string
            Json: string
        }

    [<CLIMutable>]
    type Outputs =
        {
            Label: string
            Formatted: string
            Message:string
        } with
            static member Create(label:string, formatted:string, ?message:string) =
                let message = defaultArg message ""
                { Label = label; Formatted = formatted; Message = message }

    type DecodedInputs<'a> =
        {
            Label: string
            Decoded: Type<'a> option
            Message: string
        }

    [<RequireQualifiedAccess>]
    module Inputs =
        let Default = { Label = ""; Json = ""}
        let row label json = { Label = label; Json = json }


    [<RequireQualifiedAccess>]
    module Outputs =
        let fromInput formatted (input: Inputs) =
            {
                Label = input.Label
                Formatted = formatted
                Message = ""
            }

        let row label formatted = { Label = label; Formatted = formatted; Message = "" }

    type Inputs with
        static member Default = Inputs.Default
        member self.ToDecoded(typeDecoder:Decoder<Type<'a>>) =
            match self.Json |> Decode.fromString typeDecoder with
            | Ok decoded -> { Label = self.Label; Decoded = Some decoded; Message = "" }
            | Error message -> { Label = self.Label; Decoded = None; Message = message }


open StringFormattingContext

type StringFormattingContext() as self =
    member val AllInputs = [] with get, set
    member val DecodedTypeInputs = [] with get, set
    member val AllOutputs = [] with get, set

    member _.``Given a set of JSON encoded Type nodes``(nodes:InputTable<Inputs>) =
        self.AllInputs <- nodes |> Seq.toList

    member _.``When I decode the Type nodes``(decoder:Decoder<Type<unit>>) =
        self.DecodedTypeInputs <-
            self.AllInputs
            |> List.map (fun input -> input.ToDecoded decoder)

    member _.``When I format the Type nodes``() =
        self.AllOutputs <- self.DecodedTypeInputs |> List.map (fun decoded ->
            {
                Label = decoded.Label
                Message = decoded.Message
                Formatted = decoded.Decoded |> Option.map toString |> Option.defaultValue ""
            }
        )
    member _.``Then the formatted Type nodes should match the expected output``(expected:VerifiableDataTable<Outputs>) =
        expected.SetActual(self.AllOutputs)
