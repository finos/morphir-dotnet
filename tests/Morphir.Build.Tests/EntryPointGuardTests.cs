// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using FluentAssertions;
using Morphir.TestSupport;
using TUnit.Core;

namespace Morphir.Build.Tests;

/// <summary>
/// DEP-1 build guards. The managed morphir holds FSharp.Compiler.Service, so its build refuses Native AOT and trimming.
/// morphirup must run where no .NET is installed, so its publish refuses anything but Native AOT.
/// The AOT-compatible and the managed entry points both build an assembly named morphir, so a project whose resolved
/// references hold two different morphir.dll files fails instead of keeping whichever copy came last.
/// </summary>
[Category("PlatformSpecific")]
public class EntryPointGuardTests
{
    private const string ManagedProject = "src/Morphir.Managed/Morphir.Managed.csproj";
    private const string AotGuard = "MorphirRefuseAotAndTrimming";
    private const string AssemblyGuard = "MorphirRefuseTwoMorphirAssemblies";
    private const string MorphirupProject = "src/Morphirup/Morphirup.fsproj";
    private const string MorphirupGuard = "MorphirupRequireNativeAot";

    [Test]
    [Arguments("PublishAot")]
    [Arguments("PublishTrimmed")]
    public async Task TheManagedMorphirRefusesNativeAotAndTrimming(string property)
    {
        var result = await MsBuild(Path.Combine(TestFixture.GetRepositoryRoot(), ManagedProject), AotGuard, $"-p:{property}=true");

        result.ExitCode.Should().NotBe(0, result.CombinedOutput);
        result.CombinedOutput.Should().Contain("src/Morphir.Managed holds FSharp.Compiler.Service");
    }

    [Test]
    public async Task TheManagedMorphirAcceptsItsReleaseProfiles()
    {
        // The self-contained and framework-dependent profiles publish it untrimmed and without Native AOT.
        var result = await MsBuild(Path.Combine(TestFixture.GetRepositoryRoot(), ManagedProject), AotGuard,
            "-p:PublishTrimmed=false", "-p:PublishSingleFile=false", "-p:SelfContained=true");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
    }

    [Test]
    [Arguments("")]
    [Arguments("-p:PublishAot=false")]
    [Arguments("-p:SelfContained=true -p:PublishTrimmed=true -p:PublishSingleFile=true")]
    public async Task MorphirupRefusesAPublishWithoutNativeAot(string properties)
    {
        var result = await MsBuild(Path.Combine(TestFixture.GetRepositoryRoot(), MorphirupProject), MorphirupGuard,
            properties.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        result.ExitCode.Should().NotBe(0, result.CombinedOutput);
        result.CombinedOutput.Should().Contain("src/Morphirup ships only as a Native AOT executable");
    }

    [Test]
    [Arguments("-p:PublishAot=true")]
    [Arguments("-p:MorphirProbeAot=true")]
    public async Task MorphirupAcceptsANativeAotPublish(string property)
    {
        // -p:MorphirProbeAot=true is how the Fallout probe publishes it.
        var result = await MsBuild(Path.Combine(TestFixture.GetRepositoryRoot(), MorphirupProject), MorphirupGuard, property);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
    }

    [Test]
    public async Task TheManagedReleaseProfilesPassTheGuard()
    {
        foreach (var deployment in new[] { Morphir.Build.Helpers.Deployment.SelfContained, Morphir.Build.Helpers.Deployment.FrameworkDependent })
        {
            deployment.Project.Should().Be(ManagedProject);
            var result = await MsBuild(Path.Combine(TestFixture.GetRepositoryRoot(), ManagedProject), AotGuard, Properties(deployment.BuildProperties));
            result.ExitCode.Should().Be(0, $"{deployment.Id}: {result.CombinedOutput}");
        }
    }

    [Test]
    public async Task TheNativeAotProfileCannotPublishTheManagedMorphir()
    {
        var result = await MsBuild(Path.Combine(TestFixture.GetRepositoryRoot(), ManagedProject), AotGuard,
            Properties(Morphir.Build.Helpers.Deployment.NativeAot.BuildProperties));
        result.ExitCode.Should().NotBe(0, result.CombinedOutput);
        result.CombinedOutput.Should().Contain("src/Morphir.Managed holds FSharp.Compiler.Service and cannot be published with PublishAot or PublishTrimmed")
            .And.Contain("PublishAot='true'").And.Contain("Publish src/Morphir for Native AOT", "the guard, not some other failure, refuses the profile");
    }

    private static string[] Properties(IReadOnlyDictionary<string, string> properties) =>
        [.. properties.Select(property => $"-p:{property.Key}={property.Value}")];

    [Test]
    public async Task TwoMorphirAssembliesInOneProjectFail()
    {
        var result = await MsBuild(await SyntheticProject(
            ("aot/bin/morphir.dll", "aot/Morphir.csproj"),
            ("managed/bin/morphir.dll", "managed/Morphir.Managed.csproj")), AssemblyGuard);

        result.ExitCode.Should().NotBe(0, result.CombinedOutput);
        result.CombinedOutput.Should().Contain("two different morphir.dll").And.Contain("Morphir.csproj").And.Contain("Morphir.Managed.csproj");
    }

    [Test]
    public async Task OneMorphirAssemblyFromOneProjectPasses()
    {
        // The reference assembly and the implementation of the same project are one morphir.dll, not two.
        var result = await MsBuild(await SyntheticProject(
            ("aot/obj/ref/morphir.dll", "aot/Morphir.csproj"),
            ("aot/bin/morphir.dll", "aot/Morphir.csproj"),
            ("tooling/bin/Morphir.Tooling.dll", "tooling/Morphir.Tooling.csproj")), AssemblyGuard);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
    }

    /// <summary>A project that imports the repository's Directory.Build.targets and declares resolved references.</summary>
    private static async Task<string> SyntheticProject(params (string Path, string Project)[] references)
    {
        var directory = Directory.CreateTempSubdirectory("morphir-guard-").FullName;
        var targets = Path.Combine(TestFixture.GetRepositoryRoot(), "Directory.Build.targets");
        var items = string.Join("\n", references.Select(reference =>
            $"    <_ResolvedProjectReferencePaths Include=\"{Path.Combine(directory, reference.Path)}\" MSBuildSourceProjectFile=\"{Path.Combine(directory, reference.Project)}\" />"));
        var path = Path.Combine(directory, "guard.proj");
        await File.WriteAllTextAsync(path, $"<Project>\n  <Import Project=\"{targets}\" />\n  <ItemGroup>\n{items}\n  </ItemGroup>\n</Project>\n");
        return path;
    }

    private static async Task<ChildProcessResult> MsBuild(string project, string target, params string[] properties)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = TestFixture.GetRepositoryRoot() };
        foreach (var argument in new[] { "msbuild", project, $"-t:{target}", "-nologo", "-nodeReuse:false" }.Concat(properties))
            start.ArgumentList.Add(argument);
        try
        {
            return await ChildProcess.RunAsync(start, TimeSpan.FromMinutes(2));
        }
        finally
        {
            // A synthetic project lives in its own temporary folder; the repository's projects are left alone.
            var directory = Path.GetDirectoryName(project)!;
            if (Path.GetFileName(directory).StartsWith("morphir-guard-", StringComparison.Ordinal)) TolerantDirectory.TryDelete(directory);
        }
    }
}
