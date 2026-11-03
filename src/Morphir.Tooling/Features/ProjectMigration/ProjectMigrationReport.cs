// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.FSharp.Collections;
using Morphir.Configuration;
using Morphir.Configuration.IO;
using Morphir.Tooling.Features.Configuration;

namespace Morphir.Tooling.Features.ProjectMigration;

public sealed record MigrationFieldDto(string RawPath, string? CanonicalPath, JsonElement Value, string Disposition);
public sealed record MigrationConflictDto(string Key, JsonElement SourceValue, JsonElement TargetValue, ConfigurationSourceDto Source, ConfigurationSourceDto Target);
public sealed record MigrationWorkspaceDto(string Root, string? OutputDirectory);
public sealed record MigrationEditDto(string Key, JsonElement Value);
public sealed record MigrationDifferenceDto(string Key, JsonElement? Before, JsonElement? After, string? BeforePath, string? AfterPath);
public sealed record MigrationOriginChangeDto(string Key, ConfigurationOriginDto? Before, ConfigurationOriginDto? After);
public sealed record MigrationVerificationDto(MigrationWorkspaceDto? BaseWorkspace, bool Verified, JsonElement Before, JsonElement After,
    MigrationDifferenceDto[] Differences, MigrationOriginChangeDto[] OriginChanges);

/// <summary>Redacted presentation of a shared migration plan. No canonical candidate bytes are serialized.</summary>
public sealed record ProjectMigrationReport(
    string Operation, string Outcome, string? ProjectPath, ConfigurationSourceDto? Source, ConfigurationSourceDto? Target,
    bool Changed, bool DryRun, MigrationFieldDto[] Ledger, MigrationEditDto[] Edits, MigrationConflictDto[] Conflicts, MigrationVerificationDto? Verification,
    ConfigurationDiagnosticDto[] Diagnostics, ConfigurationEditArtifactDto[] RecoveryArtifacts, int ExitCode, ConfigurationValueTypeDto[] ValueTypes)
{
    public string ToJson() => JsonSerializer.Serialize(this, ProjectMigrationJsonContext.Default.ProjectMigrationReport);

    internal static ProjectMigrationReport From(ProjectMigrationPreview preview, string outcome, bool dryRun,
        int exitCode = 0, string? errorCode = null, ConfigurationEditArtifactDto[]? artifacts = null)
    {
        var types = new List<ConfigurationValueTypeDto>();
        var plan = preview.Presentation;
        var ledger = plan?.Ledger.Select((field, index) => new MigrationFieldDto(KeyPath.format(field.RawPath),
            field.CanonicalPath is null ? null : KeyPath.format(field.CanonicalPath.Value),
            ConfigurationReport.JsonValue(field.Value, $"/ledger/{index}/value", types), field.Disposition)).ToArray() ?? [];
        var edits = plan?.Edits.Select((edit, index) => new MigrationEditDto(KeyPath.format(edit.KeyPath),
            ConfigurationReport.JsonValue(Editing.redactValue(edit.KeyPath, edit.Value), $"/edits/{index}/value", types))).ToArray() ?? [];
        var conflicts = plan?.Conflicts.Select((conflict, index) => new MigrationConflictDto(KeyPath.format(conflict.KeyPath),
            ConfigurationReport.JsonValue(conflict.SourceValue, $"/conflicts/{index}/sourceValue", types),
            ConfigurationReport.JsonValue(conflict.TargetValue, $"/conflicts/{index}/targetValue", types),
            ConfigurationReport.Source(conflict.Source), ConfigurationReport.Source(conflict.Target))).ToArray() ?? [];
        MigrationVerificationDto? verification = null;
        if (preview.Verification is { } verified)
        {
            var differences = verified.Differences.Select((difference, index) => new MigrationDifferenceDto(KeyPath.format(difference.KeyPath),
                difference.Before is null ? null : ConfigurationReport.JsonValue(difference.Before.Value, $"/verification/differences/{index}/before", types),
                difference.After is null ? null : ConfigurationReport.JsonValue(difference.After.Value, $"/verification/differences/{index}/after", types),
                difference.BeforePath?.Value, difference.AfterPath?.Value)).ToArray();
            var origins = verified.OriginChanges.Select(origin => new MigrationOriginChangeDto(KeyPath.format(origin.KeyPath),
                Origin(origin.KeyPath, origin.Before?.Value), Origin(origin.KeyPath, origin.After?.Value))).ToArray();
            verification = new(verified.BaseWorkspace is null ? null : new(verified.BaseWorkspace.Value.Root, verified.BaseWorkspace.Value.OutputDirectory?.Value), verified.Verified, ConfigurationReport.JsonValue(verified.Before, "/verification/before", types),
                ConfigurationReport.JsonValue(verified.After, "/verification/after", types), differences, origins);
        }
        var diagnostics = preview.Diagnostics.Select(ConfigurationReport.Diagnostic).ToList();
        if (errorCode is not null)
            diagnostics.Add(new(errorCode, "error", "Migration application failed. Inspect the outcome and retained recovery paths.", preview.TargetPath, null, null, null));
        return new("migrate", outcome, preview.ProjectPath,
            plan is not null ? ConfigurationReport.Source(plan.Source) : Identity(preview.SourcePath, "json"),
            plan is not null ? ConfigurationReport.Source(plan.Target) : Identity(preview.TargetPath, "toml"),
            preview.Changed, dryRun, ledger, edits, conflicts, verification, diagnostics.ToArray(), artifacts ?? [], exitCode, types.ToArray());
    }

    private static ConfigurationSourceDto? Identity(string? path, string format) => path is null ? null : new(path, format, "RootPrimary", 0, null);

    private static ConfigurationOriginDto? Origin(FSharpList<string> path, ValueOrigin? origin) => origin is null ? null
        : new(KeyPath.format(path), ConfigurationReport.Source(origin.Source), origin.Position?.Value.Line, origin.Position?.Value.Column);

    internal static ProjectMigrationReport Failure(string code, string message, bool dryRun = false) => new("migrate", "blocked", null, null, null,
        false, dryRun, [], [], [], null, [new(code, "error", message, null, null, null, null)], [], 2, []);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    MaxDepth = global::Morphir.Tooling.Features.Configuration.ConfigurationReport.MaxJsonDepth)]
[JsonSerializable(typeof(ProjectMigrationReport))]
internal partial class ProjectMigrationJsonContext : JsonSerializerContext;
