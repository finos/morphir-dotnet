namespace Morphir.SDK

/// <summary>
/// Functions for working with Result values.
/// Result represents either success (Ok) or failure (Error).
/// ADAPTED FROM: morphir-elm src/Morphir/SDK/Result.elm
/// </summary>
module Result =
    
    /// <summary>Transform the success value with a function</summary>
    let inline map f result = Result.map f result

    /// <summary>Transform the error value with a function</summary>
    let inline mapError f result = Result.mapError f result

    /// <summary>Chain Result computations (monadic bind)</summary>
    let inline andThen f result = Result.bind f result

    /// <summary>Return the Ok value or a default if Error</summary>
    let inline withDefault defaultValue result =
        match result with
        | Ok value -> value
        | Error _ -> defaultValue

    /// <summary>Convert Result to Maybe, discarding the error</summary>
    let toMaybe result =
        match result with
        | Ok value -> Some value
        | Error _ -> None

    /// <summary>Convert Maybe to Result with error if Nothing</summary>
    let fromMaybe error maybe =
        match maybe with
        | Some value -> Ok value
        | None -> Error error

    /// <summary>Combine two Results with a function</summary>
    let inline map2 f ra rb =
        match ra, rb with
        | Ok a, Ok b -> Ok (f a b)
        | Error e, _ -> Error e
        | _, Error e -> Error e

    /// <summary>Combine three Results with a function</summary>
    let inline map3 f ra rb rc =
        match ra, rb, rc with
        | Ok a, Ok b, Ok c -> Ok (f a b c)
        | Error e, _, _ -> Error e
        | _, Error e, _ -> Error e
        | _, _, Error e -> Error e

    /// <summary>Combine four Results with a function</summary>
    let inline map4 f ra rb rc rd =
        match ra, rb, rc, rd with
        | Ok a, Ok b, Ok c, Ok d -> Ok (f a b c d)
        | Error e, _, _, _ -> Error e
        | _, Error e, _, _ -> Error e
        | _, _, Error e, _ -> Error e
        | _, _, _, Error e -> Error e

    /// <summary>Combine five Results with a function</summary>
    let inline map5 f ra rb rc rd re =
        match ra, rb, rc, rd, re with
        | Ok a, Ok b, Ok c, Ok d, Ok e -> Ok (f a b c d e)
        | Error err, _, _, _, _ -> Error err
        | _, Error err, _, _, _ -> Error err
        | _, _, Error err, _, _ -> Error err
        | _, _, _, Error err, _ -> Error err
        | _, _, _, _, Error err -> Error err

    /// <summary>Create an Ok value</summary>
    let inline ok value = Ok value

    /// <summary>Create an Error value</summary>
    let inline err error = Error error
