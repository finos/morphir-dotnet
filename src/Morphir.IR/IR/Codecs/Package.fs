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
