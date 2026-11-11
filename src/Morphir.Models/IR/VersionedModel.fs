namespace Morphir.IR.Model

open Morphir.IR

open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open System.Text.Json

/// The declared version, kept separately from the semantic model family and physical format.
type SourceFormatVersion =
    private
        {
            Declared: string
            Parsed: Versioning.FormatVersion
        }

    /// The exact declaration, without substituting a major version for a semantic release.
    member this.Text = this.Declared
    /// The parsed numeric or semantic version.
    member this.Version = this.Parsed

    /// The exact declaration. The default record text would format through reflection.
    override this.ToString() = this.Declared

    static member private Read(text: string | null) : Result<SourceFormatVersion, Versioning.ParseError> =
        match text with
        | null -> Error(Versioning.ParseError.InvalidFormat "Version string cannot be empty")
        | value ->
            Versioning.parse value
            |> Result.map (fun parsed -> { Declared = value; Parsed = parsed })

    /// <summary>
    /// For C# callers: parses a declared version as <c>SourceFormatVersion.parse</c> does. Exactly one of
    /// <paramref name="result"/> and <paramref name="failure"/> is set.
    /// </summary>
    static member TryParse
        (
            text: string | null,
            [<Out; MaybeNullWhen(false)>] result: byref<SourceFormatVersion>,
            [<Out; MaybeNullWhen(true)>] failure: byref<Versioning.ParseError>
        ) : bool =
        match SourceFormatVersion.Read text with
        | Ok version ->
            result <- version
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// <summary>For a version literal in source code. Throws <see cref="System.ArgumentException"/> for malformed text.</summary>
    static member Create(text: string) : SourceFormatVersion =
        match SourceFormatVersion.Read text with
        | Ok version -> version
        | Error _ -> invalidArg (nameof text) $"'{text}' is not a format version."

/// Source-version construction does not decide which versions a codec or backend supports.
[<RequireQualifiedAccess>]
module SourceFormatVersion =
    let parse text =
        Versioning.parse text
        |> Result.map (fun parsed -> { Declared = text; Parsed = parsed })

/// The semantic family, independent of JSON, Ion or another representation.
[<RequireQualifiedAccess>]
type ModelFamily =
    | Classic
    | V4

    /// <summary>The family as reports write it, for C# callers: Classic or V4.</summary>
    member this.WireValue =
        match this with
        | Classic -> "Classic"
        | V4 -> "V4"

    /// <summary>The wire value.</summary>
    override this.ToString() = this.WireValue

/// A distribution's purpose. A specification is not an empty library.
[<RequireQualifiedAccess>]
type DistributionKind =
    | Library
    | Specs
    | Application

    /// <summary>The kind as reports write it, for C# callers: Library, Specs or Application.</summary>
    member this.WireValue =
        match this with
        | Library -> "Library"
        | Specs -> "Specs"
        | Application -> "Application"

    /// <summary>The wire value.</summary>
    override this.ToString() = this.WireValue

/// Existing typed models without migration or a dependency on a serialization package.
[<RequireQualifiedAccess>]
type DistributionModel =
    | Classic of Classic.Distribution.Distribution<JsonElement, JsonElement>
    | V4 of V4.Distribution

    /// For C# callers, such as an in-process analyzer: one function for each case, so C# does not test the cases.
    member this.Match<'R>(classic: System.Func<Classic.Distribution.Distribution<JsonElement, JsonElement>, 'R>, v4: System.Func<V4.Distribution, 'R>) : 'R =
        match this with
        | Classic distribution -> classic.Invoke distribution
        | V4 distribution -> v4.Invoke distribution

/// A typed distribution and the version declared by its source.
type VersionedModel =
    {
        SourceVersion: SourceFormatVersion
        Distribution: DistributionModel
    }

    /// The family derives from the distribution, not from a filename or a guessed version.
    member this.Family =
        match this.Distribution with
        | DistributionModel.Classic _ -> ModelFamily.Classic
        | DistributionModel.V4 _ -> ModelFamily.V4

    /// The distribution's original kind.
    member this.Kind =
        match this.Distribution with
        | DistributionModel.Classic(Classic.Distribution.Library _)
        | DistributionModel.V4(V4.Library _) -> DistributionKind.Library
        | DistributionModel.Classic(Classic.Distribution.Specs _)
        | DistributionModel.V4(V4.Specs _) -> DistributionKind.Specs
        | DistributionModel.V4(V4.Application _) -> DistributionKind.Application
