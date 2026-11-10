// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Tooling.Features.Mck;

namespace Morphir.Tooling.Scenarios;

/// <summary>
/// The experimental <c>mck wasm-host</c> commands. Registered in the shared composition, so both executables,
/// <c>morphir</c> and <c>dotnet-morphir</c>, have them.
/// </summary>
public sealed class MckScenario : IMorphirCliScenario
{
    public void Configure(MorphirCliBuilder builder) => builder.AddCommandProvider<MckCommandProvider>();
}
