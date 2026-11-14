// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open Morphir.Toolchain
open Morphir.Mep

/// A GitHub repository, <c>owner/repo</c>, parsed once at the boundary.
[<Sealed>]
type GitHubRepository private (owner: string, name: string) =
    static let ownerCharacter (c: char) = Char.IsAsciiLetterOrDigit c || c = '-'
    static let nameCharacter (c: char) = Char.IsAsciiLetterOrDigit c || c = '-' || c = '.' || c = '_'

    /// The account or organization.
    member _.Owner = owner
    /// The repository name.
    member _.Name = name
    /// <c>owner/repo</c>, as the GitHub API spells it.
    member _.Text = owner + "/" + name

    override this.ToString() = this.Text

    override this.Equals(other: obj) =
        match other with
        | :? GitHubRepository as repository -> StringComparer.Ordinal.Equals(this.Text, repository.Text)
        | _ -> false

    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode this.Text

    /// Parses <c>owner/repo</c> with the GitHub character rules of the extension family schema.
    static member Parse(text: string | null) : Result<GitHubRepository, InstallationFailure> =
        let refuse () =
            Parsing.refuse "extension.invalid_source" $"Not a GitHub owner/repo: {text}"

        match text with
        | null -> refuse ()
        | text ->
            match text.Split '/' with
            | [| owner; name |] when
                owner.Length >= 1
                && owner.Length <= 39
                && Char.IsAsciiLetterOrDigit owner[0]
                && Seq.forall ownerCharacter owner
                && name.Length >= 1
                && name.Length <= 100
                && name <> "."
                && name <> ".."
                && Seq.forall nameCharacter name
                ->
                Ok(GitHubRepository(owner, name))
            | _ -> refuse ()

/// Where an installed artifact came from. A new provider adds a case; the other members of the family stay.
[<RequireQualifiedAccess>]
type ArtifactSource =
    /// A file in a registered local developer repository, by repository-relative path.
    | LocalFile of path: string
    /// An asset of a GitHub release.
    | GitHubReleaseAsset of repository: GitHubRepository * tag: string * asset: string

/// The index an installed artifact was resolved from, pinned by the digest of what was read.
[<RequireQualifiedAccess>]
type IndexProvenance =
    /// A registered local developer repository, pinned by the digest of its history file bytes.
    | LocalDirectory of identity: string * revision: Sha256Digest
    /// A GitHub release, pinned by the digest of its <c>release.json</c> asset bytes.
    | GitHubRelease of repository: GitHubRepository * tag: string * revision: Sha256Digest

/// Reading and writing the provider-tagged source and provenance members of the extension family.
module internal SourceDocuments =
    open SharedJson

    let private closed path (allowed: string list) items =
        for name, _ in items do
            if not (List.contains name allowed) then
                invalid $"{path}.{name}" "Unknown member in a closed document."

    /// A Git tag: no control characters, spaces or the characters Git forbids in a ref name.
    let validTag (tag: string) =
        tag.Length >= 1
        && tag.Length <= 255
        && tag
           |> Seq.forall (fun c -> c > ' ' && c <> '\u007f' && "~^:?*[\\".IndexOf c < 0)

    let private tag path value =
        let text = text path value

        if not (validTag text) then
            invalid path "Not a Git tag."

        text

    let private repository path value =
        match GitHubRepository.Parse(text path value) with
        | Ok repository -> repository
        | Error failure -> invalid path failure.Message

    let private digest path value =
        parsed path Sha256Digest.Parse value

    let private portable path value =
        parsed path DistributionIdentifiers.ParseDeclaredPath value
        |> fun parsed -> parsed.Text

    let private filename path value =
        parsed path DistributionIdentifiers.ParseArtifactFileName value
        |> fun parsed -> parsed.Text

    let private kind path items choices =
        let kind = required path "kind" items |> text $"{path}.kind"

        if not (List.contains kind choices) then
            invalid $"{path}.kind" "Unsupported value."

        kind

    /// Reads an artifact source. A closed context refuses members of another variant.
    let readSource path isClosed value =
        let items = members path value

        match kind path items [ "local-file"; "github-release-asset" ] with
        | "local-file" ->
            if isClosed then closed path [ "kind"; "path" ] items
            ArtifactSource.LocalFile(required path "path" items |> portable $"{path}.path")
        | _ ->
            if isClosed then closed path [ "kind"; "repository"; "tag"; "asset" ] items

            ArtifactSource.GitHubReleaseAsset(
                required path "repository" items |> repository $"{path}.repository",
                required path "tag" items |> tag $"{path}.tag",
                required path "asset" items |> filename $"{path}.asset"
            )

    /// Reads an index provenance; a lock adds the pinned <c>claims</c>, which the caller reads.
    let readProvenance path isClosed claimsRequired (absolute: string -> Value -> string) value =
        let items = members path value
        let extra = if claimsRequired then [ "claims" ] else []

        let provenance =
            match kind path items [ "local-directory"; "github-release" ] with
            | "local-directory" ->
                if isClosed then closed path ([ "kind"; "identity"; "revision" ] @ extra) items

                IndexProvenance.LocalDirectory(
                    required path "identity" items |> absolute $"{path}.identity",
                    required path "revision" items |> digest $"{path}.revision"
                )
            | _ ->
                if isClosed then closed path ([ "kind"; "repository"; "tag"; "revision" ] @ extra) items

                IndexProvenance.GitHubRelease(
                    required path "repository" items |> repository $"{path}.repository",
                    required path "tag" items |> tag $"{path}.tag",
                    required path "revision" items |> digest $"{path}.revision"
                )

        if claimsRequired then
            required path "claims" items |> ignore

        provenance

    let sourceValue source =
        match source with
        | ArtifactSource.LocalFile path -> Value.Object [ "kind", Value.String "local-file"; "path", Value.String path ]
        | ArtifactSource.GitHubReleaseAsset(repository, tag, asset) ->
            Value.Object [
                "kind", Value.String "github-release-asset"
                "repository", Value.String repository.Text
                "tag", Value.String tag
                "asset", Value.String asset
            ]

    let provenanceValue provenance =
        match provenance with
        | IndexProvenance.LocalDirectory(identity, revision) ->
            Value.Object [
                "kind", Value.String "local-directory"
                "identity", Value.String identity
                "revision", Value.String revision.Value
            ]
        | IndexProvenance.GitHubRelease(repository, tag, revision) ->
            Value.Object [
                "kind", Value.String "github-release"
                "repository", Value.String repository.Text
                "tag", Value.String tag
                "revision", Value.String revision.Value
            ]

    /// The artifact filename a source names: the last path segment of a local file, or the release asset name.
    let filenameOf source =
        match source with
        | ArtifactSource.LocalFile path -> (path.Split '/') |> Array.last
        | ArtifactSource.GitHubReleaseAsset(_, _, asset) -> asset

    /// Every member path the variants define under <paramref name="prefix"/>, for critical-path checks.
    let sourcePaths prefix =
        [ "kind"; "path"; "repository"; "tag"; "asset" ] |> List.map (fun name -> $"{prefix}.{name}")

    let provenancePaths prefix =
        [ "kind"; "identity"; "repository"; "tag"; "revision" ]
        |> List.map (fun name -> $"{prefix}.{name}")
