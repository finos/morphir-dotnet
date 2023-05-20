namespace Morphir

open Morphir.IR.FQName
open Morphir.IR.Name
open Morphir.IR
open Morphir.IR.Type
open Morphir.SDK.Dict
open Morphir.SDK

type IR =
    {
        ValueSpecifications: Dict<FQName, Value.Specification<unit>>
        ValueDefinitions: Dict<FQName, Value.Definition<unit, Type<unit>>>
        TypeSpecifications: Dict<FQName, Type.Specification<unit>>
        TypeConstructors: Dict<FQName, FQName * Name list * Type.ConstructorArgs<unit>>
    }

    /// Creates an empty IR with no types or values.
    static member Empty = {
        ValueSpecifications = Dict.empty
        ValueDefinitions = Dict.empty
        TypeSpecifications = Dict.empty
        TypeConstructors = Dict.empty
    }

module IR =

    /// Creates an empty IR with no types or values.
    let empty = IR.Empty
