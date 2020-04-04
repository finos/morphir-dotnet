module Morphir.IR.Type

open Morphir.IR.AccessControlled
open Morphir.IR.Name
open Morphir.IR.FQName
open Morphir.SDK.Maybe
open Morphir.IR

type Type<'A>
    = Variable of Attributes:'A * Name:Name
    | Reference of Attributes:'A * TypeName:FQName * TypeParameters:Type<'A> list
    | Tuple of Attributes:'A * ElementTypes:Type<'A> list
    | Record of Attributes:'A * FieldTypes:Field<'A> list
    | ExtensibleRecord of  Attributes:'A * VariableName:Name *  FieldTypes:Field<'A> list
    | Function of Attributes:'A * ArgumentType:Type<'A> * ReturnType:Type<'A>
    | Unit of Attributes:'A with
    member this.Attributes with get() =
        match this with
            | Variable (a, _) -> a
            | Reference (a, _, _) -> a
            | Tuple (a, _) -> a
            | Record (a, _) -> a
            | ExtensibleRecord (a, _,_) -> a
            | Function (a, _, _) -> a
            | Unit a -> a

    interface Expression<'A> with
        member this.Attributes with get() = this.Attributes

and Field<'A> =
    { Name: Name
      Type: Type<'A> }
and Constructor<'A>
    = Constructor of Name * (Name * Type<'A>) list
and Constructors<'A>
    = Constructor<'A> list
and Specification<'A>
    = TypeAliasSpecification of TypeParams:Name list * TypeExpr:Type<'A>
    | OpaqueTypeSpecification of TypeParams:Name list
    | CustomTypeSpecification of TypeParams:Name list * Constructors:Constructors<'A>
and Definition<'A>
    = TypeAliasDefinition of TypeParams:Name list * TypeExpr:Type<'A>
    | CustomTypeDefinition of TypeParams:Name list * Constructors:AccessControlled<Constructors<'A>>

let definitionToSpecification (def:Definition<'A>) : Specification<'A> =
    match def with
    | TypeAliasDefinition(p, exp) ->
        TypeAliasSpecification(p, exp)
    | CustomTypeDefinition(p, accessControlledCtors) ->
        match accessControlledCtors |> withPublicAccess with
        | Just ctors ->
                CustomTypeSpecification(p, ctors)
        | Nothing ->
                OpaqueTypeSpecification p

let variable attributes name =
    Variable(attributes, name)

let reference attributes typeName typeParameters =
    Reference(attributes, typeName, typeParameters)

let tuple attributes elementTypes =
    Tuple(attributes, elementTypes)

let record attributes fieldTypes =
    Record(attributes, fieldTypes)

let extensibleRecord attributes variableName fieldTypes =
    ExtensibleRecord(attributes, variableName, fieldTypes)

let ``function`` attributes argumentType returnType =
    Function(attributes, argumentType, returnType)

let unit attributes =
    Unit(attributes)

let typeAliasDefinition typeParams typeExp =
    TypeAliasDefinition(typeParams, typeExp)

let customTypeDefinition typeParams ctors =
    CustomTypeDefinition(typeParams, ctors)

let typeAliasSpecification typeParams typeExp =
    TypeAliasSpecification(typeParams, typeExp)

let opaqueTypeSpecification typeParams =
    OpaqueTypeSpecification typeParams

let customTypeSpecification typeParams ctors =
    CustomTypeSpecification(typeParams, ctors)

let typeAttributes (typeExpr:Type<'Attributes>) : 'Attributes =
    typeExpr.Attributes
