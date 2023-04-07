module Morphir.IR.Codecs.Package
open Json
open Morphir.IR.Package
open Morphir.SDK

let encodeSpecification encodeTypeAttributes (spec:Specification<'ta>) :Value =
    Encode.object [
        "modules",
        spec.Modules
        |> Dict.toList
        |> Encode.list (fun (moduleName, moduleSpec) ->
            Encode.list id [
                Path.encoder moduleName
                Module.encodeSpecification encodeTypeAttributes moduleSpec
            ]
        )
    ]

let decodeSpecification decodeTypeAttributes: Decode.Decoder<Specification<'ta>> =
    Decode.map specification
        (Decode.field "modules"
            (Decode.map Dict.fromList
                (Decode.list
                    (Decode.map2 Tuple.pair
                        (Decode.index 0 Path.decoder)
                        (Decode.index 1 (Module.decodeSpecification decodeTypeAttributes))
                    )
                )
            )
        )

let encodeDefinition encodeTypeAttributes encodeValueAttributes (def:Definition<'ta,'va>):Value =
    Encode.object [
        "modules",
        def.Modules
        |> Dict.toList
        |> Encode.list (fun (moduleName, moduleDef) ->
            Encode.list id [
                Path.encoder moduleName
                AccessControlled.encoder (Module.encodeDefinition encodeTypeAttributes encodeValueAttributes) moduleDef
            ]
        )
    ]

let decodeDefinition decodeTypeAttributes decodeValueAttributes: Decode.Decoder<Definition<'ta,'va>> =
    Decode.map definition
        (Decode.field "modules"
            (Decode.map Dict.fromList
                (Decode.list
                    (Decode.map2 Tuple.pair
                        (Decode.index 0 Path.decoder)
                        (Decode.index 1 (AccessControlled.decoder (Module.decodeDefinition decodeTypeAttributes decodeValueAttributes)))
                    )
                )
            )
        )
