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

    /// <summary>The application of the dotnet tool, for tests.</summary>
    internal static MorphirCliApp CreateApp(string[] args) => MorphirApplication.CreateApp(args, Host, Compose);

    private static int Main(string[] args) => MorphirApplication.Run(args, Host, Compose);
}
