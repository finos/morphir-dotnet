// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using FluentAssertions;
using Morphir.Build.Helpers;
using Morphir.TestSupport;
using TUnit.Core;

namespace Morphir.Build.Tests;

/// <summary>
/// HOST-1: an entry point's offering is fixed at build time. src/Morphir.BuildProfile.targets turns the release
/// profile's MorphirDeployment and RuntimeIdentifier into F# union cases in BuildProfileInfo.g.cs; a build without a
/// profile states no deployment, and a profile the entry point cannot be published as fails the build.
/// </summary>
public class BuildProfileTargetsTests
{
    private const string Managed = "self-contained;framework-dependent";

    [Test]
    [Category("PlatformSpecific")]
    public async Task ABuildWithoutAProfileStatesNoDeployment()
    {
        var (exit, output, generated) = await Generate(Managed);
        exit.Should().Be(0, output);
        generated.Should().Contain("Profile => Morphir.Extensions.BuildProfile.NoProfile;");
        generated.Should().Contain("EntryPoint => Morphir.Extensions.NonEmptySet<Morphir.Extensions.Deployment>.Create(Morphir.Extensions.Deployment.SelfContained, Morphir.Extensions.Deployment.FrameworkDependent);");
        generated.Should().Contain("namespace Probe;");
    }

    [Test]
    [Category("PlatformSpecific")]
    [Arguments("self-contained", "win-arm64", "Morphir.Extensions.BuildProfile.Of(Morphir.Extensions.Deployment.SelfContained, Morphir.Extensions.Platform.WinArm64)")]
    [Arguments("framework-dependent", "linux-x64", "Morphir.Extensions.BuildProfile.Of(Morphir.Extensions.Deployment.FrameworkDependent, Morphir.Extensions.Platform.LinuxX64)")]
    public async Task ANamedProfileStatesItsDeploymentAndPlatform(string deployment, string rid, string expected)
    {
        var (exit, output, generated) = await Generate(Managed, $"-p:MorphirDeployment={deployment}", $"-p:RuntimeIdentifier={rid}");
        exit.Should().Be(0, output);
        generated.Should().Contain($"Profile => {expected};");
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task TheToolProfileIsPortable()
    {
        var (exit, output, generated) = await Generate("dotnet-tool", "-p:MorphirDeployment=dotnet-tool");
        exit.Should().Be(0, output);
        generated.Should().Contain("Profile => Morphir.Extensions.BuildProfile.Portable(Morphir.Extensions.Deployment.DotnetTool);");
    }

    [Test]
    [Category("PlatformSpecific")]
    [Arguments("native-aot", "win-x64", "is published as self-contained;framework-dependent, not as native-aot")]
    [Arguments("legacy-single-file", "win-x64", "is not one of native-aot, self-contained, framework-dependent and dotnet-tool")]
    [Arguments("self-contained", "linux-musl-x64", "needs one of the six first-delivery runtime identifiers")]
    [Arguments("self-contained", "", "needs one of the six first-delivery runtime identifiers")]
    public async Task AProfileThatDoesNotFitTheEntryPointFailsTheBuild(string deployment, string rid, string message)
    {
        var (exit, output, _) = await Generate(Managed, $"-p:MorphirDeployment={deployment}", $"-p:RuntimeIdentifier={rid}");
        exit.Should().NotBe(0, output);
        output.Should().Contain(message);
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task AnEntryPointMustNameItsOfferings()
    {
        var (exit, output, _) = await Generate("");
        exit.Should().NotBe(0, output);
        output.Should().Contain("MorphirEntryPointDeployments must name the offerings");
    }

    [Test]
    public void EachNamedProfileSetsItsOwnDeployment()
    {
        foreach (var deployment in Morphir.Build.Helpers.Deployment.All)
            deployment.BuildProperties.Should().Contain("MorphirDeployment", deployment.Id);
        // The legacy single-file publish is not a named profile, so it states no deployment.
        LegacyAsset.TrimmedSingleFile.BuildProperties.Should().NotContainKey("MorphirDeployment");
        LegacyAsset.UntrimmedSingleFile.BuildProperties.Should().NotContainKey("MorphirDeployment");
    }

    [Test]
    [Category("PlatformSpecific")]
    [Arguments("src/Morphir/Morphir.csproj", "native-aot")]
    [Arguments("src/Morphir.Managed/Morphir.Managed.csproj", "self-contained;framework-dependent")]
    [Arguments("src/Morphir.Tool/Morphir.Tool.csproj", "dotnet-tool")]
    public async Task EachEntryPointNamesTheOfferingsItsProfilesBuild(string project, string expected)
    {
        var start = ChildProcess.StartInfo("dotnet", "msbuild", Path.Combine(TestFixture.GetRepositoryRoot(), project),
            "-getProperty:MorphirEntryPointDeployments", "-nologo", "-nodeReuse:false");
        start.WorkingDirectory = TestFixture.GetRepositoryRoot();
        var result = await ChildProcess.RunAsync(start, TimeSpan.FromMinutes(2));
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.StandardOutput.Trim().Should().Be(expected);
        // Every profile that publishes this project names a deployment the entry point accepts.
        foreach (var deployment in Morphir.Build.Helpers.Deployment.All.Where(deployment => deployment.Project == project))
            expected.Split(';').Should().Contain(deployment.Id);
    }

    /// <summary>Runs the target in a project that imports the targets file, and returns the generated file.</summary>
    private static async Task<(int Exit, string Output, string Generated)> Generate(string entryPoint, params string[] properties)
    {
        await using var sandbox = TestSandbox.Create("morphir-build-profile-");
        var targets = Path.Combine(TestFixture.GetRepositoryRoot(), "src", "Morphir.BuildProfile.targets");
        var project = sandbox.PathOf("probe.proj");
        await File.WriteAllTextAsync(project,
            "<Project>\n" +
            "  <PropertyGroup>\n" +
            "    <RootNamespace>Probe</RootNamespace>\n" +
            "    <IntermediateOutputPath>obj/</IntermediateOutputPath>\n" +
            $"    <MorphirEntryPointDeployments>{entryPoint}</MorphirEntryPointDeployments>\n" +
            "  </PropertyGroup>\n" +
            $"  <Import Project=\"{targets}\" />\n" +
            "  <Target Name=\"CoreCompile\" />\n" +
            "</Project>\n");

        var start = ChildProcess.StartInfo("dotnet", ["msbuild", project, "-t:MorphirGenerateBuildProfile", "-nologo", "-nodeReuse:false", .. properties]);
        start.WorkingDirectory = sandbox.Root;
        var result = await ChildProcess.RunAsync(start, TimeSpan.FromMinutes(2));
        var generatedPath = sandbox.PathOf("obj", "BuildProfileInfo.g.cs");
        var generated = File.Exists(generatedPath) ? await File.ReadAllTextAsync(generatedPath) : "";
        return (result.ExitCode, result.CombinedOutput, generated);
    }
}
