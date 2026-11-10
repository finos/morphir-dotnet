// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

/// The download and installed sizes of one artifact. The recipe does not carry them; authenticated target metadata
/// gives the download length.
type ArtifactSize =
    { DownloadBytes: uint64
      InstalledBytes: uint64 }

/// Why no plan could be made.
[<RequireQualifiedAccess>]
type PlanFailure =
    | Unresolved of failure: ResolutionFailure
    /// A downloaded artifact whose sizes the caller did not supply.
    | SizeUnknown of target: TargetPath

/// What plan-only setup is given. Every input is already authenticated: authenticating the recipe is the caller's.
type PlanRequest =
    { Recipe: Recipe
      RecipeRepository: Sha256
      RecipeTarget: TargetPath
      Request: Request
      /// The platform being set up; the plan installs for it.
      Platform: Platform
      Sizes: TargetPath -> ArtifactSize option
      MetadataRefresh: MetadataRefresh }

/// <summary>
/// Setup plans (manager-result page, "Plans"). Planning is pure: it reads nothing, downloads nothing, runs no probe
/// and changes no selection. It resolves every platform the lock would record, so a plan never exists for a lock that
/// setup could not write.
/// </summary>
module Planning =

    let private plannedProvider (sizes: TargetPath -> ArtifactSize option) (selected: Selected) =
        let sized (target: TargetPath) =
            sizes target |> Option.map Ok |> Option.defaultValue (Error(PlanFailure.SizeUnknown target))

        let provider (provider: RecipeProvider) delivery (bound: TargetBinding) probe =
            sized bound.Target
            |> Result.map (fun size ->
                PlannedComponent.Provider(
                    provider.Operation,
                    provider.ExtensionId,
                    delivery,
                    { Version = provider.Version
                      Source = ComponentSource.RecipeDigest(provider.Release.RepositoryId, bound.Target, bound.Sha256)
                      DownloadBytes = size.DownloadBytes
                      InstalledBytes = size.InstalledBytes
                      UpdateOwner = Ownership.ManagerOwned
                      Prerequisites = provider.Prerequisites
                      Probe = probe }
                ))

        fun (platform: Platform) ->
            match selected with
            | Selected.BuiltIn builtIn ->
                Ok(
                    PlannedComponent.Provider(
                        builtIn.Operation,
                        builtIn.ExtensionId,
                        Delivery.BuiltIn,
                        { Version = builtIn.Version
                          Source = ComponentSource.BuiltIn
                          DownloadBytes = 0UL
                          InstalledBytes = 0UL
                          UpdateOwner = Ownership.ManagerOwned
                          Prerequisites = []
                          Probe = Probe.InHost }
                    )
                )
            | Selected.Provider recipeProvider ->
                match recipeProvider.Delivery with
                | RecipeDelivery.Process platforms ->
                    // Resolution selected this provider only because it has an artifact for the platform.
                    let artifact = platforms |> List.find (fst >> (=) platform) |> snd
                    provider recipeProvider Delivery.Process artifact Probe.ProcessWithUserRights
                | RecipeDelivery.Wasm(_, artifact) -> provider recipeProvider Delivery.Wasm artifact Probe.InHost
                | RecipeDelivery.DotnetTool package ->
                    provider recipeProvider Delivery.DotnetTool { Target = package.Target; Sha256 = package.Sha256 } Probe.ProcessWithUserRights

    /// The plan for one platform and the lock setup would write, which records every platform of the request and the
    /// platform being set up.
    let plan (input: PlanRequest) : Result<DigestedPlan * Lock, PlanFailure> =
        result {
            let platforms = Resolution.lockPlatforms input.Request input.Platform

            let! lock, resolved =
                Resolution.lock input.Recipe input.RecipeRepository input.RecipeTarget input.Request platforms
                |> Result.mapError PlanFailure.Unresolved

            let current = resolved |> List.find (fun r -> r.Platform = input.Platform)
            let offering = current.Offering
            let! hostSize =
                input.Sizes current.Closure.Host.Target
                |> Option.map Ok
                |> Option.defaultValue (Error(PlanFailure.SizeUnknown current.Closure.Host.Target))

            let host =
                PlannedComponent.Host(
                    offering.Implementation,
                    offering.Deployment,
                    { Version = offering.Version
                      Source =
                        ComponentSource.RepositoryTarget(
                            lock.Recipe.RepositoryId,
                            current.Closure.Host.Target,
                            current.Closure.Host.Sha256
                        )
                      DownloadBytes = hostSize.DownloadBytes
                      InstalledBytes = hostSize.InstalledBytes
                      UpdateOwner = Ownership.ManagerOwned
                      Prerequisites = offering.Prerequisites
                      Probe = Probe.None }
                )

            let! providers =
                current.Selected
                |> Result.traverse (fun selected -> plannedProvider input.Sizes selected input.Platform)

            let plan =
                { Implementation = offering.Implementation
                  Deployment = offering.Deployment
                  Platform = input.Platform
                  Recipe = lock.Recipe
                  MetadataRefresh = input.MetadataRefresh
                  Components = host :: providers }

            return ResultDocument.digest plan, lock
        }
