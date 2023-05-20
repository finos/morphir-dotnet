[<RequireQualifiedAccess>]
module Morphir.IR.Codecs.AccessControlled

open Json
open Morphir.IR
open Morphir.IR.AccessControlled

let encodeAccess access : Value =
    match access with
    | Public -> Encode.string "Public"
    | Private -> Encode.string "Private"

let decodeAccess: Decode.Decoder<string> -> Decode.Decoder<Access> =
    Decode.andThen (fun access ->
        match access with
        | "Public" -> Decode.succeed Public
        | "Private" -> Decode.succeed Private
        | _ -> Decode.fail $"Unknown access controlled type: {access}"
    )

let encoder (encodeValue: 'a -> Value) (ac: AccessControlled<'a>) : Value =
    Encode.object [ "access", encodeAccess ac.Access; "value", encodeValue ac.Value ]

let decoder decodeValue : Decode.Decoder<AccessControlled<'a>> =
    Decode.map2
        accessControlled
        (Decode.field "access" Decode.string
         |> decodeAccess)
        (Decode.field "value" decodeValue)
