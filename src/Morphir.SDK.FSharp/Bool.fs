namespace Morphir.SDK

/// <summary>
/// Boolean operations for Morphir SDK.
/// Most boolean operations are already available in F#, so this module is minimal.
/// ADAPTED FROM: morphir-elm src/Morphir/SDK/Bool.elm
/// </summary>
module Bool =
    
    /// <summary>Logical NOT</summary>
    let inline not a = not a

    /// <summary>Logical XOR</summary>
    let inline xor a b = a <> b
