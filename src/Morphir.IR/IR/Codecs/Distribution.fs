module Morphir.IR.Codecs.Distribution
open Json
open Morphir.IR.Distribution
open Morphir.SDK

let currentFormatVersion = 2

let encoder (distro:Distribution) :Value =
    match distro with
    | Library(packagePath, dependencies, def) ->
        Encode.list id
            [ Encode.string "library"
              Path.encoder packagePath
              (dependencies
                |> Dict.toList
                |> Encode.list (fun (packageName, packageSpec) ->
                    Encode.list id [
                        Path.encoder packageName
                        Package.encodeSpecification encodeUnit packageSpec
                    ])
              )
              def |> Package.encodeDefinition encodeUnit (Type.encoder encodeUnit)
            ]

let decoder: Decode.Decoder<Distribution> =
    Decode.index 0 Decode.string
    |> Decode.andThen (fun kind ->
        match kind with
        | "Library" ->
            Decode.map3 library
                (Decode.index 1 Path.decoder)
                (Decode.index 2
                    (Decode.map Dict.fromList
                        (Decode.list
                            (Decode.map2 Tuple.pair
                                (Decode.index 0 Path.decoder)
                                (Decode.index 1 (Package.decodeSpecification decodeUnit))
                            )
                        )
                     )
                 )
                (Decode.index 3 (Package.decodeDefinition decodeUnit (Type.decoder decodeUnit)))
        | _ -> Decode.fail $"Unknown distribution kind: {kind}"
    )

let encodeVersionedDistribution distro =
    Encode.object [
        "formatVersion", Encode.int currentFormatVersion
        "distribution", encoder distro
    ]

let decodeVersionedDistribution:Decode.Decoder<Distribution> =
    Decode.oneOf [
        Decode.field "formatVersion" Decode.int
        |> Decode.andThen (fun formatVersion ->
            if formatVersion = currentFormatVersion then
                Decode.field "distribution" decoder
            else
                Decode.fail $"The IR is using format (version {formatVersion}) but the latest format version is {currentFormatVersion}. Please regenerate it!"
        )
        Decode.fail "The IR is in an old format that doesn't have a format version on it. Please regenerate the it!"
    ]
