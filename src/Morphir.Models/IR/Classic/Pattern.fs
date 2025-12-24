namespace Morphir.IR.Classic

/// <summary>
/// Pattern module provides pattern matching types for Morphir IR.
/// Patterns are used for destructuring and filtering values in lambda,
/// let destructure, and pattern match expressions.
/// </summary>
module Pattern =

    open Morphir.IR
    open Literal

    /// <summary>
    /// Pattern represents a pattern matching expression in the Morphir IR.
    /// Each variant includes attributes as the first parameter.
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
    /// Creates a wildcard pattern (matches any value without binding).
    /// </summary>
    let wildcardPattern<'attributes> (attributes: 'attributes) : Pattern<'attributes> =
        WildcardPattern attributes

    /// <summary>
    /// Creates an as-pattern (binds a name to a value matched by a nested pattern).
    /// </summary>
    let asPattern<'attributes> (attributes: 'attributes) (nestedPattern: Pattern<'attributes>) (variableName: Name) : Pattern<'attributes> =
        AsPattern(attributes, nestedPattern, variableName)

    /// <summary>
    /// Creates a tuple pattern (matches a tuple by matching each element).
    /// </summary>
    let tuplePattern<'attributes> (attributes: 'attributes) (elementPatterns: Pattern<'attributes> list) : Pattern<'attributes> =
        TuplePattern(attributes, elementPatterns)

    /// <summary>
    /// Creates a constructor pattern (matches a specific type constructor and its arguments).
    /// </summary>
    let constructorPattern<'attributes> (attributes: 'attributes) (fqName: FQName) (argumentPatterns: Pattern<'attributes> list) : Pattern<'attributes> =
        ConstructorPattern(attributes, fqName, argumentPatterns)

    /// <summary>
    /// Creates an empty list pattern (matches an empty list).
    /// </summary>
    let emptyListPattern<'attributes> (attributes: 'attributes) : Pattern<'attributes> =
        EmptyListPattern attributes

    /// <summary>
    /// Creates a head-tail pattern (matches a non-empty list by head and tail).
    /// </summary>
    let headTailPattern<'attributes> (attributes: 'attributes) (headPattern: Pattern<'attributes>) (tailPattern: Pattern<'attributes>) : Pattern<'attributes> =
        HeadTailPattern(attributes, headPattern, tailPattern)

    /// <summary>
    /// Creates a literal pattern (matches an exact literal value).
    /// </summary>
    let literalPattern<'attributes> (attributes: 'attributes) (literal: Literal) : Pattern<'attributes> =
        LiteralPattern(attributes, literal)

    /// <summary>
    /// Creates a unit pattern (matches the unit value).
    /// </summary>
    let unitPattern<'attributes> (attributes: 'attributes) : Pattern<'attributes> =
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

