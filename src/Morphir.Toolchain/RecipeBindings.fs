// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

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

    /// <summary>The release record of a <c>tool-release</c> offering: its descriptor target is an authenticated
    /// <c>tool-release</c> for the tool and version (provisional: with a known status), not revoked, served with the
    /// authenticated length and digest, and readable as a v1 tool release. Provisional, the stricter reading: the
    /// descriptor's own tool id and version are the offering's, since the profile calls a mismatch in those immutable
    /// values <c>release_descriptor_mismatch</c>.</summary>
    let private hostRelease
        (targets: Map<string, AuthenticatedTarget>)
        (bytesOf: string -> byte array option)
        (at: DocumentPath)
        (host: HostOffering)
        (toolId: ToolId)
        : Result<ToolRelease, TargetRefusal> =
        result {
            let path = $"releases/{toolId.Text}/{host.Version.Text}.json"
            let sourcePath = at.Child "source"

            let state =
                targets.TryFind path
                |> Option.bind (fun entry -> morphir entry |> Option.map (fun m -> entry, m))
                |> Option.filter (fun (_, m) ->
                    text "kind" m = Some "tool-release"
                    && text "toolId" m = Some toolId.Text
                    && text "version" m = Some host.Version.Text
                    && List.contains (text "status" m) [ Some "active"; Some "yanked"; Some "revoked" ])

            match state with
            | None -> return! trust TrustCode.TargetNotAuthenticated sourcePath
            | Some(entry, m) ->
                if text "status" m = Some "revoked" then
                    return! trust TrustCode.ReleaseRevoked (at.Child "version")

                let! bytes = fetch bytesOf path sourcePath
                let! _ = read entry bytes sourcePath

                match ToolReleaseDocument.read bytes with
                | Error _ -> return! trust TrustCode.ReleaseDescriptorMismatch sourcePath
                | Ok release when release.ToolId.Text <> toolId.Text || release.Version <> host.Version ->
                    return! trust TrustCode.ReleaseDescriptorMismatch sourcePath
                | Ok release -> return release
        }

    /// <summary>The v1 release rows of one bound host artifact: the release lists the target, the listed artifact's
    /// v1 platform is the bound platform (a musl platform has none), and the target's <c>custom.morphir</c> is a
    /// <c>tool-artifact</c> of the release's tool, version and that platform.</summary>
    let private releaseArtifact (release: ToolRelease) (platform: Platform) (entry: AuthenticatedTarget) (binding: TargetBinding) (at: DocumentPath) =
        let targetPath = at.Child "target"

        match release.Artifacts |> List.tryFind (fun artifact -> artifact.TargetPath = binding.Target.Text) with
        | None -> trust TrustCode.ReleaseDescriptorMismatch targetPath
        | Some artifact ->
            match toolPlatform platform with
            | Some(os, arch) when artifact.Platform.Os = os && artifact.Platform.Arch = arch ->
                let agrees =
                    morphir entry
                    |> Option.exists (fun m ->
                        text "kind" m = Some "tool-artifact"
                        && text "toolId" m = Some release.ToolId.Text
                        && text "version" m = Some release.Version.Text
                        && (member' "platform" m |> Option.exists (fun p -> text "os" p = Some os && text "arch" p = Some arch)))

                if agrees then Ok() else trust TrustCode.ReleaseDescriptorMismatch targetPath
            | _ -> trust TrustCode.ArtifactPlatformMismatch targetPath

    /// <summary>The host binding rows for each offering, in order, and each platform in platform table order. A
    /// <c>dotnet-tool</c> offering has no v1 release, so only its platforms' target and digest rows apply.</summary>
    let checkHosts
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
                    | HostSource.ToolRelease toolId -> hostRelease targets bytesOf at host toolId |> Result.map Some
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
                      Release = release
                      Artifacts = artifacts }
            })

    /// <summary>The descriptor rows of one provider: <c>descriptorTarget</c> is authenticated with
    /// <c>descriptorSha256</c>, its bytes have the authenticated length and digest, they read as an extension release
    /// descriptor, its <c>extensionId</c> and <c>version</c> are the provider's, and every artifact's claims name the
    /// descriptor's identity, as publication already requires.</summary>
    let private descriptor
        (repository: Sha256)
        (targets: Map<string, AuthenticatedTarget>)
        (bytesOf: string -> byte array option)
        (at: DocumentPath)
        (provider: RecipeProvider)
        : Result<RecipeDigest * ExtensionRelease, TargetRefusal> =
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

            let! bytes = fetch bytesOf release.DescriptorTarget.Text targetPath
            let! _ = read entry bytes targetPath

            let! described =
                ExtensionReleaseDocument.read bytes
                |> Result.mapError (fun _ -> { Code = Code.Trust TrustCode.ReleaseDescriptorMismatch; Path = targetPath })

            if described.ExtensionId.Text <> provider.ExtensionId.Text then
                return! trust TrustCode.ReleaseDescriptorMismatch (at.Child "extensionId")

            if described.Version <> provider.Version then
                return! trust TrustCode.ReleaseDescriptorMismatch (at.Child "version")

            let namesDescriptor (artifact: DescribedArtifact) =
                artifact.Claims.Extension.Id.Text = described.ExtensionId.Text
                && artifact.Claims.Extension.Version = described.Version

            if not (List.forall namesDescriptor described.Artifacts) then
                return! trust TrustCode.ReleaseDescriptorMismatch targetPath

            return digest, described
        }

    /// <summary>The descriptor's entry for a bound artifact: the descriptor lists it in its own directory, with the
    /// delivery's runtime and the bound digest.</summary>
    let private listedArtifact
        (release: ExtensionRelease)
        (descriptorTarget: TargetPath)
        (runtime: ArtifactRuntime)
        (binding: TargetBinding)
        (owner: DocumentPath)
        : Result<DescribedArtifact, TargetRefusal> =
        let directory =
            match descriptorTarget.Text.LastIndexOf '/' with
            | -1 -> ""
            | at -> descriptorTarget.Text.Substring(0, at + 1)

        match release.Artifacts |> List.tryFind (fun artifact -> directory + artifact.Filename = binding.Target.Text) with
        | None -> trust TrustCode.ReleaseDescriptorMismatch (owner.Child "artifactTarget")
        | Some artifact when artifact.Runtime <> runtime -> trust TrustCode.ReleaseDescriptorMismatch (owner.Child "artifactTarget")
        | Some artifact when artifact.Sha256 <> binding.Sha256 -> trust TrustCode.TargetDigestMismatch (owner.Child "artifactSha256")
        | Some artifact -> Ok artifact

    /// The provider's model tuple as the parsed value a claim set lists.
    let private tupleValue (tuple: ModelTuple) =
        Value.Object
            [ "operation", Value.String(ModelOperation.text tuple.Operation)
              "profile", Value.String tuple.Profile.Text
              "codec", Value.String tuple.Codec.Text
              "envelopeVersion", Value.String tuple.EnvelopeVersion.Text
              "irVersion", Value.String tuple.IrVersion.Text ]

    /// Equal as parsed values: object member order does not matter, array order and number spelling do.
    let private sameValue (a: Value) (b: Value) =
        match Canonical.bytes a, Canonical.bytes b with
        | Ok x, Ok y -> x = y
        | _ -> false

    /// <summary>The claim rows of one artifact's claims, in the page's order: the capability kind
    /// (<c>frontend</c> for <c>compile</c>, <c>backend</c> for <c>generate</c>), the provider's MEP version in
    /// <c>protocolVersions</c> (exact text), the language or target (<c>compile</c> or <c>generate</c> is
    /// <c>true</c> and the operand is listed), and the provider's <c>modelTuple</c> exactly among
    /// <c>modelTransport.tuples</c>. These are what the publisher claims; checking them is not a probe.</summary>
    let private capabilities (at: DocumentPath) (provider: RecipeProvider) (claims: ClaimSet) : Result<unit, TargetRefusal> =
        let mismatch (name: string) = trust TrustCode.ReleaseDescriptorMismatch (at.Child name)

        let kind =
            match provider.Operation with
            | Operation.Compile _ -> CapabilityKind.Frontend
            | Operation.Generate _ -> CapabilityKind.Backend

        if not (List.contains kind claims.Extension.Types) then
            mismatch "operation"
        elif not (List.contains (ProtocolVersion.text provider.Protocol.Version) claims.ProtocolVersions) then
            mismatch "protocolVersion"
        else
            let operand =
                match provider.Operation with
                | Operation.Compile language ->
                    "language", claims.Frontend |> Option.exists (fun f -> f.Compile && List.contains language.Text f.Languages)
                | Operation.Generate target ->
                    "target", claims.Backend |> Option.exists (fun b -> b.Generate && List.contains target.Text b.Targets)

            match operand with
            | name, false -> mismatch name
            | _ when not (claims.ModelTuples |> List.exists (sameValue (tupleValue provider.Protocol.Tuple))) -> mismatch "modelTuple"
            | _ -> Ok()

    let private claimsOf (artifact: DescribedArtifact) =
        { Claims = artifact.Claims
          ClaimCheck = artifact.ClaimCheck }

    /// <summary>The artifact rows of a provider's delivery: each bound artifact or package is authenticated with its
    /// digest; a process or WASM artifact is listed by the descriptor in its own directory, with the delivery's runtime
    /// and the bound digest; and a process artifact's target triple is the bound platform's. Process platforms are
    /// checked in platform table order. A <c>dotnet-tool</c> package is described by every artifact's claims.</summary>
    let private artifacts
        (repository: Sha256)
        (targets: Map<string, AuthenticatedTarget>)
        (at: DocumentPath)
        (provider: RecipeProvider)
        (release: ExtensionRelease)
        : Result<(RecipeDigest * BoundClaims list) list, TargetRefusal> =
        let descriptorTarget = provider.Release.DescriptorTarget

        match provider.Delivery with
        | RecipeDelivery.Process platforms ->
            platforms
            |> Result.traverse (fun (platform, binding) ->
                result {
                    let owner = (at.Child "platforms").Child(Platform.name platform)
                    let! _, digest = bound repository targets owner "artifactTarget" "artifactSha256" binding
                    let! artifact = listedArtifact release descriptorTarget ArtifactRuntime.Process binding owner

                    if artifact.Platform <> Some(targetTriple platform) then
                        return! trust TrustCode.ArtifactPlatformMismatch (owner.Child "artifactTarget")

                    do! capabilities at provider artifact.Claims
                    return digest, [ claimsOf artifact ]
                })
        | RecipeDelivery.Wasm(_, binding) ->
            result {
                let! _, digest = bound repository targets at "artifactTarget" "artifactSha256" binding
                let! artifact = listedArtifact release descriptorTarget ArtifactRuntime.Wasm binding at
                do! capabilities at provider artifact.Claims
                return [ digest, [ claimsOf artifact ] ]
            }
        | RecipeDelivery.DotnetTool package ->
            result {
                let! _, digest =
                    bound repository targets at "packageTarget" "packageSha256" { Target = package.Target; Sha256 = package.Sha256 }

                // The version 2 descriptor has no runtime for .NET tool packages: every artifact's claims describe it.
                do! release.Artifacts |> Result.traverse (fun artifact -> capabilities at provider artifact.Claims) |> Result.map ignore
                return [ digest, release.Artifacts |> List.map claimsOf ]
            }

    /// <summary>The provider binding rows for one provider, in the page's order: its repository first, before its
    /// descriptor is read.</summary>
    let private checkProvider
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

            let! digest, release = descriptor repository targets bytesOf at provider
            let! bound = artifacts repository targets at provider release

            return
                { Provider = provider
                  Descriptor = digest
                  Release = release
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
            let! hosts = checkHosts repository targets bytesOf recipe

            let! providers =
                recipe.Providers |> List.indexed |> Result.traverse (fun (index, provider) -> checkProvider repository targets bytesOf index provider)

            return { Hosts = hosts; Providers = providers }
        }
