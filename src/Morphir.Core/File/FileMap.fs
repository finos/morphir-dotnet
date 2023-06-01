module Morphir.File.FileMap

open Morphir.SDK.Dict

type FileMap = FileMap of Dict<string list * string, string>

let empty: FileMap = FileMap Map.empty

module Codec =
    open Json

    let encodeFileMap (FileMap(fileMap)) : Json.Value =
        fileMap
        |> Map.toList
        |> Encode.list (fun ((dirPath, fileName), content) ->
            Encode.list id [
                Encode.list id [ Encode.list Encode.string dirPath; Encode.string fileName ]
                Encode.string content
            ]
        )
