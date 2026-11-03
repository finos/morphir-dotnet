namespace Morphir.SDK

/// <summary>
/// Integer operations for Morphir SDK.
/// ADAPTED FROM: morphir-elm src/Morphir/SDK/Int.elm
/// </summary>
module Int =
    
    /// <summary>Convert int to string</summary>
    let inline toString (n: int) = n.ToString()

    /// <summary>Parse string to int</summary>
    let fromString (s: string) =
        match System.Int32.TryParse(s) with
        | true, n -> Some n
        | false, _ -> None

    /// <summary>Convert float to int (truncate)</summary>
    let inline fromFloat (f: float) = int f

    /// <summary>Convert int to float</summary>
    let inline toFloat (n: int) = float n
