module Morphir.IR.FQName

open Morphir.IR.Path
open Morphir.IR.Name
open Morphir.IR.QName

type FQName = FQName of packagePath:Path * modulePath:Path * localName:Name

let inline fQName packagePath modulePath localName =
    FQName (packagePath, modulePath, localName)

let fromQName packagePath qName =
    let (QName (modulePath, localName)) = qName
    FQName (packagePath, modulePath, localName)

let getPackagePath = function
    | FQName (packagePath, _, _) -> packagePath

let getModulePath = function
    | FQName (_, modulePath, _) -> modulePath

let getLocalName = function
    | FQName (_, _, localName) -> localName
