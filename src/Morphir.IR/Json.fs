namespace Json

open Newtonsoft.Json.Linq
open Thoth.Json.Net

// [<AutoOpen>]
// module Prelude =
//     let inline emptyArray () :Value = JArray()

type Value = JsonValue

module Value =
    let inline parse (json: string) : Value = JToken.Parse json


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
    let inline succeed (output: 'a) : Decoder<'a> = Decode.succeed output
