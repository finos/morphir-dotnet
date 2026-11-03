// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.Json.Serialization;
using Morphir.Configuration;

namespace Morphir.Tooling.Features.Configuration;

public sealed record ConfigurationEditArtifactDto(string Path, string Kind, string TransactionId);
public sealed record ConfigurationEditReport(
    string Operation, string Outcome, ConfigurationSourceDto? Target, string? Key,
    JsonElement? Before, JsonElement? After, bool Changed, bool DryRun,
    ConfigurationReport? Effective, ConfigurationDiagnosticDto[] Diagnostics,
    ConfigurationEditArtifactDto[] RecoveryArtifacts, int ExitCode, ConfigurationValueTypeDto[] ValueTypes)
{
    public string ToJson() => JsonSerializer.Serialize(this, ConfigurationEditJsonContext.Default.ConfigurationEditReport);

    internal static ConfigurationEditReport From(EditPlan plan, string outcome, bool dryRun, int exitCode = 0,
        string? errorCode = null, ConfigurationEditArtifactDto[]? artifacts = null)
    {
        var types = new List<ConfigurationValueTypeDto>();
        var diagnostics = plan.Effective.Diagnostics.Select(ConfigurationReport.Diagnostic).ToList();
        if (errorCode is not null)
            diagnostics.Add(new(errorCode, "error", "Configuration application failed. Inspect the outcome and retained recovery paths.", plan.Target.Path, KeyPath.format(plan.Key), null, null));
        return new(plan.Operation, outcome, ConfigurationReport.Source(plan.Target), KeyPath.format(plan.Key),
            plan.Before is null ? null : ConfigurationReport.JsonValue(Editing.redactValue(plan.Key, plan.Before.Value), "/before", types),
            plan.After is null ? null : ConfigurationReport.JsonValue(Editing.redactValue(plan.Key, plan.After.Value), "/after", types),
            plan.Changed, dryRun, ConfigurationReport.From(plan.Effective, true, true), diagnostics.ToArray(), artifacts ?? [], exitCode, types.ToArray());
    }

    internal static ConfigurationEditReport Failure(string operation, string code, int exitCode = 2,
        IEnumerable<ConfigDiagnostic>? diagnostics = null) => new(operation, "failed", null, null, null, null, false, false, null,
        (diagnostics ?? []).Select(ConfigurationReport.Diagnostic)
            .Append(new ConfigurationDiagnosticDto(code, "error", "Configuration edit failed. Check the code, selectors and input type.", null, null, null, null)).ToArray(), [], exitCode, []);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = global::Morphir.Tooling.Features.Configuration.ConfigurationReport.MaxJsonDepth)]
[JsonSerializable(typeof(ConfigurationEditReport))]
internal partial class ConfigurationEditJsonContext : JsonSerializerContext;
