// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morphir.Compilation;
using Morphir.Lang.CSharp;

namespace Morphir.Tooling.Scenarios;

/// <summary>
/// Registers the in-process C# frontend for both hosts and every offering, the Native AOT <c>morphir</c> included.
/// The frontend runs on Roslyn (<c>Microsoft.CodeAnalysis.CSharp</c>) without the Workspaces layer, which does run under
/// Native AOT. The F# frontend is different: it needs the F# compiler service, so only the managed entry points register
/// it.
/// </summary>
public sealed class CSharpFrontendScenario : IMorphirCliScenario
{
    /// <inheritdoc />
    public void Configure(MorphirCliBuilder builder) =>
        builder.ConfigureServices(services =>
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IFrontend>(CSharpFrontend.Registration)));
}
