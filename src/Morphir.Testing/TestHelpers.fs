namespace Morphir.Testing

/// <summary>
/// Assertion module providing Elm-like test API for Expecto tests.
/// This module provides a more Elm-friendly API that aligns with the Elm test library,
/// making it easier to write tests that match the structure and style of morphir-elm tests.
/// </summary>
module Assertions =

    open Expecto
    open System

    /// <summary>
    /// Elm-like Expect module that provides pipeline-friendly assertion functions.
    /// Matches the API style of Elm's Expect module for consistency with morphir-elm tests.
    /// </summary>
    module Expect =

        /// <summary>
        /// Asserts that two values are equal, with an auto-generated meaningful message.
        /// Supports pipeline style: actual |> equal expected
        /// This matches Elm's Expect.equal API where the expected value comes first.
        /// </summary>
        let equal (expected: 'a) (actual: 'a) : unit =
            let message = $"Expected {expected}, but got {actual}"
            Expect.equal actual expected message

        /// <summary>
        /// Asserts that two values are equal, with a custom message.
        /// Supports pipeline style: actual |> equalWithMessage expected "custom message"
        /// </summary>
        let equalWithMessage (expected: 'a) (message: string) (actual: 'a) : unit =
            Expect.equal actual expected message

        /// <summary>
        /// Asserts that a value is true, with an auto-generated meaningful message.
        /// Supports pipeline style: value |> isTrue
        /// </summary>
        let isTrue (value: bool) : unit =
            let message = if value then "Value is true as expected" else "Expected value to be true, but got false"
            Expect.isTrue value message

        /// <summary>
        /// Asserts that a value is true, with a custom message.
        /// Supports pipeline style: value |> isTrueWithMessage "custom message"
        /// </summary>
        let isTrueWithMessage (message: string) (value: bool) : unit =
            Expect.isTrue value message

        /// <summary>
        /// Asserts that a value is false, with an auto-generated meaningful message.
        /// Supports pipeline style: value |> isFalse
        /// </summary>
        let isFalse (value: bool) : unit =
            let message = if value then "Expected value to be false, but got true" else "Value is false as expected"
            Expect.isFalse value message

        /// <summary>
        /// Asserts that a value is false, with a custom message.
        /// Supports pipeline style: value |> isFalseWithMessage "custom message"
        /// </summary>
        let isFalseWithMessage (message: string) (value: bool) : unit =
            Expect.isFalse value message

