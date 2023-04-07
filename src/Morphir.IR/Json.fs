namespace Json

open Morphir.SDK
open Morphir.SDK.Maybe
open Newtonsoft.Json.Linq
open Thoth.Json.Net

// [<AutoOpen>]
// module Prelude =
//     let inline emptyArray () :Value = JArray()

type Value = JsonValue
type DecodeError = Thoth.Json.Net.DecoderError

module Value =
    let inline parse (json: string) : Value = JToken.Parse json
    let inline CreateNull () : Value = JValue.CreateNull()


module Encode =
    type Value = JsonValue

    let inline bool value = Encode.bool value

    let inline decimal value = Encode.decimal value

    let inline float value = Encode.float value
    let inline int value = Encode.int value
    let inline int64 value = Encode.int64 value
    let inline string (value: string) : Value = Encode.string value

    let list (func: 'a -> Value) (values: #seq<'a>) : Value =
        let addEntry (acc: JArray) c =
            acc.Add(func c)
            acc

        values
        |> Seq.fold addEntry (JArray())
        :> Value

    let object: (string * Value) list -> Value = Encode.object

    let nil = Encode.nil

module Decode =
    type Decoder<'T> = Thoth.Json.Net.Decoder<'T>

    let inline andThen (cb: 'a -> Decoder<'b>) (decoder: Decoder<'a>) : Decoder<'b> =
        Decode.andThen cb decoder

    let bool: Decoder<bool> = Decode.bool
    let char: Decoder<char> = Decode.char

    let int: Decoder<int> = Decode.int

    let float: Decoder<float> = Decode.float

    let decimal: Decoder<decimal> = Decode.decimal


    let int16: Decoder<int16> = Decode.int16
    let int64: Decoder<int64> = Decode.int64

    let inline fromString (decoder: Decoder<_>) = Decode.fromString decoder

    let inline fail (message: string) : Decoder<'a> = Decode.fail message

    let inline field (fieldName: string) (decoder: Decoder<'a>) : Decoder<'a> =
        Decode.field fieldName decoder

    let inline index (requestedIndex: int) (decoder: Decoder<'a>) =
        Decode.index requestedIndex decoder


    let inline list (decoder: Decoder<'a>) : Decoder<'a list> = Decode.list decoder

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

    let inline map4
        (ctor: 'a -> 'b -> 'c -> 'd -> 'e)
        (decoder1: Decoder<'a>)
        (decoder2: Decoder<'b>)
        (decoder3: Decoder<'c>)
        (decoder4: Decoder<'d>)
        : Decoder<'e> =
        Decode.map4 ctor decoder1 decoder2 decoder3 decoder4

    let inline option (decoder: Decoder<'a>) : Decoder<'a option> = Decode.option decoder
    let maybe (decoder: Decoder<'a>) : Decoder<Maybe<'a>> =
        Decode.option decoder
        |> Decode.map (fun x -> Conversions.Options.optionsToMaybe x)

    let inline oneOf (decoders: Decoder<'a> list) : Decoder<'a> = Decode.oneOf decoders

    let inline succeed (output: 'a) : Decoder<'a> = Decode.succeed output

    let lazyily (thunk: unit -> Decoder<'a>) : Decoder<'a> = andThen thunk (succeed ())
    let string: Decoder<string> = Decode.string
