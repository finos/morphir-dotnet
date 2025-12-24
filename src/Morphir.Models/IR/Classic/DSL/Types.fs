namespace Morphir.IR.Classic.DSL

/// <summary>
/// Types module provides Computation Expression builders for creating Type-related IR constructs.
/// </summary>
module Types =

    open Morphir.IR.Name
    open Morphir.IR.FQName
    open Morphir.IR.Classic.Type

    /// <summary>
    /// TypeBuilder provides a Computation Expression for creating Type values.
    /// Defaults to unit () for attributes.
    /// </summary>
    type TypeBuilder<'attributes>() =
        let defaultAttrs = Unchecked.defaultof<'attributes>

        /// <summary>
        /// Yields a Type directly.
        /// </summary>
        member _.Yield(typ: Type<'attributes>) = typ

        /// <summary>
        /// Combines multiple Types (takes the last one).
        /// </summary>
        member _.Combine(_: Type<'attributes>, typ: Type<'attributes>) = typ

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> Type<'attributes>) =
            items |> Seq.map f |> Seq.last

        /// <summary>
        /// Zero case (Unit type).
        /// </summary>
        member _.Zero() = Unit defaultAttrs

        /// <summary>
        /// Creates a Variable type (type variable/generic parameter).
        /// </summary>
        member _.Variable(name: Name) = Variable(defaultAttrs, name)

        /// <summary>
        /// Creates a Variable type from a string.
        /// </summary>
        member _.Variable(str: string) =
            Variable(defaultAttrs, Morphir.IR.Name.fromString str)

        /// <summary>
        /// Creates a Reference type (reference to another type).
        /// </summary>
        member _.Reference(fqName: FQName) = Reference(defaultAttrs, fqName, [])

        /// <summary>
        /// Creates a Reference type with type arguments.
        /// </summary>
        member _.Reference(fqName: FQName, typeArgs: Type<'attributes> list) =
            Reference(defaultAttrs, fqName, typeArgs)

        /// <summary>
        /// Creates a Tuple type (composition of multiple types).
        /// </summary>
        member _.Tuple(elementTypes: Type<'attributes> list) =
            Tuple(defaultAttrs, elementTypes)

        /// <summary>
        /// Creates a Record type (named fields).
        /// </summary>
        member _.Record(fields: Field<'attributes> list) =
            Record(defaultAttrs, fields)

        /// <summary>
        /// Creates an ExtensibleRecord type (record that can be extended).
        /// </summary>
        member _.ExtensibleRecord(variableName: Name, fields: Field<'attributes> list) =
            ExtensibleRecord(defaultAttrs, variableName, fields)

        /// <summary>
        /// Creates an ExtensibleRecord type with variable name from string.
        /// </summary>
        member _.ExtensibleRecord(variableName: string, fields: Field<'attributes> list) =
            ExtensibleRecord(
                defaultAttrs,
                Morphir.IR.Name.fromString variableName,
                fields
            )

        /// <summary>
        /// Creates a Function type (arg -> return).
        /// </summary>
        member _.FunctionType(argType: Type<'attributes>, returnType: Type<'attributes>) =
            Function(defaultAttrs, argType, returnType)

        /// <summary>
        /// Creates a Unit type.
        /// </summary>
        member _.Unit() = Unit defaultAttrs

        /// <summary>
        /// Creates a Field for use in Record types.
        /// </summary>
        member _.Field(name: Name, typ: Type<'attributes>) = field name typ

        /// <summary>
        /// Creates a Field from a string name.
        /// </summary>
        member _.Field(name: string, typ: Type<'attributes>) =
            field (Morphir.IR.Name.fromString name) typ

    /// <summary>
    /// TypeBuilderWithAttrs provides a Computation Expression for creating Type values with explicit attributes.
    /// </summary>
    type TypeBuilderWithAttrs<'attributes>(attrs: 'attributes) =
        /// <summary>
        /// Yields a Type directly.
        /// </summary>
        member _.Yield(typ: Type<'attributes>) = typ

        /// <summary>
        /// Combines multiple Types (takes the last one).
        /// </summary>
        member _.Combine(_: Type<'attributes>, typ: Type<'attributes>) = typ

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> Type<'attributes>) =
            items |> Seq.map f |> Seq.last

        /// <summary>
        /// Zero case (Unit type).
        /// </summary>
        member _.Zero() = Unit attrs

        /// <summary>
        /// Creates a Variable type (type variable/generic parameter).
        /// </summary>
        member _.Variable(name: Name) = Variable(attrs, name)

        /// <summary>
        /// Creates a Variable type from a string.
        /// </summary>
        member _.Variable(str: string) =
            Variable(attrs, Morphir.IR.Name.fromString str)

        /// <summary>
        /// Creates a Reference type (reference to another type).
        /// </summary>
        member _.Reference(fqName: FQName) = Reference(attrs, fqName, [])

        /// <summary>
        /// Creates a Reference type with type arguments.
        /// </summary>
        member _.Reference(fqName: FQName, typeArgs: Type<'attributes> list) =
            Reference(attrs, fqName, typeArgs)

        /// <summary>
        /// Creates a Tuple type (composition of multiple types).
        /// </summary>
        member _.Tuple(elementTypes: Type<'attributes> list) =
            Tuple(attrs, elementTypes)

        /// <summary>
        /// Creates a Record type (named fields).
        /// </summary>
        member _.Record(fields: Field<'attributes> list) =
            Record(attrs, fields)

        /// <summary>
        /// Creates an ExtensibleRecord type (record that can be extended).
        /// </summary>
        member _.ExtensibleRecord(variableName: Name, fields: Field<'attributes> list) =
            ExtensibleRecord(attrs, variableName, fields)

        /// <summary>
        /// Creates an ExtensibleRecord type with variable name from string.
        /// </summary>
        member _.ExtensibleRecord(variableName: string, fields: Field<'attributes> list) =
            ExtensibleRecord(attrs, Morphir.IR.Name.fromString variableName, fields)

        /// <summary>
        /// Creates a Function type (arg -> return).
        /// </summary>
        member _.Function(argType: Type<'attributes>, returnType: Type<'attributes>) =
            Function(attrs, argType, returnType)

        /// <summary>
        /// Creates a Unit type.
        /// </summary>
        member _.Unit() = Unit attrs

        /// <summary>
        /// Creates a Field for use in Record types.
        /// </summary>
        member _.Field(name: Name, typ: Type<'attributes>) = field name typ

        /// <summary>
        /// Creates a Field from a string name.
        /// </summary>
        member _.Field(name: string, typ: Type<'attributes>) =
            field (Morphir.IR.Name.fromString name) typ

    /// <summary>
    /// TypeBuilder for unit attributes.
    /// </summary>
    type TypeBuilder() =
        inherit TypeBuilder<unit>()

        /// <summary>
        /// Sets explicit attributes for the builder.
        /// </summary>
        member _.WithAttributes<'a>(attrs: 'a) = TypeBuilderWithAttrs<'a>(attrs)

    /// <summary>
    /// Global builder instance for use in Computation Expressions.
    /// </summary>
    let type' = TypeBuilder()

    /// <summary>
    /// Alias for type' builder for convenience.
    /// </summary>
    let irType = type'

