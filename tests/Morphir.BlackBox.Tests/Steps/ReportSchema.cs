// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Json.Schema;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>The check of a JSON report against its published schema, a fixture of <c>Fixtures/Schemas</c>.</summary>
public static class ReportSchema
{
    /// <summary>The report matches the schema <paramref name="schema"/>, for example <c>config-edit-result</c>.</summary>
    public static void ShouldMatchSchema(this JsonCommandReport report, IScenarioWorkspace workspace, string schema)
    {
        // A registry of its own: scenarios run in parallel and must not change the global registry of the library.
        var definition = JsonSchema.FromText(
            workspace.ReadFixtureText($"Schemas/{schema}.schema.json"), new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        report.Check(definition.Evaluate(report.Document).IsValid, $"The report does not match its published schema {schema}.");
    }
}
