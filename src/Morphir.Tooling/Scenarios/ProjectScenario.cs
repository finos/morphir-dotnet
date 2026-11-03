// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Tooling.Features.ProjectMigration;

namespace Morphir.Tooling.Scenarios;

/// <summary>Project command registration performs no discovery or migration.</summary>
public sealed class ProjectScenario : IMorphirCliScenario
{
    public void Configure(MorphirCliBuilder builder) => builder.AddCommandProvider<ProjectCommandProvider>();
}
