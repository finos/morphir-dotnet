// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Tooling.Features.Configuration;

namespace Morphir.Tooling.Scenarios;

/// <summary>Configuration inspection and explicit editing; registration performs no discovery or loading.</summary>
public sealed class ConfigurationScenario : IMorphirCliScenario
{
    public void Configure(MorphirCliBuilder builder) => builder.AddCommandProvider<ConfigurationCommandProvider>();
}
