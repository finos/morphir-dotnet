// Entry point for the Morphir.Tool dotnet tool package (CLI-3, DEP-1). It runs the shared command layer
// (MorphirApplication) with the managed composition, as the managed morphir does. Only the managed composition
// references Morphir.Frontend.FSharp, so the AOT-compatible morphir never contains the F# compiler service.
using Morphir.Tooling;
using Morphir.Tooling.Managed;

namespace Morphir.Tool;

internal static class Program
{
    /// <summary>
    /// The JIT dotnet tool host <c>dotnet-morphir</c>, with the version of this build. This is the one place where this
    /// entry point states who it is.
    /// </summary>
    internal static Morphir.Extensions.HostIdentity Host =>
        new(Morphir.Extensions.MorphirHost.DotnetTool, Morphir.Extensions.ReleaseVersion.Create(VersionInfo.Version));

    /// <summary>The composition of the dotnet tool on top of the shared one: the managed composition.</summary>
    internal static void Compose(MorphirCliBuilder builder) => ManagedComposition.Compose(builder);

    /// <summary>
    /// The application of the dotnet tool, for tests: the composition that <c>Main</c> runs, then <paramref name="observe"/>,
    /// which only reads the builder.
    /// </summary>
    internal static MorphirCliApp CreateApp(string[] args, Action<MorphirCliBuilder>? observe = null) =>
        MorphirApplication.CreateApp(args, Host, builder =>
        {
            Compose(builder);
            observe?.Invoke(builder);
        });

    /// <summary>
    /// Runs the CLI as <c>dotnet-morphir</c>. <see cref="MorphirApplication.Run"/> dispatches <c>extension worker</c>
    /// (task 4.1 of add-wasm-extension-host) before it builds the CLI host or applies <see cref="Compose"/>, so the
    /// worker of this executable runs with this host's identity and none of its composition.
    /// </summary>
    private static int Main(string[] args) => MorphirApplication.Run(args, Host, Compose);
}
