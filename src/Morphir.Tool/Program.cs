// Entry point for the Morphir.Tool dotnet tool package (CLI-3). It runs the shared composition of
// Morphir.Program and adds its own: the in-process F# frontend. Only this project references
// Morphir.Frontend.FSharp, so the AOT executable morphir never contains the F# compiler service.
using Morphir.Tooling;

namespace Morphir.Tool;

internal static class Program
{
    /// <summary>The JIT dotnet tool host <c>dotnet-morphir</c>, with the version of this build.</summary>
    internal static Morphir.Extensions.HostIdentity Host =>
        new(Morphir.Extensions.MorphirHost.DotnetTool, Morphir.Extensions.ReleaseVersion.Create(VersionInfo.Version));

    /// <summary>The composition of the dotnet tool on top of the shared one.</summary>
    internal static void Compose(MorphirCliBuilder builder) => builder.AddScenario<FSharpFrontendScenario>();

    /// <summary>The application of the dotnet tool, for tests.</summary>
    internal static MorphirCliApp CreateApp(string[] args) => Morphir.Program.CreateApp(args, Host, Compose);

    /// <summary>
    /// Runs the CLI as <c>dotnet-morphir</c>. <c>Morphir.Program.Run</c> dispatches <c>extension worker</c> (task 4.1
    /// of add-wasm-extension-host) before it builds the CLI host or applies <see cref="Compose"/>, so the worker of
    /// this executable runs with this host's identity and none of its composition.
    /// </summary>
    private static int Main(string[] args) => Morphir.Program.Run(args, Host, Compose);
}
