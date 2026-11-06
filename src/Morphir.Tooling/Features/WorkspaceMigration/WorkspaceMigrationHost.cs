// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Configuration;
using Morphir.Configuration.IO;
using Morphir.Tooling.Features.Configuration;

namespace Morphir.Tooling.Features.WorkspaceMigration;

public sealed record WorkspaceMigrationInvocation(string? Directory, string[] Includes, string[] Excludes, bool DryRun);

public interface IWorkspaceMigrationHost
{
    WorkspaceMigrationReport Execute(WorkspaceMigrationInvocation invocation);
}

/// <summary>The supplied/current directory is the native authorization boundary.</summary>
public sealed class NativeWorkspaceMigrationHost : IWorkspaceMigrationHost
{
    public WorkspaceMigrationReport Execute(WorkspaceMigrationInvocation invocation)
    {
        var root = ProjectPath.normalize(Path.GetFullPath(invocation.Directory ?? Environment.CurrentDirectory));
        var home = ProjectSystem.MorphirHomeDirectory(ProjectSystem.CaptureMachineLocations(), root);
        var service = new WorkspaceMigrationService(new FileSystemProjectTree(root, home), new NativeConfigurationTransactionHost());
        var preview = service.Plan(root, invocation.Includes, invocation.Excludes);
        if (preview.PendingRecovery.Length > 0)
            return WorkspaceMigrationReport.From(preview, "recovery-required", invocation.DryRun, 2,
                artifacts: preview.PendingRecovery.SelectMany(inspection => inspection.Artifacts).Distinct()
                    .OrderBy(artifact => artifact.Path, StringComparer.Ordinal)
                    .Select(artifact => new ConfigurationEditArtifactDto(artifact.Path, artifact.Kind.ToString(), artifact.TransactionId)).ToArray(),
                unresolvedPaths: preview.PendingRecovery.SelectMany(inspection => inspection.UnresolvedPaths.Length > 0
                    ? inspection.UnresolvedPaths : inspection.Targets).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        if (!preview.CanApply) return WorkspaceMigrationReport.From(preview, "blocked", invocation.DryRun, 2);
        if (invocation.DryRun) return WorkspaceMigrationReport.From(preview, "preview", true);
        var applied = service.Apply(preview);
        var outcome = applied.Status switch
        {
            ConfigurationTransactionStatus.Written => "written",
            ConfigurationTransactionStatus.Unchanged => "unchanged",
            ConfigurationTransactionStatus.Conflict => "conflict",
            ConfigurationTransactionStatus.RolledBack => "rolled-back",
            ConfigurationTransactionStatus.RecoveryRequired => "recovery-required",
            _ => "io-failure"
        };
        var success = applied.Status is ConfigurationTransactionStatus.Written or ConfigurationTransactionStatus.Unchanged;
        var inspection = new ConfigurationTransaction();
        var uncertain = applied.Artifacts.Where(artifact => artifact.Kind == ConfigurationArtifactKind.Journal)
            .Select(artifact => inspection.InspectRecovery(artifact.Path)).SelectMany(recovery => recovery.UncertainDirectories)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return WorkspaceMigrationReport.From(preview, outcome, false, success ? 0 : 2, success ? null : applied.Code,
            applied.Artifacts.Select(artifact => new ConfigurationEditArtifactDto(artifact.Path, artifact.Kind.ToString(), artifact.TransactionId)).ToArray(),
            applied.RestoredPaths.ToArray(), applied.UnresolvedPaths.ToArray(), uncertain, applied.PreservedDirectories.ToArray());
    }
}
