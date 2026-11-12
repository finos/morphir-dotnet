// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Nodes;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>The steps that put the model of a scenario into its workspace, and that change it.</summary>
[Binding]
public sealed class ModelSteps(IScenarioWorkspace workspace, ScenarioRun run)
{
    [Given("the model fixture {string}")]
    public void TheModelFixture(string fixture) => run.Model = workspace.CopyFixture(fixture);

    [Given("the model tree fixture {string}")]
    public void TheModelTreeFixture(string fixture) => run.ModelTree = workspace.CopyFixtureDirectory(fixture);

    [Given("no model file {string}")]
    public void NoModelFile(string name) => run.Model = workspace.File(name);

    [When("I replace {string} with {string} in the model")]
    public void ReplaceInTheModel(string text, string replacement)
    {
        var model = workspace.ReadText(run.Model);
        if (!model.Contains(text, StringComparison.Ordinal))
            throw new InvalidOperationException($"The model {run.Model.Name} does not contain '{text}'.");
        workspace.WriteText(run.Model, model.Replace(text, replacement, StringComparison.Ordinal));
    }

    /// <summary>Removes one module of a classic (format version 1 to 3) library model: <c>distribution[3].modules</c>.</summary>
    [When("I remove the module {string} from the model")]
    public void RemoveTheModuleFromTheModel(string module)
    {
        var model = JsonNode.Parse(workspace.ReadText(run.Model))!;
        var modules = model["distribution"]![3]!["modules"]!.AsArray();
        var matches = modules.Where(entry => entry![0]!.ToJsonString().Contains(module, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"The model {run.Model.Name} has {matches.Length} modules with '{module}' in the name, not one.");
        modules.Remove(matches[0]);
        workspace.WriteText(run.Model, model.ToJsonString());
    }
}
