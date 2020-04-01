module Morphir.IR.QName

open Morphir.IR.Name
open Morphir.IR.Path
open Morphir.SDK

type QName = QName of  modulePath:Path * localName:Name

/// Turn a qualified name into a tuple.
let toTuple = function
    | QName (path, name) -> (path,name)

/// Turn a tuple into a qualified name.
let fromTuple = function
    | (path, name) -> QName (path, name)

/// Create a qualified name.
let fromName modulePath localName =
    QName (modulePath, localName)

/// Get the module path part of a qualified name.
let getModulePath = function
    | QName (modulePath, _) -> modulePath

/// Get the local name part of a qualified name.
let getLocalName = function
    | QName (_, localName) -> localName

let toString (pathPartToString: Name -> string)  (nameToString: Name -> string) (sep:string) = function
    | QName (mPath, lName) ->
        mPath
            |> Path.toList
            |> List.map pathPartToString
            |> List.append [ nameToString lName ]
            |> String.join sep
