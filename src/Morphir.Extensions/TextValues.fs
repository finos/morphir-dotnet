namespace Morphir.Extensions

open System
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open Morphir.IR

/// <summary>An extension display name: 1 to 128 characters, no white space at the ends, no control character.</summary>
[<CustomEquality; CustomComparison>]
type ExtensionName =
    private
    | ExtensionName of string

    /// <summary>The text.</summary>
    member this.Value =
        let (ExtensionName value) = this
        value

    /// <summary>The canonical text.</summary>
    override this.ToString() = this.Value

    /// <summary>True for a value of the same type with equal content. Text is compared ordinally.</summary>
    override this.Equals(other: obj | null) =
        match other with
        | :? ExtensionName as name ->
            String.Equals(this.Value, name.Value, StringComparison.Ordinal)
        | _ -> false

    /// <summary>A hash that agrees with <c>Equals</c>.</summary>
    override this.GetHashCode() =
        StringComparer.Ordinal.GetHashCode this.Value

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | :? ExtensionName as name -> String.CompareOrdinal(this.Value, name.Value)
            | _ -> invalidArg (nameof other) "Not an ExtensionName."

    /// <summary>Parses untrusted text. For F# callers.</summary>
    static member Parse(text: string | null) : Result<ExtensionName, ParseFailure> =
        Text.display "extension name" 128 text
        |> Result.map ExtensionName

    /// <summary>Parses untrusted text. For C# callers.</summary>
    static member TryParse
        (text: string | null,
                        [<Out; MaybeNullWhen(false)>] result: byref<ExtensionName>,
                        [<Out; MaybeNullWhen(true)>] failure: byref<ParseFailure>)
        : bool =
        match ExtensionName.Parse text with
        | Ok name ->
            result <- name
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// <summary>Builds a name from a literal in source code. Throws <c>ArgumentException</c> on invalid text.</summary>
    static member Create(literal: string) : ExtensionName =
        match ExtensionName.Parse literal with
        | Ok name -> name
        | Error failure ->
            invalidArg (nameof literal) (Text.refusedLiteral "extension name" literal failure)

/// <summary>An extension description: 1 to 1024 characters, no white space at the ends, no control character.</summary>
[<CustomEquality; CustomComparison>]
type ExtensionDescription =
    private
    | ExtensionDescription of string

    /// <summary>The text.</summary>
    member this.Value =
        let (ExtensionDescription value) = this
        value

    /// <summary>The canonical text.</summary>
    override this.ToString() = this.Value

    /// <summary>True for a value of the same type with equal content. Text is compared ordinally.</summary>
    override this.Equals(other: obj | null) =
        match other with
        | :? ExtensionDescription as description ->
            String.Equals(this.Value, description.Value, StringComparison.Ordinal)
        | _ -> false

    /// <summary>A hash that agrees with <c>Equals</c>.</summary>
    override this.GetHashCode() =
        StringComparer.Ordinal.GetHashCode this.Value

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | :? ExtensionDescription as description ->
                String.CompareOrdinal(this.Value, description.Value)
            | _ -> invalidArg (nameof other) "Not an ExtensionDescription."

    /// <summary>Parses untrusted text. For F# callers.</summary>
    static member Parse(text: string | null) : Result<ExtensionDescription, ParseFailure> =
        Text.display "extension description" 1024 text
        |> Result.map ExtensionDescription

    /// <summary>Parses untrusted text. For C# callers.</summary>
    static member TryParse
        (text: string | null,
                        [<Out; MaybeNullWhen(false)>] result: byref<ExtensionDescription>,
                        [<Out; MaybeNullWhen(true)>] failure: byref<ParseFailure>)
        : bool =
        match ExtensionDescription.Parse text with
        | Ok description ->
            result <- description
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// <summary>Builds a description from a literal in source code. Throws <c>ArgumentException</c> on invalid text.</summary>
    static member Create(literal: string) : ExtensionDescription =
        match ExtensionDescription.Parse literal with
        | Ok description -> description
        | Error failure ->
            invalidArg
                (nameof literal)
                (Text.refusedLiteral "extension description" literal failure)

/// <summary>A release version in canonical SemVer 2.0.0 form, for example <c>0.4.0-rc.1</c>.</summary>
/// <remarks>It wraps <c>Versioning.SemanticVersion</c>, whose record constructor is public, so that only parsed values exist.</remarks>
[<CustomEquality; NoComparison>]
type ReleaseVersion =
    private
    | ReleaseVersion of Versioning.SemanticVersion

    /// The parsed SemVer value. Internal: its record has a public constructor and option fields.
    member internal this.SemanticVersion =
        let (ReleaseVersion version) = this
        version

    /// <summary>The major version number.</summary>
    member this.Major = this.SemanticVersion.Major

    /// <summary>The minor version number.</summary>
    member this.Minor = this.SemanticVersion.Minor

    /// <summary>The patch version number.</summary>
    member this.Patch = this.SemanticVersion.Patch

    /// <summary>The pre-release identifiers, for example <c>rc.1</c>, or null when there are none.</summary>
    member this.PreRelease = Option.toObj this.SemanticVersion.PreRelease

    /// <summary>The build metadata, or null when there is none.</summary>
    member this.BuildMetadata = Option.toObj this.SemanticVersion.BuildMetadata

    /// <summary>The canonical text.</summary>
    override this.ToString() =
        Versioning.SemanticVersion.toString this.SemanticVersion

    /// <summary>True for a value of the same type with equal content. Text is compared ordinally.</summary>
    override this.Equals(other: obj | null) =
        match other with
        | :? ReleaseVersion as version ->
            String.Equals(this.ToString(), version.ToString(), StringComparison.Ordinal)
        | _ -> false

    /// <summary>A hash that agrees with <c>Equals</c>.</summary>
    override this.GetHashCode() =
        StringComparer.Ordinal.GetHashCode(this.ToString())

    /// <summary>Parses untrusted text. Only the canonical form is accepted (no leading zeros or padding). For F# callers.</summary>
    static member Parse(text: string | null) : Result<ReleaseVersion, ParseFailure> =
        let field = "release version"

        match text with
        | null -> Error(ParseFailure.Empty field)
        | value when value.Length = 0 -> Error(ParseFailure.Empty field)
        | value ->
            match Versioning.SemanticVersion.parse value with
            | Ok version when Versioning.SemanticVersion.toString version = value ->
                Ok(ReleaseVersion version)
            | Ok _ ->
                Error(ParseFailure.InvalidVersion(field, $"'{value}' is not in canonical form"))
            | Error error ->
                let detail =
                    match error with
                    | Versioning.ParseError.InvalidFormat message
                    | Versioning.ParseError.InvalidVersionNumber message
                    | Versioning.ParseError.InvalidPreReleaseFormat message
                    | Versioning.ParseError.InvalidBuildMetadataFormat message -> message

                Error(ParseFailure.InvalidVersion(field, detail))

    /// <summary>Parses untrusted text. For C# callers.</summary>
    static member TryParse
        (text: string | null,
                        [<Out; MaybeNullWhen(false)>] result: byref<ReleaseVersion>,
                        [<Out; MaybeNullWhen(true)>] failure: byref<ParseFailure>)
        : bool =
        match ReleaseVersion.Parse text with
        | Ok version ->
            result <- version
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// <summary>Builds a version from a literal or a build-time constant. Throws <c>ArgumentException</c> on invalid text.</summary>
    static member Create(literal: string) : ReleaseVersion =
        match ReleaseVersion.Parse literal with
        | Ok version -> version
        | Error failure ->
            invalidArg (nameof literal) (Text.refusedLiteral "release version" literal failure)
