namespace Morphir.IR.Classic.DSL

open System.Runtime.CompilerServices

/// <summary>
/// Patterns module provides Computation Expression builders for creating Pattern-related IR constructs.
/// </summary>
module Patterns =

    open Morphir.IR
    open Morphir.IR.Classic
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
        member _.Yield(pattern: Pattern.Pattern<'attributes>) = pattern

        /// <summary>
        /// Yields unit as Pattern.UnitPattern (tagless syntax).
        /// </summary>
        member _.Yield((): unit) = Pattern.UnitPattern defaultAttrs

        /// <summary>
        /// Yields a 2-tuple of patterns as Pattern.TuplePattern (tagless syntax).
        /// </summary>
        member _.Yield((p1, p2): Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes>) =
            Pattern.TuplePattern(defaultAttrs, [ p1; p2 ])

        /// <summary>
        /// Yields a 3-tuple of patterns as Pattern.TuplePattern (tagless syntax).
        /// </summary>
        member _.Yield((p1, p2, p3): Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes>) =
            Pattern.TuplePattern(defaultAttrs, [ p1; p2; p3 ])

        /// <summary>
        /// Yields a 4-tuple of patterns as Pattern.TuplePattern (tagless syntax).
        /// </summary>
        member _.Yield((p1, p2, p3, p4): Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes>) =
            Pattern.TuplePattern(defaultAttrs, [ p1; p2; p3; p4 ])

        /// <summary>
        /// Yields a 5-tuple of patterns as Pattern.TuplePattern (tagless syntax).
        /// </summary>
        member _.Yield((p1, p2, p3, p4, p5): Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes>) =
            Pattern.TuplePattern(defaultAttrs, [ p1; p2; p3; p4; p5 ])

        /// <summary>
        /// Combines multiple Patterns (takes the last one).
        /// </summary>
        member _.Combine(_: Pattern.Pattern<'attributes>, pattern: Pattern.Pattern<'attributes>) = pattern

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> Pattern.Pattern<'attributes>) =
            items |> Seq.map f |> Seq.last

        /// <summary>
        /// Zero case (Pattern.WildcardPattern).
        /// </summary>
        member _.Zero() = Pattern.WildcardPattern defaultAttrs

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> Pattern.Pattern<'attributes>) = f

        /// <summary>
        /// Runs the builder to produce the final Pattern.
        /// </summary>
        member _.Run(f: unit -> Pattern.Pattern<'attributes>) = f()

        /// Regular methods for direct usage (Pascal case)
        /// <summary>
        /// Creates a Pattern.WildcardPattern (regular method).
        /// </summary>
        member _.Wildcard() = Pattern.WildcardPattern defaultAttrs

        /// <summary>
        /// Creates a VariablePattern (regular method).
        /// </summary>
        member _.Variable(name: Name) =
            Pattern.AsPattern(defaultAttrs, Pattern.WildcardPattern defaultAttrs, name)

        /// <summary>
        /// Creates a VariablePattern from string (regular method).
        /// </summary>
        member _.Variable(name: string) =
            Pattern.AsPattern(defaultAttrs, Pattern.WildcardPattern defaultAttrs, Name.fromString name)

        /// <summary>
        /// Creates a VariablePattern (short form of Variable).
        /// </summary>
        member _.Var(name: Name) =
            Pattern.AsPattern(defaultAttrs, Pattern.WildcardPattern defaultAttrs, name)

        /// <summary>
        /// Creates a VariablePattern from string (short form of Variable).
        /// </summary>
        member _.Var(name: string) =
            Pattern.AsPattern(defaultAttrs, Pattern.WildcardPattern defaultAttrs, Name.fromString name)

        /// <summary>
        /// Creates a Pattern.TuplePattern (regular method).
        /// </summary>
        member _.Tuple(patterns: Pattern.Pattern<'attributes> list) =
            Pattern.TuplePattern(defaultAttrs, patterns)

        /// <summary>
        /// Creates a Pattern.ConstructorPattern (regular method).
        /// </summary>
        member _.Constructor(fqName: FQName, patterns: Pattern.Pattern<'attributes> list) =
            Pattern.ConstructorPattern(defaultAttrs, fqName, patterns)

        /// <summary>
        /// Creates a Pattern.LiteralPattern (regular method).
        /// </summary>
        member _.Literal(lit: Literal) = Pattern.LiteralPattern(defaultAttrs, lit)

        /// <summary>
        /// Creates a Pattern.UnitPattern (regular method).
        /// </summary>
        member _.Unit() = Pattern.UnitPattern defaultAttrs

        /// <summary>
        /// Creates an Pattern.EmptyListPattern (regular method).
        /// </summary>
        member _.EmptyList() = Pattern.EmptyListPattern defaultAttrs

        /// <summary>
        /// Creates a Pattern.HeadTailPattern (regular method).
        /// </summary>
        member _.HeadTail(headPattern: Pattern.Pattern<'attributes>, tailPattern: Pattern.Pattern<'attributes>) =
            Pattern.HeadTailPattern(defaultAttrs, headPattern, tailPattern)

        /// <summary>
        /// Creates an Pattern.AsPattern (regular method).
        /// </summary>
        member _.AsPattern(nested: Pattern.Pattern<'attributes>, name: Name) =
            Pattern.AsPattern(defaultAttrs, nested, name)

        /// <summary>
        /// Creates an Pattern.AsPattern with string name (regular method).
        /// </summary>
        member _.AsPattern(nested: Pattern.Pattern<'attributes>, name: string) =
            Pattern.AsPattern(defaultAttrs, nested, Name.fromString name)

        /// CustomOperations for CE usage (Pascal case)
        /// <summary>
        /// Creates a Pattern.WildcardPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Wildcard")>]
        member _.WildcardOp(_state: Pattern.Pattern<'attributes>) = Pattern.WildcardPattern defaultAttrs

        /// <summary>
        /// Creates a VariablePattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Variable")>]
        member _.VariableOp(_state: Pattern.Pattern<'attributes>, name: string) =
            Pattern.AsPattern(defaultAttrs, Pattern.WildcardPattern defaultAttrs, Name.fromString name)

        /// <summary>
        /// Creates a VariablePattern with Name (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Variable")>]
        member _.VariableOp(_state: Pattern.Pattern<'attributes>, name: Name) =
            Pattern.AsPattern(defaultAttrs, Pattern.WildcardPattern defaultAttrs, name)

        /// <summary>
        /// Creates a Pattern.TuplePattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Tuple")>]
        member _.TupleOp(_state: Pattern.Pattern<'attributes>, patterns: Pattern.Pattern<'attributes> list) =
            Pattern.TuplePattern(defaultAttrs, patterns)

        /// <summary>
        /// Creates a Pattern.ConstructorPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Constructor")>]
        member _.ConstructorOp(_state: Pattern.Pattern<'attributes>, fqName: FQName, patterns: Pattern.Pattern<'attributes> list) =
            Pattern.ConstructorPattern(defaultAttrs, fqName, patterns)

        /// <summary>
        /// Creates an Pattern.EmptyListPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("EmptyList")>]
        member _.EmptyListOp(_state: Pattern.Pattern<'attributes>) = Pattern.EmptyListPattern defaultAttrs

        /// <summary>
        /// Creates a Pattern.HeadTailPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("HeadTail")>]
        member _.HeadTailOp(_state: Pattern.Pattern<'attributes>, headPattern: Pattern.Pattern<'attributes>, tailPattern: Pattern.Pattern<'attributes>) =
            Pattern.HeadTailPattern(defaultAttrs, headPattern, tailPattern)

        /// <summary>
        /// Creates a Pattern.LiteralPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Literal")>]
        member _.LiteralOp(_state: Pattern.Pattern<'attributes>, lit: Literal) =
            Pattern.LiteralPattern(defaultAttrs, lit)

        /// <summary>
        /// Creates a Pattern.UnitPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Unit")>]
        member _.UnitOp(_state: Pattern.Pattern<'attributes>) = Pattern.UnitPattern defaultAttrs

        /// <summary>
        /// Creates an Pattern.AsPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Pattern.AsPattern")>]
        member _.AsPatternOp(_state: Pattern.Pattern<'attributes>, nested: Pattern.Pattern<'attributes>, name: Name) =
            Pattern.AsPattern(defaultAttrs, nested, name)

        /// <summary>
        /// Creates an Pattern.AsPattern with string name (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Pattern.AsPattern")>]
        member _.AsPatternOp(_state: Pattern.Pattern<'attributes>, nested: Pattern.Pattern<'attributes>, name: string) =
            Pattern.AsPattern(defaultAttrs, nested, Name.fromString name)

    /// <summary>
    /// PatternBuilderWithAttrs provides a Computation Expression for creating Pattern values with explicit attributes.
    /// </summary>
    type PatternBuilderWithAttrs<'attributes>(attrs: 'attributes) =
        /// <summary>
        /// Yields a Pattern directly.
        /// </summary>
        member _.Yield(pattern: Pattern.Pattern<'attributes>) = pattern

        /// <summary>
        /// Yields unit as Pattern.UnitPattern (tagless syntax).
        /// </summary>
        member _.Yield((): unit) = Pattern.UnitPattern attrs

        /// <summary>
        /// Yields a 2-tuple of patterns as Pattern.TuplePattern (tagless syntax).
        /// </summary>
        member _.Yield((p1, p2): Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes>) =
            Pattern.TuplePattern(attrs, [ p1; p2 ])

        /// <summary>
        /// Yields a 3-tuple of patterns as Pattern.TuplePattern (tagless syntax).
        /// </summary>
        member _.Yield((p1, p2, p3): Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes>) =
            Pattern.TuplePattern(attrs, [ p1; p2; p3 ])

        /// <summary>
        /// Yields a 4-tuple of patterns as Pattern.TuplePattern (tagless syntax).
        /// </summary>
        member _.Yield((p1, p2, p3, p4): Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes>) =
            Pattern.TuplePattern(attrs, [ p1; p2; p3; p4 ])

        /// <summary>
        /// Yields a 5-tuple of patterns as Pattern.TuplePattern (tagless syntax).
        /// </summary>
        member _.Yield((p1, p2, p3, p4, p5): Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes> * Pattern.Pattern<'attributes>) =
            Pattern.TuplePattern(attrs, [ p1; p2; p3; p4; p5 ])

        /// <summary>
        /// Combines multiple Patterns (takes the last one).
        /// </summary>
        member _.Combine(_: Pattern.Pattern<'attributes>, pattern: Pattern.Pattern<'attributes>) = pattern

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> Pattern.Pattern<'attributes>) =
            items |> Seq.map f |> Seq.last

        /// <summary>
        /// Zero case (Pattern.WildcardPattern).
        /// </summary>
        member _.Zero() = Pattern.WildcardPattern attrs

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> Pattern.Pattern<'attributes>) = f

        /// <summary>
        /// Runs the builder to produce the final Pattern.
        /// </summary>
        member _.Run(f: unit -> Pattern.Pattern<'attributes>) = f()

        /// Regular methods for direct usage (Pascal case)
        /// <summary>
        /// Creates a Pattern.WildcardPattern (regular method).
        /// </summary>
        member _.Wildcard() = Pattern.WildcardPattern attrs

        /// <summary>
        /// Creates a VariablePattern (regular method).
        /// </summary>
        member _.Variable(name: Name) =
            Pattern.AsPattern(attrs, Pattern.WildcardPattern attrs, name)

        /// <summary>
        /// Creates a VariablePattern from string (regular method).
        /// </summary>
        member _.Variable(name: string) =
            Pattern.AsPattern(attrs, Pattern.WildcardPattern attrs, Name.fromString name)

        /// <summary>
        /// Creates a VariablePattern (short form of Variable).
        /// </summary>
        member _.Var(name: Name) =
            Pattern.AsPattern(attrs, Pattern.WildcardPattern attrs, name)

        /// <summary>
        /// Creates a VariablePattern from string (short form of Variable).
        /// </summary>
        member _.Var(name: string) =
            Pattern.AsPattern(attrs, Pattern.WildcardPattern attrs, Name.fromString name)

        /// <summary>
        /// Creates a Pattern.TuplePattern (regular method).
        /// </summary>
        member _.Tuple(patterns: Pattern.Pattern<'attributes> list) =
            Pattern.TuplePattern(attrs, patterns)

        /// <summary>
        /// Creates a Pattern.ConstructorPattern (regular method).
        /// </summary>
        member _.Constructor(fqName: FQName, patterns: Pattern.Pattern<'attributes> list) =
            Pattern.ConstructorPattern(attrs, fqName, patterns)

        /// <summary>
        /// Creates a Pattern.LiteralPattern (regular method).
        /// </summary>
        member _.Literal(lit: Literal) = Pattern.LiteralPattern(attrs, lit)

        /// <summary>
        /// Creates a Pattern.UnitPattern (regular method).
        /// </summary>
        member _.Unit() = Pattern.UnitPattern attrs

        /// <summary>
        /// Creates an Pattern.EmptyListPattern (regular method).
        /// </summary>
        member _.EmptyList() = Pattern.EmptyListPattern attrs

        /// <summary>
        /// Creates a Pattern.HeadTailPattern (regular method).
        /// </summary>
        member _.HeadTail(headPattern: Pattern.Pattern<'attributes>, tailPattern: Pattern.Pattern<'attributes>) =
            Pattern.HeadTailPattern(attrs, headPattern, tailPattern)

        /// <summary>
        /// Creates an Pattern.AsPattern (regular method).
        /// </summary>
        member _.AsPattern(nested: Pattern.Pattern<'attributes>, name: Name) =
            Pattern.AsPattern(attrs, nested, name)

        /// <summary>
        /// Creates an Pattern.AsPattern with string name (regular method).
        /// </summary>
        member _.AsPattern(nested: Pattern.Pattern<'attributes>, name: string) =
            Pattern.AsPattern(attrs, nested, Name.fromString name)

        /// CustomOperations for CE usage (Pascal case)
        /// <summary>
        /// Creates a Pattern.WildcardPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Wildcard")>]
        member _.WildcardOp(_state: Pattern.Pattern<'attributes>) = Pattern.WildcardPattern attrs

        /// <summary>
        /// Creates a VariablePattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Variable")>]
        member _.VariableOp(_state: Pattern.Pattern<'attributes>, name: string) =
            Pattern.AsPattern(attrs, Pattern.WildcardPattern attrs, Name.fromString name)

        /// <summary>
        /// Creates a VariablePattern with Name (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Variable")>]
        member _.VariableOp(_state: Pattern.Pattern<'attributes>, name: Name) =
            Pattern.AsPattern(attrs, Pattern.WildcardPattern attrs, name)

        /// <summary>
        /// Creates a Pattern.TuplePattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Tuple")>]
        member _.TupleOp(_state: Pattern.Pattern<'attributes>, patterns: Pattern.Pattern<'attributes> list) =
            Pattern.TuplePattern(attrs, patterns)

        /// <summary>
        /// Creates a Pattern.ConstructorPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Constructor")>]
        member _.ConstructorOp(_state: Pattern.Pattern<'attributes>, fqName: FQName, patterns: Pattern.Pattern<'attributes> list) =
            Pattern.ConstructorPattern(attrs, fqName, patterns)

        /// <summary>
        /// Creates an Pattern.EmptyListPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("EmptyList")>]
        member _.EmptyListOp(_state: Pattern.Pattern<'attributes>) = Pattern.EmptyListPattern attrs

        /// <summary>
        /// Creates a Pattern.HeadTailPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("HeadTail")>]
        member _.HeadTailOp(_state: Pattern.Pattern<'attributes>, headPattern: Pattern.Pattern<'attributes>, tailPattern: Pattern.Pattern<'attributes>) =
            Pattern.HeadTailPattern(attrs, headPattern, tailPattern)

        /// <summary>
        /// Creates a Pattern.LiteralPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Literal")>]
        member _.LiteralOp(_state: Pattern.Pattern<'attributes>, lit: Literal) =
            Pattern.LiteralPattern(attrs, lit)

        /// <summary>
        /// Creates a Pattern.UnitPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Unit")>]
        member _.UnitOp(_state: Pattern.Pattern<'attributes>) = Pattern.UnitPattern attrs

        /// <summary>
        /// Creates an Pattern.AsPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Pattern.AsPattern")>]
        member _.AsPatternOp(_state: Pattern.Pattern<'attributes>, nested: Pattern.Pattern<'attributes>, name: Name) =
            Pattern.AsPattern(attrs, nested, name)

        /// <summary>
        /// Creates an Pattern.AsPattern with string name (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Pattern.AsPattern")>]
        member _.AsPatternOp(_state: Pattern.Pattern<'attributes>, nested: Pattern.Pattern<'attributes>, name: string) =
            Pattern.AsPattern(attrs, nested, Name.fromString name)

    /// <summary>
    /// PatternBuilder for unit attributes.
    /// </summary>
    [<Sealed>]
    type PatternBuilder() =
        let defaultAttrs = ()

        /// <summary>
        /// Yields a Pattern directly.
        /// </summary>
        member _.Yield(pattern: Pattern.Pattern<unit>) = pattern

        /// <summary>
        /// Yields unit as Pattern.UnitPattern (tagless syntax).
        /// </summary>
        member _.Yield((): unit) = Pattern.UnitPattern defaultAttrs

        /// <summary>
        /// Yields a 2-tuple of patterns as Pattern.TuplePattern (tagless syntax).
        /// </summary>
        member _.Yield((p1, p2): Pattern.Pattern<unit> * Pattern.Pattern<unit>) =
            Pattern.TuplePattern(defaultAttrs, [ p1; p2 ])

        /// <summary>
        /// Yields a 3-tuple of patterns as Pattern.TuplePattern (tagless syntax).
        /// </summary>
        member _.Yield((p1, p2, p3): Pattern.Pattern<unit> * Pattern.Pattern<unit> * Pattern.Pattern<unit>) =
            Pattern.TuplePattern(defaultAttrs, [ p1; p2; p3 ])

        /// <summary>
        /// Yields a 4-tuple of patterns as Pattern.TuplePattern (tagless syntax).
        /// </summary>
        member _.Yield((p1, p2, p3, p4): Pattern.Pattern<unit> * Pattern.Pattern<unit> * Pattern.Pattern<unit> * Pattern.Pattern<unit>) =
            Pattern.TuplePattern(defaultAttrs, [ p1; p2; p3; p4 ])

        /// <summary>
        /// Yields a 5-tuple of patterns as Pattern.TuplePattern (tagless syntax).
        /// </summary>
        member _.Yield((p1, p2, p3, p4, p5): Pattern.Pattern<unit> * Pattern.Pattern<unit> * Pattern.Pattern<unit> * Pattern.Pattern<unit> * Pattern.Pattern<unit>) =
            Pattern.TuplePattern(defaultAttrs, [ p1; p2; p3; p4; p5 ])

        /// <summary>
        /// Combines multiple Patterns (takes the last one).
        /// </summary>
        member _.Combine(_: Pattern.Pattern<unit>, pattern: Pattern.Pattern<unit>) = pattern

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> Pattern.Pattern<unit>) =
            items |> Seq.map f |> Seq.last

        /// <summary>
        /// Zero case (Pattern.WildcardPattern).
        /// </summary>
        member _.Zero() = Pattern.WildcardPattern defaultAttrs

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> Pattern.Pattern<unit>) = f

        /// <summary>
        /// Runs the builder to produce the final Pattern.
        /// </summary>
        member _.Run(f: unit -> Pattern.Pattern<unit>) = f()

        /// Regular methods for direct usage (Pascal case)
        /// <summary>
        /// Creates a Pattern.WildcardPattern (regular method).
        /// </summary>
        member _.Wildcard() = Pattern.WildcardPattern defaultAttrs

        /// <summary>
        /// Creates a VariablePattern (regular method).
        /// </summary>
        member _.Variable(name: Name) =
            Pattern.AsPattern(defaultAttrs, Pattern.WildcardPattern defaultAttrs, name)

        /// <summary>
        /// Creates a VariablePattern from string (regular method).
        /// </summary>
        member _.Variable(name: string) =
            Pattern.AsPattern(defaultAttrs, Pattern.WildcardPattern defaultAttrs, Name.fromString name)

        /// <summary>
        /// Creates a VariablePattern (short form of Variable).
        /// </summary>
        member _.Var(name: Name) =
            Pattern.AsPattern(defaultAttrs, Pattern.WildcardPattern defaultAttrs, name)

        /// <summary>
        /// Creates a VariablePattern from string (short form of Variable).
        /// </summary>
        member _.Var(name: string) =
            Pattern.AsPattern(defaultAttrs, Pattern.WildcardPattern defaultAttrs, Name.fromString name)

        /// <summary>
        /// Creates a Pattern.TuplePattern (regular method).
        /// </summary>
        member _.Tuple(patterns: Pattern.Pattern<unit> list) =
            Pattern.TuplePattern(defaultAttrs, patterns)

        /// <summary>
        /// Creates a Pattern.ConstructorPattern (regular method).
        /// </summary>
        member _.Constructor(fqName: FQName, patterns: Pattern.Pattern<unit> list) =
            Pattern.ConstructorPattern(defaultAttrs, fqName, patterns)

        /// <summary>
        /// Creates a Pattern.LiteralPattern (regular method).
        /// </summary>
        member _.Literal(lit: Literal) = Pattern.LiteralPattern(defaultAttrs, lit)

        /// <summary>
        /// Creates a Pattern.UnitPattern (regular method).
        /// </summary>
        member _.Unit() = Pattern.UnitPattern defaultAttrs

        /// <summary>
        /// Creates an Pattern.EmptyListPattern (regular method).
        /// </summary>
        member _.EmptyList() = Pattern.EmptyListPattern defaultAttrs

        /// <summary>
        /// Creates a Pattern.HeadTailPattern (regular method).
        /// </summary>
        member _.HeadTail(headPattern: Pattern.Pattern<unit>, tailPattern: Pattern.Pattern<unit>) =
            Pattern.HeadTailPattern(defaultAttrs, headPattern, tailPattern)

        /// <summary>
        /// Creates an Pattern.AsPattern (regular method).
        /// </summary>
        member _.AsPattern(nested: Pattern.Pattern<unit>, name: Name) =
            Pattern.AsPattern(defaultAttrs, nested, name)

        /// <summary>
        /// Creates an Pattern.AsPattern with string name (regular method).
        /// </summary>
        member _.AsPattern(nested: Pattern.Pattern<unit>, name: string) =
            Pattern.AsPattern(defaultAttrs, nested, Name.fromString name)

        /// CustomOperations for CE usage (Pascal case)
        /// <summary>
        /// Creates a Pattern.WildcardPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Wildcard")>]
        member _.WildcardOp(_state: Pattern.Pattern<unit>) =
            Pattern.WildcardPattern defaultAttrs

        /// <summary>
        /// Creates a VariablePattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Variable")>]
        member _.VariableOp(_state: Pattern.Pattern<unit>, name: string) =
            Pattern.AsPattern(defaultAttrs, Pattern.WildcardPattern defaultAttrs, Name.fromString name)

        /// <summary>
        /// Creates a VariablePattern with Name (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Variable")>]
        member _.VariableOp(_state: Pattern.Pattern<unit>, name: Name) =
            Pattern.AsPattern(defaultAttrs, Pattern.WildcardPattern defaultAttrs, name)

        /// <summary>
        /// Creates a Pattern.TuplePattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Tuple")>]
        member _.TupleOp(_state: Pattern.Pattern<unit>, patterns: Pattern.Pattern<unit> list) =
            Pattern.TuplePattern(defaultAttrs, patterns)

        /// <summary>
        /// Creates a Pattern.ConstructorPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Constructor")>]
        member _.ConstructorOp(_state: Pattern.Pattern<unit>, fqName: FQName, patterns: Pattern.Pattern<unit> list) =
            Pattern.ConstructorPattern(defaultAttrs, fqName, patterns)

        /// <summary>
        /// Creates an Pattern.EmptyListPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("EmptyList")>]
        member _.EmptyListOp(_state: Pattern.Pattern<unit>) =
            Pattern.EmptyListPattern defaultAttrs

        /// <summary>
        /// Creates a Pattern.HeadTailPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("HeadTail")>]
        member _.HeadTailOp(_state: Pattern.Pattern<unit>, headPattern: Pattern.Pattern<unit>, tailPattern: Pattern.Pattern<unit>) =
            Pattern.HeadTailPattern(defaultAttrs, headPattern, tailPattern)

        /// <summary>
        /// Creates a Pattern.LiteralPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Literal")>]
        member _.LiteralOp(_state: Pattern.Pattern<unit>, lit: Literal) =
            Pattern.LiteralPattern(defaultAttrs, lit)

        /// <summary>
        /// Creates a Pattern.UnitPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Unit")>]
        member _.UnitOp(_state: Pattern.Pattern<unit>) =
            Pattern.UnitPattern defaultAttrs

        /// <summary>
        /// Creates an Pattern.AsPattern (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Pattern.AsPattern")>]
        member _.AsPatternOp(_state: Pattern.Pattern<unit>, nested: Pattern.Pattern<unit>, name: Name) =
            Pattern.AsPattern(defaultAttrs, nested, name)

        /// <summary>
        /// Creates an Pattern.AsPattern with string name (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Pattern.AsPattern")>]
        member _.AsPatternOp(_state: Pattern.Pattern<unit>, nested: Pattern.Pattern<unit>, name: string) =
            Pattern.AsPattern(defaultAttrs, nested, Name.fromString name)

        /// <summary>
        /// Sets explicit attributes for the builder.
        /// </summary>
        member _.WithAttributes<'a>(attrs: 'a) = PatternBuilderWithAttrs<'a>(attrs)

    /// <summary>
    /// Global builder instance for use in Computation Expressions.
    /// </summary>
    let pattern = PatternBuilder()

