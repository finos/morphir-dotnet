namespace Morphir.IR.Classic

open Morphir.IR
open AccessControlled

/// <summary>
/// Type represents a type expression in the Morphir IR.
/// Each variant includes attributes as the first parameter.
/// </summary>
type Type<'attributes> =
    | Variable of 'attributes * Name
    | Reference of 'attributes * FQName * Type<'attributes> list
    | Tuple of 'attributes * Type<'attributes> list
    | Record of 'attributes * Field<'attributes> list
    | ExtensibleRecord of 'attributes * Name * Field<'attributes> list
    | Function of 'attributes * Type<'attributes> * Type<'attributes>
    | Unit of 'attributes

/// <summary>
/// Field represents a named field in a Record or ExtensibleRecord type.
/// </summary>
and Field<'attributes> = { Name: Name; Type: Type<'attributes> }

/// <summary>
/// Type module provides helper functions for the complete type system for Morphir IR.
/// All types support generic attributes for extensibility.
/// </summary>
[<RequireQualifiedAccess>]
module Type =

    /// <summary>
    /// Constructors represents a map of constructor names to their arguments.
    /// Used in CustomTypeSpecification and CustomTypeDefinition.
    /// </summary>
    type Constructors<'attributes> = Map<Name, (Name * Type<'attributes>) list>

    /// <summary>
    /// DerivedTypeDetails contains information for a derived type specification.
    /// </summary>
    type DerivedTypeDetails<'attributes> =
        { BaseType: Type<'attributes>
          FromBaseType: FQName
          ToBaseType: FQName }

    /// <summary>
    /// TypeSpecification defines the interface of a type without implementation details.
    /// </summary>
    type TypeSpecification<'attributes> =
        | TypeAliasSpecification of Name list * Type<'attributes>
        | OpaqueTypeSpecification of Name list
        | CustomTypeSpecification of Name list * Constructors<'attributes>
        | DerivedTypeSpecification of Name list * DerivedTypeDetails<'attributes>

    /// <summary>
    /// TypeDefinition provides the complete implementation of a type.
    /// </summary>
    type TypeDefinition<'attributes> =
        | TypeAliasDefinition of Name list * Type<'attributes>
        | CustomTypeDefinition of Name list * AccessControlled<Constructors<'attributes>>

    // Helper functions for Field

    /// <summary>
    /// Creates a Field from a name and type.
    /// </summary>
    let field<'attributes> (name: Name) (typ: Type<'attributes>) : Field<'attributes> =
        { Name = name; Type = typ }

    // Helper functions for Type Expressions

    /// <summary>
    /// Creates a Variable type (type variable/generic parameter).
    /// </summary>
    let variable<'attributes> (attributes: 'attributes) (name: Name) : Type<'attributes> =
        Variable(attributes, name)

    /// <summary>
    /// Creates a Reference type (reference to another type).
    /// </summary>
    let reference<'attributes> (attributes: 'attributes) (fqName: FQName) (typeArgs: Type<'attributes> list) : Type<'attributes> =
        Reference(attributes, fqName, typeArgs)

    /// <summary>
    /// Creates a Tuple type (composition of multiple types).
    /// </summary>
    let tuple<'attributes> (attributes: 'attributes) (elementTypes: Type<'attributes> list) : Type<'attributes> =
        Tuple(attributes, elementTypes)

    /// <summary>
    /// Creates a Record type (named fields).
    /// </summary>
    let record<'attributes> (attributes: 'attributes) (fields: Field<'attributes> list) : Type<'attributes> =
        Record(attributes, fields)

    /// <summary>
    /// Creates an ExtensibleRecord type (record that can be extended).
    /// </summary>
    let extensibleRecord<'attributes> (attributes: 'attributes) (variableName: Name) (fields: Field<'attributes> list) : Type<'attributes> =
        ExtensibleRecord(attributes, variableName, fields)

    /// <summary>
    /// Creates a Function type (arg -> return).
    /// </summary>
    let functionType<'attributes> (attributes: 'attributes) (argType: Type<'attributes>) (returnType: Type<'attributes>) : Type<'attributes> =
        Function(attributes, argType, returnType)

    /// <summary>
    /// Creates a Unit type.
    /// </summary>
    let unit<'attributes> (attributes: 'attributes) : Type<'attributes> =
        Unit attributes


    // Helper functions for Type Specifications

    /// <summary>
    /// Creates a TypeAliasSpecification.
    /// </summary>
    let typeAliasSpecification<'attributes> (typeParams: Name list) (typ: Type<'attributes>) : TypeSpecification<'attributes> =
        TypeAliasSpecification(typeParams, typ)

    /// <summary>
    /// Creates an OpaqueTypeSpecification.
    /// </summary>
    let opaqueTypeSpecification<'attributes> (typeParams: Name list) : TypeSpecification<'attributes> =
        OpaqueTypeSpecification typeParams

    /// <summary>
    /// Creates a CustomTypeSpecification.
    /// </summary>
    let customTypeSpecification<'attributes> (typeParams: Name list) (constructors: Constructors<'attributes>) : TypeSpecification<'attributes> =
        CustomTypeSpecification(typeParams, constructors)

    /// <summary>
    /// Creates a DerivedTypeSpecification.
    /// </summary>
    let derivedTypeSpecification<'attributes> (typeParams: Name list) (details: DerivedTypeDetails<'attributes>) : TypeSpecification<'attributes> =
        DerivedTypeSpecification(typeParams, details)

    // Helper functions for Type Definitions

    /// <summary>
    /// Creates a TypeAliasDefinition.
    /// </summary>
    let typeAliasDefinition<'attributes> (typeParams: Name list) (typ: Type<'attributes>) : TypeDefinition<'attributes> =
        TypeAliasDefinition(typeParams, typ)

    /// <summary>
    /// Creates a CustomTypeDefinition.
    /// </summary>
    let customTypeDefinition<'attributes> (typeParams: Name list) (constructors: AccessControlled<Constructors<'attributes>>) : TypeDefinition<'attributes> =
        CustomTypeDefinition(typeParams, constructors)

    // toString functions

    /// <summary>
    /// Converts a Type to its string representation.
    /// </summary>
    let rec toString (typ: Type<'attributes>) : string =
        let rec renderTypeArg arg =
            match arg with
            | Function _ -> $"({toString arg})"
            | _ -> toString arg

        let fieldToString (field: Field<'attributes>) : string =
            $"{Name.toCamelCase field.Name} : {toString field.Type}"

        match typ with
        | Variable(_, name) -> Name.toCamelCase name
        | Reference(_, fqName, typeArgs) ->
            let nameText = FQName.toString fqName
            match typeArgs with
            | [] -> nameText
            | _ ->
                let argsText =
                    typeArgs
                    |> List.map renderTypeArg
                    |> String.concat " "
                $"{nameText} {argsText}"
        | Tuple(_, elements) ->
            let elementsText =
                elements
                |> List.map toString
                |> String.concat ", "
            $"({elementsText})"
        | Record(_, fields) ->
            let fieldsText =
                fields
                |> List.map fieldToString
                |> String.concat ", "
            $"{{ {fieldsText} }}"
        | ExtensibleRecord(_, variableName, fields) ->
            let fieldsText =
                fields
                |> List.map fieldToString
                |> String.concat ", "
            $"{{ {Name.toCamelCase variableName} | {fieldsText} }}"
        | Function(_, argumentType, returnType) ->
            let argumentText =
                match argumentType with
                | Function _ -> $"({toString argumentType})"
                | _ -> toString argumentType
            $"{argumentText} -> {toString returnType}"
        | Unit _ -> "()"

    /// <summary>
    /// Converts a Field to its string representation.
    /// </summary>
    let fieldToString (field: Field<'attributes>) : string =
        $"{Name.toCamelCase field.Name} : {toString field.Type}"

    /// <summary>
    /// Converts a TypeSpecification to its string representation.
    /// </summary>
    module TypeSpecification =
        let toString (spec: TypeSpecification<'attributes>) : string =
            let formatTypeParams (params': Name list) : string =
                match params' with
                | [] -> ""
                | _ ->
                    params'
                    |> List.map Name.toCamelCase
                    |> String.concat " "

            match spec with
            | TypeAliasSpecification(typeParams, typ) ->
                let paramsText = formatTypeParams typeParams
                match paramsText with
                | "" -> $"type alias = {toString typ}"
                | _ -> $"type alias {paramsText} = {toString typ}"
            | OpaqueTypeSpecification typeParams ->
                let paramsText = formatTypeParams typeParams
                match paramsText with
                | "" -> "type"
                | _ -> $"type {paramsText}"
            | CustomTypeSpecification(typeParams, constructors) ->
                let paramsText = formatTypeParams typeParams
                let constructorsText =
                    constructors
                    |> Map.toList  // Map.toList already returns entries in key-sorted order (deterministic)
                    |> List.map (fun (ctorName, args) ->
                        let argsText =
                            args
                            |> List.map (fun (argName, _) ->
                                // Only include argument name, not type annotation (matches test expectations)
                                Name.toCamelCase argName)
                            |> String.concat " "
                        match argsText with
                        | "" -> Name.toTitleCase ctorName
                        | _ -> $"{Name.toTitleCase ctorName} {argsText}")
                    |> String.concat " | "
                match paramsText with
                | "" -> $"type = {constructorsText}"
                | _ -> $"type {paramsText} = {constructorsText}"
            | DerivedTypeSpecification(typeParams, details) ->
                let paramsText = formatTypeParams typeParams
                match paramsText with
                | "" -> $"type derived from {toString details.BaseType}"
                | _ -> $"type {paramsText} derived from {toString details.BaseType}"

    /// <summary>
    /// Converts a TypeDefinition to its string representation.
    /// </summary>
    module TypeDefinition =
        let toString (def: TypeDefinition<'attributes>) : string =
            let formatTypeParams (params': Name list) : string =
                match params' with
                | [] -> ""
                | _ ->
                    params'
                    |> List.map Name.toCamelCase
                    |> String.concat " "

            match def with
            | TypeAliasDefinition(typeParams, typ) ->
                let paramsText = formatTypeParams typeParams
                match paramsText with
                | "" -> $"type alias = {toString typ}"
                | _ -> $"type alias {paramsText} = {toString typ}"
            | CustomTypeDefinition(typeParams, accessControlled) ->
                let paramsText = formatTypeParams typeParams
                match accessControlled.Access with
                | AccessControlled.Public ->
                    let constructors = accessControlled.Value
                    let constructorsText =
                        constructors
                        |> Map.toList  // Map.toList already returns entries in key-sorted order (deterministic)
                        |> List.map (fun (ctorName, args) ->
                            let argsText =
                                args
                                |> List.map (fun (argName, _) ->
                                    // Only include argument name, not type annotation (matches test expectations)
                                    Name.toCamelCase argName)
                                |> String.concat " "
                            match argsText with
                            | "" -> Name.toTitleCase ctorName
                            | _ -> $"{Name.toTitleCase ctorName} {argsText}")
                        |> String.concat " | "
                    match paramsText with
                    | "" -> $"type = {constructorsText}"
                    | _ -> $"type {paramsText} = {constructorsText}"
                | AccessControlled.Private ->
                    match paramsText with
                    | "" -> "type"
                    | _ -> $"type {paramsText}"

/// <summary>
/// TypeExtensions provides extension methods and operators for Type that are available in both F# and C#.
/// This module is marked AutoOpen so extensions are available whenever Morphir.IR.Classic is opened.
/// </summary>
[<AutoOpen>]
module TypeExtensions =

    open System.Runtime.CompilerServices

    /// <summary>
    /// F# type extensions for Type (natural F# style).
    /// </summary>
    type Type<'attributes> with
        /// <summary>
        /// Fluent method to create a Function type from an existing type.
        /// Creates a function type where this type is the argument and the provided type is the return type.
        /// Uses default attributes.
        /// </summary>
        /// <param name="returnType">The return type of the function</param>
        member this.Arrow(returnType: Type<'attributes>) : Type<'attributes> =
            Function(Unchecked.defaultof<'attributes>, this, returnType)

        /// <summary>
        /// Fluent method to create a Function type with explicit attributes.
        /// </summary>
        /// <param name="attributes">The attributes for the Function type</param>
        /// <param name="returnType">The return type of the function</param>
        member this.Arrow(attributes: 'attributes, returnType: Type<'attributes>) : Type<'attributes> =
            Function(attributes, this, returnType)

    /// <summary>
    /// C#-visible extension methods for Type (using Extension attribute).
    /// These make the same methods available in C# as extension methods.
    /// </summary>
    [<Extension>]
    type TypeExtensionsForCSharp =
        /// <summary>
        /// Fluent method to create a Function type from an existing type (C# extension).
        /// Creates a function type where this type is the argument and the provided type is the return type.
        /// Uses default attributes.
        /// </summary>
        [<Extension>]
        static member Arrow(this: Type<'attributes>, returnType: Type<'attributes>) : Type<'attributes> =
            Function(Unchecked.defaultof<'attributes>, this, returnType)

        /// <summary>
        /// Fluent method to create a Function type with explicit attributes (C# extension).
        /// </summary>
        [<Extension>]
        static member Arrow(this: Type<'attributes>, attributes: 'attributes, returnType: Type<'attributes>) : Type<'attributes> =
            Function(attributes, this, returnType)

    /// <summary>
    /// Right-associative operator for creating function types.
    /// Usage: intType ^-> stringType creates Int -> String
    /// Chains correctly: intType ^-> charType ^-> stringType creates Int -> (Char -> String)
    /// Note: Uses ^-> instead of :-> because : is reserved in F# for type annotations
    /// This operator is F#-only (not available in C#).
    /// </summary>
    let (^->) (argType: Type<'attributes>) (returnType: Type<'attributes>) : Type<'attributes> =
        Function(Unchecked.defaultof<'attributes>, argType, returnType)

