// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.MiddleEnd

open Morphir.IR
open Morphir.IR.Model

/// <summary>
/// A model that is in the current IR: the family V4 and the current format version. A pass takes this type, so no pass
/// has to check the version of its input. Only <see cref="CurrentModel.tryOf"/> and <see cref="CurrentModel.ofDistribution"/>
/// build one.
/// </summary>
[<CustomEquality; NoComparison>]
type CurrentModel =
    private
        {
            Versioned: VersionedModel
            Current: V4.Distribution
        }

    /// <summary>The model with its declared version, as the readers and writers of models take it.</summary>
    member this.Model: VersionedModel = this.Versioned

    /// <summary>The current-IR distribution.</summary>
    member this.Distribution: V4.Distribution = this.Current

    /// <summary>The same model with another distribution of the current IR. The declared version stays.</summary>
    member this.WithDistribution(distribution: V4.Distribution) : CurrentModel =
        {
            Versioned = { this.Versioned with Distribution = DistributionModel.V4 distribution }
            Current = distribution
        }

    override this.Equals(other: obj | null) =
        match other with
        | :? CurrentModel as model -> this.Current = model.Current
        | _ -> false

    override this.GetHashCode() = hash this.Current

    override this.ToString() = "current model, format version " + this.Versioned.SourceVersion.Text

    /// <summary>For C# callers: the current model of a model, or null when the model is not in the current IR.</summary>
    static member OfOrNull(model: VersionedModel) : CurrentModel | null =
        match CurrentModel.Read model with
        | Some value -> value
        | None -> null

    static member internal Read(model: VersionedModel) : CurrentModel option =
        match model.Distribution with
        | DistributionModel.V4 distribution when CurrentIr.isCurrent model ->
            Some { Versioned = model; Current = distribution }
        | DistributionModel.V4 _
        | DistributionModel.Classic _ -> None

/// <summary>F# functions for <see cref="CurrentModel"/>.</summary>
[<RequireQualifiedAccess>]
module CurrentModel =

    /// <summary>The current model of a model, or none when the model is not in the current IR.</summary>
    let tryOf (model: VersionedModel) : CurrentModel option = CurrentModel.Read model

    /// <summary>A current model of a distribution, with the current format version as its declared version.</summary>
    let ofDistribution (distribution: V4.Distribution) : CurrentModel =
        {
            SourceVersion = CurrentIr.reachableVersion
            Distribution = DistributionModel.V4 distribution
        }
        |> CurrentModel.Read
        |> Option.get
