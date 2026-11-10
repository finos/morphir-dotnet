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

    /// <summary>A prerequisite applies on a platform when it names no platforms or names that one. A plan lists only
    /// the prerequisites of its own platform: they are inside <c>planSha256</c>.</summary>
    let appliesOn (platform: Platform) (prerequisite: Prerequisite) =
        prerequisite.Platforms |> Option.forall (List.contains platform)

    let private on platform prerequisites =
        prerequisites |> List.filter (appliesOn platform)

    /// The component for one selected provider, with the artifact its locked form binds on the platform.
    let private plannedProvider (sizes: TargetPath -> ArtifactSize option) (platform: Platform) (selected: Selected, locked: LockedProvider) =
        let downloaded (provider: RecipeProvider) (delivery: PlannedDelivery) (bound: TargetBinding) =
            match sizes bound.Target with
            | None -> Error(PlanFailure.SizeUnknown bound.Target)
            | Some size ->
                Ok(
                    PlannedComponent.Provider(
                        provider.Operation,
                        provider.ExtensionId,
                        delivery,
                        { Version = provider.Version
                          Source = ComponentSource.RecipeDigest(provider.Release.RepositoryId, bound.Target, bound.Sha256)
                          DownloadBytes = size.DownloadBytes
                          InstalledBytes = size.InstalledBytes
                          UpdateOwner = Ownership.ManagerOwned
                          Prerequisites = on platform provider.Prerequisites }
                    )
                )

        match selected, locked.Delivery with
        | Selected.BuiltIn builtIn, _ ->
            Ok(
                PlannedComponent.Provider(
                    builtIn.Operation,
                    builtIn.ExtensionId,
                    PlannedDelivery.BuiltIn LocalProbe.InHost,
                    { Version = builtIn.Version
                      Source = ComponentSource.BuiltIn
                      DownloadBytes = 0UL
                      InstalledBytes = 0UL
                      UpdateOwner = Ownership.ManagerOwned
                      Prerequisites = [] }
                )
            )
        | Selected.Provider provider, LockedDelivery.Process(_, artifact, _) -> downloaded provider PlannedDelivery.Process artifact
        | Selected.Provider provider, LockedDelivery.Wasm(_, _, artifact, _) ->
            downloaded provider (PlannedDelivery.Wasm LocalProbe.InHost) artifact
        | Selected.Provider provider, LockedDelivery.DotnetTool(_, package, _) ->
            downloaded provider PlannedDelivery.DotnetTool { Target = package.Target; Sha256 = package.Sha256 }
        | Selected.Provider provider, LockedDelivery.BuiltIn _ ->
            failwith $"unreachable: resolution locked the provider {provider.ExtensionId.Text} as a built-in"
    /// The plan for one platform and the lock setup would write, which records every platform of the request and the
    /// platform being set up.
    let plan (input: PlanRequest) : Result<DigestedPlan * Lock, PlanFailure> =
        result {
            let! lock, resolved =
                Resolution.lock input.Recipe input.RecipeRepository input.RecipeTarget input.Request input.Platform
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
                      Prerequisites = on input.Platform offering.Prerequisites },
                    LocalProbe.None
                )

            let! providers =
                List.zip current.Selected current.Closure.Providers
                |> Result.traverse (plannedProvider input.Sizes input.Platform)

            let plan =
                { Implementation = offering.Implementation
                  Deployment = offering.Deployment
                  Platform = input.Platform
                  Recipe = lock.Recipe
                  MetadataRefresh = input.MetadataRefresh
                  Components = host :: providers }

            return ResultDocument.digest plan, lock
        }
