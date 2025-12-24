namespace Morphir.IR

open System
open System.Text.RegularExpressions

/// <summary>
/// Versioning module provides format version information for Morphir IR.
/// FormatVersion represents the version of the IR format specification.
/// </summary>
module Versioning =

    /// <summary>
    /// ParseError represents errors that can occur when parsing version strings.
    /// </summary>
    type ParseError =
        | InvalidFormat of string
        | InvalidVersionNumber of string
        | InvalidPreReleaseFormat of string
        | InvalidBuildMetadataFormat of string

    /// <summary>
    /// SemanticVersion represents a semantic version number following SemVer 2.0.0 specification.
    /// Format: MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]
    /// </summary>
    type SemanticVersion =
        { Major: int
          Minor: int
          Patch: int
          PreRelease: string option
          BuildMetadata: string option }

    /// <summary>
    /// SemanticVersion module provides functions for working with semantic versions.
    /// </summary>
    module SemanticVersion =

        /// <summary>
        /// Validates that a string contains only valid semver identifiers (alphanumeric, hyphens, dots).
        /// </summary>
        let private isValidIdentifier (identifier: string) : bool =
            if String.IsNullOrEmpty(identifier) then false
            else
                let pattern = Regex("^[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*$", RegexOptions.Compiled)
                pattern.IsMatch(identifier)

        /// <summary>
        /// Parses a semantic version string into a SemanticVersion.
        /// Returns Result with SemanticVersion on success or ParseError on failure.
        /// </summary>
        let parse (text: string) : Result<SemanticVersion, ParseError> =
            if String.IsNullOrWhiteSpace(text) then
                Error(ParseError.InvalidFormat "Version string cannot be empty")
            else
                // Split on '+' to separate build metadata
                let parts = text.Split('+')
                let versionAndPreRelease = parts.[0]
                let buildMetadata =
                    if parts.Length > 1 then
                        let build = String.Join("+", parts.[1..])
                        if isValidIdentifier build then Some build else None
                    else
                        None

                if buildMetadata.IsNone && parts.Length > 1 then
                    Error(ParseError.InvalidBuildMetadataFormat text)
                else
                    // Split on '-' to separate pre-release (only first '-' separates version from pre-release)
                    let dashIndex = versionAndPreRelease.IndexOf('-')
                    let versionString, preRelease =
                        if dashIndex >= 0 then
                            versionAndPreRelease.Substring(0, dashIndex),
                            let prerelease = versionAndPreRelease.Substring(dashIndex + 1)
                            if isValidIdentifier prerelease then Some prerelease else None
                        else
                            versionAndPreRelease, None

                    if preRelease.IsNone && dashIndex >= 0 then
                        Error(ParseError.InvalidPreReleaseFormat text)
                    else
                        // Parse MAJOR.MINOR.PATCH
                        let versionNumbers = versionString.Split('.')
                        if versionNumbers.Length <> 3 then
                            Error(ParseError.InvalidFormat $"Expected format MAJOR.MINOR.PATCH, got: {versionString}")
                        else
                            match Int32.TryParse(versionNumbers.[0]), Int32.TryParse(versionNumbers.[1]), Int32.TryParse(versionNumbers.[2]) with
                            | (true, major), (true, minor), (true, patch) ->
                                if major < 0 || minor < 0 || patch < 0 then
                                    Error(ParseError.InvalidVersionNumber "Version numbers must be non-negative")
                                else
                                    Ok
                                        { Major = major
                                          Minor = minor
                                          Patch = patch
                                          PreRelease = preRelease
                                          BuildMetadata = buildMetadata }
                            | _ -> Error(ParseError.InvalidVersionNumber $"Invalid version numbers: {versionString}")

        /// <summary>
        /// Converts a SemanticVersion to its string representation.
        /// </summary>
        let toString (semanticVersion: SemanticVersion) : string =
            let baseVersion = $"{semanticVersion.Major}.{semanticVersion.Minor}.{semanticVersion.Patch}"
            let withPreRelease =
                match semanticVersion.PreRelease with
                | Some prerelease -> $"{baseVersion}-{prerelease}"
                | None -> baseVersion
            match semanticVersion.BuildMetadata with
            | Some build -> $"{withPreRelease}+{build}"
            | None -> withPreRelease

    /// <summary>
    /// FormatVersion represents the version of the Morphir IR format.
    /// </summary>
    type FormatVersion =
        | Classic of int
        | SemVer of SemanticVersion

    /// <summary>
    /// Gets the version string for a FormatVersion.
    /// </summary>
    let version (formatVersion: FormatVersion) : string =
        match formatVersion with
        | Classic n -> n.ToString()
        | SemVer sv -> SemanticVersion.toString sv

    /// <summary>
    /// Determines if a FormatVersion is a classic (stable) version.
    /// Classic versions are classic, SemVer versions are not.
    /// </summary>
    let isClassic (formatVersion: FormatVersion) : bool =
        match formatVersion with
        | Classic _ -> true
        | SemVer _ -> false

    /// <summary>
    /// Parses a version string into a FormatVersion.
    /// Returns Result with FormatVersion on success or ParseError on failure.
    /// Tries to parse as integer (Classic) first, then as semantic version (SemVer).
    /// </summary>
    let parse (text: string) : Result<FormatVersion, ParseError> =
        if String.IsNullOrWhiteSpace(text) then
            Error(ParseError.InvalidFormat "Version string cannot be empty")
        else
            // Try parsing as integer (Classic)
            match Int32.TryParse(text) with
            | true, n when n >= 0 -> Ok(Classic n)
            | true, _ -> Error(ParseError.InvalidVersionNumber "Version number must be non-negative")
            | false, _ ->
                // Try parsing as semantic version (SemVer)
                match SemanticVersion.parse text with
                | Ok sv -> Ok(SemVer sv)
                | Error err -> Error err

