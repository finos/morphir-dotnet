namespace Morphir.IR.Classic.DSL

/// <summary>
/// Patterns module provides Computation Expression builders for creating Pattern-related IR constructs.
/// </summary>
module Patterns =

    open Morphir.IR.Name
    open Morphir.IR.FQName
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
        /// Creates a WildcardPattern.
        /// </summary>
        member _.Wildcard() = WildcardPattern defaultAttrs

        /// <summary>
        /// Creates an AsPattern (pattern with name binding).
        /// </summary>
        member _.AsPattern(nested: Pattern<'attributes>, name: Name) =
            AsPattern(defaultAttrs, nested, name)

        /// <summary>
        /// Creates an AsPattern with string name.
        /// </summary>
        member _.AsPattern(nested: Pattern<'attributes>, name: string) =
            AsPattern(defaultAttrs, nested, Morphir.IR.Name.fromString name)

        /// <summary>
        /// Creates a TuplePattern.
        /// </summary>
        member _.Tuple(patterns: Pattern<'attributes> list) =
            TuplePattern(defaultAttrs, patterns)

        /// <summary>
        /// Creates a ConstructorPattern.
        /// </summary>
        member _.Constructor(fqName: FQName, patterns: Pattern<'attributes> list) =
            ConstructorPattern(defaultAttrs, fqName, patterns)

        /// <summary>
        /// Creates an EmptyListPattern.
        /// </summary>
        member _.EmptyList() = EmptyListPattern defaultAttrs

        /// <summary>
        /// Creates a HeadTailPattern (cons pattern).
        /// </summary>
        member _.HeadTail(headPattern: Pattern<'attributes>, tailPattern: Pattern<'attributes>) =
            HeadTailPattern(defaultAttrs, headPattern, tailPattern)

        /// <summary>
        /// Creates a LiteralPattern.
        /// </summary>
        member _.Literal(lit: Literal) = LiteralPattern(defaultAttrs, lit)

        /// <summary>
        /// Creates a UnitPattern.
        /// </summary>
        member _.Unit() = UnitPattern defaultAttrs

        /// <summary>
        /// Creates a VariablePattern (alias for AsPattern with WildcardPattern).
        /// </summary>
        member _.Variable(name: Name) =
            AsPattern(defaultAttrs, WildcardPattern defaultAttrs, name)

        /// <summary>
        /// Creates a VariablePattern with string name.
        /// </summary>
        member _.Variable(name: string) =
            AsPattern(defaultAttrs, WildcardPattern defaultAttrs, Morphir.IR.Name.fromString name)

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
        /// Creates a WildcardPattern.
        /// </summary>
        member _.Wildcard() = WildcardPattern attrs

        /// <summary>
        /// Creates an AsPattern (pattern with name binding).
        /// </summary>
        member _.AsPattern(nested: Pattern<'attributes>, name: Name) =
            AsPattern(attrs, nested, name)

        /// <summary>
        /// Creates an AsPattern with string name.
        /// </summary>
        member _.AsPattern(nested: Pattern<'attributes>, name: string) =
            AsPattern(attrs, nested, Morphir.IR.Name.fromString name)

        /// <summary>
        /// Creates a TuplePattern.
        /// </summary>
        member _.Tuple(patterns: Pattern<'attributes> list) =
            TuplePattern(attrs, patterns)

        /// <summary>
        /// Creates a ConstructorPattern.
        /// </summary>
        member _.Constructor(fqName: FQName, patterns: Pattern<'attributes> list) =
            ConstructorPattern(attrs, fqName, patterns)

        /// <summary>
        /// Creates an EmptyListPattern.
        /// </summary>
        member _.EmptyList() = EmptyListPattern attrs

        /// <summary>
        /// Creates a HeadTailPattern (cons pattern).
        /// </summary>
        member _.HeadTail(headPattern: Pattern<'attributes>, tailPattern: Pattern<'attributes>) =
            HeadTailPattern(attrs, headPattern, tailPattern)

        /// <summary>
        /// Creates a LiteralPattern.
        /// </summary>
        member _.Literal(lit: Literal) = LiteralPattern(attrs, lit)

        /// <summary>
        /// Creates a UnitPattern.
        /// </summary>
        member _.Unit() = UnitPattern attrs

        /// <summary>
        /// Creates a VariablePattern (alias for AsPattern with WildcardPattern).
        /// </summary>
        member _.Variable(name: Name) =
            AsPattern(attrs, WildcardPattern attrs, name)

        /// <summary>
        /// Creates a VariablePattern with string name.
        /// </summary>
        member _.Variable(name: string) =
            AsPattern(attrs, WildcardPattern attrs, Morphir.IR.Name.fromString name)

    /// <summary>
    /// PatternBuilder for unit attributes.
    /// </summary>
    type PatternBuilder() =
        inherit PatternBuilder<unit>()

        /// <summary>
        /// Sets explicit attributes for the builder.
        /// </summary>
        member _.WithAttributes<'a>(attrs: 'a) = PatternBuilderWithAttrs<'a>(attrs)

    /// <summary>
    /// Global builder instance for use in Computation Expressions.
    /// </summary>
    let pattern = PatternBuilder()

