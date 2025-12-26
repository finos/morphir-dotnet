namespace Morphir.IR.Classic.DSL

/// <summary>
/// Types module provides Computation Expression builders for creating Type-related IR constructs.
/// </summary>
module Types =

    open Morphir.IR
    open Morphir.IR.Classic

    /// <summary>
    /// TypeBuilder provides a Computation Expression for creating Type values.
    /// Defaults to unit () for attributes.
    /// </summary>
    type TypeBuilder<'attributes>() =
        let defaultAttrs = Unchecked.defaultof<'attributes>

        /// <summary>
        /// Yields a Type directly.
        /// </summary>
        member _.Yield(typ: Type.Type<'attributes>) = typ

        /// <summary>
        /// Yields unit as Type.Unit (tagless syntax).
        /// </summary>
        member _.Yield((): unit) = Type.Unit defaultAttrs

        /// <summary>
        /// Combines multiple Types (takes the last one).
        /// </summary>
        member _.Combine(_: Type.Type<'attributes>, typ: Type.Type<'attributes>) = typ

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> Type.Type<'attributes>) =
            items |> Seq.map f |> Seq.last

        /// <summary>
        /// Zero case (Unit type).
        /// </summary>
        member _.Zero() = Type.Unit defaultAttrs

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> Type.Type<'attributes>) = f

        /// <summary>
        /// Runs the builder to produce the final Type.
        /// </summary>
        member _.Run(f: unit -> Type.Type<'attributes>) = f()

        /// Regular methods for direct usage (lowercase Fun.Blazor style)

        /// <summary>
        /// Creates a Variable type (type variable/generic parameter).
        /// </summary>
        member _.variable(name: Name) = Type.Variable(defaultAttrs, name)

        /// <summary>
        /// Creates a Variable type from a string.
        /// </summary>
        member _.variable(str: string) =
            Type.Variable(defaultAttrs, Name.fromString str)

        /// <summary>
        /// Creates a Reference type (reference to another type).
        /// </summary>
        member _.reference(fqName: FQName) = Type.Reference(defaultAttrs, fqName, [])

        /// <summary>
        /// Creates a Reference type with type arguments.
        /// </summary>
        member _.reference(fqName: FQName, typeArgs: Type.Type<'attributes> list) =
            Type.Reference(defaultAttrs, fqName, typeArgs)

        /// <summary>
        /// Creates a Tuple type (composition of multiple types).
        /// </summary>
        member _.tuple(elementTypes: Type.Type<'attributes> list) =
            Type.Tuple(defaultAttrs, elementTypes)

        /// <summary>
        /// Creates a Record type (named fields).
        /// </summary>
        member _.record(fields: Type.Field<'attributes> list) =
            Type.Record(defaultAttrs, fields)

        /// <summary>
        /// Creates an ExtensibleRecord type (record that can be extended).
        /// </summary>
        member _.extensibleRecord(variableName: Name, fields: Type.Field<'attributes> list) =
            Type.ExtensibleRecord(defaultAttrs, variableName, fields)

        /// <summary>
        /// Creates an ExtensibleRecord type with variable name from string.
        /// </summary>
        member _.extensibleRecord(variableName: string, fields: Type.Field<'attributes> list) =
            Type.ExtensibleRecord(
                defaultAttrs,
                Name.fromString variableName,
                fields
            )

        /// <summary>
        /// Creates a Function type (arg -> return).
        /// </summary>
        member _.functionType(argType: Type.Type<'attributes>, returnType: Type.Type<'attributes>) =
            Type.Function(defaultAttrs, argType, returnType)

        /// <summary>
        /// Creates a Function type from parameter list and return type.
        /// Chains parameters correctly: func [Int; Char] String = Int -> Char -> String
        /// </summary>
        member _.func(paramTypes: Type.Type<'attributes> list, returnType: Type.Type<'attributes>) =
            List.foldBack (fun param acc -> Type.Function(defaultAttrs, param, acc)) paramTypes returnType

        /// <summary>
        /// Creates a Unit type.
        /// </summary>
        member _.unit() = Type.Unit defaultAttrs

        /// <summary>
        /// Creates a Field for use in Record types.
        /// </summary>
        member _.field(name: Name, typ: Type.Type<'attributes>) = Type.field name typ

        /// <summary>
        /// Creates a Field from a string name.
        /// </summary>
        member _.field(name: string, typ: Type.Type<'attributes>) =
            Type.field (Name.fromString name) typ

    /// <summary>
    /// TypeBuilderWithAttrs provides a Computation Expression for creating Type values with explicit attributes.
    /// </summary>
    type TypeBuilderWithAttrs<'attributes>(attrs: 'attributes) =
        /// <summary>
        /// Yields a Type directly.
        /// </summary>
        member _.Yield(typ: Type.Type<'attributes>) = typ

        /// <summary>
        /// Yields unit as Type.Unit (tagless syntax).
        /// </summary>
        member _.Yield((): unit) = Type.Unit attrs

        /// <summary>
        /// Combines multiple Types (takes the last one).
        /// </summary>
        member _.Combine(_: Type.Type<'attributes>, typ: Type.Type<'attributes>) = typ

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> Type.Type<'attributes>) =
            items |> Seq.map f |> Seq.last

        /// <summary>
        /// Zero case (Unit type).
        /// </summary>
        member _.Zero() = Type.Unit attrs

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> Type.Type<'attributes>) = f

        /// <summary>
        /// Runs the builder to produce the final Type.
        /// </summary>
        member _.Run(f: unit -> Type.Type<'attributes>) = f()

        /// Regular methods for direct usage (lowercase Fun.Blazor style)

        /// <summary>
        /// Creates a Variable type (type variable/generic parameter).
        /// </summary>
        member _.variable(name: Name) = Type.Variable(attrs, name)

        /// <summary>
        /// Creates a Variable type from a string.
        /// </summary>
        member _.variable(str: string) =
            Type.Variable(attrs, Name.fromString str)

        /// <summary>
        /// Creates a Reference type (reference to another type).
        /// </summary>
        member _.reference(fqName: FQName) = Type.Reference(attrs, fqName, [])

        /// <summary>
        /// Creates a Reference type with type arguments.
        /// </summary>
        member _.reference(fqName: FQName, typeArgs: Type.Type<'attributes> list) =
            Type.Reference(attrs, fqName, typeArgs)

        /// <summary>
        /// Creates a Tuple type (composition of multiple types).
        /// </summary>
        member _.tuple(elementTypes: Type.Type<'attributes> list) =
            Type.Tuple(attrs, elementTypes)

        /// <summary>
        /// Creates a Record type (named fields).
        /// </summary>
        member _.record(fields: Type.Field<'attributes> list) =
            Type.Record(attrs, fields)

        /// <summary>
        /// Creates an ExtensibleRecord type (record that can be extended).
        /// </summary>
        member _.extensibleRecord(variableName: Name, fields: Type.Field<'attributes> list) =
            Type.ExtensibleRecord(attrs, variableName, fields)

        /// <summary>
        /// Creates an ExtensibleRecord type with variable name from string.
        /// </summary>
        member _.extensibleRecord(variableName: string, fields: Type.Field<'attributes> list) =
            Type.ExtensibleRecord(attrs, Name.fromString variableName, fields)

        /// <summary>
        /// Creates a Function type (arg -> return).
        /// </summary>
        member _.functionType(argType: Type.Type<'attributes>, returnType: Type.Type<'attributes>) =
            Type.Function(attrs, argType, returnType)

        /// <summary>
        /// Creates a Function type from parameter list and return type.
        /// Chains parameters correctly: func [Int; Char] String = Int -> Char -> String
        /// </summary>
        member _.func(paramTypes: Type.Type<'attributes> list, returnType: Type.Type<'attributes>) =
            List.foldBack (fun param acc -> Type.Function(attrs, param, acc)) paramTypes returnType

        /// <summary>
        /// Creates a Unit type.
        /// </summary>
        member _.unit() = Type.Unit attrs

        /// <summary>
        /// Creates a Field for use in Record types.
        /// </summary>
        member _.field(name: Name, typ: Type.Type<'attributes>) = Type.field name typ

        /// <summary>
        /// Creates a Field from a string name.
        /// </summary>
        member _.field(name: string, typ: Type.Type<'attributes>) =
            Type.field (Name.fromString name) typ

    /// <summary>
    /// TypeBuilder for unit attributes.
    /// </summary>
    [<Sealed>]
    type TypeBuilder() =
        let defaultAttrs = ()

        /// <summary>
        /// Yields a Type directly.
        /// </summary>
        member _.Yield(typ: Type.Type<unit>) = typ

        /// <summary>
        /// Yields unit as Type.Unit (tagless syntax).
        /// </summary>
        member _.Yield((): unit) = Type.Unit defaultAttrs

        /// <summary>
        /// Combines multiple Types (takes the last one).
        /// </summary>
        member _.Combine(_: Type.Type<unit>, typ: Type.Type<unit>) = typ

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> Type.Type<unit>) =
            items |> Seq.map f |> Seq.last

        /// <summary>
        /// Zero case (Unit type).
        /// </summary>
        member _.Zero() = Type.Unit defaultAttrs

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> Type.Type<unit>) = f

        /// <summary>
        /// Runs the builder to produce the final Type.
        /// </summary>
        member _.Run(f: unit -> Type.Type<unit>) = f()

        /// Regular methods for direct usage (lowercase Fun.Blazor style)

        /// <summary>
        /// Creates a Variable type (type variable/generic parameter).
        /// </summary>
        member _.variable(name: Name) = Type.Variable(defaultAttrs, name)

        /// <summary>
        /// Creates a Variable type from a string.
        /// </summary>
        member _.variable(str: string) =
            Type.Variable(defaultAttrs, Name.fromString str)

        /// <summary>
        /// Creates a Reference type (reference to another type).
        /// </summary>
        member _.reference(fqName: FQName) = Type.Reference(defaultAttrs, fqName, [])

        /// <summary>
        /// Creates a Reference type with type arguments.
        /// </summary>
        member _.reference(fqName: FQName, typeArgs: Type.Type<unit> list) =
            Type.Reference(defaultAttrs, fqName, typeArgs)

        /// <summary>
        /// Creates a Tuple type (composition of multiple types).
        /// </summary>
        member _.tuple(elementTypes: Type.Type<unit> list) =
            Type.Tuple(defaultAttrs, elementTypes)

        /// <summary>
        /// Creates a Record type (named fields).
        /// </summary>
        member _.record(fields: Type.Field<unit> list) =
            Type.Record(defaultAttrs, fields)

        /// <summary>
        /// Creates an ExtensibleRecord type (record that can be extended).
        /// </summary>
        member _.extensibleRecord(variableName: Name, fields: Type.Field<unit> list) =
            Type.ExtensibleRecord(defaultAttrs, variableName, fields)

        /// <summary>
        /// Creates an ExtensibleRecord type with variable name from string.
        /// </summary>
        member _.extensibleRecord(variableName: string, fields: Type.Field<unit> list) =
            Type.ExtensibleRecord(defaultAttrs, Name.fromString variableName, fields)

        /// <summary>
        /// Creates a Function type (arg -> return).
        /// </summary>
        member _.functionType(argType: Type.Type<unit>, returnType: Type.Type<unit>) =
            Type.Function(defaultAttrs, argType, returnType)

        /// <summary>
        /// Creates a Function type from parameter list and return type.
        /// Chains parameters correctly: func [Int; Char] String = Int -> Char -> String
        /// </summary>
        member _.func(paramTypes: Type.Type<unit> list, returnType: Type.Type<unit>) =
            List.foldBack (fun param acc -> Type.Function(defaultAttrs, param, acc)) paramTypes returnType

        /// <summary>
        /// Creates a Unit type.
        /// </summary>
        member _.unit() = Type.Unit defaultAttrs

        /// <summary>
        /// Creates a Field for use in Record types.
        /// </summary>
        member _.field(name: Name, typ: Type.Type<unit>) = Type.field name typ

        /// <summary>
        /// Creates a Field from a string name.
        /// </summary>
        member _.field(name: string, typ: Type.Type<unit>) =
            Type.field (Name.fromString name) typ

        /// CustomOperations for CE query-style syntax (lowercase Fun.Blazor style)

        /// <summary>
        /// Creates a Reference type (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("reference")>]
        member _.ReferenceOp(_state: Type.Type<unit>, fqName: FQName) =
            Type.Reference(defaultAttrs, fqName, [])

        /// <summary>
        /// Creates a Reference type with type arguments (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("reference")>]
        member _.ReferenceOp(_state: Type.Type<unit>, fqName: FQName, typeArgs: Type.Type<unit> list) =
            Type.Reference(defaultAttrs, fqName, typeArgs)

        /// <summary>
        /// Creates a Tuple type (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("tuple")>]
        member _.TupleOp(_state: Type.Type<unit>, elementTypes: Type.Type<unit> list) =
            Type.Tuple(defaultAttrs, elementTypes)

        /// <summary>
        /// Creates a Record type (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("record")>]
        member _.RecordOp(_state: Type.Type<unit>, fields: Type.Field<unit> list) =
            Type.Record(defaultAttrs, fields)

        /// <summary>
        /// Creates a Variable type (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("variable")>]
        member _.VariableOp(_state: Type.Type<unit>, name: string) =
            Type.Variable(defaultAttrs, Name.fromString name)

        /// <summary>
        /// Creates a Variable type from Name (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("variable")>]
        member _.VariableOp(_state: Type.Type<unit>, name: Name) =
            Type.Variable(defaultAttrs, name)

        /// <summary>
        /// Creates a field in a record (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("field")>]
        member _.FieldOp(_state: Type.Type<unit>, name: string, typ: Type.Type<unit>) =
            Type.field (Name.fromString name) typ

        /// <summary>
        /// Creates a Function type (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("functionType")>]
        member _.FunctionTypeOp(_state: Type.Type<unit>, argType: Type.Type<unit>, returnType: Type.Type<unit>) =
            Type.Function(defaultAttrs, argType, returnType)

        /// <summary>
        /// Creates a Function type from parameter list (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("func")>]
        member _.FuncOp(_state: Type.Type<unit>, paramTypes: Type.Type<unit> list, returnType: Type.Type<unit>) =
            List.foldBack (fun param acc -> Type.Function(defaultAttrs, param, acc)) paramTypes returnType

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

