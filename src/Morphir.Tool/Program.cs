// Entry point for the Morphir.Tool dotnet tool package
// Delegates all command processing to the Morphir namespace

namespace Morphir.Tool;

internal static class Program
{
    /// <summary>The JIT dotnet tool host <c>dotnet-morphir</c>, with the version of this build.</summary>
    internal static Morphir.Extensions.HostIdentity Host =>
        new(Morphir.Extensions.MorphirHost.DotnetTool, Morphir.Extensions.ReleaseVersion.Create(VersionInfo.Version));

    // Delegate to the shared composition in Morphir.Program, stating this host.
    private static int Main(string[] args) => Morphir.Program.Run(args, Host);
}
