namespace Morphir.IR.Classic.DSL

open System.Runtime.CompilerServices

/// <summary>
/// Literals module provides Computation Expression builders for creating Literal values.
/// Supports both tagless (type-inferred) and tagged (explicit) syntax.
/// </summary>
module Literals =

    open Morphir.IR.Classic.Literal

    /// <summary>
    /// LiteralBuilder provides a Computation Expression for creating Literal values.
    /// Supports hybrid pattern: both tagless (Yield overloads) and tagged (CustomOperations).
    /// </summary>
    type LiteralBuilder() =
        /// <summary>
        /// Yields unit as empty StringLiteral (for CustomOperation initial state).
        /// </summary>
        member _.Yield((): unit) = StringLiteral ""

        /// <summary>
        /// Yields a Literal directly (pass-through for existing Literals).
        /// </summary>
        member _.Yield(lit: Literal) = lit

        /// <summary>
        /// Yields a bool as BoolLiteral (tagless syntax).
        /// </summary>
        member _.Yield(value: bool) = BoolLiteral value

        /// <summary>
        /// Yields a char as CharLiteral (tagless syntax).
        /// </summary>
        member _.Yield(value: char) = CharLiteral value

        /// <summary>
        /// Yields a string as StringLiteral (tagless syntax).
        /// </summary>
        member _.Yield(value: string) = StringLiteral value

        /// <summary>
        /// Yields an int64 as WholeNumberLiteral (tagless syntax).
        /// </summary>
        member _.Yield(value: int64) = WholeNumberLiteral value

        /// <summary>
        /// Yields an int32 as WholeNumberLiteral, converting to int64 (tagless syntax).
        /// </summary>
        member _.Yield(value: int) = WholeNumberLiteral(int64 value)

        /// <summary>
        /// Yields a float as FloatLiteral (tagless syntax).
        /// </summary>
        member _.Yield(value: float) = FloatLiteral value

        /// <summary>
        /// Yields a decimal as DecimalLiteral (tagless syntax).
        /// </summary>
        member _.Yield(value: decimal) = DecimalLiteral value

        /// <summary>
        /// Combines multiple Literals (takes the last one).
        /// </summary>
        member _.Combine(_: Literal, lit: Literal) = lit

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> Literal) =
            items |> Seq.map f |> Seq.last

        /// <summary>
        /// Zero case (empty string literal).
        /// </summary>
        member _.Zero() = StringLiteral ""

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> Literal) = f

        /// <summary>
        /// Runs the builder to produce the final Literal.
        /// </summary>
        member _.Run(f: unit -> Literal) = f()

        /// Tagged syntax via CustomOperations (for explicit documentation)
        /// <summary>
        /// Creates a BoolLiteral (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Bool")>]
        member _.BoolOp(_state: Literal, value: bool) = BoolLiteral value

        /// <summary>
        /// Creates a CharLiteral (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Char")>]
        member _.CharOp(_state: Literal, value: char) = CharLiteral value

        /// <summary>
        /// Creates a StringLiteral (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("String")>]
        member _.StringOp(_state: Literal, value: string) = StringLiteral value

        /// <summary>
        /// Creates a WholeNumberLiteral from int64 (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Int")>]
        member _.IntOp(_state: Literal, value: int64) = WholeNumberLiteral value

        /// <summary>
        /// Creates a WholeNumberLiteral from int32, converting to int64 (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Int")>]
        member _.IntOp(_state: Literal, value: int) = WholeNumberLiteral(int64 value)

        /// <summary>
        /// Creates a FloatLiteral (CustomOperation for CE).
        /// </summary>
        [<CustomOperation("Float")>]
        member _.FloatOp(_state: Literal, value: float) = FloatLiteral value

        /// <summary>
        /// Creates a DecimalLiteral from decimal type (CustomOperation for CE).
        /// Use 'm' suffix: literal { Decimal 123.45m }
        /// </summary>
        [<CustomOperation("Decimal")>]
        member _.DecimalOp(_state: Literal, value: decimal) = DecimalLiteral value

    /// <summary>
    /// Global builder instance for use in Computation Expressions.
    /// </summary>
    let literal = LiteralBuilder()

