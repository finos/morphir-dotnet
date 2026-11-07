namespace Morphir.Extensions

open System
open Morphir.IR.Model

/// <summary>Builds the IR versions that a backend declares, from literals in source code.</summary>
[<AbstractClass; Sealed>]
type SourceFormatVersions =
    /// <summary>
    /// The parsed versions, in their declared spelling (<c>3</c> and <c>3.0.0</c> stay distinct). Throws
    /// <c>ArgumentException</c> with the parse detail for a literal that is not a format version.
    /// </summary>
    static member Create
        (
            first: string,
            [<ParamArray>] rest: string array
        ) : NonEmptySet<SourceFormatVersion> =
        let parse (literal: string) =
            match SourceFormatVersion.parse literal with
            | Ok version -> version
            | Error error ->
                invalidArg
                    (nameof literal)
                    $"Invalid format version literal '{literal}': {Text.versionError error}"

        NonEmptySet.create
            (parse first)
            (rest
             |> Array.map parse)
