// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Configuration;
using Morphir.Configuration.IO;
using Morphir.Tooling.Features.Configuration;

namespace Morphir.Tooling.Features.ProjectMigration;

public sealed record ProjectMigrationInvocation(string? Path, string? Output, string? Boundary, bool DryRun);

public interface IProjectMigrationHost
{
    ProjectMigrationReport Execute(ProjectMigrationInvocation invocation);
}

/// <summary>Explicit native host composition; the reusable libraries own all migration policy.</summary>
public sealed class NativeProjectMigrationHost : IProjectMigrationHost
{
    public ProjectMigrationReport Execute(ProjectMigrationInvocation invocation)
    {
        var start = ProjectPath.normalize(Environment.CurrentDirectory);
        var boundary = ProjectPath.normalize(System.IO.Path.GetFullPath(invocation.Boundary ?? System.IO.Path.GetPathRoot(start)!));
        var service = new ProjectMigrationService(new FileSystemProjectTree(boundary), new NativeConfigurationTransactionHost());
        var preview = service.Plan(start, invocation.Path, invocation.Output);
        if (!preview.CanApply) return ProjectMigrationReport.From(preview, "blocked", invocation.DryRun, 2);
        if (invocation.DryRun) return ProjectMigrationReport.From(preview, "preview", true);
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
        return ProjectMigrationReport.From(preview, outcome, false, success ? 0 : 2, success ? null : applied.Code,
            applied.Artifacts.Select(artifact => new ConfigurationEditArtifactDto(artifact.Path, artifact.Kind.ToString(), artifact.TransactionId)).ToArray());
    }
}
