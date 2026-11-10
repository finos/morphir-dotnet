// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
// Entry point of the managed morphir executable (DEP-1). It runs the shared command layer with the managed
// composition, as dotnet-morphir does. The AOT-compatible morphir is src/Morphir, which has no F# compiler service.
using Morphir.Extensions;
using Morphir.Tooling;
using Morphir.Tooling.Managed;

namespace Morphir.Managed;

internal static class Program
{
    /// <summary>
    /// The host identity of this entry point, with the version of this build. This is the one place where this entry
    /// point states who it is. It runs as the command <c>morphir</c>.
    /// </summary>
    internal static HostIdentity Host => new(MorphirHost.Native, ReleaseVersion.Create(VersionInfo.Version));

    /// <summary>The composition of the managed morphir on top of the shared one.</summary>
    internal static void Compose(MorphirCliBuilder builder) => ManagedComposition.Compose(builder);

    /// <summary>
    /// The application of the managed morphir, for tests: the composition that <c>Main</c> runs, then <paramref name="observe"/>,
    /// which only reads the builder.
    /// </summary>
    internal static MorphirCliApp CreateApp(string[] args, Action<MorphirCliBuilder>? observe = null) =>
        MorphirApplication.CreateApp(args, Host, builder =>
        {
            Compose(builder);
            observe?.Invoke(builder);
        });

    private static int Main(string[] args) => MorphirApplication.Run(args, Host, Compose);
}
