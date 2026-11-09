// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Tooling;

namespace Morphir.Tooling.Managed;

/// <summary>
/// The managed composition (DEP-1): what the managed entry points add after the shared command layer
/// <see cref="MorphirApplication"/>. The managed <c>morphir</c> and <c>dotnet-morphir</c> pass <see cref="Compose"/> as
/// their composition, so they run the same commands with the same frontends. The AOT-compatible <c>morphir</c> never
/// references this assembly, because it brings in the F# compiler service.
/// </summary>
public static class ManagedComposition
{
    /// <summary>Adds the managed scenarios to the builder: the in-process F# frontend.</summary>
    public static void Compose(MorphirCliBuilder builder) => builder.AddScenario<FSharpFrontendScenario>();
}
