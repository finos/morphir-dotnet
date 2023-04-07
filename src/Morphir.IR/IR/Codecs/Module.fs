module Morphir.IR.Codecs.Module

open Json
open Morphir.SDK
open Morphir.IR.Module
let encodeSpecification encodeTypeAttributes (spec:Specification<'ta>):Value =
    Encode.object [
        "types",
        spec.Types
        |> Dict.toList
        |> Encode.list (fun (name, typeSpec) ->
            Encode.list id [
                Name.encoder name
                typeSpec |> Documented.encoder (Type.encodeSpecification encodeTypeAttributes)
            ]
        )
        "values",
        spec.Values
        |> Dict.toList
        |> Encode.list (fun (name, valueSpec) ->
            Encode.list id [
                Name.encoder name
                valueSpec |> Documented.encoder (Value.encodeSpecification encodeTypeAttributes)
            ]
        )
        "doc",
        spec.Doc |> Maybe.map Encode.string |> Maybe.withDefault Encode.nil
    ]

let decodeSpecification decodeTypeAttributes:Decode.Decoder<Specification<'ta>> =
    Decode.map3 specification
        (Decode.field "types"
            (Decode.map Dict.fromList
                (Decode.list (Decode.map2 Tuple.pair
                    (Decode.field "name" Name.decoder)
                    (Decode.field "spec" (Documented.decoder (Type.decodeSpecification decodeTypeAttributes)))
                ))
            )
         )
        (Decode.field "values"
            (Decode.map Dict.fromList
                (Decode.list
                    (Decode.map2 Tuple.pair
                        (Decode.index 0 Name.decoder)
                        (Decode.index 1 (Documented.decoder (Value.decodeSpecification decodeTypeAttributes)))
                    )
                )
            )
        )
        (Decode.oneOf [
            Decode.field "doc" (Decode.maybe Decode.string)
            Decode.succeed Maybe.Nothing
        ])

let encodeDefinition encodeTypeAttributes encodeValueAttributes def =
    Encode.object [
        "types",
        def.Types
        |> Dict.toList
        |> Encode.list (fun (name, typeDef) ->
            Encode.list id [
                Name.encoder name
                typeDef |> (AccessControlled.encoder (Documented.encoder (Type.encodeDefinition encodeTypeAttributes)))
            ]
        )
        "values",
        def.Values
        |> Dict.toList
        |> Encode.list (fun (name, valueDef) ->
            Encode.list id [
                Name.encoder name
                valueDef |> (AccessControlled.encoder (Documented.encoder (Value.encodeDefinition encodeTypeAttributes encodeValueAttributes)))
            ]
        )
        "def",
        def.Doc |> Maybe.map Encode.string |> Maybe.withDefault Encode.nil
    ]

let decodeDefinition (decodeTypeAttributes:Decode.Decoder<'ta>) (decodeValueAttributes:Decode.Decoder<'va>) :Decode.Decoder<Definition<'ta,'va>> =
    Decode.map3 definition
        (Decode.field "types"
            (Decode.map Dict.fromList
                (Decode.list (Decode.map2 Tuple.pair
                    (Decode.field "name" Name.decoder)
                    (Decode.field "def" (AccessControlled.decoder (Documented.decoder (Type.decodeDefinition decodeTypeAttributes))))
                ))
            )
         )
        (Decode.field "values"
            (Decode.map Dict.fromList
                (Decode.list
                    (Decode.map2 Tuple.pair
                        (Decode.index 0 Name.decoder)
                        (Decode.index 1 (AccessControlled.decoder (Documented.decoder (Value.decodeDefinition decodeTypeAttributes decodeValueAttributes))))
                    )
                )
            )
        )
        (Decode.oneOf [
            Decode.field "def" (Decode.maybe Decode.string)
            Decode.succeed Maybe.Nothing
        ])

