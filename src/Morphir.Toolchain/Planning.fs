// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

/// <summary>The download and installed sizes of one artifact. <c>downloadBytes</c> is the authenticated length of the
/// target, from the TUF targets metadata; the source of <c>installedBytes</c> is not settled in <c>0.1.0-draft.1</c>.
/// Neither is in the recipe, so the caller supplies both.</summary>
type ArtifactSize = {
    DownloadBytes: uint64
    InstalledBytes: uint64
}

/// Why no plan could be made.
[<RequireQualifiedAccess>]
type PlanFailure =
    | Unresolved of failure: ResolutionFailure
    /// A downloaded artifact whose sizes the caller did not supply.
    | SizeUnknown of target: TargetPath

/// What plan-only setup is given. Every input is already authenticated: authenticating the recipe is the caller's.
type PlanRequest = {
    Recipe: Recipe
    RecipeRepository: Sha256
    RecipeTarget: TargetPath
    Request: Request
    /// The platform being set up; the plan installs for it.
    Platform: Platform
    Sizes: TargetPath -> ArtifactSize option
    MetadataRefresh: MetadataRefresh
}

/// <summary>
/// Setup plans (manager-result page, "Plans"). Planning is pure: it reads nothing, downloads nothing, runs no probe
/// and changes no selection. It resolves every platform the lock would record, so a plan never exists for a lock that
/// setup could not write.
/// </summary>
module Planning =

    /// <summary>A prerequisite applies on a platform when it names no platforms or names that one. A plan lists only
    /// the prerequisites of its own platform: they are inside <c>planSha256</c>.</summary>
    let appliesOn (platform: Platform) (prerequisite: Prerequisite) =
        prerequisite.Platforms
        |> Option.forall (List.contains platform)

    let private on platform prerequisites =
        prerequisites
        |> List.filter (appliesOn platform)
        |> List.map PlannedPrerequisite.ofPrerequisite

    /// The component for one selected provider, with the artifact its locked form binds on the platform.
    let private plannedProvider
        (sizes: TargetPath -> ArtifactSize option)
        (platform: Platform)
        (selected: Selected, locked: LockedProvider)
        =
        let downloaded
            (provider: RecipeProvider)
            (delivery: PlannedDelivery)
            (bound: TargetBinding)
            =
            match sizes bound.Target with
            | None -> Error(PlanFailure.SizeUnknown bound.Target)
            | Some size ->
                Ok(
                    PlannedComponent.Provider(
                        provider.Operation,
                        provider.ExtensionId,
                        delivery,
                        {
                            Version = provider.Version
                            Source =
                                ComponentSource.RecipeDigest(
                                    provider.Release.RepositoryId,
                                    bound.Target,
                                    bound.Sha256
                                )
                            DownloadBytes = size.DownloadBytes
                            InstalledBytes = size.InstalledBytes
                            UpdateOwner = Ownership.ManagerOwned
                            Prerequisites = on platform provider.Prerequisites
                        }
                    )
                )

        match selected, locked.Delivery with
        | Selected.BuiltIn builtIn, _ ->
            Ok(
                PlannedComponent.Provider(
                    builtIn.Operation,
                    builtIn.ExtensionId,
                    PlannedDelivery.BuiltIn,
                    {
                        Version = builtIn.Version
                        Source = ComponentSource.BuiltIn
                        DownloadBytes = 0UL
                        InstalledBytes = 0UL
                        UpdateOwner = Ownership.ManagerOwned
                        Prerequisites = []
                    }
                )
            )
        | Selected.Provider provider, LockedDelivery.Process(_, artifact, _) ->
            downloaded provider PlannedDelivery.Process artifact
        | Selected.Provider provider, LockedDelivery.Wasm(_, _, artifact, _) ->
            downloaded provider PlannedDelivery.Wasm artifact
        | Selected.Provider provider, LockedDelivery.DotnetTool(_, package, _) ->
            downloaded
                provider
                PlannedDelivery.DotnetTool
                {
                    Target = package.Target
                    Sha256 = package.Sha256
                }
        | Selected.Provider provider, LockedDelivery.BuiltIn _ ->
            failwith
                $"unreachable: resolution locked the provider {provider.ExtensionId.Text} as a built-in"

    /// The plan for one platform and the lock setup would write, which records every platform of the request and the
    /// platform being set up.
    let plan (input: PlanRequest) : Result<DigestedPlan * Lock, PlanFailure> = result {
        let! lock, resolved =
            Resolution.lock
                input.Recipe
                input.RecipeRepository
                input.RecipeTarget
                input.Request
                input.Platform
            |> Result.mapError PlanFailure.Unresolved

        let current =
            resolved
            |> List.find (fun r -> r.Platform = input.Platform)

        let offering = current.Offering

        let! hostSize =
            input.Sizes current.Closure.Host.Target
            |> Option.map Ok
            |> Option.defaultValue (Error(PlanFailure.SizeUnknown current.Closure.Host.Target))

        let host =
            PlannedComponent.Host(
                offering.Implementation,
                offering.Deployment,
                {
                    Version = offering.Version
                    Source =
                        ComponentSource.RepositoryTarget(
                            lock.Recipe.RepositoryId,
                            current.Closure.Host.Target,
                            current.Closure.Host.Sha256
                        )
                    DownloadBytes = hostSize.DownloadBytes
                    InstalledBytes = hostSize.InstalledBytes
                    UpdateOwner = Ownership.ManagerOwned
                    Prerequisites = on input.Platform offering.Prerequisites
                }
            )

        let! providers =
            List.zip current.Selected current.Closure.Providers
            |> Result.traverse (plannedProvider input.Sizes input.Platform)

        let plan = {
            Implementation = offering.Implementation
            Deployment = offering.Deployment
            Platform = input.Platform
            Recipe = lock.Recipe
            MetadataRefresh = input.MetadataRefresh
            Components =
                host
                :: providers
            Lock = lock
        }

        return ResultDocument.digest plan, lock
    }

/// Explicit caller consent for the local candidate journey. It confers no publisher trust.
[<RequireQualifiedAccess>]
type LocalConsent = | LocalDeveloper

/// In-memory input to the actual local setup path. Persisted representations are
/// owned by the separate local-toolchains contract, never a historical recipe.
type LocalHostFile = {
    Artifact: TargetBinding
    Bytes: uint64
}

type LocalHostCandidate = {
    ManifestSha256: Sha256
    Host: HostIdentity
    Platform: Platform
    SourceCommit: NonBlankText
    BuildIdentity: NonBlankText
    ArchiveSha256: Sha256
    ArchiveBytes: uint64
    EntryPoint: TargetBinding
    Files: LocalHostFile list
}

[<RequireQualifiedAccess>]
type LocalCandidateRefusal =
    | ExplicitConsentRequired
    | PlatformMismatch
    | UnsupportedOffering
    | ArchiveBound
    | ClosureMismatch

/// A validated local plan, still not an installation or publisher authentication.
type LocalCandidatePlan =
    private
    | LocalCandidatePlan of origin: LocalConsent * candidate: LocalHostCandidate

    member this.Origin = let (LocalCandidatePlan(origin, _)) = this in origin
    member this.Candidate = let (LocalCandidatePlan(_, candidate)) = this in candidate

module LocalCandidatePlanning =
    let private uniqueFiles (files: LocalHostFile list) =
        let seen =
            System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)

        files
        |> List.forall (fun file -> seen.Add file.Artifact.Target.Text)

    let private validate supported currentPlatform maximumArchiveBytes (candidate: LocalHostCandidate) =
        if
            candidate.Platform
            <> currentPlatform
        then
            Error LocalCandidateRefusal.PlatformMismatch
        elif
            not (supported candidate.Platform)
            || candidate.Host.Implementation.Text
               <> "dotnet"
            || candidate.Host.Deployment.Text
               <> "self-contained"
        then
            Error LocalCandidateRefusal.UnsupportedOffering
        elif
            candidate.ArchiveBytes = 0UL
            || candidate.ArchiveBytes > maximumArchiveBytes
        then
            Error LocalCandidateRefusal.ArchiveBound
        elif
            candidate.Files.IsEmpty
            || candidate.Files.Length > 8192
            || candidate.Files
               |> List.filter (fun file -> file.Artifact = candidate.EntryPoint)
               |> List.length
               <> 1
            || candidate.Files
               |> List.exists (fun file -> file.Bytes > maximumArchiveBytes)
            || not (uniqueFiles candidate.Files)
        then
            Error LocalCandidateRefusal.ClosureMismatch
        else
            Ok ()

    /// One validation route for historical local setup apply and dry-run. It performs no I/O.
    let plan currentPlatform consent maximumArchiveBytes candidate =
        match consent with
        | None -> Error LocalCandidateRefusal.ExplicitConsentRequired
        | Some LocalConsent.LocalDeveloper ->
            validate ((=) Platform.WinX64) currentPlatform maximumArchiveBytes candidate
            |> Result.map (fun () -> LocalCandidatePlan(LocalConsent.LocalDeveloper, candidate))

    // Preview.4 accepts the two Windows process platforms and macOS ARM64. Acquisition or
    // explicit developer consent is checked by its caller, never granted by this plan.
    let internal planRelease currentPlatform maximumArchiveBytes candidate =
        validate Platform.isLocalToolchain currentPlatform maximumArchiveBytes candidate

/// The explicit local pointer binds immutable receipt bytes, not a repository or a recipe.
type LocalCandidateSelection = {
    CandidateSha256: Sha256
    ReceiptSha256: Sha256
    ReceiptPath: TargetPath
}

/// The root-owned local-toolchains family. Historical/preview toolchain codecs are unchanged.
module LocalCandidateDocument =
    open Reading

    let schema name =
        $"https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.1/{name}.schema.json"

    /// The archive format of a host archive: ZIP on Windows, tar with gzip elsewhere, as the release archives are.
    let archiveFormat platform =
        if Platform.isWindows platform then "zip" else "tar-gzip"

    let installationPath (candidate: LocalHostCandidate) =
        $"store/tools/sha256/{candidate.ArchiveSha256.Text}"

    let generationPath (identity: Sha256) =
        $"toolchains/generations/{identity.Text}"

    let receiptPath identity =
        generationPath identity
        + "/receipt.json"

    let selectionPath = "toolchains/default.json"

    let private closed names (o: ObjectNode) =
        match
            o.Members
            |> List.tryFind (fun (name, _) -> not (List.contains name names))
        with
        | Some(name, _) -> refuse ReaderCode.InvalidDocument (o.Path.Child name)
        | None -> Ok o

    let private envelopeFor family kind names bytes = result {
        let! value = Syntax.parseJson bytes
        let! o = asObject (node value DocumentPath.root)

        match o.Members with
        | ("$schema", _) :: _ -> ()
        | _ -> return! refuse ReaderCode.InvalidDocument (DocumentPath.root.Child "$schema")

        let! identity = required "$schema" text o

        if
            identity
            <> $"https://morphir.finos.org/schemas/local-toolchains/{family}/{kind}.schema.json"
        then
            return! refuse ReaderCode.UnsupportedContractVersion (o.Path.Child "$schema")

        return!
            closed
                ("$schema"
                 :: names)
                o
    }

    let private envelope kind names bytes = envelopeFor "0.1.0-preview.1" kind names bytes

    let private constant name expected o = result {
        let! actual = required name text o

        if
            actual
            <> expected
        then
            return! refuse ReaderCode.InconsistentDocument (o.Path.Child name)

        return ()
    }

    let private identityFor releaseFamily o = result {
        do! if releaseFamily then required "origin" (tag ["local-developer",();"release-checksum",()]) o
            else constant "origin" "local-developer" o
        do! constant "implementation" "dotnet" o
        do! constant "deployment" "self-contained" o
        do! if releaseFamily then Ok () else constant "platform" "win-x64" o
        let! platform = required "platform" (if releaseFamily then tag ["win-x64",Platform.WinX64;"win-arm64",Platform.WinArm64;"osx-arm64",Platform.OsxArm64] else platform) o
        let! version = required "version" version o
        let! source = required "sourceCommit" text o

        if
            source.Length
            <> 40
            || source
               |> Seq.exists (fun c ->
                   not (
                       isAsciiDigit c
                       || c >= 'a'
                          && c <= 'f'
                   )
               )
        then
            return! refuse ReaderCode.InvalidValue (o.Path.Child "sourceCommit")

        let! sourceCommit =
            NonBlankText.tryParse source
            |> Result.mapError (fun code -> {
                Code = code
                Path = o.Path.Child "sourceCommit"
            })

        let! buildIdentity = required "buildIdentity" (validated NonBlankText.tryParse) o
        return version, sourceCommit, buildIdentity, platform
    }

    let private identity o = identityFor false o |> Result.map(fun (version,source,build,_) -> version,source,build)

    let private portablePath n = result {
        let! path = targetPath n
        // The local family has an additional ASCII lexical restriction, while portability stays with TargetPath.
        if
            not (System.Char.IsAsciiLetterOrDigit path.Text[0])
            || path.Text
               |> Seq.exists (fun c ->
                   not (
                       System.Char.IsAsciiLetterOrDigit c
                       || "._/-".Contains c
                   )
               )
        then
            return! refuse ReaderCode.InvalidValue n.Path

        return path
    }

    let private entryPoint n = result {
        let! o =
            asObject n
            |> Result.bind (closed [ "path"; "sha256" ])

        let! path = required "path" portablePath o
        let! digest = required "sha256" sha256 o
        return { Target = path; Sha256 = digest }
    }

    let internal readCandidateFor family extraNames (bytes: byte array) : Result<LocalHostCandidate, ReaderRefusal> = result {
        let! o =
            envelopeFor family
                "candidate"
                ([
                    "origin"
                    "implementation"
                    "deployment"
                    "platform"
                    "version"
                    "sourceRepository"
                    "sourceCommit"
                    "buildIdentity"
                    "archive"
                    "entryPoint"
                    "files"
                ] @ extraNames)
                bytes

        let! version, sourceCommit, buildIdentity, platform = identityFor (family = "0.1.0-preview.4") o
        do! constant "sourceRepository" "https://github.com/finos/morphir-dotnet" o

        let! archive =
            objectMember "archive" o
            |> Result.bind (closed [ "format"; "sha256"; "bytes" ])

        do! constant "format" (archiveFormat platform) archive
        let! digest = required "sha256" sha256 archive
        let! length = required "bytes" integer archive

        if length = 0UL then
            return! refuse ReaderCode.InvalidValue (archive.Path.Child "bytes")

        let! entry = required "entryPoint" entryPoint o

        let file n = result {
            let! record =
                asObject n
                |> Result.bind (closed [ "path"; "sha256"; "bytes" ])

            let! path = required "path" portablePath record
            let! sha = required "sha256" sha256 record
            let! bytes = required "bytes" integer record

            return {
                Artifact = { Target = path; Sha256 = sha }
                Bytes = bytes
            }
        }

        let! files = required "files" (array file) o

        if
            files.IsEmpty
            || files.Length > 8192
        then
            return! refuse ReaderCode.InvalidValue (o.Path.Child "files")

        let seen =
            System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)

        if
            files
            |> List.exists (fun file -> not (seen.Add file.Artifact.Target.Text))
        then
            return! refuse ReaderCode.InconsistentDocument (o.Path.Child "files")

        if
            files
            |> List.filter (fun file -> file.Artifact = entry)
            |> List.length
            <> 1
        then
            return! refuse ReaderCode.InconsistentDocument (o.Path.Child "entryPoint")

        let! implementation =
            Token.tryParse "dotnet"
            |> Result.mapError (fun code -> { Code = code; Path = o.Path })

        let! deployment =
            Token.tryParse "self-contained"
            |> Result.mapError (fun code -> { Code = code; Path = o.Path })

        return {
            ManifestSha256 = Sha256.ofBytes bytes
            Host = {
                Implementation = implementation
                Deployment = deployment
                Version = version
            }
            Platform = platform
            SourceCommit = sourceCommit
            BuildIdentity = buildIdentity
            ArchiveSha256 = digest
            ArchiveBytes = length
            EntryPoint = entry
            Files = files
        }
    }

    let readCandidate bytes = readCandidateFor "0.1.0-preview.1" [] bytes

    let private entryValue (entry: TargetBinding) =
        Value.Object [
            "path", Value.String entry.Target.Text
            "sha256", Value.String entry.Sha256.Text
        ]

    let private identityValues (candidate: LocalHostCandidate) = [
        "origin", Value.String "local-developer"
        "implementation", Value.String candidate.Host.Implementation.Text
        "deployment", Value.String candidate.Host.Deployment.Text
        "platform", Value.String(Platform.name candidate.Platform)
        "version", Value.String candidate.Host.Version.Text
        "sourceCommit", Value.String candidate.SourceCommit.Text
        "buildIdentity", Value.String candidate.BuildIdentity.Text
    ]

    /// Only the composition that actually completed its installed version probe may write this receipt.
    let writeReceipt (candidate: LocalHostCandidate) =
        Value.Object(
            [ "$schema", Value.String(schema "receipt") ]
            @ identityValues candidate
            @ [
                "candidateSha256", Value.String candidate.ManifestSha256.Text
                "archiveSha256", Value.String candidate.ArchiveSha256.Text
                "installationPath", Value.String(installationPath candidate)
                "entryPoint", entryValue candidate.EntryPoint
                "owner", Value.String "manager-owned"
                "readiness", Value.String "launch-verified"
            ]
        )
        |> JsonText.toBytes

    let receiptMatches (candidate: LocalHostCandidate) bytes : Result<unit, ReaderRefusal> = result {
        let! o =
            envelope
                "receipt"
                [
                    "origin"
                    "implementation"
                    "deployment"
                    "platform"
                    "version"
                    "sourceCommit"
                    "buildIdentity"
                    "candidateSha256"
                    "archiveSha256"
                    "installationPath"
                    "entryPoint"
                    "owner"
                    "readiness"
                ]
                bytes

        let! version, commit, build = identity o

        if
            version
            <> candidate.Host.Version
            || commit
               <> candidate.SourceCommit
            || build
               <> candidate.BuildIdentity
        then
            return! refuse ReaderCode.InconsistentDocument o.Path

        do! constant "candidateSha256" candidate.ManifestSha256.Text o
        do! constant "archiveSha256" candidate.ArchiveSha256.Text o
        do! constant "installationPath" (installationPath candidate) o
        do! constant "owner" "manager-owned" o
        do! constant "readiness" "launch-verified" o
        let! entry = required "entryPoint" entryPoint o

        if
            entry
            <> candidate.EntryPoint
        then
            return! refuse ReaderCode.InconsistentDocument (o.Path.Child "entryPoint")

        return ()
    }

    let readSelection bytes : Result<LocalCandidateSelection, ReaderRefusal> = result {
        let! o =
            envelope
                "selection"
                [ "origin"; "candidateSha256"; "receiptSha256"; "receiptPath" ]
                bytes

        do! constant "origin" "local-developer" o
        let! candidate = required "candidateSha256" sha256 o
        let! receipt = required "receiptSha256" sha256 o
        let! path = required "receiptPath" targetPath o

        if
            path.Text
            <> receiptPath candidate
        then
            return! refuse ReaderCode.InconsistentDocument (o.Path.Child "receiptPath")

        return {
            CandidateSha256 = candidate
            ReceiptSha256 = receipt
            ReceiptPath = path
        }
    }

    let writeSelection (candidate: LocalHostCandidate) receipt =
        Value.Object [
            "$schema", Value.String(schema "selection")
            "origin", Value.String "local-developer"
            "candidateSha256", Value.String candidate.ManifestSha256.Text
            "receiptSha256", Value.String((Sha256.ofBytes receipt).Text)
            "receiptPath", Value.String(receiptPath candidate.ManifestSha256)
        ]
        |> JsonText.toBytes
