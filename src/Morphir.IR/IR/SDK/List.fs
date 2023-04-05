module Morphir.IR.SDK.List

open Morphir.IR
open Morphir.IR.Module
open Morphir.IR.Type
open Morphir.IR.Value
open Morphir.SDK.Maybe
open Morphir.SDK
open Morphir.IR.SDK.Basics

let moduleName: ModuleName = Path.fromString "List"

let listType attributes itemType =
    reference attributes (toFQName moduleName "List") [ itemType ]

let construct (attributes: 'va) : Value<_, 'va> =
    Value.Reference(attributes, toFQName moduleName "cons")

let moduleSpec: Module.Specification<unit> = {
    Types =
        Dict.fromList [
            namedTypeSpec
                "List"
                (OpaqueTypeSpecification [ Name.fromString "a" ])
                "Type that represents a list of values."
        ]
    Values =
        Dict.fromList [
            vSpec "singleton" [ "a", tVar "a" ] (listType () (tVar "a"))
            vSpec "repeat" [ "n", intType (); "a", tVar "a" ] (listType () (tVar "a"))
            vSpec "range" [ "from", intType (); "to", intType () ] (listType () (intType ()))
            vSpec
                "cons"
                [ "head", tVar "a"; "tail", listType () (tVar "a") ]
                (listType () (tVar "a"))
        ]
    Doc =
        Just
            "Contains the List type (representing a list of values), and it's associated functions."
}
