module rec Morphir.IR.Type

open Morphir.Pattern
open Morphir.IR.AccessControlled
open Morphir.IR.Name
open Morphir.IR.FQName
open Morphir.SDK.Dict
open Morphir.SDK.Maybe
open Morphir.IR
open Morphir.SDK

type Type<'A> =
    | Variable of Attributes: 'A * Name: Name
    | Reference of Attributes: 'A * TypeName: FQName * TypeParameters: Type<'A> list
    | Tuple of Attributes: 'A * ElementTypes: Type<'A> list
    | Record of Attributes: 'A * FieldTypes: Field<'A> list
    | ExtensibleRecord of Attributes: 'A * VariableName: Name * FieldTypes: Field<'A> list
    | Function of Attributes: 'A * ArgumentType: Type<'A> * ReturnType: Type<'A>
    | Unit of Attributes: 'A

    member this.Attributes =
        match this with
        | Variable (a, _) -> a
        | Reference (a, _, _) -> a
        | Tuple (a, _) -> a
        | Record (a, _) -> a
        | ExtensibleRecord (a, _, _) -> a
        | Function (a, _, _) -> a
        | Unit a -> a

    interface Expression<'A> with
        member this.Attributes = this.Attributes

type Field<'A> = { Name: Name; Type: Type<'A> }

type Constructors<'A> = Dict<Name, ConstructorArgs<'A>>
type Constructor<'a> = Name * ConstructorArgs<'a>

type ConstructorArgs<'a> = List<Name * Type<'a>>

/// <summary>
/// Represents the specification (in other words the interface) of a type. There are 4 different shapes:
/// <see cref="TypeAliasSpecification"/>, <see cref="OpaqueTypeSpecification"/>, <see cref="CustomTypeSpecification"/>, and <see cref="DerivedTypeSpecification"/>.
/// </summary>
type Specification<'A> =
    /// <summary>
    /// Represents an alias for another type.
    /// </summary>
    /// <example>
    /// <code>
    /// type Foo = String
    /// </code>
    /// </example>
    | TypeAliasSpecification of TypeParams: Name list * TypeExpr: Type<'A>
    /// Represents a type with an unknown structure.
    /// In Elm you could achieve this with a custom type that doesn't expose its constructors.
    | OpaqueTypeSpecification of TypeParams: Name list
    | CustomTypeSpecification of TypeParams: Name list * Constructors: Constructors<'A>
    | DerivedTypeSpecification of TypeParams: Name list * MappingInfo: DerivedTypeMappingInfo<'A>

and DerivedTypeMappingInfo<'A> = {
    BaseType: Type<'A>
    FromBaseType: FQName
    ToBaseType: FQName
}

and Definition<'A> =
    | TypeAliasDefinition of TypeParams: Name list * TypeExpr: Type<'A>
    | CustomTypeDefinition of
        TypeParams: Name list *
        Constructors: AccessControlled<Constructors<'A>>

type Field<'A> with

    member this.MapName f = { this with Name = f (this.Name) }

    member this.MapType f = {
        Name = this.Name
        Type = f (this.Type)
    }


let definitionToSpecification (def: Definition<'A>) : Specification<'A> =
    match def with
    | TypeAliasDefinition (p, exp) -> TypeAliasSpecification(p, exp)
    | CustomTypeDefinition (p, accessControlledCtors) ->
        match
            accessControlledCtors
            |> withPublicAccess
        with
        | Just ctors -> CustomTypeSpecification(p, ctors)
        | Nothing -> OpaqueTypeSpecification p

let definitionToSpecificationWithPrivate (def: Definition<'A>) : Specification<'A> =
    match def with
    | TypeAliasDefinition (p, exp) -> TypeAliasSpecification(p, exp)
    | CustomTypeDefinition (p, accessControlledCtors) ->
        accessControlledCtors
        |> withPrivateAccess
        |> CustomTypeSpecification(p, _)
let variable attributes name = Variable(attributes, name)

let reference attributes typeName typeParameters =
    Reference(attributes, typeName, typeParameters)

let tuple attributes elementTypes = Tuple(attributes, elementTypes)

let record attributes fieldTypes = Record(attributes, fieldTypes)

let extensibleRecord attributes variableName fieldTypes =
    ExtensibleRecord(attributes, variableName, fieldTypes)

let ``function`` attributes argumentType returnType =
    Function(attributes, argumentType, returnType)

let unit attributes = Unit(attributes)

let typeAliasDefinition typeParams typeExp =
    TypeAliasDefinition(typeParams, typeExp)

let customTypeDefinition typeParams ctors = CustomTypeDefinition(typeParams, ctors)

let typeAliasSpecification typeParams typeExp =
    TypeAliasSpecification(typeParams, typeExp)

let opaqueTypeSpecification typeParams = OpaqueTypeSpecification typeParams

let customTypeSpecification typeParams ctors =
    CustomTypeSpecification(typeParams, ctors)

let field name fieldType = { Name = name; Type = fieldType }

let mapFieldName f (field: Field<'A>) = field.MapName f

let mapFieldType f (field: Field<'A>) : Field<'B> = field.MapType f

let matchField (matchFieldName: Pattern<Name, 'A>) (matchFieldType: Pattern<Type<'A>, 'B>) field =
    Maybe.map2 Tuple.pair (matchFieldName field.Name) (matchFieldType field.Type)

let rec mapTypeAttributes (f: 'a -> 'b) : Type<'a> -> Type<'b> =
    function
    | Variable (a, name) -> Variable(f (a), name)
    | Reference (a, fQName, argTypes) ->
        let newArgTypes =
            argTypes
            |> List.map (mapTypeAttributes f)

        Reference(f (a), fQName, newArgTypes)

    | Tuple (a, elemTypes) ->
        Tuple(
            (f a),
            (elemTypes
             |> List.map (mapTypeAttributes f))
        )

    | Record (a, fields) ->
        Record(
            (f a),
            (fields
             |> List.map (mapFieldType (mapTypeAttributes f)))
        )

    | ExtensibleRecord (a, name, fields) ->
        ExtensibleRecord(
            (f a),
            name,
            (fields
             |> List.map (mapFieldType (mapTypeAttributes f)))
        )

    | Function (a, argType, returnType) ->
        Function(
            (f a),
            (argType
             |> mapTypeAttributes f),
            (returnType
             |> mapTypeAttributes f)
        )
    | Unit a -> Unit(f a)

let typeAttributes (typeExpr: Type<'Attributes>) : 'Attributes = typeExpr.Attributes

let eraseAttributes: Definition<'a> -> Definition<unit> =
    function
    | TypeAliasDefinition (typeVars, tpe) ->
        TypeAliasDefinition(typeVars, mapTypeAttributes (fun _ -> ()) tpe)
    | CustomTypeDefinition (typeVars, acsCtrlConstructors) ->
        let eraseCtor (types: ('Name * Type<'a>) list) : ('Name * Type<unit>) list =
            types
            |> List.map (fun (n, t) -> (n, mapTypeAttributes (fun _ -> ()) t))

        let eraseAccessControlledCtors acsCtrlCtors =
            AccessControlled.map
                (fun ctors ->
                    ctors
                    |> Dict.map (fun _ -> eraseCtor)
                )
                acsCtrlCtors

        CustomTypeDefinition(typeVars, eraseAccessControlledCtors acsCtrlConstructors)

type Definition<'A> with

    member this.EraseAttributes() = eraseAttributes this
