// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.MiddleEnd

open Morphir.Compilation
open Morphir.IR
open Morphir.IR.Model

/// <summary>
/// The current IR and the registered upgrade steps that reach it. A new IR adds one step to this list and moves the
/// current format version; nothing else names a format version.
/// </summary>
[<RequireQualifiedAccess>]
module CurrentIr =

    /// <summary>The current format version: IR version 4.</summary>
    [<CompiledName("Version")>]
    let version: Versioning.FormatVersion = Versioning.Classic 4

    /// <summary>The registered upgrade steps, oldest first.</summary>
    [<CompiledName("Steps")>]
    let steps: IIrUpgrade list = [ ClassicToCurrent.step ]

    /// <summary>True when the model is a current-IR model.</summary>
    [<CompiledName("IsCurrent")>]
    let isCurrent (model: VersionedModel) : bool =
        model.Family = ModelFamily.V4 && FormatVersion.sameVersion model.SourceVersion.Version version

    /// <summary>Upgrades a model to the current IR with the registered steps.</summary>
    [<CompiledName("Upgrade")>]
    let upgrade (input: UpgradeInput) : Checked<UpgradeOutcome> = UpgradeChain.upgrade steps version input

    /// <summary>The descriptor of a model that the upgrade of a classic library would give: family, version and kind.</summary>
    [<CompiledName("ReachableVersion")>]
    let reachableVersion: SourceFormatVersion = SourceFormatVersion.Create(Versioning.version version)
