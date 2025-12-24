namespace Morphir.IR

/// <summary>
/// Versioning module provides format version information for Morphir IR.
/// FormatVersion represents the version of the IR format specification.
/// </summary>
module Versioning =

    /// <summary>
    /// FormatVersion represents the version of the Morphir IR format.
    /// </summary>
    type FormatVersion =
        | Version2
        | Experimental

    /// <summary>
    /// Gets the version string for a FormatVersion.
    /// </summary>
    let version (formatVersion: FormatVersion) : string =
        match formatVersion with
        | Version2 -> "2"
        | Experimental -> "3.0-Experimental"

    /// <summary>
    /// Determines if a FormatVersion is a classic (stable) version.
    /// Version2 is classic, Experimental is not.
    /// </summary>
    let isClassic (formatVersion: FormatVersion) : bool =
        match formatVersion with
        | Version2 -> true
        | Experimental -> false

    /// <summary>
    /// Parses a version string into a FormatVersion.
    /// Returns the fallback version if the string doesn't match any known version.
    /// </summary>
    let parse (text: string) (fallbackVersion: FormatVersion) : FormatVersion =
        match text with
        | "2" -> Version2
        | "3.0-Experimental" -> Experimental
        | _ -> fallbackVersion

    /// <summary>
    /// The Version2 format version (stable/classic).
    /// </summary>
    let v2 = Version2

