module Morphir.IR.SDK.Decimal

open Morphir.IR
open Morphir.IR.Module
open Morphir.SDK
open Morphir.SDK.Maybe
open Morphir.IR.SDK.Common
open Morphir.IR.Type
open Morphir.IR.SDK.Basics

let moduleName: ModuleName = Path.fromString "Decimal"

let decimalType (attributes: 'a) : Type<'a> =
    reference attributes (toFQName moduleName "Decimal") []

let roundingModeType (attributes: 'a) : Type<'a> =
    reference attributes (toFQName moduleName "RoundingMode") []

let moduleSpec: Module.Specification<unit> = {
    Types =
        Dict.fromList [
            namedTypeSpec "Decimal" (OpaqueTypeSpecification []) "Type that represents a Decimal."
        ]
    Values =
        Dict.fromList [
            vSpec "fromInt" [ ("n", intType ()) ] (decimalType ())
            vSpec "fromFloat" [ ("f", floatType ()) ] (decimalType ())
        ]
    Doc =
        Just
            "Contains the Decimal type representing a real number with some decimal precision, and it's associated functions."

}
