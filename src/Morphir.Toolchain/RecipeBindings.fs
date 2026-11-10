// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

open System.Collections.Generic

/// <summary>Authentication of bytes by a recipe: the authenticated repository's publisher bound exactly these bytes,
/// by this target and digest, to the toolset. It says nothing about whether anyone ran them.</summary>
type RecipeDigest =
    { RepositoryId: Sha256
      Target: TargetPath
      Sha256: Sha256 }

/// A host offering whose bindings were checked: its v1 release, when it is a tool release, and each bound artifact.
type BoundHost =
    { Offering: HostOffering
      Release: ToolRelease option
      Artifacts: (Platform * RecipeDigest) list }

/// The claims a bound artifact states, with the descriptor's own word on whether they were checked by running it. This
/// is a separate fact from the <see cref="RecipeDigest"/> that authenticates the bytes.
type BoundClaims =
    { Claims: ClaimSet
      ClaimCheck: DescriptorClaimCheck }

/// A provider whose bindings were checked: its descriptor, the release it describes, and each bound artifact or
/// package with the claims that describe it.
type BoundProvider =
    { Provider: RecipeProvider
      Descriptor: RecipeDigest
      Release: ExtensionRelease
      Artifacts: (RecipeDigest * BoundClaims list) list }

/// Every host and provider binding of a recipe, checked against one authenticated repository.
type BoundRecipe =
    { Hosts: BoundHost list
      Providers: BoundProvider list }

/// <summary>
/// The host and provider binding rows of the toolset recipe page's authenticated recipe targets (the second and third
/// tables), as pure functions over the recipe, the authenticated targets a TUF client supplies, and the bytes it
/// serves for each target. Each row refuses with its code and path, and the first failure is returned. Nothing is
/// downloaded and no metadata is verified here.
/// </summary>
/// <remarks>
/// SECURITY: this code has not had a security review. It checks that a recipe binds exactly what the authenticated
/// repository published. It does not verify metadata (the TUF client does), does not run an artifact, and does not
/// make a claim set probed. A failed read of a target that a binding names is reported at that binding member.
/// <para>Each descriptor target and each tool release is fetched, authenticated and read once per check, however many
/// providers or offerings name it, and claims are looked up in sets: the check is linear in the recipe and the
/// targets it reads (review of #583, A1).</para>
/// </remarks>
module RecipeBindings =

    let private refuse code (path: DocumentPath) : Result<'T, TargetRefusal> = Error { Code = code; Path = path }
    let private trust code path = refuse (Code.Trust code) path

    let private member' (name: string) (value: Value) =
        match value with
        | Value.Object members -> members |> List.tryFind (fst >> (=) name) |> Option.map snd
        | _ -> None

    let private text name value =
        match member' name value with
        | Some(Value.String s) -> Some s
        | _ -> None

    let private morphir (target: AuthenticatedTarget) = target.Custom |> Option.bind (member' "morphir")

    /// The tool release metadata v1 platform of a platform (overview page, platform table); musl has none.
    let toolPlatform (platform: Platform) =
        match platform with
        | Platform.WinX64 -> Some("windows", "x86_64")
        | Platform.WinArm64 -> Some("windows", "aarch64")
        | Platform.OsxX64 -> Some("macos", "x86_64")
        | Platform.OsxArm64 -> Some("macos", "aarch64")
        | Platform.LinuxX64 -> Some("linux", "x86_64")
        | Platform.LinuxArm64 -> Some("linux", "aarch64")
        | Platform.LinuxMuslX64
        | Platform.LinuxMuslArm64 -> None

    /// The extension descriptor target triple of a platform (overview page, platform table).
    let targetTriple (platform: Platform) =
        match platform with
        | Platform.WinX64 -> "x86_64-pc-windows-msvc"
        | Platform.WinArm64 -> "aarch64-pc-windows-msvc"
        | Platform.OsxX64 -> "x86_64-apple-darwin"
        | Platform.OsxArm64 -> "aarch64-apple-darwin"
        | Platform.LinuxX64 -> "x86_64-unknown-linux-gnu"
        | Platform.LinuxArm64 -> "aarch64-unknown-linux-gnu"
        | Platform.LinuxMuslX64 -> "x86_64-unknown-linux-musl"
        | Platform.LinuxMuslArm64 -> "aarch64-unknown-linux-musl"

    /// The capability rows of one claim set as sets: the kinds, the MEP versions, the languages when <c>compile</c> is
    /// <c>true</c>, the targets when <c>generate</c> is <c>true</c>, and the model tuples by their comparison key.
    type private ClaimRows =
        { Kinds: Set<CapabilityKind>
          Protocols: Set<string>
          Compiles: Set<string>
          Generates: Set<string>
          Tuples: Set<byte array> }

    /// A tool release read once per check: the release, and its first artifact for each target path.
    type private ReadRelease =
        { Tool: ToolRelease
          ByTargetPath: Dictionary<string, ToolReleaseArtifact> }

    /// A descriptor read once per check: the release it describes, whether every artifact's claims name it, each
    /// artifact by its target in the descriptor's directory with its rows, and the rows every artifact shares.
    type private ReadDescriptor =
        { Described: ExtensionRelease
          NamesItself: bool
          ByTarget: Dictionary<string, DescribedArtifact * ClaimRows>
          Rows: ClaimRows list
          Shared: ClaimRows
          AllClaims: BoundClaims list }

    /// What one check has read, keyed by target: a failed read is kept as its code (and, for a release, the member it is
    /// reported at), so a second offering or provider that names the target is refused alike without reading it again.
    type private Reads =
        { Releases: Dictionary<string * string, Result<ReadRelease, Code * string>>
          Descriptors: Dictionary<string, Result<ReadDescriptor, Code>> }

    let private newReads () =
        { Releases = Dictionary(HashIdentity.Structural)
          Descriptors = Dictionary(System.StringComparer.Ordinal) }

    let private cached (cache: Dictionary<'K, 'V>) (key: 'K) (make: unit -> 'V) =
        match cache.TryGetValue key with
        | true, value -> value
        | _ ->
            let value = make ()
            cache[key] <- value
            value

    /// The bytes the source serves for an authenticated target. A source that has none, or fails, has not served
    /// them: <c>repository_unavailable</c> at the binding member.
    let private fetch (bytesOf: string -> byte array option) (target: string) (at: DocumentPath) =
        match (try bytesOf target with _ -> None) with
        | Some bytes -> Ok bytes
        | None -> trust TrustCode.RepositoryUnavailable at

    /// Served bytes against their authenticated entry, length before digest, refused at the binding member.
    let private read (entry: AuthenticatedTarget) (bytes: byte array) (at: DocumentPath) =
        RecipeTargets.authenticate (Some entry) bytes |> Result.mapError (fun refusal -> { refusal with Path = at })

    /// A bound target: authenticated, with the bound digest. The members name where each refusal is reported.
    let private bound
        (repository: Sha256)
        (targets: Map<string, AuthenticatedTarget>)
        (owner: DocumentPath)
        (targetMember: string)
        (digestMember: string)
        (binding: TargetBinding)
        : Result<AuthenticatedTarget * RecipeDigest, TargetRefusal> =
        match targets.TryFind binding.Target.Text with
        | None -> trust TrustCode.TargetNotAuthenticated (owner.Child targetMember)
        | Some entry when entry.Sha256 <> binding.Sha256 -> trust TrustCode.TargetDigestMismatch (owner.Child digestMember)
        | Some entry ->
            Ok(
                entry,
                { RepositoryId = repository
                  Target = binding.Target
                  Sha256 = binding.Sha256 }
            )

    /// The release record of a tool and version, with the member of the offering a refusal is reported at.
    let private readRelease
        (targets: Map<string, AuthenticatedTarget>)
        (bytesOf: string -> byte array option)
        (toolId: ToolId)
        (version: SemVer)
        : Result<ReadRelease, Code * string> =
        let path = $"releases/{toolId.Text}/{version.Text}.json"
        let source code = Error(Code.Trust code, "source")

        let state =
            targets.TryFind path
            |> Option.bind (fun entry -> morphir entry |> Option.map (fun m -> entry, m))
            |> Option.filter (fun (_, m) ->
                text "kind" m = Some "tool-release"
                && text "toolId" m = Some toolId.Text
                && text "version" m = Some version.Text
                && List.contains (text "status" m) [ Some "active"; Some "yanked"; Some "revoked" ])

        match state with
        | None -> source TrustCode.TargetNotAuthenticated
        | Some(_, m) when text "status" m = Some "revoked" -> Error(Code.Trust TrustCode.ReleaseRevoked, "version")
        | Some(entry, _) ->
            match fetch bytesOf path DocumentPath.root |> Result.bind (fun bytes -> read entry bytes DocumentPath.root |> Result.map (fun _ -> bytes)) with
            | Error refusal -> Error(refusal.Code, "source")
            | Ok bytes ->
                match ToolReleaseDocument.read bytes with
                | Ok release when release.ToolId.Text = toolId.Text && release.Version = version ->
                    let byTargetPath = Dictionary<string, ToolReleaseArtifact>(System.StringComparer.Ordinal)

                    for artifact in release.Artifacts do
                        byTargetPath.TryAdd(artifact.TargetPath, artifact) |> ignore

                    Ok { Tool = release; ByTargetPath = byTargetPath }
                | _ -> source TrustCode.ReleaseDescriptorMismatch

    /// <summary>The release record of a <c>tool-release</c> offering: its descriptor target is an authenticated
    /// <c>tool-release</c> for the tool and version (provisional: with a known status), not revoked, served with the
    /// authenticated length and digest, and readable as a v1 tool release. Provisional, the stricter reading: the
    /// descriptor's own tool id and version are the offering's, since the profile calls a mismatch in those immutable
    /// values <c>release_descriptor_mismatch</c>. Read once per check for each tool and version.</summary>
    let private hostRelease
        (reads: Reads)
        (targets: Map<string, AuthenticatedTarget>)
        (bytesOf: string -> byte array option)
        (at: DocumentPath)
        (host: HostOffering)
        (toolId: ToolId)
        : Result<ReadRelease, TargetRefusal> =
        cached reads.Releases (toolId.Text, host.Version.Text) (fun () -> readRelease targets bytesOf toolId host.Version)
        |> Result.mapError (fun (code, name) -> { Code = code; Path = at.Child name })

    /// <summary>The v1 release rows of one bound host artifact: the release lists the target, the listed artifact's
    /// v1 platform is the bound platform (a musl platform has none), and the target's <c>custom.morphir</c> is a
    /// <c>tool-artifact</c> of the release's tool, version and that platform.</summary>
    let private releaseArtifact (release: ReadRelease) (platform: Platform) (entry: AuthenticatedTarget) (binding: TargetBinding) (at: DocumentPath) =
        let targetPath = at.Child "target"

        match release.ByTargetPath.TryGetValue binding.Target.Text with
        | false, _ -> trust TrustCode.ReleaseDescriptorMismatch targetPath
        | true, artifact ->
            match toolPlatform platform with
            | Some(os, arch) when artifact.Platform.Os = os && artifact.Platform.Arch = arch ->
                let agrees =
                    morphir entry
                    |> Option.exists (fun m ->
                        text "kind" m = Some "tool-artifact"
                        && text "toolId" m = Some release.Tool.ToolId.Text
                        && text "version" m = Some release.Tool.Version.Text
                        && (member' "platform" m |> Option.exists (fun p -> text "os" p = Some os && text "arch" p = Some arch)))

                if agrees then Ok() else trust TrustCode.ReleaseDescriptorMismatch targetPath
            | _ -> trust TrustCode.ArtifactPlatformMismatch targetPath

    let private hosts
        (reads: Reads)
        (repository: Sha256)
        (targets: Map<string, AuthenticatedTarget>)
        (bytesOf: string -> byte array option)
        (recipe: Recipe)
        : Result<BoundHost list, TargetRefusal> =
        recipe.Hosts
        |> List.indexed
        |> Result.traverse (fun (index, host) ->
            result {
                let at = (DocumentPath.root.Child "hosts").At index

                let! release =
                    match host.Source with
                    | HostSource.ToolRelease toolId -> hostRelease reads targets bytesOf at host toolId |> Result.map Some
                    | HostSource.DotnetTool _ -> Ok None

                let! artifacts =
                    host.Platforms
                    |> Result.traverse (fun (platform, binding) ->
                        result {
                            let platformPath = (at.Child "platforms").Child(Platform.name platform)
                            let! entry, digest = bound repository targets platformPath "target" "sha256" binding

                            match release with
                            | Some release -> do! releaseArtifact release platform entry binding platformPath
                            | None -> ()

                            return platform, digest
                        })

                return
                    { Offering = host
                      Release = release |> Option.map _.Tool
                      Artifacts = artifacts }
            })

    /// <summary>The host binding rows for each offering, in order, and each platform in platform table order. A
    /// <c>dotnet-tool</c> offering has no v1 release, so only its platforms' target and digest rows apply.</summary>
    let checkHosts
        (repository: Sha256)
        (targets: Map<string, AuthenticatedTarget>)
        (bytesOf: string -> byte array option)
        (recipe: Recipe)
        : Result<BoundHost list, TargetRefusal> =
        hosts (newReads ()) repository targets bytesOf recipe

    /// The comparison key of a model tuple: equal as parsed values, so object member order does not matter, and array
    /// order and number spelling do.
    let private tupleKey (value: Value) = Canonical.bytes value |> Result.toOption

    let private rowsOf (claims: ClaimSet) : ClaimRows =
        { Kinds = set claims.Extension.Types
          Protocols = set claims.ProtocolVersions
          Compiles =
            match claims.Frontend with
            | Some frontend when frontend.Compile -> frontend.Languages
            | _ -> Set.empty
          Generates =
            match claims.Backend with
            | Some backend when backend.Generate -> backend.Targets
            | _ -> Set.empty
          Tuples = claims.ModelTuples |> List.choose tupleKey |> set }

    /// The rows every claim set has: a provider these admit is admitted by each of them.
    let private sharedRows (rows: ClaimRows list) : ClaimRows =
        match rows with
        | [] ->
            { Kinds = Set.empty
              Protocols = Set.empty
              Compiles = Set.empty
              Generates = Set.empty
              Tuples = Set.empty }
        | _ ->
            { Kinds = rows |> List.map _.Kinds |> Set.intersectMany
              Protocols = rows |> List.map _.Protocols |> Set.intersectMany
              Compiles = rows |> List.map _.Compiles |> Set.intersectMany
              Generates = rows |> List.map _.Generates |> Set.intersectMany
              Tuples = rows |> List.map _.Tuples |> Set.intersectMany }

    let private claimsOf (artifact: DescribedArtifact) =
        { Claims = artifact.Claims
          ClaimCheck = artifact.ClaimCheck }

    /// A descriptor's bytes, authenticated against its entry and read, with its artifacts indexed.
    let private readDescriptor (entry: AuthenticatedTarget) (bytesOf: string -> byte array option) (target: TargetPath) : Result<ReadDescriptor, Code> =
        match fetch bytesOf target.Text DocumentPath.root |> Result.bind (fun bytes -> read entry bytes DocumentPath.root |> Result.map (fun _ -> bytes)) with
        | Error refusal -> Error refusal.Code
        | Ok bytes ->
            match ExtensionReleaseDocument.read bytes with
            | Error _ -> Error(Code.Trust TrustCode.ReleaseDescriptorMismatch)
            | Ok described ->
                let directory =
                    match target.Text.LastIndexOf '/' with
                    | -1 -> ""
                    | at -> target.Text.Substring(0, at + 1)

                let rows = described.Artifacts |> List.map (fun artifact -> artifact, rowsOf artifact.Claims)
                let byTarget = Dictionary<string, DescribedArtifact * ClaimRows>(System.StringComparer.Ordinal)

                for artifact, row in rows do
                    byTarget.TryAdd(directory + artifact.Filename, (artifact, row)) |> ignore

                let namesDescriptor (artifact: DescribedArtifact) =
                    artifact.Claims.Extension.Id.Text = described.ExtensionId.Text
                    && artifact.Claims.Extension.Version = described.Version

                Ok
                    { Described = described
                      NamesItself = List.forall namesDescriptor described.Artifacts
                      ByTarget = byTarget
                      Rows = rows |> List.map snd
                      Shared = sharedRows (rows |> List.map snd)
                      AllClaims = described.Artifacts |> List.map claimsOf }

    /// <summary>The descriptor rows of one provider: <c>descriptorTarget</c> is authenticated with
    /// <c>descriptorSha256</c>, its bytes have the authenticated length and digest, they read as an extension release
    /// descriptor, its <c>extensionId</c> and <c>version</c> are the provider's, and every artifact's claims name the
    /// descriptor's identity, as publication already requires. The bytes are read once per check for each
    /// target.</summary>
    let private descriptor
        (reads: Reads)
        (repository: Sha256)
        (targets: Map<string, AuthenticatedTarget>)
        (bytesOf: string -> byte array option)
        (at: DocumentPath)
        (provider: RecipeProvider)
        : Result<RecipeDigest * ReadDescriptor, TargetRefusal> =
        result {
            let release = provider.Release
            let targetPath = at.Child "descriptorTarget"

            let! entry, digest =
                bound
                    repository
                    targets
                    at
                    "descriptorTarget"
                    "descriptorSha256"
                    { Target = release.DescriptorTarget
                      Sha256 = release.DescriptorSha256 }

            let! read =
                cached reads.Descriptors release.DescriptorTarget.Text (fun () -> readDescriptor entry bytesOf release.DescriptorTarget)
                |> Result.mapError (fun code -> { Code = code; Path = targetPath })

            if read.Described.ExtensionId.Text <> provider.ExtensionId.Text then
                return! trust TrustCode.ReleaseDescriptorMismatch (at.Child "extensionId")

            if read.Described.Version <> provider.Version then
                return! trust TrustCode.ReleaseDescriptorMismatch (at.Child "version")

            if not read.NamesItself then
                return! trust TrustCode.ReleaseDescriptorMismatch targetPath

            return digest, read
        }

    /// <summary>The descriptor's entry for a bound artifact: the descriptor lists it in its own directory, with the
    /// delivery's runtime and the bound digest.</summary>
    let private listedArtifact
        (read: ReadDescriptor)
        (runtime: ArtifactRuntime)
        (binding: TargetBinding)
        (owner: DocumentPath)
        : Result<DescribedArtifact * ClaimRows, TargetRefusal> =
        match read.ByTarget.TryGetValue binding.Target.Text with
        | false, _ -> trust TrustCode.ReleaseDescriptorMismatch (owner.Child "artifactTarget")
        | true, (artifact, _) when artifact.Runtime <> runtime -> trust TrustCode.ReleaseDescriptorMismatch (owner.Child "artifactTarget")
        | true, (artifact, _) when artifact.Sha256 <> binding.Sha256 -> trust TrustCode.TargetDigestMismatch (owner.Child "artifactSha256")
        | true, found -> Ok found

    /// The provider's model tuple as the parsed value a claim set lists.
    let private tupleValue (tuple: ModelTuple) =
        Value.Object
            [ "operation", Value.String(ModelOperation.text tuple.Operation)
              "profile", Value.String tuple.Profile.Text
              "codec", Value.String tuple.Codec.Text
              "envelopeVersion", Value.String tuple.EnvelopeVersion.Text
              "irVersion", Value.String tuple.IrVersion.Text ]

    /// The first claim row, in the page's order, that the rows do not admit for the provider, by its member name.
    let private failingRow (provider: RecipeProvider) (rows: ClaimRows) =
        let kind, (operandName, admitted) =
            match provider.Operation with
            | Operation.Compile language -> CapabilityKind.Frontend, ("language", rows.Compiles.Contains language.Text)
            | Operation.Generate target -> CapabilityKind.Backend, ("target", rows.Generates.Contains target.Text)

        if not (rows.Kinds.Contains kind) then
            Some "operation"
        elif not (rows.Protocols.Contains(ProtocolVersion.text provider.Protocol.Version)) then
            Some "protocolVersion"
        elif not admitted then
            Some operandName
        elif not (tupleKey (tupleValue provider.Protocol.Tuple) |> Option.exists rows.Tuples.Contains) then
            Some "modelTuple"
        else
            None

    /// <summary>The claim rows of one artifact's claims, in the page's order: the capability kind
    /// (<c>frontend</c> for <c>compile</c>, <c>backend</c> for <c>generate</c>), the provider's MEP version in
    /// <c>protocolVersions</c> (exact text), the language or target (<c>compile</c> or <c>generate</c> is
    /// <c>true</c> and the operand is listed), and the provider's <c>modelTuple</c> exactly among
    /// <c>modelTransport.tuples</c>. These are what the publisher claims; checking them is not a probe.</summary>
    let private capabilities (at: DocumentPath) (provider: RecipeProvider) (rows: ClaimRows) : Result<unit, TargetRefusal> =
        match failingRow provider rows with
        | Some name -> trust TrustCode.ReleaseDescriptorMismatch (at.Child name)
        | None -> Ok()

    /// <summary>The artifact rows of a provider's delivery: each bound artifact or package is authenticated with its
    /// digest; a process or WASM artifact is listed by the descriptor in its own directory, with the delivery's runtime
    /// and the bound digest; and a process artifact's target triple is the bound platform's. Process platforms are
    /// checked in platform table order. A <c>dotnet-tool</c> package is described by every artifact's claims: when the
    /// rows they share admit the provider, each admits it; otherwise the first that does not is found in order.</summary>
    let private artifacts
        (repository: Sha256)
        (targets: Map<string, AuthenticatedTarget>)
        (at: DocumentPath)
        (provider: RecipeProvider)
        (read: ReadDescriptor)
        : Result<(RecipeDigest * BoundClaims list) list, TargetRefusal> =
        match provider.Delivery with
        | RecipeDelivery.Process platforms ->
            platforms
            |> Result.traverse (fun (platform, binding) ->
                result {
                    let owner = (at.Child "platforms").Child(Platform.name platform)
                    let! _, digest = bound repository targets owner "artifactTarget" "artifactSha256" binding
                    let! artifact, rows = listedArtifact read ArtifactRuntime.Process binding owner

                    if artifact.Platform <> Some(targetTriple platform) then
                        return! trust TrustCode.ArtifactPlatformMismatch (owner.Child "artifactTarget")

                    do! capabilities at provider rows
                    return digest, [ claimsOf artifact ]
                })
        | RecipeDelivery.Wasm(_, binding) ->
            result {
                let! _, digest = bound repository targets at "artifactTarget" "artifactSha256" binding
                let! artifact, rows = listedArtifact read ArtifactRuntime.Wasm binding at
                do! capabilities at provider rows
                return [ digest, [ claimsOf artifact ] ]
            }
        | RecipeDelivery.DotnetTool package ->
            result {
                let! _, digest =
                    bound repository targets at "packageTarget" "packageSha256" { Target = package.Target; Sha256 = package.Sha256 }

                // The version 2 descriptor has no runtime for .NET tool packages: every artifact's claims describe it.
                if (failingRow provider read.Shared).IsSome then
                    do! read.Rows |> Result.traverse (capabilities at provider) |> Result.map ignore

                return [ digest, read.AllClaims ]
            }

    /// <summary>The provider binding rows for one provider, in the page's order: its repository first, before its
    /// descriptor is read.</summary>
    let private checkProvider
        (reads: Reads)
        (repository: Sha256)
        (targets: Map<string, AuthenticatedTarget>)
        (bytesOf: string -> byte array option)
        (index: int)
        (provider: RecipeProvider)
        : Result<BoundProvider, TargetRefusal> =
        result {
            let at = (DocumentPath.root.Child "providers").At index

            if provider.Release.RepositoryId <> repository then
                return! trust TrustCode.RepositoryMismatch (at.Child "repositoryId")

            let! digest, read = descriptor reads repository targets bytesOf at provider
            let! bound = artifacts repository targets at provider read

            return
                { Provider = provider
                  Descriptor = digest
                  Release = read.Described
                  Artifacts = bound }
        }

    /// <summary>
    /// Every host and provider binding of a recipe, against the repository whose metadata authenticated it: the host
    /// rows for each offering, then the provider rows for each provider. No exception leaves it.
    /// </summary>
    let check
        (repository: Sha256)
        (targets: Map<string, AuthenticatedTarget>)
        (bytesOf: string -> byte array option)
        (recipe: Recipe)
        : Result<BoundRecipe, TargetRefusal> =
        result {
            let reads = newReads ()
            let! hosts = hosts reads repository targets bytesOf recipe

            let! providers =
                recipe.Providers
                |> List.indexed
                |> Result.traverse (fun (index, provider) -> checkProvider reads repository targets bytesOf index provider)

            return { Hosts = hosts; Providers = providers }
        }
