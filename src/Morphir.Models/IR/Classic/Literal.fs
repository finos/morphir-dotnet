namespace Morphir.IR.Classic

/// <summary>
/// Literal module provides constant value types for Morphir IR.
/// Literals represent immutable constant data.
/// </summary>
module Literal =

    /// <summary>
    /// Literal represents a constant value in the Morphir IR.
    /// </summary>
    type Literal =
        | BoolLiteral of bool
        | CharLiteral of char
        | StringLiteral of string
        | WholeNumberLiteral of int64
        | FloatLiteral of float
        | DecimalLiteral of string

    /// <summary>
    /// Creates a boolean literal.
    /// </summary>
    let boolLiteral (value: bool) : Literal = BoolLiteral value

    /// <summary>
    /// Creates a character literal.
    /// </summary>
    let charLiteral (value: char) : Literal = CharLiteral value

    /// <summary>
    /// Creates a string literal.
    /// </summary>
    let stringLiteral (value: string) : Literal = StringLiteral value

    /// <summary>
    /// Creates a whole number (integer) literal.
    /// </summary>
    let wholeNumberLiteral (value: int64) : Literal = WholeNumberLiteral value

    /// <summary>
    /// Creates a floating-point number literal.
    /// </summary>
    let floatLiteral (value: float) : Literal = FloatLiteral value

    /// <summary>
    /// Creates a decimal literal (arbitrary-precision, stored as string).
    /// </summary>
    let decimalLiteral (value: string) : Literal = DecimalLiteral value

