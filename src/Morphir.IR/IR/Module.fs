module Morphir.IR.Module

open Morphir.IR.Name
open Morphir.IR.AccessControlled
open Morphir.SDK.Dict
open Morphir.SDK

type Specification<'A> =
    { Types: Dict<Name, Type.Specification<'A>>
      Values: Dict<Name, Value.Specification<'A>> }

type Definition<'A> =
    { Types: Dict<Name, AccessControlled<Type.Definition<'A>>>
      Values: Dict<Name, AccessControlled<Value.Definition<'A>>> }

let definitionToSpecification (def:Definition<'A>) :Specification<'A> =
    { Types =
        def.Types
            |> Dict.toList
            |> List.filterMap
                (fun ( path, accessControlledType ) ->
                    accessControlledType
                        |> withPublicAccess
                        |> Maybe.map
                            (fun typeDef ->
                                ( path, Type.definitionToSpecification typeDef )
                            )
                )
            |> Dict.fromList
      Values = Dict.empty }


module Specification =
    let empty =
        { Types = Map.empty
          Values = Map.empty }

type Definition<'A> with
    member this.ToSpecification():Specification<'A> =
        definitionToSpecification this

