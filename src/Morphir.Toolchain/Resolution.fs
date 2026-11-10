// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

/// Why a closure could not be resolved: the stable code and the request member it concerns, with what a diagnostic
/// reports about it.
type ResolutionFailure =
    { Code: ArtifactCode
      /// A member of the request, such as <c>toolset.providers[0]</c>.
      Path: DocumentPath
      Platform: Platform
      Component: Component
      /// The ABI the host lacks, exactly for <c>abi_unavailable</c>.
      Abi: Token option
      /// Offerings the recipe does have, for host failures.
      Alternatives: Alternative list }

/// The provider resolution chose for an operation.
[<RequireQualifiedAccess>]
type Selected =
    | BuiltIn of builtIn: BuiltIn
    | Provider of provider: RecipeProvider

/// A resolved closure with the recipe entries it was taken from.
type Resolved =
    { Offering: HostOffering
      Platform: Platform
      Closure: Closure
      Selected: Selected list }

/// <summary>
/// Selecting a closure (toolset recipe page): a pure function of a recipe, a request and a platform. It substitutes no
/// implementation, deployment, release, platform or ABI, and never skips a provider that does not fit in favor of
/// another.
/// </summary>
module Resolution =

    let private root = DocumentPath.root
    let private toolset = root.Child "toolset"

    let private alternativesFor (recipe: Recipe) (request: Request) (platform: Platform) =
        [ for offering in recipe.Hosts do
              for candidate, _ in offering.Platforms do
                  let sameOffering =
                      offering.Implementation = request.Host.Implementation
                      && offering.Deployment = request.Host.Deployment

                  if (sameOffering && candidate <> platform) || (not sameOffering && candidate = platform) then
                      { Implementation = Some offering.Implementation
                        Deployment = Some offering.Deployment
                        Platform = Some candidate } ]
        |> List.sortBy (fun alternative -> alternative.Platform |> Option.map Platform.order)

    /// Resolves the closure for one platform.
    let resolve (recipe: Recipe) (request: Request) (platform: Platform) : Result<Resolved, ResolutionFailure> =
        let hostComponent = Component.Host(request.Host.Implementation, request.Host.Deployment)

        let fail code path on abi alternatives =
            Error
                { Code = code
                  Path = path
                  Platform = platform
                  Component = on
                  Abi = abi
                  Alternatives = alternatives }

        let hostFailure path =
            fail ArtifactCode.UnsupportedArtifact path hostComponent None (alternativesFor recipe request platform)

        result {
            if recipe.Preset <> request.Toolset.Preset then
                return! fail ArtifactCode.UnsupportedArtifact (toolset.Child "preset") hostComponent None []

            let! offering =
                recipe.Hosts
                |> List.tryFind (fun host ->
                    host.Implementation = request.Host.Implementation && host.Deployment = request.Host.Deployment)
                |> Option.map Ok
                |> Option.defaultWith (fun () -> hostFailure (root.Child "host"))

            match request.Host.Release with
            | HostRelease.Exact version when version <> offering.Version ->
                return! fail ArtifactCode.UnsupportedArtifact ((root.Child "host").Child "version") hostComponent None []
            | _ -> ()

            let! hostArtifact =
                offering.Platforms
                |> List.tryFind (fst >> (=) platform)
                |> Option.map (snd >> Ok)
                |> Option.defaultWith (fun () -> hostFailure (root.Child "platforms"))

            let choices = request.Toolset.Providers |> List.indexed

            let misfit (path: DocumentPath) (operation: Operation) (provider: RecipeProvider) =
                match Fit.onPlatform offering platform provider with
                | None -> Ok(Selected.Provider provider)
                | Some(Misfit.AbiUnavailable abi) ->
                    fail ArtifactCode.AbiUnavailable path (Component.Provider operation) (Some abi) []
                | Some _ -> fail ArtifactCode.UnsupportedArtifact path (Component.Provider operation) None []

            let select (operation: Operation) : Result<Selected, ResolutionFailure> =
                let builtIn = offering.BuiltIns |> List.tryFind (fun b -> b.Operation = operation)

                let candidates =
                    recipe.Providers |> List.filter (fun provider -> provider.Operation = operation)

                match choices |> List.tryFind (fun (_, choice) -> choice.Operation = operation) with
                | Some(index, choice) ->
                    let path = (toolset.Child "providers").At index
                    let chosenDelivery = ProviderChoice.delivery choice
                    let chosenId = ProviderChoice.extensionId choice
                    let chosenAbi = ProviderChoice.abi choice

                    let builtInAllowed (b: BuiltIn) =
                        (chosenDelivery.IsNone || chosenDelivery = Some Delivery.BuiltIn)
                        && (chosenId.IsNone || chosenId = Some b.ExtensionId)
                        && chosenAbi.IsNone

                    match builtIn with
                    | Some b when builtInAllowed b -> Ok(Selected.BuiltIn b)
                    | _ ->
                        let matches (provider: RecipeProvider) =
                            (chosenDelivery.IsNone || chosenDelivery = Some(RecipeProvider.delivery provider))
                            && (chosenId.IsNone || chosenId = Some provider.ExtensionId)
                            && (chosenAbi.IsNone || chosenAbi = RecipeProvider.abi provider)

                        match candidates |> List.tryFind matches with
                        | Some provider -> misfit path operation provider
                        | None -> fail ArtifactCode.UnsupportedArtifact path (Component.Provider operation) None []
                | None ->
                    match builtIn, candidates with
                    | Some b, _ -> Ok(Selected.BuiltIn b)
                    | None, provider :: _ -> misfit toolset operation provider
                    | None, [] -> fail ArtifactCode.UnsupportedArtifact toolset (Component.Provider operation) None []

            let! selected = recipe.Operations |> Result.traverse select

            // A choice for an operation the recipe does not require matches nothing.
            match choices |> List.tryFind (fun (_, choice) -> not (List.contains choice.Operation recipe.Operations)) with
            | Some(index, choice) ->
                return!
                    fail
                        ArtifactCode.UnsupportedArtifact
                        ((toolset.Child "providers").At index)
                        (Component.Provider choice.Operation)
                        None
                        []
            | None -> ()

            let providers =
                selected
                |> List.choose (function
                    | Selected.BuiltIn b -> Some(Locked.ofBuiltIn b)
                    | Selected.Provider provider -> Locked.ofProvider platform provider)

            return
                { Offering = offering
                  Platform = platform
                  Closure = { Host = hostArtifact; Providers = providers }
                  Selected = selected }
        }

    /// The platforms a lock records for a request on a machine: the request's platforms, extended with the platform
    /// being set up, in platform table order.
    let lockPlatforms (request: Request) (current: Platform) =
        (current :: defaultArg request.Platforms [])
        |> List.distinct
        |> List.sortBy Platform.order

    /// <summary>
    /// Resolves every platform from one recipe and writes the lock. Two closures never come from independent channel
    /// resolutions. The first failure in platform table order is returned.
    /// </summary>
    let lock
        (recipe: Recipe)
        (recipeRepository: Sha256)
        (recipeTarget: TargetPath)
        (request: Request)
        (platforms: Platform list)
        : Result<Lock * Resolved list, ResolutionFailure> =
        if List.isEmpty platforms then
            invalidArg (nameof platforms) "A lock records at least one platform."

        result {
            let! resolved = platforms |> List.distinct |> List.sortBy Platform.order |> Result.traverse (resolve recipe request)
            let offering = (List.head resolved).Offering

            let lock =
                { RequestSha256 = request.Identity
                  Recipe =
                    { RepositoryId = recipeRepository
                      Target = recipeTarget
                      Sha256 = recipe.Sha256 }
                  Host =
                    { Implementation = offering.Implementation
                      Version = offering.Version
                      Deployment = offering.Deployment }
                  Platforms = resolved |> List.map (fun r -> r.Platform, r.Closure)
                  Identity = Sha256.ofBytes [||] }

            return { lock with Identity = Sha256.ofBytes (LockDocument.write lock) }, resolved
        }
