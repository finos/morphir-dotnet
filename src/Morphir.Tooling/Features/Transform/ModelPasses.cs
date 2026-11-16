// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Text.Json;
using Morphir.Compilation;
using Diagnostic = Morphir.Compilation.Diagnostic;
using Morphir.Extensions;
using Morphir.IR.Model;
using Morphir.MiddleEnd;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Features.Transform;

/// <summary>Where a model came from: the pins for the addresses of the application list, and the records of an upgrade.</summary>
/// <param name="Artifact">The artifact that the addresses name.</param>
/// <param name="InputVersion">The format version of the input model of the pipeline.</param>
/// <param name="Snapshot">The digest of the bytes of the input model.</param>
/// <param name="Applications">The records of an upgrade that ran before the passes.</param>
public sealed record ModelLineage(
    ArtifactSelector Artifact,
    Morphir.IR.Versioning.FormatVersion InputVersion,
    Sha256Digest Snapshot,
    ImmutableArray<RewriteApplication> Applications)
{
    /// <summary>The lineage of a model that no upgrade changed: the model is the input.</summary>
    public static ModelLineage Of(LoadedModel model) =>
        new(ArtifactSelector.NewWorkspace("input"), model.Model.SourceVersion.Version, Generate.ModelUpgrade.SnapshotOf(model.Original), []);
}

/// <summary>A loaded model after the passes of a stage, with what the host reports about them.</summary>
public sealed record PassedModel(LoadedModel Model, PassesReport? Report, ImmutableArray<GenerationDiagnostic> Diagnostics);

/// <summary>A model after the passes of a stage, and the report. The diagnostics are on the checked result.</summary>
public sealed record PassedVersionedModel(VersionedModel Model, PassesReport? Report)
{
    /// <summary>
    /// The lineage for the next stage: the pins of the input of the pipeline and the application list after this stage. Null
    /// when this stage has no record and no pass ran, so the next stage reads its model as the input.
    /// </summary>
    public ModelLineage? Lineage { get; init; }
}

/// <summary>
/// The registered passes of the host and what each backend cannot take. <c>generate</c> runs the passes of the stage before
/// the backend of its target, and <c>compile</c> runs the passes of the stage after a frontend. A pass error stops the run.
/// </summary>
public sealed class ModelPasses
{
    private readonly IReadOnlyList<IModelTransform> transforms;
    private readonly IReadOnlyDictionary<string, ImmutableArray<IrFeature>> cannotTake;

    /// <summary>The passes of this release and the profiles of its built-in backends.</summary>
    public static ModelPasses BuiltIn { get; } = new(BuiltInTransforms.All, BuiltInTransforms.CannotTake);

    /// <summary>No pass and no profile: for a backend that is an extension, which reads the bytes of the model.</summary>
    public static ModelPasses None { get; } = new([], new Dictionary<string, ImmutableArray<IrFeature>>());

    public ModelPasses(IReadOnlyList<IModelTransform> transforms, IReadOnlyDictionary<string, ImmutableArray<IrFeature>> cannotTake)
    {
        this.transforms = transforms;
        this.cannotTake = cannotTake;
    }

    /// <summary>The registered passes, in order of registration.</summary>
    public IReadOnlyList<IModelTransform> Transforms => transforms;

    /// <summary>True when at least one registered pass has the stage.</summary>
    public bool HasPasses(PassStage stage) => PassPipeline.Selected(stage, transforms).Count > 0;

    /// <summary>Runs the passes of the stage before the backend of the target, then checks that no feature remains that the backend cannot take.</summary>
    public Outcome<PassedModel> RunBeforeBackend(LoadedModel model, string target)
    {
        var lineage = model.Lineage ?? ModelLineage.Of(model);
        var result = Run(PassStage.NewBeforeBackend(target), model.Model, lineage, target);
        var path = model.Original.SourcePath;
        if (!result.TryGetValue(out var passed))
            return new Outcome<PassedModel>.Failure(new GenerationFailure(GenerationFailureKind.Semantic, Map(result.Diagnostics, path)));

        var after = ReferenceEquals(passed.Model, model.Model) ? model : model with { Model = passed.Model };
        return new Outcome<PassedModel>.Success(new PassedModel(after, passed.Report, Map(result.Diagnostics, path)));
    }

    /// <summary>
    /// Runs the passes of the stage after a frontend, on the model that the frontend wrote. The model is the input of the
    /// pipeline, so <paramref name="snapshot"/> is the digest of its bytes.
    /// </summary>
    public Checked<PassedVersionedModel> RunAfterFrontend(VersionedModel model, Sha256Digest snapshot) =>
        Run(PassStage.AfterFrontend, model, new ModelLineage(ArtifactSelector.NewWorkspace("input"), model.SourceVersion.Version, snapshot, []), null);

    private Checked<PassedVersionedModel> Run(PassStage stage, VersionedModel model, ModelLineage lineage, string? target)
    {
        var result = PassPipeline.Run(stage, transforms, model, lineage.Artifact, lineage.InputVersion, lineage.Snapshot, lineage.Applications);
        if (!result.TryGetValue(out var passed)) return Checked<PassedVersionedModel>.Fail(result.Diagnostics);

        var notes = result.Diagnostics.ToList();
        if (passed.SkippedOrNull is { } skipped) notes.Add(skipped);

        if (target is not null && cannotTake.TryGetValue(target, out var features) && !features.IsEmpty
            && CurrentModel.OfOrNull(passed.Model) is { } current)
        {
            var profile = BackendProfile.Create(target, features);
            var order = FeatureOrder.Check(profile, current);
            if (!order.TryGetValue(out _)) return Checked<PassedVersionedModel>.Fail(notes.Concat(order.Diagnostics));
        }

        var ran = passed.ReportOrNull;
        var report = ran is not null ? PassesReport.From(ran) : null;
        var next = ran is not null ? lineage with { Applications = [.. ran.Applications] } : null;
        return Checked<PassedVersionedModel>.Of(new PassedVersionedModel(passed.Model, report) { Lineage = next }, notes);
    }

    private static ImmutableArray<GenerationDiagnostic> Map(IEnumerable<Diagnostic> diagnostics, string? path) =>
        [.. diagnostics.Select(diagnostic => Map(diagnostic, path))];

    /// <summary>
    /// The generation diagnostic of a middle-end diagnostic: the code, the message and the severity. A diagnostic that carries
    /// a model location in its data (<c>location</c>) gives that location, and the path is the input file. A diagnostic with
    /// no code gets <paramref name="fallbackCode"/>.
    /// </summary>
    internal static GenerationDiagnostic Map(Diagnostic diagnostic, string? path, string fallbackCode = "transform.diagnostic")
    {
        string? location = diagnostic.Data is { ValueKind: JsonValueKind.Object } data
            && data.TryGetProperty("location", out var text) && text.ValueKind == JsonValueKind.String
                ? text.GetString()
                : null;
        return new(diagnostic.CodeOrNull?.Value ?? fallbackCode, diagnostic.Message, path, location, diagnostic.Severity.WireValue);
    }
}
