using Morphir.Extensions;

namespace Morphir.Tooling.Tests;

/// <summary>Host identities for tests: a development build of an entry point, which no named profile produced.</summary>
internal static class TestHosts
{
    /// <summary>
    /// The host as a build with no profile: <c>morphir</c> as the managed entry point (self-contained and
    /// framework-dependent), <c>dotnet-morphir</c> as the tool.
    /// </summary>
    public static HostIdentity Unprofiled(MorphirHost host, ReleaseVersion version) =>
        HostIdentity.Create(host, version,
            host.Equals(MorphirHost.DotnetTool)
                ? NonEmptySet<Deployment>.Create(Deployment.DotnetTool)
                : NonEmptySet<Deployment>.Create(Deployment.SelfContained, Deployment.FrameworkDependent),
            BuildProfile.NoProfile, ExecutionMode.Jit);
}
