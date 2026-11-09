// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morphir.Compilation;
using Morphir.Frontend.FSharp;
using Morphir.Tooling;

namespace Morphir.Tool;

/// <summary>
/// Registers the in-process F# frontend (CLI-3). The frontend uses the F# compiler service, which is not
/// trim-safe or AOT-safe, so only the JIT dotnet tool has this scenario.
/// </summary>
public sealed class FSharpFrontendScenario : IMorphirCliScenario
{
    /// <inheritdoc />
    public void Configure(MorphirCliBuilder builder) =>
        builder.ConfigureServices(services =>
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IFrontend>(FSharpFrontend.Registration)));
}
