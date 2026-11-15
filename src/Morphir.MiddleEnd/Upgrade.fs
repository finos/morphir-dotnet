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

    let rec private isPrefix (prefix: NodeStep list) (steps: NodeStep list) =
        match prefix, steps with
        | [], _ -> true
        | head :: prefixTail, step :: stepsTail when head = step -> isPrefix prefixTail stepsTail
        | _ -> false

    /// <summary>True when <paramref name="inner"/> is the place <paramref name="outer"/> or a place below it.</summary>
    let private isWithin ((outerRoot, outerSteps): Position) ((innerRoot, innerSteps): Position) =
        outerRoot = innerRoot && isPrefix outerSteps innerSteps

    /// <summary>
    /// The place in the input of the chain of a place in the model of the chain so far. A place that a record names as its
    /// <c>After</c> has the <c>Before</c> of that record. A place in a node that a record names has the <c>Before</c> of the
    /// deepest such record: the place is not in the input, so the node of the input that it came from is that node. Any
    /// other place is in the input at the same path.
    /// </summary>
    let private originOf (records: RewriteApplication list) (position: Position) : Position =
        match records |> List.tryFind (fun record -> positionOf record.After = position) with
        | Some record -> positionOf record.Before
        | None ->
            records
            |> List.filter (fun record -> isWithin (positionOf record.After) position)
            |> List.sortByDescending (fun record -> List.length record.After.Steps)
            |> List.tryHead
            |> Option.map (fun record -> positionOf record.Before)
            |> Option.defaultValue position

    /// <summary>
    /// Upgrades a model to the current IR. Each step runs on the output of the step before. The application list of the
    /// result names the input of the whole chain in each <c>Before</c>, and a node that a later step replaces keeps its
    /// first origin: the record of the earlier step is replaced by one record from that origin to the last replacement.
    /// A record of an earlier step below a node that a later step replaces is dropped, and each record that stays has its
    /// <c>After</c> pinned to the output of the chain. The chain ends when the model has the current format version.
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

        let rec go (model: VersionedModel) (snapshot: Sha256Digest) (applications: RewriteApplication list) (remaining: int) =
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
                        // A node that this step replaces: its place in the input of the chain, from the records so far.
                        let rewritten =
                            outcome.Applications
                            |> List.map (fun record ->
                                let root, path = originOf applications (positionOf record.Before)

                                { record with
                                    Before =
                                        NodeAddress.unchecked
                                            input.Artifact
                                            inputFormat
                                            root
                                            path
                                            (ArtifactRevision.Pinned input.Snapshot)
                                })

                        // The earlier records for a node that this step replaces, or for a node below it, are superseded.
                        let replaced = outcome.Applications |> List.map (fun record -> positionOf record.Before)
                        let outputFormat = FormatTriplet.ofVersion outcome.Model.SourceVersion.Version

                        // A node that this step does not move keeps its path, and its address is now in the output of this step.
                        let kept =
                            applications
                            |> List.filter (fun record -> not (replaced |> List.exists (fun place -> isWithin place (positionOf record.After))))
                            |> List.map (fun record ->
                                { record with
                                    After =
                                        NodeAddress.unchecked
                                            record.After.Artifact
                                            outputFormat
                                            record.After.Root
                                            record.After.Steps
                                            (ArtifactRevision.Pinned outcome.Snapshot)
                                })

                        go outcome.Model outcome.Snapshot (kept @ rewritten) (remaining - 1))

        go input.Model input.Snapshot [] (List.length steps + 1)
