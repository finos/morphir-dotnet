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

    /// <summary>The model of the scenario that is one file. Setting it makes the model a file.</summary>
    public WorkspaceFile Model
    {
        get => _model ?? throw new InvalidOperationException("The scenario has no model file before this step.");
        set
        {
            _model = value;
            ModelTree = null;
        }
    }

    /// <summary>True when the scenario has a model file.</summary>
    public bool HasModel => _model is not null;

    /// <summary>The model of the scenario that is a tree of files, or null when the model is one file.</summary>
    public WorkspaceDirectory? ModelTree { get; set; }
}
