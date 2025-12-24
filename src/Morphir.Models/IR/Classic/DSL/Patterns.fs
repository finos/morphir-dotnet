namespace Morphir.IR.Classic.DSL

open System.Runtime.CompilerServices

/// <summary>
/// Patterns module provides Computation Expression builders for creating Pattern-related IR constructs.
/// </summary>
module Patterns =

    open Morphir.IR
    open Morphir.IR.Classic.Pattern
    open Morphir.IR.Classic.Literal

    /// <summary>
    /// PatternBuilder provides a Computation Expression for creating Pattern values.
    /// Defaults to unit () for attributes.
    /// </summary>
    type PatternBuilder<'attributes>() =
        let defaultAttrs = Unchecked.defaultof<'attributes>

        /// <summary>
        /// Yields a Pattern directly.
        /// </summary>
        member _.Yield(pattern: Pattern<'attributes>) = pattern

        /// <summary>
        /// Combines multiple Patterns (takes the last one).
        /// </summary>
        member _.Combine(_: Pattern<'attributes>, pattern: Pattern<'attributes>) = pattern

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> Pattern<'attributes>) =
            items |> Seq.map f |> Seq.last

        /// <summary>
        /// Zero case (WildcardPattern).
        /// </summary>
        member _.Zero() = WildcardPattern defaultAttrs

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> Pattern<'attributes>) = f

        /// <summary>
        /// Runs the builder to produce the final Pattern.
        /// </summary>
        member _.Run(f: unit -> Pattern<'attributes>) = f()

        /// Regular methods for direct usage (Pascal case)
        /// <summary>
        /// Creates a WildcardPattern (regular method).
        /// </summary>
        member _.Wildcard() = WildcardPattern defaultAttrs

        /// <summary>
        /// Creates a VariablePattern (regular method).
        /// </summary>
        member _.Variable(name: Name) =
            AsPattern(defaultAttrs, WildcardPattern defaultAttrs, name)

        /// <summary>
        /// Creates a VariablePattern from string (regular method).
        /// </summary>
        member _.Variable(name: string) =
            AsPattern(defaultAttrs, WildcardPattern defaultAttrs, Name.fromString name)

        /// <summary>
        /// Creates a TuplePattern (regular method).
        /// </summary>
        member _.Tuple(patterns: Pattern<'attributes> list) =
            TuplePattern(defaultAttrs, patterns)

        /// <summary>
        /// Creates a ConstructorPattern (regular method).
        /// </summary>
        member _.Constructor(fqName: FQName, patterns: Pattern<'attributes> list) =
            ConstructorPattern(defaultAttrs, fqName, patterns)

        /// <summary>
        /// Creates a LiteralPattern (regular method).
        /// </summary>
        member _.Literal(lit: Literal) = LiteralPattern(defaultAttrs, lit)

        /// <summary>
        /// Creates a UnitPattern (regular method).
        /// </summary>
        member _.Unit() = UnitPattern defaultAttrs

        /// CustomOperations for query-style syntax (lowercase)
        /// <summary>
        /// Creates a WildcardPattern.
        /// </summary>
        [<CustomOperation("wildcard")>]
        member _.WildcardOp(_state: Pattern<'attributes>) = WildcardPattern defaultAttrs

        /// <summary>
        /// Creates an AsPattern (pattern with name binding).
        /// </summary>
        [<CustomOperation("asPattern")>]
        member _.AsPattern(_state: Pattern<'attributes>, nested: Pattern<'attributes>, name: Name) =
            AsPattern(defaultAttrs, nested, name)

        /// <summary>
        /// Creates an AsPattern with string name.
        /// </summary>
        [<CustomOperation("asPattern")>]
        member _.AsPattern(_state: Pattern<'attributes>, nested: Pattern<'attributes>, name: string) =
            AsPattern(defaultAttrs, nested, Name.fromString name)

        /// <summary>
        /// Creates a TuplePattern.
        /// </summary>
        [<CustomOperation("tuple")>]
        member _.Tuple(_state: Pattern<'attributes>, patterns: Pattern<'attributes> list) =
            TuplePattern(defaultAttrs, patterns)

        /// <summary>
        /// Creates a ConstructorPattern.
        /// </summary>
        [<CustomOperation("constructor")>]
        member _.Constructor(_state: Pattern<'attributes>, fqName: FQName, patterns: Pattern<'attributes> list) =
            ConstructorPattern(defaultAttrs, fqName, patterns)

        /// <summary>
        /// Creates an EmptyListPattern.
        /// </summary>
        [<CustomOperation("emptyList")>]
        member _.EmptyList(_state: Pattern<'attributes>) = EmptyListPattern defaultAttrs

        /// <summary>
        /// Creates a HeadTailPattern (cons pattern).
        /// </summary>
        [<CustomOperation("headTail")>]
        member _.HeadTail(_state: Pattern<'attributes>, headPattern: Pattern<'attributes>, tailPattern: Pattern<'attributes>) =
            HeadTailPattern(defaultAttrs, headPattern, tailPattern)

        /// <summary>
        /// Creates a LiteralPattern.
        /// </summary>
        [<CustomOperation("literal")>]
        member _.Literal(_state: Pattern<'attributes>, lit: Literal) = LiteralPattern(defaultAttrs, lit)

        /// <summary>
        /// Creates a UnitPattern.
        /// </summary>
        [<CustomOperation("unit")>]
        member _.Unit(_state: Pattern<'attributes>) = UnitPattern defaultAttrs

        /// <summary>
        /// Creates a VariablePattern (alias for AsPattern with WildcardPattern).
        /// </summary>
        [<CustomOperation("variable")>]
        member _.Variable(_state: Pattern<'attributes>, name: Name) =
            AsPattern(defaultAttrs, WildcardPattern defaultAttrs, name)

        /// <summary>
        /// Creates a VariablePattern with string name.
        /// </summary>
        [<CustomOperation("variable")>]
        member _.Variable(_state: Pattern<'attributes>, name: string) =
            AsPattern(defaultAttrs, WildcardPattern defaultAttrs, Name.fromString name)

    /// <summary>
    /// PatternBuilderWithAttrs provides a Computation Expression for creating Pattern values with explicit attributes.
    /// </summary>
    type PatternBuilderWithAttrs<'attributes>(attrs: 'attributes) =
        /// <summary>
        /// Yields a Pattern directly.
        /// </summary>
        member _.Yield(pattern: Pattern<'attributes>) = pattern

        /// <summary>
        /// Combines multiple Patterns (takes the last one).
        /// </summary>
        member _.Combine(_: Pattern<'attributes>, pattern: Pattern<'attributes>) = pattern

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> Pattern<'attributes>) =
            items |> Seq.map f |> Seq.last

        /// <summary>
        /// Zero case (WildcardPattern).
        /// </summary>
        member _.Zero() = WildcardPattern attrs

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> Pattern<'attributes>) = f

        /// <summary>
        /// Runs the builder to produce the final Pattern.
        /// </summary>
        member _.Run(f: unit -> Pattern<'attributes>) = f()

        /// <summary>
        /// Creates a WildcardPattern.
        /// </summary>
        [<CustomOperation("wildcard")>]
        member _.Wildcard(_state: Pattern<'attributes>) = WildcardPattern attrs

        /// <summary>
        /// Creates an AsPattern (pattern with name binding).
        /// </summary>
        [<CustomOperation("asPattern")>]
        member _.AsPattern(_state: Pattern<'attributes>, nested: Pattern<'attributes>, name: Name) =
            AsPattern(attrs, nested, name)

        /// <summary>
        /// Creates an AsPattern with string name.
        /// </summary>
        [<CustomOperation("asPattern")>]
        member _.AsPattern(_state: Pattern<'attributes>, nested: Pattern<'attributes>, name: string) =
            AsPattern(attrs, nested, Name.fromString name)

        /// <summary>
        /// Creates a TuplePattern.
        /// </summary>
        [<CustomOperation("tuple")>]
        member _.Tuple(_state: Pattern<'attributes>, patterns: Pattern<'attributes> list) =
            TuplePattern(attrs, patterns)

        /// <summary>
        /// Creates a ConstructorPattern.
        /// </summary>
        [<CustomOperation("constructor")>]
        member _.Constructor(_state: Pattern<'attributes>, fqName: FQName, patterns: Pattern<'attributes> list) =
            ConstructorPattern(attrs, fqName, patterns)

        /// <summary>
        /// Creates an EmptyListPattern.
        /// </summary>
        [<CustomOperation("emptyList")>]
        member _.EmptyList(_state: Pattern<'attributes>) = EmptyListPattern attrs

        /// <summary>
        /// Creates a HeadTailPattern (cons pattern).
        /// </summary>
        [<CustomOperation("headTail")>]
        member _.HeadTail(_state: Pattern<'attributes>, headPattern: Pattern<'attributes>, tailPattern: Pattern<'attributes>) =
            HeadTailPattern(attrs, headPattern, tailPattern)

        /// <summary>
        /// Creates a LiteralPattern.
        /// </summary>
        [<CustomOperation("literal")>]
        member _.Literal(_state: Pattern<'attributes>, lit: Literal) = LiteralPattern(attrs, lit)

        /// <summary>
        /// Creates a UnitPattern.
        /// </summary>
        [<CustomOperation("unit")>]
        member _.Unit(_state: Pattern<'attributes>) = UnitPattern attrs

        /// <summary>
        /// Creates a VariablePattern (alias for AsPattern with WildcardPattern).
        /// </summary>
        [<CustomOperation("variable")>]
        member _.Variable(_state: Pattern<'attributes>, name: Name) =
            AsPattern(attrs, WildcardPattern attrs, name)

        /// <summary>
        /// Creates a VariablePattern with string name.
        /// </summary>
        [<CustomOperation("variable")>]
        member _.Variable(_state: Pattern<'attributes>, name: string) =
            AsPattern(attrs, WildcardPattern attrs, Name.fromString name)

    /// <summary>
    /// PatternBuilder for unit attributes.
    /// </summary>
    [<Sealed>]
    type PatternBuilder() =
        let defaultAttrs = ()

        /// <summary>
        /// Yields a Pattern directly.
        /// </summary>
        member _.Yield(pattern: Pattern<unit>) = pattern

        /// <summary>
        /// Combines multiple Patterns (takes the last one).
        /// </summary>
        member _.Combine(_: Pattern<unit>, pattern: Pattern<unit>) = pattern

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> Pattern<unit>) =
            items |> Seq.map f |> Seq.last

        /// <summary>
        /// Zero case (WildcardPattern).
        /// </summary>
        member _.Zero() = WildcardPattern defaultAttrs

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> Pattern<unit>) = f

        /// <summary>
        /// Runs the builder to produce the final Pattern.
        /// </summary>
        member _.Run(f: unit -> Pattern<unit>) = f()

        /// Regular methods for direct usage (Pascal case)
        /// <summary>
        /// Creates a WildcardPattern (regular method).
        /// </summary>
        member _.Wildcard() = WildcardPattern defaultAttrs

        /// <summary>
        /// Creates a VariablePattern (regular method).
        /// </summary>
        member _.Variable(name: Name) =
            AsPattern(defaultAttrs, WildcardPattern defaultAttrs, name)

        /// <summary>
        /// Creates a VariablePattern from string (regular method).
        /// </summary>
        member _.Variable(name: string) =
            AsPattern(defaultAttrs, WildcardPattern defaultAttrs, Name.fromString name)

        /// <summary>
        /// Creates a TuplePattern (regular method).
        /// </summary>
        member _.Tuple(patterns: Pattern<unit> list) =
            TuplePattern(defaultAttrs, patterns)

        /// <summary>
        /// Creates a ConstructorPattern (regular method).
        /// </summary>
        member _.Constructor(fqName: FQName, patterns: Pattern<unit> list) =
            ConstructorPattern(defaultAttrs, fqName, patterns)

        /// <summary>
        /// Creates a LiteralPattern (regular method).
        /// </summary>
        member _.Literal(lit: Literal) = LiteralPattern(defaultAttrs, lit)

        /// <summary>
        /// Creates a UnitPattern (regular method).
        /// </summary>
        member _.Unit() = UnitPattern defaultAttrs

        /// Lowercase helper properties and functions for CE usage
        /// <summary>
        /// Wildcard pattern property (lowercase for CE).
        /// </summary>
        member _.wildcard = WildcardPattern defaultAttrs

        /// <summary>
        /// Creates a VariablePattern (lowercase for CE).
        /// </summary>
        member _.variable(name: Name) =
            AsPattern(defaultAttrs, WildcardPattern defaultAttrs, name)

        /// <summary>
        /// Creates a VariablePattern from string (lowercase for CE).
        /// </summary>
        member _.variable(name: string) =
            AsPattern(defaultAttrs, WildcardPattern defaultAttrs, Name.fromString name)

        /// <summary>
        /// Creates a TuplePattern (lowercase for CE).
        /// </summary>
        member _.tuple(patterns: Pattern<unit> list) =
            TuplePattern(defaultAttrs, patterns)

        /// <summary>
        /// Creates a ConstructorPattern (lowercase for CE).
        /// </summary>
        member _.constructor(fqName: FQName, patterns: Pattern<unit> list) =
            ConstructorPattern(defaultAttrs, fqName, patterns)

        /// <summary>
        /// Creates a LiteralPattern (lowercase for CE).
        /// </summary>
        member _.literal(lit: Literal) = LiteralPattern(defaultAttrs, lit)

        /// <summary>
        /// Unit pattern property (lowercase for CE).
        /// </summary>
        member _.unit = UnitPattern defaultAttrs

        /// <summary>
        /// Sets explicit attributes for the builder.
        /// </summary>
        member _.WithAttributes<'a>(attrs: 'a) = PatternBuilderWithAttrs<'a>(attrs)

    /// <summary>
    /// Global builder instance for use in Computation Expressions.
    /// </summary>
    let pattern = PatternBuilder()

