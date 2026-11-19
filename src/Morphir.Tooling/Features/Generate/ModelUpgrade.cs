// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Security.Cryptography;
using Morphir.Compilation;
using Morphir.IR.Model;
using Morphir.MiddleEnd;

namespace Morphir.Tooling.Features.Generate;

/// <summary>
/// Brings a loaded classic model to the current IR before a backend that reads the current IR only. A backend that reads
/// the classic model directly, such as the F# backend, gets the model as it was loaded.
/// </summary>
internal static class ModelUpgrade
{
    /// <summary>The digest that pins the addresses of the input: the SHA-256 of its bytes, in order of path for a tree.</summary>
    internal static Sha256Digest SnapshotOf(RetainedInput input)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        switch (input)
        {
            case RetainedInput.File file:
                hash.AppendData(file.Entry.CopyBytes());
                break;
            case RetainedInput.Tree tree:
                foreach (var entry in tree.Entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal))
                {
                    hash.AppendData(System.Text.Encoding.UTF8.GetBytes(entry.RelativePath));
                    hash.AppendData([0]);
                    hash.AppendData(entry.CopyBytes());
                }

                break;
        }

        return Sha256Digest.OfBytes(hash.GetHashAndReset());
    }

    /// <summary>The descriptor that the upgrade of this model would give. No bytes are read.</summary>
    internal static ModelDescriptor Reachable(ModelDescriptor model) =>
        new(CurrentIr.ReachableVersion, ModelFamily.V4, model.Kind) { RequiredSemantics = model.RequiredSemantics };

    /// <summary>Upgrades a loaded model to the current IR, or says why it cannot.</summary>
    internal static Outcome<LoadedModel> Upgrade(LoadedModel loaded) => Upgrade(loaded, CurrentIr.Upgrade);

    /// <summary>Upgrades a loaded model with <paramref name="upgrade"/>, or says why it cannot.</summary>
    internal static Outcome<LoadedModel> Upgrade(LoadedModel loaded, Func<UpgradeInput, Checked<UpgradeOutcome>> upgrade)
    {
        var snapshot = SnapshotOf(loaded.Original);
        var input = new UpgradeInput(loaded.Model, ArtifactSelector.NewWorkspace("input"), snapshot);
        var result = upgrade(input);
        if (!result.TryGetValue(out var outcome))
        {
            // A step names the node that it cannot express in the data of its diagnostic; the path is the input file.
            var diagnostics = result.Diagnostics
                .Select(diagnostic => Transform.ModelPasses.Map(diagnostic, loaded.Original.SourcePath, "ir.upgrade_failed"))
                .ToImmutableArray();
            return new Outcome<LoadedModel>.Failure(new GenerationFailure(GenerationFailureKind.Semantic, diagnostics));
        }

        return new Outcome<LoadedModel>.Success(loaded with
        {
            Model = outcome.Model,
            Descriptor = ModelDescriptor.From(outcome.Model) with { RequiredSemantics = loaded.Descriptor.RequiredSemantics },
            Lineage = new Transform.ModelLineage(input.Artifact, loaded.Model.SourceVersion.Version, snapshot, [.. outcome.Applications]),
        });
    }
}
