// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.Steps;

/// <summary>The state that the step classes of one scenario share: the model, and the result of the last command.</summary>
public sealed class ScenarioRun
{
    private CommandResult? _result;
    private WorkspaceFile? _model;

    /// <summary>The result of the last command of the scenario.</summary>
    public CommandResult Result
    {
        get => _result ?? throw new InvalidOperationException("The scenario ran no command before this step.");
        set => _result = value;
    }

    /// <summary>The model file of the scenario.</summary>
    public WorkspaceFile Model
    {
        get => _model ?? throw new InvalidOperationException("The scenario has no model before this step.");
        set => _model = value;
    }
}
