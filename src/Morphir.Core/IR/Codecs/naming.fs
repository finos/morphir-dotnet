namespace Morphir.IR.Codecs

open Json
open Json.Decode
open Microsoft.FSharp.Core
open Morphir.IR
open Morphir.IR.KindOfName
open Morphir.IR.FQName
open Morphir.IR.Name
open Morphir.IR.Path
open Morphir.IR.QName

[<RequireQualifiedAccess>]
module Name =
    let inline encoder (name: Name) : Value = Encode.list Encode.string name

    let decoder: Decoder<Name> =
        Decode.list Decode.string
        |> Decode.map Name.fromList

[<RequireQualifiedAccess>]
module Path =
    let inline encoder (path: Path) : Value = Encode.list Name.encoder path

    let decoder: Decoder<Path> =
        Decode.list Name.decoder
        |> Decode.map fromList

[<RequireQualifiedAccess>]
module QName =
    open Json
    open Json.Decode

    let encoder =
        function
        | QName(modulePath, name) -> Encode.list id [ Path.encoder modulePath; Name.encoder name ]

    let decoder: Decoder<QName> =
        Decode.map2 qName (Decode.index 0 Path.decoder) (Decode.index 1 Name.decoder)

[<RequireQualifiedAccess>]
module FQName =
    let encoder =
        function
        | FQName(packagePath, modulePath, localName) ->
            Encode.list id [
                Path.encoder packagePath
                Path.encoder modulePath
                Name.encoder localName
            ]

    let decoder: Decoder<FQName> =
        Decode.map3
            fQName
            (Decode.index 0 Path.decoder)
            (Decode.index 1 Path.decoder)
            (Decode.index 2 Name.decoder)

[<RequireQualifiedAccess>]
module KindOfName =
    let encoder =
        function
        | Type -> Encode.string "Type"
        | Constructor -> Encode.string "Constructor"
        | Value -> Encode.string "Value"

    let decoder: Decoder<KindOfName> =
        Decode.string
        |> Decode.andThen (
            function
            | "Type" -> Decode.succeed Type
            | "Constructor" -> Decode.succeed Constructor
            | "Value" -> Decode.succeed Value
            | _ -> Decode.fail "Invalid KindOfName"
        )
