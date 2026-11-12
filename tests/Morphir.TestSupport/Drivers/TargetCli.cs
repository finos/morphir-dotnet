// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Reflection;

namespace Morphir.TestSupport.Drivers;

/// <summary>
/// The executable of the target of a run, for a test outside the black-box project that starts it with its own
/// runner: a conformance test that judges the output with the kit library in the test process. It reads the same
/// settings and resolves the same artifact as the black-box session, with no fallback to another form.
/// </summary>
/// <param name="Target">The target of the run.</param>
/// <param name="Host">The host that the command starts.</param>
/// <param name="Artifact">The file of the host: an assembly, which <see cref="FileName"/> starts with <c>dotnet</c>, or an executable.</param>
public sealed record TargetCli(TestTarget Target, MorphirHost Host, string Artifact)
{
    private static readonly List<PackagedTarget> Installed = [];

    /// <summary>The program to start: <c>dotnet</c> for an assembly, else the executable.</summary>
    public string FileName => IsAssembly ? "dotnet" : Artifact;

    /// <summary>The arguments before the arguments of the command: <c>exec</c> and the assembly, or none.</summary>
    public IReadOnlyList<string> PrefixArguments => IsAssembly ? ["exec", Artifact] : [];

    private bool IsAssembly => Artifact.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The executable of the run of <paramref name="testAssembly"/>, which imports Morphir.CliArtifacts.targets. When
    /// <c>MORPHIR_TEST_HOST</c> is not set and the target has <paramref name="defaultHost"/>, the command starts that
    /// host; otherwise the default host of the run. A packaged target is installed once, and removed when the process
    /// exits. A wrong setting or a missing artifact throws a <see cref="TestSessionException"/>.
    /// </summary>
    public static TargetCli OfTestRun(Assembly testAssembly, MorphirHost defaultHost) =>
        OfTestRun(testAssembly, defaultHost, Environment.GetEnvironmentVariable, File.Exists);

    public static TargetCli OfTestRun(Assembly testAssembly, MorphirHost defaultHost, Func<string, string?> variable, Func<string, bool> fileExists)
    {
        var settings = TestRunSettings.Parse(variable, fileExists);
        var host = string.IsNullOrWhiteSpace(variable(TestRunSettings.HostVariable)) && settings.Target.Has(defaultHost)
            ? defaultHost
            : settings.DefaultHost;
        var name = testAssembly.GetName().Name ?? "the test assembly";
        ArtifactLocations Locations() => ArtifactLocations.Of(ArtifactLocations.MetadataOf(testAssembly), name, variable);
        var artifact = settings.Target switch
        {
            TestTarget.DevelopmentBuild => CliArtifacts.OfDevelopmentBuild(testAssembly).PathOf(host),
            TestTarget.Executable executable => executable.Path,
            TestTarget.Trimmed or TestTarget.Untrimmed or TestTarget.NativeAot => PublishedExecutables.PathOf(settings.Target, Locations(), fileExists),
            TestTarget.Packaged packaged => Install(packaged, Locations()).Installation.LaunchPath,
            _ => throw new TestSessionException($"The target '{settings.Target.Name}' has no artifact resolution."),
        };
        return new TargetCli(settings.Target, host, artifact);
    }

    private static PackagedTarget Install(TestTarget.Packaged target, ArtifactLocations at)
    {
        var installed = PackagedTarget.Install(target, at);
        lock (Installed)
        {
            if (Installed.Count == 0)
            {
                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    lock (Installed) Installed.ForEach(installation => installation.Dispose());
                };
            }

            Installed.Add(installed);
        }

        return installed;
    }
}
