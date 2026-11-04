// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Tooling.Features.WorkspaceMigration;

namespace Morphir.Tooling.Scenarios;

/// <summary>Workspace registration performs no inventory or migration.</summary>
public sealed class WorkspaceScenario : IMorphirCliScenario
{
    public void Configure(MorphirCliBuilder builder) => builder.AddCommandProvider<WorkspaceCommandProvider>();
}
