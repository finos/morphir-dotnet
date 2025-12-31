namespace Morphir.IR.Classic

open Morphir.IR

/// <summary>
/// Pattern represents a pattern matching expression in the Morphir IR.
/// Each variant includes attributes as the first parameter.
/// Patterns are used for destructuring and filtering values in lambda,
/// let destructure, and pattern match expressions.
/// </summary>
type Pattern<'attributes> =
    | WildcardPattern of 'attributes
    | AsPattern of 'attributes * Pattern<'attributes> * Name
    | TuplePattern of 'attributes * Pattern<'attributes> list
    | ConstructorPattern of 'attributes * FQName * Pattern<'attributes> list
    | EmptyListPattern of 'attributes
    | HeadTailPattern of 'attributes * Pattern<'attributes> * Pattern<'attributes>
    | LiteralPattern of 'attributes * Literal
    | UnitPattern of 'attributes

/// <summary>
/// Pattern module provides helper functions for working with patterns.
/// Requires qualified access to avoid name conflicts.
/// </summary>
[<RequireQualifiedAccess>]
module Pattern =

    /// <summary>
    /// Creates a wildcard pattern (matches any value without binding).
    /// </summary>
    let wildcard<'attributes> (attributes: 'attributes) : Pattern<'attributes> =
        WildcardPattern attributes

    /// <summary>
    /// Creates an as-pattern (binds a name to a value matched by a nested pattern).
    /// </summary>
    let asPattern<'attributes> (attributes: 'attributes) (nestedPattern: Pattern<'attributes>) (variableName: Name) : Pattern<'attributes> =
        AsPattern(attributes, nestedPattern, variableName)

    /// <summary>
    /// Creates a tuple pattern (matches a tuple by matching each element).
    /// </summary>
    let tuple<'attributes> (attributes: 'attributes) (elementPatterns: Pattern<'attributes> list) : Pattern<'attributes> =
        TuplePattern(attributes, elementPatterns)

    /// <summary>
    /// Creates a constructor pattern (matches a specific type constructor and its arguments).
    /// </summary>
    let constructor<'attributes> (attributes: 'attributes) (fqName: FQName) (argumentPatterns: Pattern<'attributes> list) : Pattern<'attributes> =
        ConstructorPattern(attributes, fqName, argumentPatterns)

    /// <summary>
    /// Creates an empty list pattern (matches an empty list).
    /// </summary>
    let emptyList<'attributes> (attributes: 'attributes) : Pattern<'attributes> =
        EmptyListPattern attributes

    /// <summary>
    /// Creates a head-tail pattern (matches a non-empty list by head and tail).
    /// </summary>
    let headTail<'attributes> (attributes: 'attributes) (headPattern: Pattern<'attributes>) (tailPattern: Pattern<'attributes>) : Pattern<'attributes> =
        HeadTailPattern(attributes, headPattern, tailPattern)

    /// <summary>
    /// Creates a literal pattern (matches an exact literal value).
    /// </summary>
    let literal<'attributes> (attributes: 'attributes) (literal: Literal) : Pattern<'attributes> =
        LiteralPattern(attributes, literal)

    /// <summary>
    /// Creates a unit pattern (matches the unit value).
    /// </summary>
    let unit<'attributes> (attributes: 'attributes) : Pattern<'attributes> =
        UnitPattern attributes


    // toString functions

    /// <summary>
    /// Converts a Pattern to its string representation.
    /// </summary>
    let rec toString (pattern: Pattern<'attributes>) : string =
        match pattern with
        | WildcardPattern _ -> "_"
        | AsPattern(_, nested, name) ->
            $"{toString nested} as {Name.toCamelCase name}"
        | TuplePattern(_, patterns) ->
            let patternsText =
                patterns
                |> List.map toString
                |> String.concat " , "
            $"({patternsText})"
        | ConstructorPattern(_, fqName, patterns) ->
            let nameText = FQName.toString fqName
            match patterns with
            | [] -> nameText
            | _ ->
                let argsText =
                    patterns
                    |> List.map toString
                    |> String.concat " "
                $"{nameText} {argsText}"
        | EmptyListPattern _ -> "[]"
        | HeadTailPattern(_, headPattern, tailPattern) ->
            $"{toString headPattern} :: {toString tailPattern}"
        | LiteralPattern(_, literal) -> Literal.toString literal
        | UnitPattern _ -> "()"

/// <summary>
/// PatternExtensions provides extension methods for Pattern that are available in both F# and C#.
/// This module is marked AutoOpen so extensions are available whenever Morphir.IR.Classic is opened.
/// </summary>
[<AutoOpen>]
module PatternExtensions =

    open System.Runtime.CompilerServices

    /// <summary>
    /// F# type extensions for Pattern (natural F# style).
    /// </summary>
    type Pattern<'attributes> with
        /// <summary>
        /// Fluent method to create an AsPattern from an existing pattern.
        /// Wraps this pattern and binds it to the specified name.
        /// Uses default attributes.
        /// </summary>
        /// <param name="name">The name to bind to this pattern (as string)</param>
        member this.As(name: string) : Pattern<'attributes> =
            AsPattern(Unchecked.defaultof<'attributes>, this, Name.fromString name)

        /// <summary>
        /// Fluent method to create an AsPattern from an existing pattern.
        /// Wraps this pattern and binds it to the specified name.
        /// Uses default attributes.
        /// </summary>
        /// <param name="name">The name to bind to this pattern (as Name)</param>
        member this.As(name: Name) : Pattern<'attributes> =
            AsPattern(Unchecked.defaultof<'attributes>, this, name)

        /// <summary>
        /// Fluent method to create an AsPattern from an existing pattern with explicit attributes.
        /// Wraps this pattern and binds it to the specified name.
        /// </summary>
        /// <param name="attributes">The attributes for the AsPattern</param>
        /// <param name="name">The name to bind to this pattern (as string)</param>
        member this.As(attributes: 'attributes, name: string) : Pattern<'attributes> =
            AsPattern(attributes, this, Name.fromString name)

        /// <summary>
        /// Fluent method to create an AsPattern from an existing pattern with explicit attributes.
        /// Wraps this pattern and binds it to the specified name.
        /// </summary>
        /// <param name="attributes">The attributes for the AsPattern</param>
        /// <param name="name">The name to bind to this pattern (as Name)</param>
        member this.As(attributes: 'attributes, name: Name) : Pattern<'attributes> =
            AsPattern(attributes, this, name)

        /// <summary>
        /// Fluent method to create a HeadTailPattern from an existing pattern.
        /// Creates a list cons pattern where this pattern is the head and the provided pattern is the tail.
        /// Uses default attributes.
        /// </summary>
        /// <param name="tail">The pattern for the tail of the list</param>
        member this.Cons(tail: Pattern<'attributes>) : Pattern<'attributes> =
            HeadTailPattern(Unchecked.defaultof<'attributes>, this, tail)

        /// <summary>
        /// Fluent method to create a HeadTailPattern from an existing pattern with explicit attributes.
        /// Creates a list cons pattern where this pattern is the head and the provided pattern is the tail.
        /// </summary>
        /// <param name="attributes">The attributes for the HeadTailPattern</param>
        /// <param name="tail">The pattern for the tail of the list</param>
        member this.Cons(attributes: 'attributes, tail: Pattern<'attributes>) : Pattern<'attributes> =
            HeadTailPattern(attributes, this, tail)

    /// <summary>
    /// C#-visible extension methods for Pattern (using Extension attribute).
    /// These make the same methods available in C# as extension methods.
    /// </summary>
    [<Extension>]
    type PatternExtensionsForCSharp =
        /// <summary>
        /// Fluent method to create an AsPattern (C# extension).
        /// </summary>
        [<Extension>]
        static member As(this: Pattern<'attributes>, name: string) : Pattern<'attributes> =
            AsPattern(Unchecked.defaultof<'attributes>, this, Name.fromString name)

        /// <summary>
        /// Fluent method to create an AsPattern (C# extension).
        /// </summary>
        [<Extension>]
        static member As(this: Pattern<'attributes>, name: Name) : Pattern<'attributes> =
            AsPattern(Unchecked.defaultof<'attributes>, this, name)

        /// <summary>
        /// Fluent method to create an AsPattern with explicit attributes (C# extension).
        /// </summary>
        [<Extension>]
        static member As(this: Pattern<'attributes>, attributes: 'attributes, name: string) : Pattern<'attributes> =
            AsPattern(attributes, this, Name.fromString name)

        /// <summary>
        /// Fluent method to create an AsPattern with explicit attributes (C# extension).
        /// </summary>
        [<Extension>]
        static member As(this: Pattern<'attributes>, attributes: 'attributes, name: Name) : Pattern<'attributes> =
            AsPattern(attributes, this, name)

        /// <summary>
        /// Fluent method to create a HeadTailPattern (C# extension).
        /// </summary>
        [<Extension>]
        static member Cons(this: Pattern<'attributes>, tail: Pattern<'attributes>) : Pattern<'attributes> =
            HeadTailPattern(Unchecked.defaultof<'attributes>, this, tail)

        /// <summary>
        /// Fluent method to create a HeadTailPattern with explicit attributes (C# extension).
        /// </summary>
        [<Extension>]
        static member Cons(this: Pattern<'attributes>, attributes: 'attributes, tail: Pattern<'attributes>) : Pattern<'attributes> =
            HeadTailPattern(attributes, this, tail)

