namespace Morphir.Json

open System.Text.Json
open Morphir.IR.Versioning

/// <summary>
/// TagNaming module provides version-aware tag naming for Morphir JSON serialization.
/// Different format versions use different tag naming conventions.
/// </summary>
module TagNaming =

    /// <summary>
    /// Strips "Literal" suffix if present and converts to snake_case with _literal suffix.
    /// Example: "BoolLiteral" -> "bool_literal"
    /// </summary>
    let private toSnakeCaseLiteralTag (baseName: string) : string =
        // Remove "Literal" suffix if present
        let nameWithoutSuffix =
            if baseName.EndsWith("Literal") then
                baseName.Substring(0, baseName.Length - 7)
            else
                baseName
        nameWithoutSuffix.ToLowerInvariant() + "_literal"

    /// <summary>
    /// Determines the literal tag format based on FormatVersion.
    /// v1, v2: snake_case with _literal suffix (e.g., "bool_literal")
    /// v3+: PascalCase (e.g., "BoolLiteral")
    /// </summary>
    let literalTag (version: FormatVersion) (baseName: string) : string =
        match version with
        | Classic n when n <= 2 -> toSnakeCaseLiteralTag baseName
        | Classic _ -> baseName  // v3+ use PascalCase
        | SemVer _ -> baseName   // SemVer versions use PascalCase

    /// <summary>
    /// Determines the type tag format based on FormatVersion.
    /// v1: lowercase (e.g., "variable")
    /// v2+: PascalCase (e.g., "Variable")
    /// </summary>
    let typeTag (version: FormatVersion) (baseName: string) : string =
        match version with
        | Classic 1 -> baseName.ToLowerInvariant()
        | Classic _ -> baseName  // v2+ use PascalCase
        | SemVer _ -> baseName   // SemVer versions use PascalCase

    /// <summary>
    /// Determines the value tag format based on FormatVersion.
    /// v1, v2: lowercase (e.g., "apply")
    /// v3+: PascalCase (e.g., "Apply")
    /// </summary>
    let valueTag (version: FormatVersion) (baseName: string) : string =
        match version with
        | Classic n when n <= 2 -> baseName.ToLowerInvariant()
        | Classic _ -> baseName  // v3+ use PascalCase
        | SemVer _ -> baseName   // SemVer versions use PascalCase

    /// <summary>
    /// Determines the pattern tag format based on FormatVersion.
    /// v1, v2: snake_case (e.g., "as_pattern")
    /// v3+: PascalCase (e.g., "AsPattern")
    /// </summary>
    let patternTag (version: FormatVersion) (baseName: string) : string =
        match version with
        | Classic n when n <= 2 ->
            // Convert PascalCase to snake_case
            baseName
            |> Seq.fold (fun (acc: System.Text.StringBuilder) c ->
                if System.Char.IsUpper(c) && acc.Length > 0 then
                    acc.Append('_').Append(System.Char.ToLowerInvariant(c))
                else
                    acc.Append(System.Char.ToLowerInvariant(c))
            ) (System.Text.StringBuilder())
            |> fun sb -> sb.ToString()
        | Classic _ -> baseName  // v3+ use PascalCase
        | SemVer _ -> baseName   // SemVer versions use PascalCase

    /// <summary>
    /// Determines the distribution tag format based on FormatVersion.
    /// v1: lowercase (e.g., "library")
    /// v2+: PascalCase (e.g., "Library")
    /// </summary>
    let distributionTag (version: FormatVersion) (baseName: string) : string =
        match version with
        | Classic 1 -> baseName.ToLowerInvariant()
        | Classic _ -> baseName  // v2+ use PascalCase
        | SemVer _ -> baseName   // SemVer versions use PascalCase

    /// <summary>
    /// Determines the access control tag format based on FormatVersion.
    /// v1: lowercase (e.g., "public", "private")
    /// v2+: PascalCase (e.g., "Public", "Private")
    /// </summary>
    let accessTag (version: FormatVersion) (baseName: string) : string =
        match version with
        | Classic 1 -> baseName.ToLowerInvariant()
        | Classic _ -> baseName  // v2+ use PascalCase
        | SemVer _ -> baseName   // SemVer versions use PascalCase


/// <summary>
/// MorphirJsonOptions provides configuration for Morphir JSON serialization.
/// </summary>
type MorphirJsonOptions =
    { /// <summary>The format version to use for serialization.</summary>
      FormatVersion: FormatVersion

      /// <summary>Whether to write indented JSON output.</summary>
      WriteIndented: bool }


/// <summary>
/// MorphirJsonOptions module provides functions for creating and working with serialization options.
/// </summary>
module MorphirJsonOptions =

    /// <summary>Options configured for format version 1.</summary>
    let v1 = { FormatVersion = Classic 1; WriteIndented = false }

    /// <summary>Options configured for format version 2.</summary>
    let v2 = { FormatVersion = Classic 2; WriteIndented = false }

    /// <summary>Options configured for format version 3.</summary>
    let v3 = { FormatVersion = Classic 3; WriteIndented = false }

    /// <summary>Default options (format version 3).</summary>
    let defaultOptions = v3

    /// <summary>
    /// Creates a new MorphirJsonOptions with the specified format version.
    /// </summary>
    let withFormatVersion (version: FormatVersion) (options: MorphirJsonOptions) : MorphirJsonOptions =
        { options with FormatVersion = version }

    /// <summary>
    /// Creates a new MorphirJsonOptions with indented output enabled.
    /// </summary>
    let withIndented (options: MorphirJsonOptions) : MorphirJsonOptions =
        { options with WriteIndented = true }

    /// <summary>
    /// Checks if the format version is a classic (integer) version.
    /// </summary>
    let isClassicVersion (options: MorphirJsonOptions) : bool =
        Morphir.IR.Versioning.isClassic options.FormatVersion

    /// <summary>
    /// Gets the major version number from the format version.
    /// For Classic versions, returns the integer value.
    /// For SemVer versions, returns the Major component.
    /// </summary>
    let getMajorVersion (options: MorphirJsonOptions) : int =
        match options.FormatVersion with
        | Classic n -> n
        | SemVer sv -> sv.Major

    /// <summary>
    /// Creates base JsonSerializerOptions without converters.
    /// Converters should be added separately to avoid circular dependencies.
    /// </summary>
    let createBaseSerializerOptions (options: MorphirJsonOptions) : JsonSerializerOptions =
        let jsonOptions = JsonSerializerOptions()
        jsonOptions.WriteIndented <- options.WriteIndented
        jsonOptions.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        jsonOptions.Encoder <- System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        jsonOptions

    /// <summary>
    /// Gets the options value from an optional parameter, using the default if not provided.
    /// </summary>
    let getOrDefault (optionsParam: MorphirJsonOptions option) : MorphirJsonOptions =
        defaultArg optionsParam defaultOptions

    /// <summary>
    /// Attempts to detect the format version from JSON content based on tag naming patterns.
    /// Returns v3 if detection is inconclusive.
    /// </summary>
    let tryDetectVersion (jsonText: string) : FormatVersion =
        // Look for common tag patterns to detect version:
        // v1: lowercase type tags, snake_case literal tags
        // v2: PascalCase type tags, snake_case literal tags
        // v3: PascalCase for all tags
        if jsonText.Contains("_literal") then
            // v1 or v2 format (has snake_case literal tags)
            if jsonText.Contains("\"variable\"") || jsonText.Contains("\"reference\"") then
                Classic 1  // v1 has lowercase type tags
            else
                Classic 2  // v2 has PascalCase type tags
        else
            // v3+ format (no snake_case tags)
            Classic 3

