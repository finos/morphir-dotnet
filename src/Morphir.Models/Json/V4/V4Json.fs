namespace Morphir.Json.V4

open System
open System.Text.Json
open Morphir.IR.V4

/// <summary>A complete IR v4 document.</summary>
type V4Document = {
    /// <summary>The exact release. Integer <c>4</c> and string <c>"4.0.0"</c> both give <c>"4.0.0"</c>.</summary>
    FormatVersion: string
    /// <summary>The distribution.</summary>
    Distribution: Distribution
}

/// <summary>
/// Reads IR v4 JSON into the typed model, accepting every compact, expanded and legacy spelling and
/// normalizing them to one value. These readers return errors for bad input and do not raise.
/// </summary>
[<RequireQualifiedAccess>]
module V4Json =
    let private guarded (read: unit -> 'a) : Result<'a, V4JsonError> =
        try
            Result.Ok(read ())
        with
        | V4JsonFailure error -> Result.Error error
        | :? InsufficientExecutionStackException ->
            Result.Error
                {
                    Rule = "depth_limit_exceeded"
                    Location = "$"
                    Message = "The JSON is nested too deeply to read."
                }

    let private readNode decode (element: JsonElement) =
        guarded (fun () ->
            V4JsonDecoding.checkDuplicates "$" element
            decode "$" element
        )

    /// <summary>Reads a complete document holding a Library, Specs or Application distribution.</summary>
    /// <remarks>The result keeps no reference to the document that holds <paramref name="root"/>.</remarks>
    let readDistribution (root: JsonElement) : Result<V4Document, V4JsonError> =
        guarded (fun () ->
            let formatVersion, distribution = V4JsonDistributions.document root

            {
                FormatVersion = formatVersion
                Distribution = distribution
            }
        )

    /// <summary>Reads a type expression.</summary>
    let readType (element: JsonElement) : Result<Type, V4JsonError> =
        readNode V4JsonValues.typ element

    /// <summary>Reads a value expression.</summary>
    let readValue (element: JsonElement) : Result<Value, V4JsonError> =
        readNode V4JsonValues.value element

    /// <summary>Reads a pattern.</summary>
    let readPattern (element: JsonElement) : Result<Pattern, V4JsonError> =
        readNode V4JsonValues.pattern element
