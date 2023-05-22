module Morphir.IR.SDK.Int

open Morphir.IR
open Morphir.IR.Module
open Morphir.IR.SDK.Basics
open Morphir.IR.Type
open Morphir.SDK
open Morphir.SDK.Maybe

let moduleName: ModuleName = Path.fromString "Int"

let int8Type attributes : Type<'a> =
    reference attributes (toFQName moduleName "Int8") []

let int16Type attributes : Type<'a> =
    reference attributes (toFQName moduleName "Int16") []

let int32Type attributes : Type<'a> =
    reference attributes (toFQName moduleName "Int32") []

let int64Type attributes : Type<'a> =
    reference attributes (toFQName moduleName "Int64") []

let moduleSpec: Module.Specification<unit> = {
    Types =
        Dict.fromList [
            namedTypeSpec
                "Int8"
                (OpaqueTypeSpecification [])
                "Type that represents a 8-bit integer."
            namedTypeSpec
                "Int16"
                (OpaqueTypeSpecification [])
                "Type that represents a 16-bit integer."
            namedTypeSpec
                "Int32"
                (OpaqueTypeSpecification [])
                "Type that represents a 32-bit integer."
            namedTypeSpec
                "Int64"
                (OpaqueTypeSpecification [])
                "Type that represents a 64-bit integer."
        ]
    Values =
        Dict.fromList [
            vSpec "fromInt8" [ "n", int8Type () ] (intType ())
            vSpec "toInt8" [ "n", intType () ] (int8Type ())
            vSpec "fromInt16" [ "n", int16Type () ] (intType ())
            vSpec "toInt16" [ "n", intType () ] (int16Type ())
            vSpec "fromInt32" [ "n", int32Type () ] (intType ())
            vSpec "toInt32" [ "n", intType () ] (int32Type ())
            vSpec "fromInt64" [ "n", int64Type () ] (intType ())
            vSpec "toInt64" [ "n", intType () ] (int64Type ())
        ]
    Doc =
        Just
            "Contains types that represent 8, 16, 32, or 64 bit integers, and functions tha convert between these and the general Int type."
}
