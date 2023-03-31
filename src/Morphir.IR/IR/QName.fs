module Morphir.IR.QName

open Morphir.IR.Name
open Morphir.IR.Path
open Morphir.SDK

type QName = QName of modulePath: Path * localName: Name

let qName (modulePath: Path) (localName: Name) = QName(modulePath, localName)

/// Turn a qualified name into a tuple.
let toTuple =
    function
    | QName (path, name) -> (path, name)

/// Turn a tuple into a qualified name.
let fromTuple =
    function
    | (path, name) -> QName(path, name)

/// Create a qualified name.
let fromName modulePath localName = QName(modulePath, localName)

/// Get the module path part of a qualified name.
let getModulePath =
    function
    | QName (modulePath, _) -> modulePath

/// Get the local name part of a qualified name.
let getLocalName =
    function
    | QName (_, localName) -> localName

/// Turn a QName into a string using ':' as a separator between module and local names.
let toString =
    function
    | QName (moduleName, localName) ->
        String.join ":" [
            Path.toString Name.toTitleCase "." moduleName
            Name.toCamelCase localName
        ]

module Codec =
    open Thoth.Json.Net
    open Morphir.IR.Name.Codec
    open Morphir.IR.Path.Codec

    let encodeQName: QName -> JsonValue =
        function
        | QName (modulePath, localName) ->
            Encode.list [ encodePath modulePath; encodeName localName ]

    let decodeQName: Decoder<QName> =
        Decode.map2 qName (Decode.index 0 decodePath) (Decode.index 1 decodeName)
