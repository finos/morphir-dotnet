// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

/// What a check that compares documents found.
[<RequireQualifiedAccess>]
type BindingFailure =
    /// A <c>lock-binding</c> code.
    | Binding of problem: BindingProblem
    /// <c>repository_mismatch</c>: a document names a repository other than the one the check runs against, which is
    /// carried here, as the <c>trust-failure</c> diagnostic reports it.
    | RepositoryMismatch of repository: Sha256

/// A refusal from a check that compares one document with another, or a document with the authenticated repository.
type BindingRefusal =
    { Failure: BindingFailure
      Path: DocumentPath }

    member this.Code =
        match this.Failure with
        | BindingFailure.Binding problem -> Code.Binding(BindingProblem.code problem)
        | BindingFailure.RepositoryMismatch _ -> Code.Trust TrustCode.RepositoryMismatch

/// The locked form of a recipe's built-ins and providers.
module Locked =
    let ofBuiltIn (builtIn: BuiltIn) : LockedProvider =
        { Operation = builtIn.Operation
          ExtensionId = builtIn.ExtensionId
          Version = builtIn.Version
          Delivery = LockedDelivery.BuiltIn builtIn.ModelInput }

    /// The provider as a lock records it on a platform; <c>None</c> for a process provider without an artifact there.
    let ofProvider (platform: Platform) (provider: RecipeProvider) : LockedProvider option =
        let locked delivery =
            Some
                { Operation = provider.Operation
                  ExtensionId = provider.ExtensionId
                  Version = provider.Version
                  Delivery = delivery }

        match provider.Delivery with
        | RecipeDelivery.Process platforms ->
            platforms
            |> List.tryFind (fst >> (=) platform)
            |> Option.bind (fun (_, artifact) -> locked (LockedDelivery.Process(provider.Release, artifact, provider.Protocol)))
        | RecipeDelivery.Wasm(abi, artifact) -> locked (LockedDelivery.Wasm(abi, provider.Release, artifact, provider.Protocol))
        | RecipeDelivery.DotnetTool package -> locked (LockedDelivery.DotnetTool(provider.Release, package, provider.Protocol))

    /// The repository a locked provider names, when its delivery has one; a built-in names none.
    let repository (provider: LockedProvider) =
        match provider.Delivery with
        | LockedDelivery.BuiltIn _ -> None
        | LockedDelivery.Process(release, _, _)
        | LockedDelivery.Wasm(_, release, _, _)
        | LockedDelivery.DotnetTool(release, _, _) -> Some release.RepositoryId

/// The checks that bind documents to each other (toolchain-lock and installation-receipt pages) and to the one
/// authenticated repository a binding check runs against (toolset-recipe page, "One repository per binding check").
module Bindings =

    let private refuse problem (path: DocumentPath) : Result<'T, BindingRefusal> =
        Error
            { Failure = BindingFailure.Binding problem
              Path = path }

    let private wrongRepository repository (path: DocumentPath) : Result<'T, BindingRefusal> =
        Error
            { Failure = BindingFailure.RepositoryMismatch repository
              Path = path }

    let private root = DocumentPath.root

    /// <summary>Frozen restore, first check: the SHA-256 of the exact request bytes equals the lock's
    /// <c>requestSha256</c>. Nothing is canonicalized, so a reformatted request is <c>request_changed</c>.</summary>
    let lockBindsRequest (lock: Lock) (requestBytes: byte array) =
        if Sha256.ofBytes requestBytes = lock.RequestSha256 then
            Ok()
        else
            refuse BindingProblem.RequestChanged (root.Child "requestSha256")

    /// <summary>Frozen restore, second check: the lock records a closure for the platform. A musl machine never takes a
    /// glibc closure.</summary>
    let lockedClosure (lock: Lock) (platform: Platform) =
        match Lock.closure platform lock with
        | Some closure -> Ok closure
        | None -> refuse (BindingProblem.PlatformNotLocked platform) (root.Child "platforms")

    /// <summary>A recipe's provider bindings name the authenticated repository the checks run against; a provider that
    /// names another is <c>repository_mismatch</c> at <c>providers[i].repositoryId</c>. This is the first row of the
    /// provider binding table, which a manager applies before it reads any descriptor.</summary>
    let recipeProvidersInRepository (recipe: Recipe) (repository: Sha256) =
        result {
            for index, provider in List.indexed recipe.Providers do
                if provider.Release.RepositoryId <> repository then
                    return! wrongRepository repository (((root.Child "providers").At index).Child "repositoryId")
        }

    /// <summary>A receipt binds the exact lock bytes and repeats the closure for its platform. Paths name receipt
    /// members, except <c>platforms</c>, which names the lock's.</summary>
    let receiptBindsLock (receipt: Receipt) (lock: Lock) =
        result {
            if receipt.LockSha256 <> lock.Identity then
                return! refuse BindingProblem.LockChanged (root.Child "lockSha256")

            if receipt.Recipe <> lock.Recipe then
                return! refuse BindingProblem.ReceiptLockMismatch (root.Child "recipe")

            // Bytes the receipt says a repository authenticated came from the lock's repository: the host first,
            // then each provider in index order.
            let authenticatedBy (installation: Installation) =
                match installation.Authentication with
                | Authentication.RepositoryTarget(repositoryId, _) -> Some repositoryId
                | Authentication.RecipeDigest _
                | Authentication.LocalDeveloper
                | Authentication.ExternalOwner -> None

            let authenticationPath (owner: DocumentPath) =
                ((owner.Child "installation").Child "authentication").Child "repositoryId"

            match authenticatedBy receipt.Host.Installation with
            | Some repositoryId when repositoryId <> lock.Recipe.RepositoryId ->
                return! wrongRepository lock.Recipe.RepositoryId (authenticationPath (root.Child "host"))
            | _ -> ()

            for index, provider in List.indexed receipt.Providers do
                match provider.Installation with
                | ProviderInstallation.Installed(installation, _) ->
                    match authenticatedBy installation with
                    | Some repositoryId when repositoryId <> lock.Recipe.RepositoryId ->
                        return! wrongRepository lock.Recipe.RepositoryId (authenticationPath ((root.Child "providers").At index))
                    | _ -> ()
                | ProviderInstallation.PartOfHost -> ()

            let! closure = lockedClosure lock receipt.Platform
            let installed = receipt.Host.Identity

            if
                installed.Implementation <> lock.Host.Implementation
                || installed.Version <> lock.Host.Version
                || installed.Deployment <> lock.Host.Deployment
                || installed.Artifact <> closure.Host
            then
                return! refuse BindingProblem.ReceiptLockMismatch ((root.Child "host").Child "identity")

            let providers = root.Child "providers"

            if List.length receipt.Providers <> List.length closure.Providers then
                return! refuse BindingProblem.ReceiptLockMismatch providers

            for index, (installed, locked) in List.indexed (List.zip receipt.Providers closure.Providers) do
                if installed.Identity <> locked then
                    return! refuse BindingProblem.ReceiptLockMismatch ((providers.At index).Child "identity")
        }

    /// <summary>A lock checked against its recipe's exact bytes and the authenticated repository those bytes were read
    /// from, refused at the first failure. Closures are visited in platform table order: the host artifact, then the
    /// operations, then each provider in index order, its repository before its identity.</summary>
    let lockMatchesRecipe (lock: Lock) (recipe: Recipe) (repository: Sha256) =
        let mismatch path = refuse BindingProblem.LockRecipeMismatch path

        result {
            if recipe.Sha256 <> lock.Recipe.Sha256 then
                return! mismatch ((root.Child "recipe").Child "sha256")

            if lock.Recipe.RepositoryId <> repository then
                return! wrongRepository repository ((root.Child "recipe").Child "repositoryId")

            let offering =
                recipe.Hosts
                |> List.tryFind (fun host ->
                    host.Implementation = lock.Host.Implementation
                    && host.Deployment = lock.Host.Deployment
                    && host.Version = lock.Host.Version)

            match offering with
            | None -> return! mismatch (root.Child "host")
            | Some offering ->
                for platform, closure in lock.Platforms.Items do
                    let at = (root.Child "platforms").Child(Platform.name platform)

                    let artifact = offering.Platforms |> List.tryFind (fst >> (=) platform) |> Option.map snd

                    if artifact <> Some closure.Host then
                        return! mismatch (at.Child "hostTarget")

                    if set (closure.Providers |> List.map _.Operation) <> set recipe.Operations then
                        return! mismatch (at.Child "providers")

                    for index, provider in List.indexed closure.Providers do
                        let providerPath = (at.Child "providers").At index

                        match Locked.repository provider with
                        | Some named when named <> repository ->
                            return! wrongRepository repository (providerPath.Child "repositoryId")
                        | _ -> ()

                        let allowed =
                            [ yield! offering.BuiltIns |> List.map Locked.ofBuiltIn
                              for candidate in recipe.Providers do
                                  if Fit.onPlatform offering platform candidate |> Option.isNone then
                                      yield! Locked.ofProvider platform candidate |> Option.toList ]

                        if not (List.contains provider allowed) then
                            return! mismatch providerPath
        }
