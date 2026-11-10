// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

/// A refusal from a check that compares one document with another. The problem fixes the code and the document whose
/// member the path names.
type BindingRefusal =
    { Problem: BindingProblem
      Path: DocumentPath }

    member this.Code = BindingProblem.code this.Problem
    member this.Document = BindingProblem.document this.Code

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

/// The checks that bind documents to each other (toolchain-lock and installation-receipt pages).
module Bindings =

    let private refuse problem (path: DocumentPath) : Result<'T, BindingRefusal> =
        Error { Problem = problem; Path = path }

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

    /// <summary>A receipt binds the exact lock bytes and repeats the closure for its platform. Paths name receipt
    /// members, except <c>platforms</c>, which names the lock's.</summary>
    let receiptBindsLock (receipt: Receipt) (lock: Lock) =
        result {
            if receipt.LockSha256 <> lock.Identity then
                return! refuse BindingProblem.LockChanged (root.Child "lockSha256")

            if receipt.Recipe <> lock.Recipe then
                return! refuse BindingProblem.ReceiptLockMismatch (root.Child "recipe")

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

    /// <summary>A lock checked against its recipe's exact bytes, refused with <c>lock_recipe_mismatch</c> at the
    /// first failure. Closures are visited in platform table order: the host artifact, then the operations, then each
    /// provider in index order.</summary>
    let lockMatchesRecipe (lock: Lock) (recipe: Recipe) =
        let mismatch path = refuse BindingProblem.LockRecipeMismatch path

        result {
            if recipe.Sha256 <> lock.Recipe.Sha256 then
                return! mismatch ((root.Child "recipe").Child "sha256")

            let offering =
                recipe.Hosts
                |> List.tryFind (fun host ->
                    host.Implementation = lock.Host.Implementation
                    && host.Deployment = lock.Host.Deployment
                    && host.Version = lock.Host.Version)

            match offering with
            | None -> return! mismatch (root.Child "host")
            | Some offering ->
                for platform, closure in lock.Platforms do
                    let at = (root.Child "platforms").Child(Platform.name platform)

                    let artifact = offering.Platforms |> List.tryFind (fst >> (=) platform) |> Option.map snd

                    if artifact <> Some closure.Host then
                        return! mismatch (at.Child "hostTarget")

                    if set (closure.Providers |> List.map _.Operation) <> set recipe.Operations then
                        return! mismatch (at.Child "providers")

                    for index, provider in List.indexed closure.Providers do
                        let allowed =
                            [ yield! offering.BuiltIns |> List.map Locked.ofBuiltIn
                              for candidate in recipe.Providers do
                                  if Fit.onPlatform offering platform candidate |> Option.isNone then
                                      yield! Locked.ofProvider platform candidate |> Option.toList ]

                        if not (List.contains provider allowed) then
                            return! mismatch ((at.Child "providers").At index)
        }
