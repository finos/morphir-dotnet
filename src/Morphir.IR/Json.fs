namespace Json

open Newtonsoft.Json.Linq
open Thoth.Json.Net

// [<AutoOpen>]
// module Prelude =
//     let inline emptyArray () :Value = JArray()

type Value = JsonValue

module Value =
    let inline parse (json: string) : Value = JToken.Parse json
    let inline CreateNull () : Value = JValue.CreateNull()


module Encode =
    type Value = JsonValue
    let inline string (value: string) : Value = Encode.string value

    let list (func: 'a -> Value) (values: #seq<'a>) : Value =
        let addEntry (acc: JArray) c =
            acc.Add(func c)
            acc

        values
        |> Seq.fold addEntry (JArray())
        :> Value

    let object: (string * Value) list -> Value = Encode.object

module Decode =
    type Decoder<'T> = Thoth.Json.Net.Decoder<'T>

    let inline andThen (cb: 'a -> Decoder<'b>) (decoder: Decoder<'a>) : Decoder<'b> =
        Decode.andThen cb decoder

    let inline fromString (decoder: Decoder<_>) = Decode.fromString decoder

    let inline fail (message: string) : Decoder<'a> = Decode.fail message

    let inline index (requestedIndex: int) (decoder: Decoder<'a>) =
        Decode.index requestedIndex decoder

    let inline map (ctor: 'a -> 'b) (decoder: Decoder<'a>) : Decoder<'b> = Decode.map ctor decoder

    let inline map2
        (ctor: 'a -> 'b -> 'c)
        (decoder1: Decoder<'a>)
        (decoder2: Decoder<'b>)
        : Decoder<'c> =
        Decode.map2 ctor decoder1 decoder2

    let inline map3
        (ctor: 'a -> 'b -> 'c -> 'd)
        (decoder1: Decoder<'a>)
        (decoder2: Decoder<'b>)
        (decoder3: Decoder<'c>)
        : Decoder<'d> =
        Decode.map3 ctor decoder1 decoder2 decoder3

    let inline succeed (output: 'a) : Decoder<'a> = Decode.succeed output

    let string: Decoder<string> = Decode.string