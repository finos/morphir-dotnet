module Morphir.IR.SDK.Maybe

open Morphir.IR.SDK.Common
open Morphir.IR
open Morphir.IR.Module
open Morphir.IR.Value
open Morphir.SDK
open Morphir.SDK.Maybe
open Morphir.IR.Type

open type Type.Type

let moduleName: ModuleName = Path.fromString "Maybe"

let maybeType attributes itemType =
    Reference(attributes, toFQName moduleName "Maybe", itemType)

let private maybeTypeSpec: Type.Specification<unit> =
    let justConstructor =
        Constructor.Create("Just", ("value", variable () (Name.fromString "a")))

    let nothingConstructor = Constructor.Create("Nothing")
    Specification.Custom([ "a" ], justConstructor, nothingConstructor)

let moduleSpec: Module.Specification<unit> = {
    Types =
        Dict.fromList [
            namedTypeSpec
                "Maybe"
                maybeTypeSpec
                "Represents the relative ordering of two things. The relations are less than, equal to, and greater than."
        ]
    Values = Dict.fromList []
    Doc = Just "Type that represents an optional value."
}

let just (va: 'va) v : Value<_, 'va> =
    Value.Apply(va, (Value.Constructor(va, (toFQName moduleName "Just"))), v)

let nothing (va: 'va) : Value<_, 'va> =
    Value.Constructor(va, toFQName moduleName "Nothing")
