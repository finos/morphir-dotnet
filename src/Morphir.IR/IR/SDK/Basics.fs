module Morphir.IR.SDK.Basics

open System
open Morphir.IR
open Morphir.SDK
open Morphir.SDK.Maybe
open Morphir.IR.Module
open Morphir.IR.Documented
open Morphir.IR.Type
open Morphir.IR.SDK.Common

let moduleName: ModuleName = Path.fromString "Basics"

let inline orderType attributes =
    Reference(attributes, (toFQName moduleName "Order"), [])

let inline floatType attributes : Type<'a> =
    Reference(attributes, (toFQName moduleName "Float"), [])

let moduleSpec: Module.Specification<unit> = {
    Types =
        Dict.fromList [
            Name.fromString "Int",
            OpaqueTypeSpecification []
            |> documented "Type that represents an integer value."
        ]
    Values =
        Dict.fromList [
            // number
            vSpec
                "add"
                [
                    ("a", tVar "number")
                    ("b", tVar "number")
                ]
                (tVar "number")
            vSpec
                "subtract"
                [
                    ("a", tVar "number")
                    ("b", tVar "number")
                ]
                (tVar "number")
            vSpec
                "multiply"
                [
                    ("a", tVar "number")
                    ("b", tVar "number")
                ]
                (tVar "number")
            vSpec
                "divide"
                [
                    ("a", floatType ())
                    ("b", floatType ())
                ]
                (floatType ())
        ]
    Doc = Just "Types and functions representing basic mathematical concepts and operations"
}
