// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.MiddleEnd

open Morphir.Compilation
open Morphir.IR
open Morphir.IR.V4

/// <summary>Where in a command a pass runs.</summary>
[<RequireQualifiedAccess>]
type PassStage =
    /// <summary>Right after a frontend, on the model that it wrote. It does not depend on a target.</summary>
    | AfterFrontend
    /// <summary>Right before the backend of one target, on the model that the backend reads.</summary>
    | BeforeBackend of target: string

    /// <summary>The stage as text: <c>after-frontend</c> or <c>before-backend</c>.</summary>
    member this.WireValue =
        match this with
        | AfterFrontend -> "after-frontend"
        | BeforeBackend _ -> "before-backend"

    /// <summary>For C# code that must branch: one function for each case.</summary>
    member this.Match<'R>(afterFrontend: System.Func<'R>, beforeBackend: System.Func<string, 'R>) : 'R =
        match this with
        | AfterFrontend -> afterFrontend.Invoke()
        | BeforeBackend target -> beforeBackend.Invoke target

    /// <summary>The stage and, for a backend stage, the target: for example <c>before-backend csharp</c>.</summary>
    override this.ToString() =
        match this with
        | AfterFrontend -> this.WireValue
        | BeforeBackend target -> this.WireValue + " " + target

/// <summary>The unit of work of a pass: one module of the own package.</summary>
type ModuleUnit = {
    /// <summary>The package that holds the module.</summary>
    Package: PackageName
    /// <summary>The path of the module in the package.</summary>
    Path: Path
    /// <summary>The module.</summary>
    Definition: ModuleDefinition
}

/// <summary>What a pass gives for a module: the changed module and one record for each node that it replaced.</summary>
type PassResult = {
    /// <summary>The module after the pass.</summary>
    Module: ModuleDefinition
    /// <summary>The applications, in the positions of the module that the pass got.</summary>
    Applications: PassApplication list
}

/// <summary>
/// A pass that changes a model from the current IR to the current IR: validation, transformation or rewriting. It is a
/// registered extension of the type <c>transform</c>. A pass does not write a file, start a process or read the clock;
/// it reads other modules only through the package view.
/// </summary>
type IModelTransform =
    /// <summary>The id of the extension.</summary>
    abstract Id: string
    /// <summary>The name of the extension.</summary>
    abstract Name: string
    /// <summary>A sentence about what the pass does.</summary>
    abstract Description: string
    /// <summary>The stage that the pass claims.</summary>
    abstract Stage: PassStage
    /// <summary>The IR features that the pass removes.</summary>
    abstract Removes: Set<IrFeature>
    /// <summary>Runs the pass on one module. An error diagnostic stops the command before the backend.</summary>
    abstract Run: ModuleUnit * IPackageView -> Checked<PassResult>

/// <summary>A pass made of a rule set and a strategy.</summary>
[<RequireQualifiedAccess>]
module RewritePass =

    /// <summary>A pass that applies the rules to each module with the strategy.</summary>
    let create
        (id: string)
        (name: string)
        (description: string)
        (stage: PassStage)
        (strategy: Strategy)
        (rules: RewriteRule list)
        : IModelTransform =
        { new IModelTransform with
            member _.Id = id
            member _.Name = name
            member _.Description = description
            member _.Stage = stage
            member _.Removes = rules |> List.map (fun rule -> rule.Removes) |> Set.unionMany

            member _.Run(unit, view) =
                RuleEngine.rewriteModule strategy rules view unit.Path unit.Definition
                |> Checked.map (fun (rewritten, applications) -> { Module = rewritten; Applications = applications }) }

/// <summary>What a backend of one target cannot take: the IR features that no pass may leave in the model.</summary>
type BackendProfile = {
    /// <summary>The target id.</summary>
    Target: string
    /// <summary>The features that the backend cannot take.</summary>
    CannotTake: Set<IrFeature>
} with
    /// <summary>For C# callers: a profile of the target and the features.</summary>
    static member Create(target: string, cannotTake: System.Collections.Generic.IEnumerable<IrFeature>) : BackendProfile =
        { Target = target; CannotTake = Set.ofSeq cannotTake }

/// <summary>The diagnostic codes of the pass runner.</summary>
[<RequireQualifiedAccess>]
module PassCodes =

    /// <summary>A feature that the backend cannot take is still in the model after the passes.</summary>
    [<CompiledName("FeatureRemains")>]
    let featureRemains = DiagnosticCode.Create "transform.feature_remains"

    /// <summary>The snapshot of the output model could not be computed, so the application list cannot be pinned.</summary>
    [<CompiledName("SnapshotFailed")>]
    let snapshotFailed = DiagnosticCode.Create "transform.snapshot_failed"

    /// <summary>The model is in an older family and the passes did not run.</summary>
    [<CompiledName("Skipped")>]
    let skipped = DiagnosticCode.Create "transform.skipped"

/// <summary>A read of another module, and the pass that made it.</summary>
type PassLookup =
    { Pass: string; Read: PackageLookup }

    override this.ToString() = $"{this.Pass}: {this.Read}"

/// <summary>What the host reports about the passes: which ones ran, the application list and the reads of other modules.</summary>
type PassReport = {
    /// <summary>The ids of the passes that ran, in order.</summary>
    Passes: string list
    /// <summary>One record for each node that a pass or an upgrade moved or replaced, pinned to the input model of the pipeline.</summary>
    Applications: RewriteApplication list
    /// <summary>The reads of other modules.</summary>
    Lookups: PassLookup list
} with
    override this.ToString() = $"{this.Passes.Length} pass(es), {this.Applications.Length} application(s), {this.Lookups.Length} lookup(s)"

/// <summary>The input of the pipeline that the addresses of the application list are pinned to.</summary>
type PipelineInput = {
    /// <summary>The artifact of the input model.</summary>
    Artifact: ArtifactSelector
    /// <summary>The format of the input model, which can be older than the current IR when an upgrade ran first.</summary>
    Format: FormatTriplet
    /// <summary>The digest of the bytes of the input model.</summary>
    Snapshot: Sha256Digest
    /// <summary>The records of an upgrade that ran before the passes, pinned to the input and to the upgraded model.</summary>
    Upstream: RewriteApplication list
}

/// <summary>The model after the passes, and the report.</summary>
type PassRunOutcome = { Model: CurrentModel; Report: PassReport }

/// <summary>Selects and runs the passes of a stage.</summary>
[<RequireQualifiedAccess>]
module PassRunner =

    /// <summary>
    /// The passes of a stage, in the order given, which is the stable order of registration. For a stage before a backend,
    /// only the passes of that target. A pass of another target or of another stage is not selected.
    /// </summary>
    let select (stage: PassStage) (passes: IModelTransform list) : IModelTransform list =
        passes |> List.filter (fun pass -> pass.Stage = stage)

    let private packageOf (distribution: Distribution) : (PackageName * PackageDefinition) option =
        match distribution with
        | Library(name, _, definition) -> Some(name, definition)
        | Application(name, _, definition, _) -> Some(name, definition)
        | Specs _ -> None

    let private withPackage (distribution: Distribution) (definition: PackageDefinition) : Distribution =
        match distribution with
        | Library(name, dependencies, _) -> Library(name, dependencies, definition)
        | Application(name, dependencies, _, entryPoints) -> Application(name, dependencies, definition, entryPoints)
        | Specs _ -> distribution

    let private positionOf (address: NodeAddress) : NodePosition = { Root = address.Root; Steps = address.Steps }

    let private asPassApplication (record: RewriteApplication) : PassApplication =
        ({ Rule = record.Rule; Before = positionOf record.Before; After = positionOf record.After; Origin = record.Origin }: PassApplication)

    /// <summary>Runs one pass on every module of the package, one module at a time. Other modules show as before the pass.</summary>
    let private runPass
        (pass: IModelTransform)
        (distribution: Distribution)
        : Checked<Distribution * PassApplication list * PassLookup list> =
        match packageOf distribution with
        | None -> Checked.pass (distribution, [], [])
        | Some(packageName, definition) ->
            let step
                (state: Checked<(ModulePath * AccessControlled<ModuleDefinition>) list * PassApplication list * PassLookup list>)
                (key: ModulePath, access: AccessControlled<ModuleDefinition>)
                =
                state
                |> Checked.bind (fun (done', applications, lookups) ->
                    let (ModulePath path) = key
                    let view = RecordingPackageView(definition, path)
                    let unit = { Package = packageName; Path = path; Definition = access.Value }

                    pass.Run(unit, view)
                    |> Checked.map (fun result ->
                        let reads = view.Lookups |> List.map (fun read -> { Pass = pass.Id; Read = read })

                        done' @ [ key, { access with Value = result.Module } ],
                        PassApplications.compose applications result.Applications,
                        lookups @ reads))

            OrderedMap.toList definition.Modules
            |> List.fold step (Checked.pass ([], [], []))
            |> Checked.map (fun (modules, applications, lookups) ->
                let rebuilt =
                    match OrderedMap.ofList modules with
                    | Ok map -> map
                    | Error _ -> definition.Modules

                withPackage distribution { definition with Modules = rebuilt }, applications, lookups)

    /// <summary>
    /// Runs the passes one after the other. An error from a pass stops the run and gives no model. The application list
    /// pins each <c>Before</c> to the input model of the pipeline, and each <c>After</c> to the output model; a node that a
    /// later pass replaces keeps the first origin.
    /// </summary>
    let run (input: PipelineInput) (passes: IModelTransform list) (model: CurrentModel) : Checked<PassRunOutcome> =
        if List.isEmpty passes then
            Checked.pass { Model = model; Report = { Passes = []; Applications = input.Upstream; Lookups = [] } }
        else
            let step
                (state: Checked<Distribution * PassApplication list * PassLookup list>)
                (pass: IModelTransform)
                =
                state
                |> Checked.bind (fun (distribution, applications, lookups) ->
                    runPass pass distribution
                    |> Checked.map (fun (next, found, reads) ->
                        next, PassApplications.compose applications found, lookups @ reads))

            let start = List.map asPassApplication input.Upstream

            passes
            |> List.fold step (Checked.pass (model.Distribution, start, []))
            |> Checked.bind (fun (distribution, applications, lookups) ->
                let outputFormat = FormatTriplet.ofVersion CurrentIr.version

                let pinned =
                    if List.isEmpty applications then
                        Ok []
                    else
                        Snapshot.ofDistribution distribution
                        |> Result.map (fun output ->
                            applications
                            |> List.map (fun (record: PassApplication) ->
                                ({
                                    Rule = record.Rule
                                    Before =
                                        NodeAddress.unchecked
                                            input.Artifact
                                            input.Format
                                            record.Before.Root
                                            record.Before.Steps
                                            (ArtifactRevision.Pinned input.Snapshot)
                                    After =
                                        NodeAddress.unchecked
                                            input.Artifact
                                            outputFormat
                                            record.After.Root
                                            record.After.Steps
                                            (ArtifactRevision.Pinned output)
                                    Origin = record.Origin
                                }: RewriteApplication)))

                match pinned with
                | Error message ->
                    Checked.fail [ Diagnostic.error PassCodes.snapshotFailed $"The output model could not be hashed: {message}" ]
                | Ok records ->
                    Checked.pass
                        {
                            Model = model.WithDistribution distribution
                            Report =
                                { Passes = passes |> List.map (fun pass -> pass.Id)
                                  Applications = records
                                  Lookups = lookups }
                        })

/// <summary>The host check of the order of passes and backends.</summary>
[<RequireQualifiedAccess>]
module FeatureOrder =

    /// <summary>
    /// Checks the model after the passes: each feature that the backend cannot take must be gone. A feature that remains
    /// gives one error that names the feature, the backend and the first module that has it.
    /// </summary>
    [<CompiledName("Check")>]
    let check (profile: BackendProfile) (model: CurrentModel) : Checked<unit> =
        let modules =
            match model.Distribution with
            | Library(_, _, definition)
            | Application(_, _, definition, _) ->
                OrderedMap.toList definition.Modules |> List.map (fun (ModulePath path, access) -> path, access.Value)
            | Specs _ -> []

        let featuresByModule = modules |> List.map (fun (path, definition) -> path, ValueTraversal.featuresOfModule definition)

        let problems =
            [ for feature in profile.CannotTake do
                  match featuresByModule |> List.tryFind (fun (_, features) -> features.Contains feature) with
                  | Some(path, _) ->
                      yield
                          Diagnostic.error
                              PassCodes.featureRemains
                              $"The backend {profile.Target} cannot take the feature {feature}, and no pass before it removed the feature. It is in the module {CanonicalNames.pathToString path}."
                  | None -> () ]

        if List.isEmpty problems then Checked.pass () else Checked.fail problems

/// <summary>
/// The result of <see cref="PassPipeline.Run"/>: the model that the next stage reads, and what the host reports. For C# callers.
/// </summary>
[<Sealed>]
type PassPipelineResult internal (model: Morphir.IR.Model.VersionedModel, report: PassReport option, note: Diagnostic option) =
    /// <summary>The model after the passes. It is the input model when no pass ran.</summary>
    member _.Model = model

    override _.ToString() = "passes of " + model.SourceVersion.Text
    /// <summary>The report of the passes, or null when no pass was selected or the passes were skipped.</summary>
    member _.ReportOrNull: PassReport | null = Option.toObj report
    /// <summary>The <c>info</c> diagnostic that says why the passes did not run, or null.</summary>
    member _.SkippedOrNull: Diagnostic | null = Option.toObj note

/// <summary>Runs the passes of a stage on a versioned model. For C# hosts: arguments are plain sequences, not F# lists.</summary>
[<AbstractClass; Sealed>]
type PassPipeline =

    /// <summary>
    /// Runs the passes of the stage, in the order given. With no pass for the stage the model is returned as it is. When a
    /// pass is selected and the model is not in the current IR, no pass runs and the result has one <c>info</c> diagnostic
    /// that names the family and the format version. An error from a pass gives no result.
    /// </summary>
    /// <param name="stage">The stage.</param>
    /// <param name="transforms">The registered passes, in the order of registration.</param>
    /// <param name="model">The model that the stage reads.</param>
    /// <param name="artifact">The artifact that pins the addresses of the application list.</param>
    /// <param name="inputVersion">The format version of the input model of the pipeline.</param>
    /// <param name="snapshot">The digest of the bytes of the input model of the pipeline.</param>
    /// <param name="upstream">The records of an upgrade that ran before.</param>
    static member Run
        (
            stage: PassStage,
            transforms: System.Collections.Generic.IEnumerable<IModelTransform>,
            model: Morphir.IR.Model.VersionedModel,
            artifact: ArtifactSelector,
            inputVersion: Versioning.FormatVersion,
            snapshot: Sha256Digest,
            upstream: System.Collections.Generic.IEnumerable<RewriteApplication>
        ) : Checked<PassPipelineResult> =
        let selected = PassRunner.select stage (List.ofSeq transforms)

        if List.isEmpty selected then
            Checked.pass (PassPipelineResult(model, None, None))
        else
            match CurrentModel.tryOf model with
            | None ->
                let note = PassPipeline.SkipNote(stage, string model.Family, model.SourceVersion.Text)
                Checked.pass (PassPipelineResult(model, None, Some note))
            | Some current ->
                let input =
                    { Artifact = artifact
                      Format = FormatTriplet.ofVersion inputVersion
                      Snapshot = snapshot
                      Upstream = List.ofSeq upstream }

                PassRunner.run input selected current
                |> Checked.map (fun outcome -> PassPipelineResult(outcome.Model.Model, Some outcome.Report, None))

    /// <summary>The <c>info</c> diagnostic that says why the passes of a stage did not run on a model of an older family.</summary>
    static member SkipNote(stage: PassStage, family: string, version: string) : Diagnostic =
        Diagnostic.make
            DiagnosticSeverity.Info
            PassCodes.skipped
            $"The passes of the stage {stage} did not run: the model is in the {family} family, format version {version}, and the passes read the current IR only."

    /// <summary>The passes that a stage runs, in order.</summary>
    static member Selected
        (
            stage: PassStage,
            transforms: System.Collections.Generic.IEnumerable<IModelTransform>
        ) : System.Collections.Generic.IReadOnlyList<IModelTransform> =
        PassRunner.select stage (List.ofSeq transforms) |> Array.ofList :> _
