// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.MiddleEnd

open System
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.Model

/// <summary>What an upgrade step reads: the model, and the artifact and the snapshot that pin the addresses of its input.</summary>
type UpgradeInput = {
    Model: VersionedModel
    Artifact: ArtifactSelector
    /// <summary>The digest of the bytes that the model came from.</summary>
    Snapshot: Sha256Digest
}

/// <summary>What an upgrade step gives: the model that it writes, and one record for each node that it moves or replaces.</summary>
type UpgradeOutcome = {
    Model: VersionedModel
    /// <summary>The digest of the bytes of the output model, which pins the addresses in <c>After</c>.</summary>
    Snapshot: Sha256Digest
    Applications: RewriteApplication list
}

/// <summary>
/// One upgrade step, from one format version to the next. No contract of the middle end names a format version: a new
/// IR adds one step and moves the current format.
/// </summary>
type IIrUpgrade =
    /// <summary>The id of the step, an absolute IRI. It is the rule of each record that the step gives.</summary>
    abstract Id: RuleId
    /// <summary>The newest format version that the step reads.</summary>
    abstract From: Versioning.FormatVersion
    /// <summary>The format version that the step writes.</summary>
    abstract To: Versioning.FormatVersion
    /// <summary>True for each format version that the step reads. The classic versions 1 to 3 read as one family.</summary>
    abstract Accepts: Versioning.FormatVersion -> bool
    /// <summary>
    /// Upgrades the model. It returns an error when the model has a node that the next format cannot express. It does not
    /// change its input, read a file or start a process.
    /// </summary>
    abstract Upgrade: UpgradeInput -> Checked<UpgradeOutcome>

/// <summary>The diagnostic codes of the upgrade.</summary>
[<RequireQualifiedAccess>]
module UpgradeCodes =

    /// <summary>No registered chain of steps reaches the current IR from the format version of the model.</summary>
    [<CompiledName("NoPath")>]
    let noPath = DiagnosticCode.Create "ir.upgrade_no_path"

    /// <summary>A model has a node that the next format cannot express.</summary>
    [<CompiledName("Unrepresentable")>]
    let unrepresentable = DiagnosticCode.Create "ir.upgrade_unrepresentable"

/// <summary>Joins upgrade steps from the format version of a model to the current IR.</summary>
[<RequireQualifiedAccess>]
module UpgradeChain =

    /// <summary>The place of a node in a model: the root and the steps of its address, with no artifact, format or revision.</summary>
    type private Position = NodeRoot * NodeStep list

    let private positionOf (address: NodeAddress) : Position = address.Root, address.Steps

    /// <summary>
    /// Upgrades a model to the current IR. Each step runs on the output of the step before. The application list of the
    /// result names the input of the whole chain in each <c>Before</c>, and a node that a later step replaces keeps its
    /// first origin: the record of the earlier step is replaced by one record from that origin to the last replacement.
    /// The chain ends when the model has the current format version.
    /// </summary>
    let upgrade (steps: IIrUpgrade list) (current: Versioning.FormatVersion) (input: UpgradeInput) : Checked<UpgradeOutcome> =
        let isCurrent (model: VersionedModel) =
            model.Family = ModelFamily.V4 && FormatVersion.sameVersion model.SourceVersion.Version current

        let noPath (version: Versioning.FormatVersion) =
            Checked.fail [
                Diagnostic.error
                    UpgradeCodes.noPath
                    $"No upgrade step reads the IR format version {Versioning.version version}, so the model cannot reach the current IR {Versioning.version current}."
            ]

        let inputFormat = FormatTriplet.ofVersion input.Model.SourceVersion.Version

        let rec go
            (model: VersionedModel)
            (snapshot: Sha256Digest)
            (applications: RewriteApplication list)
            (origins: Map<Position, Position>)
            (remaining: int)
            =
            if isCurrent model then
                Checked.pass { Model = model; Snapshot = snapshot; Applications = applications }
            elif remaining = 0 then
                noPath model.SourceVersion.Version
            else
                match steps |> List.tryFind (fun step -> step.Accepts model.SourceVersion.Version) with
                | None -> noPath model.SourceVersion.Version
                | Some step ->
                    step.Upgrade { input with Model = model; Snapshot = snapshot }
                    |> Checked.bind (fun outcome ->
                        // A node that this step replaces: its place in the input of the chain, from the origins so far.
                        let originOf (position: Position) = Map.tryFind position origins |> Option.defaultValue position

                        let rewritten =
                            outcome.Applications
                            |> List.map (fun record ->
                                let root, path = originOf (positionOf record.Before)

                                { record with
                                    Before =
                                        NodeAddress.unchecked
                                            input.Artifact
                                            inputFormat
                                            root
                                            path
                                            (ArtifactRevision.Pinned input.Snapshot)
                                })

                        // The earlier records for a node that this step replaces are superseded by the new record.
                        let replaced = outcome.Applications |> List.map (fun record -> positionOf record.Before) |> Set.ofList

                        let kept =
                            applications |> List.filter (fun record -> not (Set.contains (positionOf record.After) replaced))

                        let newOrigins =
                            List.fold2
                                (fun (state: Map<Position, Position>) (record: RewriteApplication) (rebuilt: RewriteApplication) ->
                                    Map.add (positionOf record.After) (positionOf rebuilt.Before) state)
                                origins
                                outcome.Applications
                                rewritten

                        go outcome.Model outcome.Snapshot (kept @ rewritten) newOrigins (remaining - 1))

        go input.Model input.Snapshot [] Map.empty (List.length steps + 1)
