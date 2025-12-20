namespace Morphir.IR.Classic

/// <summary>
/// Type module provides the complete type system for Morphir IR, including
/// Type Expressions, Type Specifications, Type Definitions, and Field types.
/// All types support generic attributes for extensibility.
/// </summary>
module Type =

    open Morphir.IR.Name
    open Morphir.IR.FQName
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

