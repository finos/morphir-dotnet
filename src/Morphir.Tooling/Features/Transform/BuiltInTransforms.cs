// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using Morphir.Extensions;
using Morphir.MiddleEnd;

namespace Morphir.Tooling.Features.Transform;

/// <summary>
/// The passes that this release has built in. A host registers one <c>transform</c> extension entry for each pass that a
/// command runs, from this list, so the entries cannot differ from the passes.
/// </summary>
public static class BuiltInTransforms
{
    /// <summary>The built-in passes, in the stable order that a stage runs them.</summary>
    public static IReadOnlyList<IModelTransform> All { get; } = [.. Morphir.Lang.CSharp.CSharpTargetPasses.All];

    /// <summary>The IR features that the built-in backend of a target cannot take, by target id.</summary>
    public static IReadOnlyDictionary<string, ImmutableArray<IrFeature>> CannotTake { get; } =
        new Dictionary<string, ImmutableArray<IrFeature>>();
}

/// <summary>The registry entries of the built-in passes: one <c>transform</c> extension for each, with its stage.</summary>
public sealed class TransformExtensionSource : IExtensionSource
{
    private readonly IReadOnlyList<IModelTransform> transforms;

    public TransformExtensionSource(HostIdentity host) : this(host, BuiltInTransforms.All) { }

    public TransformExtensionSource(HostIdentity host, IReadOnlyList<IModelTransform> transforms)
    {
        this.transforms = transforms;
        Entries = [.. transforms.Select(transform => Entry(host.Version, transform))];
    }

    public IReadOnlyList<ExtensionEntry> Entries { get; }

    private static ExtensionEntry Entry(ReleaseVersion version, IModelTransform transform) =>
        ExtensionEntry.BuiltInTransform(
            ExtensionIdentity.Create(ExtensionId.Create(transform.Id), ExtensionName.Create(transform.Name), version,
                ExtensionDescription.Create(transform.Description)),
            TransformClaim.Create(Stage(transform.Stage)));

    /// <summary>The registry stage of a pass stage.</summary>
    public static TransformStage Stage(PassStage stage) =>
        stage.Match(() => TransformStage.AfterFrontend, target => TransformStage.NewBeforeBackend(TargetId.Create(target)));
}
