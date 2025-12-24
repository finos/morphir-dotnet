namespace Morphir.IR.Classic.DSL

/// <summary>
/// Literals module provides helper functions and Computation Expression builders for creating Literal values.
/// </summary>
module Literals =

    open Morphir.IR.Classic.Literal

    /// <summary>
    /// LiteralBuilder provides a Computation Expression for creating Literal values.
    /// </summary>
    type LiteralBuilder() =
        /// <summary>
        /// Yields a Literal directly.
        /// </summary>
        member _.Yield(lit: Literal) = lit

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
        /// Zero case (BoolLiteral false).
        /// </summary>
        member _.Zero() = BoolLiteral false

        /// <summary>
        /// Creates a BoolLiteral.
        /// </summary>
        member _.Bool(value: bool) = BoolLiteral value

        /// <summary>
        /// Creates a CharLiteral.
        /// </summary>
        member _.Char(value: char) = CharLiteral value

        /// <summary>
        /// Creates a StringLiteral.
        /// </summary>
        member _.String(value: string) = StringLiteral value

        /// <summary>
        /// Creates a WholeNumberLiteral.
        /// </summary>
        member _.Int(value: int64) = WholeNumberLiteral value

        /// <summary>
        /// Creates a FloatLiteral.
        /// </summary>
        member _.Float(value: float) = FloatLiteral value

        /// <summary>
        /// Creates a DecimalLiteral.
        /// </summary>
        member _.Decimal(value: string) = DecimalLiteral value

    /// <summary>
    /// Global builder instance for use in Computation Expressions.
    /// </summary>
    let literal = LiteralBuilder()

    /// <summary>
    /// Helper functions for creating literals (for convenience, can be used without CE).
    /// </summary>
    module Helpers =
        /// <summary>
        /// Creates a BoolLiteral.
        /// </summary>
        let boolLiteral (value: bool) = BoolLiteral value

        /// <summary>
        /// Creates a CharLiteral.
        /// </summary>
        let charLiteral (value: char) = CharLiteral value

        /// <summary>
        /// Creates a StringLiteral.
        /// </summary>
        let stringLiteral (value: string) = StringLiteral value

        /// <summary>
        /// Creates a WholeNumberLiteral.
        /// </summary>
        let wholeNumberLiteral (value: int64) = WholeNumberLiteral value

        /// <summary>
        /// Creates a FloatLiteral.
        /// </summary>
        let floatLiteral (value: float) = FloatLiteral value

        /// <summary>
        /// Creates a DecimalLiteral.
        /// </summary>
        let decimalLiteral (value: string) = DecimalLiteral value

