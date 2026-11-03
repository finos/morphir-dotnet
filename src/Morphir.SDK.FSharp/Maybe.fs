namespace Morphir.SDK

/// <summary>
/// Morphir Maybe type - alias for F# Option.
/// Maintains semantic alignment with Morphir IR Maybe type.
/// ADAPTED FROM: morphir-elm src/Morphir/SDK/Maybe.elm
/// </summary>
type Maybe<'a> = Option<'a>

/// <summary>
/// Functions for working with Maybe (Option) values.
/// These functions delegate to F# Option module for compatibility.
/// </summary>
module Maybe =
    
    /// <summary>Transform the value in a Maybe with a given function</summary>
    let inline map f maybe = Option.map f maybe

    /// <summary>Chain Maybe computations (monadic bind)</summary>
    let inline andThen f maybe = Option.bind f maybe

    /// <summary>Return the Maybe value or a default if Nothing/None</summary>
    let inline withDefault defaultValue maybe =
        Option.defaultValue defaultValue maybe

    /// <summary>Convert Maybe to Result with an error value if Nothing</summary>
    let toResult error maybe =
        match maybe with
        | Some value -> Ok value
        | None -> Error error

    /// <summary>Convert Result to Maybe, discarding the error</summary>
    let fromResult result =
        match result with
        | Ok value -> Some value
        | Error _ -> None

    /// <summary>Wrap a value in Maybe (Some)</summary>
    let inline just value = Some value

    /// <summary>The Nothing value (None)</summary>
    let nothing<'a> : Maybe<'a> = None

    /// <summary>Apply a function in a Maybe to a value in a Maybe</summary>
    let inline map2 f ma mb =
        match ma, mb with
        | Some a, Some b -> Some (f a b)
        | _ -> None

    /// <summary>Apply a function to three Maybe values</summary>
    let inline map3 f ma mb mc =
        match ma, mb, mc with
        | Some a, Some b, Some c -> Some (f a b c)
        | _ -> None

    /// <summary>Apply a function to four Maybe values</summary>
    let inline map4 f ma mb mc md =
        match ma, mb, mc, md with
        | Some a, Some b, Some c, Some d -> Some (f a b c d)
        | _ -> None

    /// <summary>Apply a function to five Maybe values</summary>
    let inline map5 f ma mb mc md me =
        match ma, mb, mc, md, me with
        | Some a, Some b, Some c, Some d, Some e -> Some (f a b c d e)
        | _ -> None
