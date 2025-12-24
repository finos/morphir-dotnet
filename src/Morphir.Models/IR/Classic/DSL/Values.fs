namespace Morphir.IR.Classic.DSL

/// <summary>
/// Values module provides Computation Expression builders for creating Value-related IR constructs.
/// </summary>
module Values =

    open Morphir.IR
    open Morphir.IR.Classic.Value
    open Morphir.IR.Classic.Type
    open Morphir.IR.Classic.Pattern
    open Morphir.IR.Classic.Literal
    open System.Collections.Generic

    /// <summary>
    /// ValueBuilder provides a Computation Expression for creating Value values.
    /// Defaults to unit () for both type and value attributes.
    /// </summary>
    type ValueBuilder<'typeAttributes, 'valueAttributes>() =
        let defaultAttrs = Unchecked.defaultof<'valueAttributes>

        /// <summary>
        /// Yields a Value directly.
        /// </summary>
        member _.Yield(value: Value<'typeAttributes, 'valueAttributes>) = value

        /// <summary>
        /// Combines multiple Values (takes the last one).
        /// </summary>
        member _.Combine(_: Value<'typeAttributes, 'valueAttributes>, value: Value<'typeAttributes, 'valueAttributes>) =
            value

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> Value<'typeAttributes, 'valueAttributes>) =
            items |> Seq.map f |> Seq.last

        /// <summary>
        /// Zero case (Unit value).
        /// </summary>
        member _.Zero() = Unit defaultAttrs

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> Value<'typeAttributes, 'valueAttributes>) = f

        /// <summary>
        /// Runs the builder to produce the final Value.
        /// </summary>
        member _.Run(f: unit -> Value<'typeAttributes, 'valueAttributes>) = f()

        /// <summary>
        /// Creates a Literal value.
        /// </summary>
        member _.Literal(lit: Literal) = Literal(defaultAttrs, lit)

        /// <summary>
        /// Creates a Constructor value.
        /// </summary>
        member _.Constructor(fqName: FQName) = Constructor(defaultAttrs, fqName)

        /// <summary>
        /// Creates a Tuple value.
        /// </summary>
        member _.Tuple(elements: Value<'typeAttributes, 'valueAttributes> list) =
            Value.Tuple(defaultAttrs, elements)

        /// <summary>
        /// Creates a List value.
        /// </summary>
        member _.List(elements: Value<'typeAttributes, 'valueAttributes> list) =
            Value.List(defaultAttrs, elements)

        /// <summary>
        /// Creates a Record value from a Map.
        /// </summary>
        member _.Record(fields: Map<Name, Value<'typeAttributes, 'valueAttributes>>) =
            Value.Record(defaultAttrs, fields)

        /// <summary>
        /// Creates a Record value from a list of key-value pairs.
        /// </summary>
        member _.Record(fields: (Name * Value<'typeAttributes, 'valueAttributes>) list) =
            Value.Record(defaultAttrs, Map.ofList fields)

        /// <summary>
        /// Creates a Record value from a list of string-key pairs.
        /// </summary>
        member _.Record(fields: (string * Value<'typeAttributes, 'valueAttributes>) list) =
            let nameValuePairs =
                fields
                |> List.map (fun (name, value) ->
                    (Name.fromString name, value))
            Value.Record(defaultAttrs, Map.ofList nameValuePairs)

        /// <summary>
        /// Creates a Variable value.
        /// </summary>
        member _.Variable(name: Name) = Variable(defaultAttrs, name)

        /// <summary>
        /// Creates a Variable value from a string.
        /// </summary>
        member _.Variable(str: string) =
            Variable(defaultAttrs, Name.fromString str)

        /// <summary>
        /// Creates a Reference value.
        /// </summary>
        member _.Reference(fqName: FQName) = Value.Reference(defaultAttrs, fqName)

        /// <summary>
        /// Creates a Field value (field access on a record).
        /// </summary>
        member _.Field(recordExpr: Value<'typeAttributes, 'valueAttributes>, fieldName: Name) =
            Field(defaultAttrs, recordExpr, fieldName)

        /// <summary>
        /// Creates a Field value with string field name.
        /// </summary>
        member _.Field(recordExpr: Value<'typeAttributes, 'valueAttributes>, fieldName: string) =
            Field(defaultAttrs, recordExpr, Name.fromString fieldName)

        /// <summary>
        /// Creates a FieldFunction value (a function that extracts a field).
        /// </summary>
        member _.FieldFunction(fieldName: Name) = FieldFunction(defaultAttrs, fieldName)

        /// <summary>
        /// Creates a FieldFunction value with string field name.
        /// </summary>
        member _.FieldFunction(fieldName: string) =
            FieldFunction(defaultAttrs, Name.fromString fieldName)

        /// <summary>
        /// Creates an Apply value (function application).
        /// </summary>
        member _.Apply(functionExpr: Value<'typeAttributes, 'valueAttributes>, argumentExpr: Value<'typeAttributes, 'valueAttributes>) =
            Apply(defaultAttrs, functionExpr, argumentExpr)

        /// <summary>
        /// Creates a Lambda value (anonymous function).
        /// </summary>
        member _.Lambda(argumentPattern: Pattern<'valueAttributes>, body: Value<'typeAttributes, 'valueAttributes>) =
            Lambda(defaultAttrs, argumentPattern, body)

        /// <summary>
        /// Creates a Lambda value with string argument name (creates wildcard pattern).
        /// </summary>
        member _.Lambda(argumentName: string, body: Value<'typeAttributes, 'valueAttributes>) =
            let pattern =
                Morphir.IR.Classic.Pattern.wildcardPattern defaultAttrs
            Lambda(defaultAttrs, pattern, body)

        /// <summary>
        /// Creates a LetDefinition value (a let binding introducing a single value).
        /// </summary>
        member _.LetDefinition
            (
                bindingName: Name,
                definition: ValueDefinition<'typeAttributes, 'valueAttributes>,
                inExpr: Value<'typeAttributes, 'valueAttributes>
            ) =
            LetDefinition(defaultAttrs, bindingName, definition, inExpr)

        /// <summary>
        /// Creates a LetRecursion value (mutually recursive let bindings).
        /// </summary>
        member _.LetRecursion
            (
                bindings: Map<Name, ValueDefinition<'typeAttributes, 'valueAttributes>>,
                inExpr: Value<'typeAttributes, 'valueAttributes>
            ) =
            LetRecursion(defaultAttrs, bindings, inExpr)

        /// <summary>
        /// Creates a Destructure value (pattern-based destructuring).
        /// </summary>
        member _.Destructure
            (
                pattern: Pattern<'valueAttributes>,
                valueToDestructure: Value<'typeAttributes, 'valueAttributes>,
                inExpr: Value<'typeAttributes, 'valueAttributes>
            ) =
            Destructure(defaultAttrs, pattern, valueToDestructure, inExpr)

        /// <summary>
        /// Creates an IfThenElse value (conditional expression).
        /// </summary>
        member _.IfThenElse
            (
                condition: Value<'typeAttributes, 'valueAttributes>,
                thenBranch: Value<'typeAttributes, 'valueAttributes>,
                elseBranch: Value<'typeAttributes, 'valueAttributes>
            ) =
            IfThenElse(defaultAttrs, condition, thenBranch, elseBranch)

        /// <summary>
        /// Creates a PatternMatch value (pattern matching with multiple cases).
        /// </summary>
        member _.PatternMatch
            (
                valueToMatch: Value<'typeAttributes, 'valueAttributes>,
                cases: (Pattern<'valueAttributes> * Value<'typeAttributes, 'valueAttributes>) list
            ) =
            PatternMatch(defaultAttrs, valueToMatch, cases)

        /// <summary>
        /// Creates an UpdateRecord value (record update expression).
        /// </summary>
        member _.UpdateRecord
            (
                recordToUpdate: Value<'typeAttributes, 'valueAttributes>,
                fieldsToUpdate: Map<Name, Value<'typeAttributes, 'valueAttributes>>
            ) =
            UpdateRecord(defaultAttrs, recordToUpdate, fieldsToUpdate)

        /// <summary>
        /// Creates an UpdateRecord value with list of field updates.
        /// </summary>
        member _.UpdateRecord
            (
                recordToUpdate: Value<'typeAttributes, 'valueAttributes>,
                fieldsToUpdate: (Name * Value<'typeAttributes, 'valueAttributes>) list
            ) =
            UpdateRecord(defaultAttrs, recordToUpdate, Map.ofList fieldsToUpdate)

        /// <summary>
        /// Creates a Unit value.
        /// </summary>
        member _.Unit() = Unit defaultAttrs

    /// <summary>
    /// ValueBuilderWithAttrs provides a Computation Expression for creating Value values with explicit attributes.
    /// </summary>
    type ValueBuilderWithAttrs<'typeAttributes, 'valueAttributes>(attrs: 'valueAttributes) =
        /// <summary>
        /// Yields a Value directly.
        /// </summary>
        member _.Yield(value: Value<'typeAttributes, 'valueAttributes>) = value

        /// <summary>
        /// Combines multiple Values (takes the last one).
        /// </summary>
        member _.Combine(_: Value<'typeAttributes, 'valueAttributes>, value: Value<'typeAttributes, 'valueAttributes>) =
            value

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> Value<'typeAttributes, 'valueAttributes>) =
            items |> Seq.map f |> Seq.last

        /// <summary>
        /// Zero case (Unit value).
        /// </summary>
        member _.Zero() = Unit attrs

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> Value<'typeAttributes, 'valueAttributes>) = f

        /// <summary>
        /// Runs the builder to produce the final Value.
        /// </summary>
        member _.Run(f: unit -> Value<'typeAttributes, 'valueAttributes>) = f()

        /// <summary>
        /// Creates a Literal value.
        /// </summary>
        member _.Literal(lit: Literal) = Literal(attrs, lit)

        /// <summary>
        /// Creates a Constructor value.
        /// </summary>
        member _.Constructor(fqName: FQName) = Constructor(attrs, fqName)

        /// <summary>
        /// Creates a Tuple value.
        /// </summary>
        member _.Tuple(elements: Value<'typeAttributes, 'valueAttributes> list) =
            Value.Tuple(attrs, elements)

        /// <summary>
        /// Creates a List value.
        /// </summary>
        member _.List(elements: Value<'typeAttributes, 'valueAttributes> list) =
            Value.List(attrs, elements)

        /// <summary>
        /// Creates a Record value from a Map.
        /// </summary>
        member _.Record(fields: Map<Name, Value<'typeAttributes, 'valueAttributes>>) =
            Value.Record(attrs, fields)

        /// <summary>
        /// Creates a Record value from a list of key-value pairs.
        /// </summary>
        member _.Record(fields: (Name * Value<'typeAttributes, 'valueAttributes>) list) =
            Value.Record(attrs, Map.ofList fields)

        /// <summary>
        /// Creates a Record value from a list of string-key pairs.
        /// </summary>
        member _.Record(fields: (string * Value<'typeAttributes, 'valueAttributes>) list) =
            let nameValuePairs =
                fields
                |> List.map (fun (name, value) ->
                    (Name.fromString name, value))
            Value.Record(attrs, Map.ofList nameValuePairs)

        /// <summary>
        /// Creates a Variable value.
        /// </summary>
        member _.Variable(name: Name) = Variable(attrs, name)

        /// <summary>
        /// Creates a Variable value from a string.
        /// </summary>
        member _.Variable(str: string) =
            Variable(attrs, Name.fromString str)

        /// <summary>
        /// Creates a Reference value.
        /// </summary>
        member _.Reference(fqName: FQName) = Value.Reference(attrs, fqName)

        /// <summary>
        /// Creates a Field value (field access on a record).
        /// </summary>
        member _.Field(recordExpr: Value<'typeAttributes, 'valueAttributes>, fieldName: Name) =
            Field(attrs, recordExpr, fieldName)

        /// <summary>
        /// Creates a Field value with string field name.
        /// </summary>
        member _.Field(recordExpr: Value<'typeAttributes, 'valueAttributes>, fieldName: string) =
            Field(attrs, recordExpr, Name.fromString fieldName)

        /// <summary>
        /// Creates a FieldFunction value (a function that extracts a field).
        /// </summary>
        member _.FieldFunction(fieldName: Name) = FieldFunction(attrs, fieldName)

        /// <summary>
        /// Creates a FieldFunction value with string field name.
        /// </summary>
        member _.FieldFunction(fieldName: string) =
            FieldFunction(attrs, Name.fromString fieldName)

        /// <summary>
        /// Creates an Apply value (function application).
        /// </summary>
        member _.Apply(functionExpr: Value<'typeAttributes, 'valueAttributes>, argumentExpr: Value<'typeAttributes, 'valueAttributes>) =
            Apply(attrs, functionExpr, argumentExpr)

        /// <summary>
        /// Creates a Lambda value (anonymous function).
        /// </summary>
        member _.Lambda(argumentPattern: Pattern<'valueAttributes>, body: Value<'typeAttributes, 'valueAttributes>) =
            Lambda(attrs, argumentPattern, body)

        /// <summary>
        /// Creates a Lambda value with string argument name (creates wildcard pattern).
        /// </summary>
        member _.Lambda(argumentName: string, body: Value<'typeAttributes, 'valueAttributes>) =
            let pattern =
                Morphir.IR.Classic.Pattern.wildcardPattern attrs
            Lambda(attrs, pattern, body)

        /// <summary>
        /// Creates a LetDefinition value (a let binding introducing a single value).
        /// </summary>
        member _.LetDefinition
            (
                bindingName: Name,
                definition: ValueDefinition<'typeAttributes, 'valueAttributes>,
                inExpr: Value<'typeAttributes, 'valueAttributes>
            ) =
            LetDefinition(attrs, bindingName, definition, inExpr)

        /// <summary>
        /// Creates a LetRecursion value (mutually recursive let bindings).
        /// </summary>
        member _.LetRecursion
            (
                bindings: Map<Name, ValueDefinition<'typeAttributes, 'valueAttributes>>,
                inExpr: Value<'typeAttributes, 'valueAttributes>
            ) =
            LetRecursion(attrs, bindings, inExpr)

        /// <summary>
        /// Creates a Destructure value (pattern-based destructuring).
        /// </summary>
        member _.Destructure
            (
                pattern: Pattern<'valueAttributes>,
                valueToDestructure: Value<'typeAttributes, 'valueAttributes>,
                inExpr: Value<'typeAttributes, 'valueAttributes>
            ) =
            Destructure(attrs, pattern, valueToDestructure, inExpr)

        /// <summary>
        /// Creates an IfThenElse value (conditional expression).
        /// </summary>
        member _.IfThenElse
            (
                condition: Value<'typeAttributes, 'valueAttributes>,
                thenBranch: Value<'typeAttributes, 'valueAttributes>,
                elseBranch: Value<'typeAttributes, 'valueAttributes>
            ) =
            IfThenElse(attrs, condition, thenBranch, elseBranch)

        /// <summary>
        /// Creates a PatternMatch value (pattern matching with multiple cases).
        /// </summary>
        member _.PatternMatch
            (
                valueToMatch: Value<'typeAttributes, 'valueAttributes>,
                cases: (Pattern<'valueAttributes> * Value<'typeAttributes, 'valueAttributes>) list
            ) =
            PatternMatch(attrs, valueToMatch, cases)

        /// <summary>
        /// Creates an UpdateRecord value (record update expression).
        /// </summary>
        member _.UpdateRecord
            (
                recordToUpdate: Value<'typeAttributes, 'valueAttributes>,
                fieldsToUpdate: Map<Name, Value<'typeAttributes, 'valueAttributes>>
            ) =
            UpdateRecord(attrs, recordToUpdate, fieldsToUpdate)

        /// <summary>
        /// Creates an UpdateRecord value with list of field updates.
        /// </summary>
        member _.UpdateRecord
            (
                recordToUpdate: Value<'typeAttributes, 'valueAttributes>,
                fieldsToUpdate: (Name * Value<'typeAttributes, 'valueAttributes>) list
            ) =
            UpdateRecord(attrs, recordToUpdate, Map.ofList fieldsToUpdate)

        /// <summary>
        /// Creates a Unit value.
        /// </summary>
        member _.Unit() = Unit attrs

    /// <summary>
    /// ValueBuilder for unit attributes (sealed, non-inheriting version).
    /// </summary>
    [<Sealed>]
    type ValueBuilder() =
        let defaultAttrs = ()

        /// <summary>
        /// Yields a Value directly.
        /// </summary>
        member _.Yield(value: Value<unit, unit>) = value

        /// <summary>
        /// Combines multiple Values (takes the last one).
        /// </summary>
        member _.Combine(_: Value<unit, unit>, value: Value<unit, unit>) = value

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> Value<unit, unit>) =
            items |> Seq.map f |> Seq.last

        /// <summary>
        /// Zero case (Unit value).
        /// </summary>
        member _.Zero() = Unit defaultAttrs

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> Value<unit, unit>) = f

        /// <summary>
        /// Runs the builder to produce the final Value.
        /// </summary>
        member _.Run(f: unit -> Value<unit, unit>) = f()

        /// Lowercase helper functions for CE usage
        /// <summary>
        /// Creates a Literal value (lowercase for CE).
        /// </summary>
        member _.literal(lit: Literal) = Literal(defaultAttrs, lit)

        /// <summary>
        /// Creates a Variable value (lowercase for CE).
        /// </summary>
        member _.variable(name: Name) = Variable(defaultAttrs, name)

        /// <summary>
        /// Creates a Variable value from string (lowercase for CE).
        /// </summary>
        member _.variable(str: string) = Variable(defaultAttrs, Name.fromString str)

        /// <summary>
        /// Creates a Tuple value (lowercase for CE).
        /// </summary>
        member _.tuple(elements: Value<unit, unit> list) = Value.Tuple(defaultAttrs, elements)

        /// <summary>
        /// Creates a List value (lowercase for CE).
        /// </summary>
        member _.list(elements: Value<unit, unit> list) = Value.List(defaultAttrs, elements)

        /// <summary>
        /// Creates a Record value (lowercase for CE).
        /// </summary>
        member _.record(fields: Map<Name, Value<unit, unit>>) = Value.Record(defaultAttrs, fields)

        /// <summary>
        /// Creates a Record value from list (lowercase for CE).
        /// </summary>
        member _.record(fields: (Name * Value<unit, unit>) list) = Value.Record(defaultAttrs, Map.ofList fields)

        /// <summary>
        /// Creates a Record value from string-keyed list (lowercase for CE).
        /// </summary>
        member _.record(fields: (string * Value<unit, unit>) list) =
            let nameValuePairs = fields |> List.map (fun (name, value) -> (Name.fromString name, value))
            Value.Record(defaultAttrs, Map.ofList nameValuePairs)

        /// <summary>
        /// Creates a Reference value (lowercase for CE).
        /// </summary>
        member _.reference(fqName: FQName) = Value.Reference(defaultAttrs, fqName)

        /// <summary>
        /// Creates an Apply value (lowercase for CE).
        /// </summary>
        member _.apply(functionExpr: Value<unit, unit>, argumentExpr: Value<unit, unit>) =
            Apply(defaultAttrs, functionExpr, argumentExpr)

        /// <summary>
        /// Unit value property (lowercase for CE).
        /// </summary>
        member _.unit = Unit defaultAttrs

        /// Pascal case methods for backward compatibility
        /// <summary>
        /// Creates a Literal value.
        /// </summary>
        member _.Literal(lit: Literal) = Literal(defaultAttrs, lit)

        /// <summary>
        /// Creates a Variable value.
        /// </summary>
        member _.Variable(name: Name) = Variable(defaultAttrs, name)

        /// <summary>
        /// Creates a Variable value from string.
        /// </summary>
        member _.Variable(str: string) = Variable(defaultAttrs, Name.fromString str)

        /// <summary>
        /// Creates a Tuple value.
        /// </summary>
        member _.Tuple(elements: Value<unit, unit> list) = Value.Tuple(defaultAttrs, elements)

        /// <summary>
        /// Creates a List value.
        /// </summary>
        member _.List(elements: Value<unit, unit> list) = Value.List(defaultAttrs, elements)

        /// <summary>
        /// Creates a Record value.
        /// </summary>
        member _.Record(fields: Map<Name, Value<unit, unit>>) = Value.Record(defaultAttrs, fields)

        /// <summary>
        /// Creates a Reference value.
        /// </summary>
        member _.Reference(fqName: FQName) = Value.Reference(defaultAttrs, fqName)

        /// <summary>
        /// Creates an Apply value.
        /// </summary>
        member _.Apply(functionExpr: Value<unit, unit>, argumentExpr: Value<unit, unit>) =
            Apply(defaultAttrs, functionExpr, argumentExpr)

        /// <summary>
        /// Creates a Unit value.
        /// </summary>
        member _.Unit() = Unit defaultAttrs

        /// <summary>
        /// Sets explicit value attributes for the builder (type attributes default to unit).
        /// </summary>
        member _.WithAttributes<'va>(valueAttrs: 'va) =
            ValueBuilderWithAttrs<unit, 'va>(valueAttrs)

    /// <summary>
    /// Global builder instance for use in Computation Expressions.
    /// </summary>
    let value = ValueBuilder()

