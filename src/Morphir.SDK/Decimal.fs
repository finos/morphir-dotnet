namespace Morphir.SDK

/// <summary>
/// Decimal operations for Morphir SDK.
/// ADAPTED FROM: morphir-elm src/Morphir/SDK/Decimal.elm
/// </summary>
module Decimal =
    
    /// <summary>Convert decimal to string</summary>
    let inline toString (d: decimal) = d.ToString()

    /// <summary>Parse string to decimal</summary>
    let fromString (s: string) =
        match System.Decimal.TryParse(s) with
        | true, d -> Some d
        | false, _ -> None

    /// <summary>Convert int to decimal</summary>
    let inline fromInt (n: int) = decimal n

    /// <summary>Convert float to decimal</summary>
    let inline fromFloat (f: float) = decimal f

    /// <summary>Convert decimal to float</summary>
    let inline toFloat (d: decimal) = float d

    /// <summary>Round decimal to specified number of decimal places</summary>
    let inline round (decimals: int) (d: decimal) = 
        System.Math.Round(d, decimals)

    /// <summary>Truncate decimal to integer part</summary>
    let inline truncate (d: decimal) = System.Math.Truncate(d)

    /// <summary>Get absolute value of decimal</summary>
    let inline abs (d: decimal) = System.Math.Abs(d)

    /// <summary>Negate a decimal</summary>
    let inline negate (d: decimal) = -d

    /// <summary>Add two decimals</summary>
    let inline add (d1: decimal) (d2: decimal) = d1 + d2

    /// <summary>Subtract two decimals</summary>
    let inline subtract (d1: decimal) (d2: decimal) = d1 - d2

    /// <summary>Multiply two decimals</summary>
    let inline multiply (d1: decimal) (d2: decimal) = d1 * d2

    /// <summary>Divide two decimals</summary>
    let inline divide (d1: decimal) (d2: decimal) = d1 / d2

    /// <summary>Compare two decimals</summary>
    let compare (d1: decimal) (d2: decimal) =
        match d1.CompareTo(d2) with
        | n when n < 0 -> LT
        | 0 -> EQ
        | _ -> GT
