module Morphir.IR.SDK.Rule

open Morphir.IR
open Morphir.IR.Module
open Morphir.IR.Type
open Morphir.SDK
open Morphir.SDK.Maybe
open Morphir.IR.SDK.Basics
open Morphir.IR.SDK.Maybe

let moduleName: ModuleName = Path.fromString "Rule"

let ruleType attributes itemType1 itemType2 =
    reference attributes (toFQName moduleName "Rule") [ itemType1; itemType2 ]

let ruleTypeSpec: Type.Specification<unit> =
    typeAliasSpecification [ [ "a" ]; [ "b" ] ] (tFun [ tVar "a" ] (maybeType () (tVar "b")))

let moduleSpec: Module.Specification<unit> = {
    Types = Dict.fromList [ namedTypeSpec "Rule" ruleTypeSpec "Type that represents a rule." ]
    Values =
        Dict.fromList [
            vSpec "any" [ ("value", tVar "a") ] (boolType ())
            vSpec "is" [ ("ref", tVar "a"); ("value", tVar "a") ] (boolType ())
        ]
    Doc = Just "Contains the rule type, and related functions"
}
