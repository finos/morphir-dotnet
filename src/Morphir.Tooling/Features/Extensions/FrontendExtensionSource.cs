using System.Collections.Immutable;
using Morphir.Compilation;
using Morphir.Extensions;
using Morphir.IR;

namespace Morphir.Tooling.Features.Extensions;

/// <summary>
/// The built-in in-process frontends of this release, as registry entries. Every offering lists them, so that an
/// offering without one can say why and where to get one: the in-process F# frontend is in the managed offerings and
/// not in <c>native-aot</c>. The entries are declared here, in code that every entry point shares, because the AOT
/// entry point cannot reference the F# frontend itself. Host tests check that each entry equals the frontend that the
/// managed composition registers, and that the available entries are exactly the in-process frontends that
/// <c>compile</c> can select.
/// </summary>
public sealed class FrontendExtensionSource : IExtensionSource
{
    /// <summary>The id of the in-process F# frontend: the frontend identity that the compile report names.</summary>
    public static readonly ExtensionId FSharpId = ExtensionId.Create("morphir-dotnet.fsharp");

    /// <summary>The language of the in-process F# frontend.</summary>
    public static readonly LanguageId FSharpLanguage = LanguageId.Create("fsharp");

    /// <summary>The file extensions that the in-process F# frontend accepts.</summary>
    public static readonly ImmutableArray<string> FSharpFileExtensions = [".fs", ".fsi"];

    /// <summary>The offerings that have the in-process F# frontend.</summary>
    public static readonly NonEmptySet<Deployment> FSharpDeployments =
        NonEmptySet<Deployment>.Create(Deployment.SelfContained, Deployment.FrameworkDependent, Deployment.DotnetTool);

    /// <summary>Why the in-process F# frontend is not in an offering, and the delivery and step that serve it there.</summary>
    public static readonly UnavailableReason FSharpUnavailable = UnavailableReason.Create(
        "The native-aot morphir has no in-process F# frontend; the self-contained and framework-dependent morphir and dotnet-morphir have one. " +
        "Here, install the F# frontend process delivery morphir-fsharp-frontend and configure it with [frontend.fsharp].extension or --extension.");

    /// <summary>
    /// The sentence that <c>compile</c> adds when no frontend reads F# in an offering without the in-process one. The
    /// compile message itself adds how to configure an extension.
    /// </summary>
    public const string FSharpCompileHint =
        "The self-contained and framework-dependent morphir and dotnet-morphir have a built-in F# frontend; " +
        "this offering uses the F# frontend process delivery morphir-fsharp-frontend.";

    public FrontendExtensionSource(HostIdentity host) => Entries = [FSharp(host.Version)];

    public IReadOnlyList<ExtensionEntry> Entries { get; }

    /// <summary>
    /// The F# frontend's claim: its language and file extensions, and the IR versions of the standard builders and
    /// writers, which are what an in-process frontend produces.
    /// </summary>
    public static FrontendClaim FSharpClaim
    {
        get
        {
            var versions = FrontendTargets.Standard.Select(target => target.Version).ToArray();
            return FrontendClaim.Create(
                NonEmptySet<FrontendLanguage>.Create(FrontendLanguage.Create(FSharpLanguage, [.. FSharpFileExtensions])),
                NonEmptySet<Versioning.FormatVersion>.Create(versions[0], versions[1..]));
        }
    }

    private static ExtensionEntry FSharp(ReleaseVersion version) =>
        ExtensionEntry.BuiltInFrontend(
            ExtensionIdentity.Create(FSharpId, ExtensionName.Create("F# frontend"), version,
                ExtensionDescription.Create("Compiles F# source files to a Morphir model in process.")),
            FSharpClaim,
            EntryDeployments.OnlyIn(FSharpDeployments, FSharpUnavailable));
}
