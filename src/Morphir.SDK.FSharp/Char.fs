namespace Morphir.SDK

/// <summary>
/// Character operations for Morphir SDK.
/// ADAPTED FROM: morphir-elm src/Morphir/SDK/Char.elm
/// </summary>
module Char =
    
    /// <summary>Check if character is uppercase</summary>
    let inline isUpper (c: char) = System.Char.IsUpper(c)

    /// <summary>Check if character is lowercase</summary>
    let inline isLower (c: char) = System.Char.IsLower(c)

    /// <summary>Check if character is alphabetic</summary>
    let inline isAlpha (c: char) = System.Char.IsLetter(c)

    /// <summary>Check if character is alphanumeric</summary>
    let inline isAlphaNum (c: char) = System.Char.IsLetterOrDigit(c)

    /// <summary>Check if character is a digit</summary>
    let inline isDigit (c: char) = System.Char.IsDigit(c)

    /// <summary>Check if character is whitespace</summary>
    let inline isWhitespace (c: char) = System.Char.IsWhiteSpace(c)

    /// <summary>Convert character to uppercase</summary>
    let inline toUpper (c: char) = System.Char.ToUpperInvariant(c)

    /// <summary>Convert character to lowercase</summary>
    let inline toLower (c: char) = System.Char.ToLowerInvariant(c)

    /// <summary>Convert character to its code point (int)</summary>
    let inline toCode (c: char) = int c

    /// <summary>Convert code point to character</summary>
    let fromCode (code: int) =
        if code >= 0 && code <= 0x10FFFF then
            Some (char code)
        else
            None
