using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Compilation;
using Morphir.Extensions;
using Morphir.Tooling;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Extensions;

namespace Morphir;

// JSON source generator context for AOT compilation
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Tooling.Features.VerifyIR.VerifyIRResult))]
internal partial class MorphirJsonContext : JsonSerializerContext
{
}

/// <summary>
/// The AOT-compatible entry point of the <c>morphir</c> executable (DEP-1). It runs the shared command layer
/// <see cref="MorphirApplication"/> with <see cref="NativeComposition"/>, which has no F# compiler service. The managed
/// <c>morphir</c> (<c>src/Morphir.Managed</c>) and <c>dotnet-morphir</c> (<c>src/Morphir.Tool</c>) run the same layer
/// with the managed composition instead. This project must never reference <c>Morphir.Tooling.Managed</c> or
/// <c>Morphir.Frontend.FSharp</c>.
/// </summary>
public static class Program
{
    /// <summary>
    /// The host identity of this entry point, with the version of this build. This is the one place where this entry
    /// point states who it is.
    /// </summary>
    internal static HostIdentity Host => HostIdentity.Create(MorphirHost.Native, ReleaseVersion.Create(VersionInfo.Version),
        BuildProfileInfo.EntryPoint, BuildProfileInfo.Profile, ExecutionMode.OfThisProcess);

    /// <summary>
    /// Main entry point for the CLI application.
    /// </summary>
    public static int Main(string[] args) => Run(args, Host, NativeComposition);

    /// <summary>
    /// The composition of the AOT executable on top of the shared one. It has no F# frontend; a compile of F#
    /// says where to find one.
    /// </summary>
    public static void NativeComposition(MorphirCliBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton(new FrontendHint(FrontendExtensionSource.FSharpLanguage,
            FrontendExtensionSource.FSharpCompileHint)));

    /// <summary>
    /// Runs the CLI as the given host, through the shared command layer <see cref="MorphirApplication"/>.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <param name="host">The host identity.</param>
    /// <param name="extend">The host's own composition, applied after the shared composition.</param>
    public static int Run(string[] args, HostIdentity host, Action<MorphirCliBuilder>? extend = null) =>
        MorphirApplication.Run(args, host, extend);

    /// <summary>
    /// The shared composition of every host (<see cref="MorphirApplication.CreateApp"/>), then the host's own
    /// composition. The host identity is registered as a singleton.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <param name="host">The host identity.</param>
    /// <param name="extend">The host's own composition, applied after the shared composition.</param>
    public static MorphirCliApp CreateApp(string[] args, HostIdentity host, Action<MorphirCliBuilder>? extend = null) =>
        MorphirApplication.CreateApp(args, host, extend);
}
