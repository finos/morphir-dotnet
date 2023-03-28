namespace Morphir.Bogus

open System.IO
open System.Text.Json
open System.Text.Json.Serialization

module Serializer =
    let DefaultJsonSerializerOptions = JsonSerializerOptions()

type internal Serializer() =
    static member ReadToEnd(stream: Stream) : string =
        use reader = new StreamReader(stream)
        reader.ReadToEnd()

    static member Deserialize<'T>(stream: Stream) : 'T =
        Serializer.Deserialize<'T>(stream, Serializer.DefaultJsonSerializerOptions)

    static member Deserialize<'T>(stream: Stream, options: JsonSerializerOptions) : 'T =
        JsonSerializer.Deserialize<'T>(Serializer.ReadToEnd(stream), options)
