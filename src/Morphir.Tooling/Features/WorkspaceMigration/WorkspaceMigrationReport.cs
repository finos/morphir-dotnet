// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.Json.Serialization;
using Morphir.Configuration;
using Morphir.Configuration.IO;
using Morphir.Tooling.Features.Configuration;
using Morphir.Tooling.Features.ProjectMigration;

namespace Morphir.Tooling.Features.WorkspaceMigration;

public sealed record WorkspaceInventoryDto(string Path, string RelativePath, string Kind, string? Reason, string? Name);
public sealed record WorkspaceAdoptionEffectDto(string ProjectPath, string Kind, string? Before, string? After);

/// <summary>Safe aggregate presentation. Canonical candidate bytes are never serialized.</summary>
public sealed record WorkspaceMigrationReport(
    string Operation, string Outcome, string? RootPath, bool RootIsProject, bool Changed, bool DryRun, bool Verified,
    WorkspaceInventoryDto[] Inventory, ProjectMigrationReport[] Projects, string[] Members, string? WorkspaceTarget,
    MigrationEditDto[] WorkspaceEdits, WorkspaceAdoptionEffectDto[] AdoptionEffects, ConfigurationDiagnosticDto[] Diagnostics,
    ConfigurationEditArtifactDto[] RecoveryArtifacts, string[] RestoredPaths, string[] UnresolvedPaths,
    string[] UncertainDirectories, string[] PreservedDirectories, int ExitCode,
    ConfigurationValueTypeDto[] ValueTypes)
{
    public string ToJson() => JsonSerializer.Serialize(this, WorkspaceMigrationJsonContext.Default.WorkspaceMigrationReport);

    internal static WorkspaceMigrationReport From(WorkspaceMigrationPreview preview, string outcome, bool dryRun,
        int exitCode = 0, string? errorCode = null, ConfigurationEditArtifactDto[]? artifacts = null,
        string[]? restoredPaths = null, string[]? unresolvedPaths = null,
        string[]? uncertainDirectories = null, string[]? preservedDirectories = null)
    {
        var types = new List<ConfigurationValueTypeDto>();
        var projects = preview.Projects.Select(project => ProjectMigrationReport.From(project,
            !project.CanApply ? "blocked" : !project.Changed ? "unchanged" : outcome == "blocked" ? "preview" : outcome,
            dryRun, project.CanApply ? exitCode : 2)).ToArray();
        var edits = preview.WorkspaceEdits.Select((edit, index) => new MigrationEditDto(KeyPath.format(edit.KeyPath),
            ConfigurationReport.JsonValue(Editing.redactValue(edit.KeyPath, edit.Value), $"/workspaceEdits/{index}/value", types))).ToArray();
        var diagnostics = preview.Diagnostics.Select(ConfigurationReport.Diagnostic).ToList();
        if (errorCode is not null)
            diagnostics.Add(new(errorCode, "error", "Workspace application failed. Inspect the outcome and retained recovery evidence.", preview.WorkspaceTarget, null, null, null));
        return new("workspace-migrate", outcome, preview.RootPath, preview.RootIsProject, preview.Changed, dryRun, preview.Verified,
            preview.Inventory.Select(entry => new WorkspaceInventoryDto(entry.Path, entry.RelativePath, entry.Kind.ToString(), entry.Reason, entry.Name)).ToArray(),
            projects, preview.Members.ToArray(), preview.WorkspaceTarget, edits,
            preview.AdoptionEffects.Select(effect => new WorkspaceAdoptionEffectDto(effect.ProjectPath, effect.Kind, effect.Before, effect.After)).ToArray(),
            diagnostics.ToArray(), artifacts ?? [], restoredPaths ?? [], unresolvedPaths ?? [],
            uncertainDirectories ?? preview.PendingRecovery.SelectMany(inspection => inspection.UncertainDirectories)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), preservedDirectories ?? [], exitCode, types.ToArray());
    }

    internal static WorkspaceMigrationReport Failure(string code, string message, bool dryRun = false) => new("workspace-migrate", "blocked",
        null, false, false, dryRun, false, [], [], [], null, [], [], [new(code, "error", message, null, null, null, null)], [], [], [], [], [], 2, []);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    MaxDepth = global::Morphir.Tooling.Features.Configuration.ConfigurationReport.MaxJsonDepth)]
[JsonSerializable(typeof(WorkspaceMigrationReport))]
internal partial class WorkspaceMigrationJsonContext : JsonSerializerContext;
