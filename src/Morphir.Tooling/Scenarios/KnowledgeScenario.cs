// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using Morphir.Tooling.Features.Knowledge;

namespace Morphir.Tooling.Scenarios;

/// <summary>Knowledge bundle commands shared by the .NET hosts.</summary>
public sealed class KnowledgeScenario : IMorphirCliScenario
{
    public void Configure(MorphirCliBuilder builder)
    {
        var knowledge = new Command("knowledge", "Work with Open Knowledge Format bundles");
        knowledge.Subcommands.Add(new KnowledgeValidateCommand());
        builder.AddCommand(knowledge);
    }
}
