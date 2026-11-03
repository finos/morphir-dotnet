namespace Morphir.SDK

/// <summary>
/// Tuple helper functions for working with 2-tuples and 3-tuples.
/// ADAPTED FROM: morphir-elm src/Morphir/SDK/Tuple.elm
/// </summary>
module Tuple =
    
    /// <summary>Create a 2-tuple</summary>
    let inline pair a b = (a, b)

    /// <summary>Get the first element of a 2-tuple</summary>
    let inline first (a, _) = a

    /// <summary>Get the second element of a 2-tuple</summary>
    let inline second (_, b) = b

    /// <summary>Map a function over the first element</summary>
    let inline mapFirst f (a, b) = (f a, b)

    /// <summary>Map a function over the second element</summary>
    let inline mapSecond f (a, b) = (a, f b)

    /// <summary>Map functions over both elements</summary>
    let inline mapBoth f g (a, b) = (f a, g b)

    /// <summary>Create a 3-tuple</summary>
    let inline triple a b c = (a, b, c)
