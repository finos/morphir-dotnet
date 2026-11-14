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

/// A NuGet feed as the extension family records it: an <c>https</c> service index URL whose path ends in
/// <c>/index.json</c>, with no userinfo, query or fragment, or an ordinary absolute folder path. A credential is never
/// part of it.
[<Sealed>]
type NuGetFeed private (text: string) =
    static let url =
        Text.RegularExpressions.Regex(@"^https://[^/?#@\s]+(?:/[^?#\s]*)?/index\.json$", Text.RegularExpressions.RegexOptions.CultureInvariant)

    static let folder =
        Text.RegularExpressions.Regex(@"^(?:/|[A-Z]:\\|\\\\(?!\?\\))", Text.RegularExpressions.RegexOptions.CultureInvariant)

    /// The feed as the extension family spells it.
    member _.Text = text

    /// True for a local folder feed, false for an HTTPS service index.
    member _.IsFolder = not (text.StartsWith("https://", StringComparison.Ordinal))

    override _.ToString() = text

    override _.Equals(other: obj) =
        match other with
        | :? NuGetFeed as feed -> StringComparer.Ordinal.Equals(text, feed.Text)
        | _ -> false

    override _.GetHashCode() = StringComparer.Ordinal.GetHashCode text

    /// Parses a recorded feed. <c>http</c>, userinfo, a query or a fragment are refused.
    static member Parse(text: string | null) : Result<NuGetFeed, InstallationFailure> =
        match text with
        | null -> Parsing.refuse "extension.invalid_source" "The NuGet feed is empty."
        | text when url.IsMatch text -> Ok(NuGetFeed text)
        | text when folder.IsMatch text && not (text.StartsWith(@"\\?\", StringComparison.Ordinal)) ->
            Ok(NuGetFeed text)
        | text ->
            Parsing.refuse
                "extension.invalid_source"
                $"Not an https NuGet service index ending in /index.json, or an absolute folder: {text}"

/// A NuGet package id and version as the extension family records them: the id in lower case and the version in
/// normalized form with no build metadata.
[<Sealed>]
type NuGetPackageKey private (id: string, version: NuGet.Versioning.NuGetVersion) =
    static let recordedId =
        Text.RegularExpressions.Regex(@"^[a-z0-9_]+(?:[.-][a-z0-9_]+)*$", Text.RegularExpressions.RegexOptions.CultureInvariant)

    static let recordedVersion =
        Text.RegularExpressions.Regex(
            @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(\.[1-9][0-9]*)?(-((0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*)(\.(0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*))*))?$",
            Text.RegularExpressions.RegexOptions.CultureInvariant
        )

    /// The lower-case package id.
    member _.Id = id
    /// The normalized version text, without build metadata.
    member _.Version = version.ToNormalizedString()
    member internal _.NuGetVersion = version

    /// True when <paramref name="other"/> is this package or a package whose id extends this id with a <c>.</c>.
    member _.IsExtendedBy(other: NuGetPackageKey) =
        StringComparer.Ordinal.Equals(other.Id, id)
        || other.Id.StartsWith(id + ".", StringComparison.Ordinal)

    override this.ToString() = $"{id} {this.Version}"

    override this.Equals(other: obj) =
        match other with
        | :? NuGetPackageKey as key -> StringComparer.Ordinal.Equals(id, key.Id) && version.Equals key.NuGetVersion
        | _ -> false

    override this.GetHashCode() = HashCode.Combine(id, this.Version)

    /// The recorded key of a package id, in any case, and a version with no build metadata.
    static member internal Of(packageId: string, version: NuGet.Versioning.NuGetVersion) =
        NuGetPackageKey(packageId.ToLowerInvariant(), NuGet.Versioning.NuGetVersion(version.Version, version.ReleaseLabels, null, null))

    /// Parses a recorded id, which must be lower case, and a recorded version, which must be normalized.
    static member Parse(id: string | null, version: string | null) : Result<NuGetPackageKey, InstallationFailure> =
        match id, version with
        | NonNull id, _ when id.Length > 100 || not (recordedId.IsMatch id) ->
            Parsing.refuse "extension.invalid_source" $"Not a lower-case NuGet package id: {id}"
        | NonNull id, NonNull version when recordedVersion.IsMatch version ->
            let mutable parsed: NuGet.Versioning.NuGetVersion | null = null

            match NuGet.Versioning.NuGetVersion.TryParse(version, &parsed), parsed with
            | true, NonNull parsed when parsed.ToNormalizedString() = version && not parsed.HasMetadata ->
                Ok(NuGetPackageKey(id, parsed))
            | _ -> Parsing.refuse "extension.invalid_source" $"Not a normalized NuGet version: {version}"
        | NonNull _, _ -> Parsing.refuse "extension.invalid_source" $"Not a normalized NuGet version: {version}"
        | Null, _ -> Parsing.refuse "extension.invalid_source" "The NuGet package id is empty."

/// Where an installed artifact came from. A new provider adds a case; the other members of the family stay.
[<RequireQualifiedAccess>]
type ArtifactSource =
    /// A file in a registered local developer repository, by repository-relative path.
    | LocalFile of path: string
    /// An asset of a GitHub release.
    | GitHubReleaseAsset of repository: GitHubRepository * tag: string * asset: string
    /// An entry of a NuGet package: <c>morphir/&lt;filename&gt;</c> for WASM, or the package <c>root</c> folder.
    | NuGetPackageEntry of feed: NuGetFeed * package: NuGetPackageKey * path: string

/// The index an installed artifact was resolved from, pinned by the digest of what was read.
[<RequireQualifiedAccess>]
type IndexProvenance =
    /// A registered local developer repository, pinned by the digest of its history file bytes.
    | LocalDirectory of identity: string * revision: Sha256Digest
    /// A GitHub release, pinned by the digest of its <c>release.json</c> asset bytes.
    | GitHubRelease of repository: GitHubRepository * tag: string * revision: Sha256Digest
    /// The main package of a NuGet release, pinned by the digest of its <c>morphir/release.json</c> bytes.
    | NuGetPackage of feed: NuGetFeed * package: NuGetPackageKey * revision: Sha256Digest

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

    let private feed path value =
        match NuGetFeed.Parse(text path value) with
        | Ok feed -> feed
        | Error failure -> invalid path failure.Message

    let private packageKey path items =
        let id = required path "id" items |> text $"{path}.id"
        let version = required path "version" items |> text $"{path}.version"

        match NuGetPackageKey.Parse(id, version) with
        | Ok key -> key
        | Error failure when failure.Message.Contains "version" -> invalid $"{path}.version" failure.Message
        | Error failure -> invalid $"{path}.id" failure.Message

    /// A package entry path: a safe relative POSIX path with no <c>.</c> or <c>..</c> segment.
    let private entryPath path value =
        portable path value

    let private kind path items choices =
        let kind = required path "kind" items |> text $"{path}.kind"

        if not (List.contains kind choices) then
            invalid $"{path}.kind" "Unsupported value."

        kind

    /// Reads an artifact source. A closed context refuses members of another variant.
    let readSource path isClosed value =
        let items = members path value

        match kind path items [ "local-file"; "github-release-asset"; "nuget-package-entry" ] with
        | "local-file" ->
            if isClosed then closed path [ "kind"; "path" ] items
            ArtifactSource.LocalFile(required path "path" items |> portable $"{path}.path")
        | "nuget-package-entry" ->
            if isClosed then closed path [ "kind"; "feed"; "id"; "version"; "path" ] items

            ArtifactSource.NuGetPackageEntry(
                required path "feed" items |> feed $"{path}.feed",
                packageKey path items,
                required path "path" items |> entryPath $"{path}.path"
            )
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
            match kind path items [ "local-directory"; "github-release"; "nuget-package" ] with
            | "local-directory" ->
                if isClosed then closed path ([ "kind"; "identity"; "revision" ] @ extra) items

                IndexProvenance.LocalDirectory(
                    required path "identity" items |> absolute $"{path}.identity",
                    required path "revision" items |> digest $"{path}.revision"
                )
            | "nuget-package" ->
                if isClosed then closed path ([ "kind"; "feed"; "id"; "version"; "revision" ] @ extra) items

                IndexProvenance.NuGetPackage(
                    required path "feed" items |> feed $"{path}.feed",
                    packageKey path items,
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
        | ArtifactSource.NuGetPackageEntry(feed, package, entry) ->
            Value.Object [
                "kind", Value.String "nuget-package-entry"
                "feed", Value.String feed.Text
                "id", Value.String package.Id
                "version", Value.String package.Version
                "path", Value.String entry
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
        | IndexProvenance.NuGetPackage(feed, package, revision) ->
            Value.Object [
                "kind", Value.String "nuget-package"
                "feed", Value.String feed.Text
                "id", Value.String package.Id
                "version", Value.String package.Version
                "revision", Value.String revision.Value
            ]

    /// The artifact filename a source names: the last path segment of a local file, the release asset name, or, for
    /// a package subtree, the <c>&lt;id&gt;.&lt;version&gt;.nupkg</c> file. A WASM module uses <see cref="moduleFilenameOf"/>.
    let filenameOf source =
        match source with
        | ArtifactSource.LocalFile path -> (path.Split '/') |> Array.last
        | ArtifactSource.GitHubReleaseAsset(_, _, asset) -> asset
        | ArtifactSource.NuGetPackageEntry(_, package, _) -> $"{package.Id}.{package.Version.ToLowerInvariant()}.nupkg"

    /// The filename of a WASM module a source names; a NuGet package entry is <c>morphir/&lt;filename&gt;</c>.
    let moduleFilenameOf source =
        match source with
        | ArtifactSource.NuGetPackageEntry(_, _, entry) when entry.StartsWith("morphir/", StringComparison.Ordinal) ->
            entry.Substring("morphir/".Length)
        | ArtifactSource.NuGetPackageEntry(_, _, entry) ->
            invalid "lock.source.path" $"A WASM module entry must be morphir/<filename>, not {entry}."
        | other -> filenameOf other

    /// A lock pairs a source only with the provenance of the same provider, and a NuGet package entry comes from the
    /// main package or a package whose id extends it, at the same feed and version.
    let checkPair path provenance source =
        match provenance, source with
        | IndexProvenance.LocalDirectory _, ArtifactSource.LocalFile _
        | IndexProvenance.GitHubRelease _, ArtifactSource.GitHubReleaseAsset _ -> ()
        | IndexProvenance.NuGetPackage(feed, main, _), ArtifactSource.NuGetPackageEntry(entryFeed, package, _) ->
            if not (main.IsExtendedBy package) then
                invalid $"{path}.source.id" "A package entry must come from the main package or a package whose id extends it."

            if not (feed.Equals entryFeed) || main.Version <> package.Version then
                invalid $"{path}.source.feed" "A package entry must come from the feed and version of the main package."
        | _ -> invalid $"{path}.source.kind" "The source is not of the provider of the index provenance."

    /// Every member path the variants define under <paramref name="prefix"/>, for critical-path checks.
    let sourcePaths prefix =
        [ "kind"; "path"; "repository"; "tag"; "asset"; "feed"; "id"; "version" ]
        |> List.map (fun name -> $"{prefix}.{name}")

    let provenancePaths prefix =
        [ "kind"; "identity"; "repository"; "tag"; "feed"; "id"; "version"; "revision" ]
        |> List.map (fun name -> $"{prefix}.{name}")
