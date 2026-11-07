namespace Morphir.IR.Model

open Morphir.IR

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

/// A distribution's purpose. A specification is not an empty library.
[<RequireQualifiedAccess>]
type DistributionKind =
    | Library
    | Specs
    | Application

/// Existing typed models without migration or a dependency on a serialization package.
[<RequireQualifiedAccess>]
type DistributionModel =
    | Classic of Classic.Distribution.Distribution<JsonElement, JsonElement>
    | V4 of V4.Distribution

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
