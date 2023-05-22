module Morphir.IR.SDK.Dict

open Morphir.IR
open Morphir.SDK
open Morphir.SDK.Maybe
open Morphir.IR.SDK.Common
open Morphir.IR.Type

let moduleName = Path.fromString "Dict"

let dictType attributes (keyType: Type<_>) (valueType: Type<_>) =
    reference attributes (toFQName moduleName "dict") [ keyType; valueType ]

let moduleSpec: Module.Specification<unit> = {
    Types = Dict.fromList []
    Values = Dict.fromList []
    Doc =
        Just
            "Contains the Char type representing a single character, and it's associated functions."
}
