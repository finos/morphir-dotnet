// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.Steps;

/// <summary>The result of the same command on the other host, for a scenario that compares two hosts.</summary>
public sealed class OtherHostRun
{
    private CommandResult? _result;

    public CommandResult Result
    {
        get => _result ?? throw new InvalidOperationException("The scenario ran no command on the other host before this step.");
        set => _result = value;
    }
}
