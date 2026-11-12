// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Morphir.Build.Helpers;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// The named release profiles of task 2.2 (DEP-1): four deployments, six first-delivery platforms, one artifact
/// identity for each supported pair, and the legacy release assets as an explicit alias.
/// </summary>
public class ReleaseProfilesTests
{
    private static readonly string[] Releases = ["1.2.3", "0.4.0-alpha.3"];

    [Test]
    public void TheDeploymentsAreTheFourOfferings() =>
        Deployment.All.Select(deployment => deployment.Id).Should().Equal("native-aot", "self-contained", "framework-dependent", "dotnet-tool");

    [Test]
    public void TheFirstDeliveryPlatformsAreTheSixPairs() =>
        TargetPlatform.All.Select(platform => platform.Rid).Should().Equal("win-x64", "win-arm64", "osx-x64", "osx-arm64", "linux-x64", "linux-arm64");

    [Test]
    [Arguments("native-aot")]
    [Arguments("self-contained")]
    [Arguments("framework-dependent")]
    [Arguments("dotnet-tool")]
    public void ADeploymentParsesFromItsId(string id)
    {
        Deployment.TryParse(id, out var deployment).Should().BeTrue();
        deployment!.Id.Should().Be(id);
    }

    [Test]
    [Arguments("")]
    [Arguments("aot")]
    [Arguments("Native-AOT")]
    [Arguments("trimmed")]
    public void AnUnknownDeploymentDoesNotParse(string id) =>
        Deployment.TryParse(id, out _).Should().BeFalse();

    [Test]
    public void TheArchiveNameFollowsTheApprovedPattern()
    {
        Name(Deployment.NativeAot, "win-x64", "1.2.3").Should().Be("morphir-dotnet-1.2.3-native-aot-win-x64.zip");
        Name(Deployment.SelfContained, "linux-arm64", "1.2.3").Should().Be("morphir-dotnet-1.2.3-self-contained-linux-arm64.tar.gz");
        Name(Deployment.FrameworkDependent, "osx-x64", "0.4.0-alpha.3").Should().Be("morphir-dotnet-0.4.0-alpha.3-framework-dependent-osx-x64.tar.gz");
    }

    [Test]
    public void TheToolKeepsItsPackageIdentity()
    {
        foreach (var platform in TargetPlatform.All)
        {
            var artifact = Defined(Deployment.DotnetTool, platform.Rid, "1.2.3");
            artifact.FileName.Should().Be("Morphir.Tool.1.2.3.nupkg");
            artifact.Platform.Should().BeNull("one portable package serves every platform");
            artifact.Launch.LaunchPath.Should().Be("dotnet-morphir");
        }
    }

    [Test]
    public void ArtifactNamesAreUniqueAcrossReleaseDeploymentAndPlatform()
    {
        var artifacts = Matrix().ToList();
        artifacts.Should().HaveCount(Releases.Length * Deployment.All.Count * TargetPlatform.All.Count);

        // Distinct bytes never share a name: one name is one (release, deployment, platform) artifact. Only the
        // portable tool package serves several platforms, and then the artifact says it is portable.
        foreach (var byName in artifacts.GroupBy(artifact => artifact.FileName))
        {
            byName.Select(artifact => (artifact.Release, artifact.Deployment.Id, artifact.Platform?.Rid)).Distinct().Should().ContainSingle(byName.Key);
            if (byName.Count() > 1) byName.First().Deployment.Should().Be(Deployment.DotnetTool);
        }

        artifacts.Where(artifact => artifact.Platform is not null).Select(artifact => artifact.FileName).Should().OnlyHaveUniqueItems();
        artifacts.Select(artifact => artifact.FileName).Distinct().Should().HaveCount(Releases.Length * (3 * TargetPlatform.All.Count + 1));
    }

    [Test]
    public void LaunchMetadataIsUniqueAcrossReleaseDeploymentAndPlatform()
    {
        var launches = Matrix().Where(artifact => artifact.Platform is not null).Select(artifact => artifact.Launch).ToList();
        launches.Select(launch => (launch.Release, launch.Deployment, launch.Platform)).Should().OnlyHaveUniqueItems();
        launches.Select(launch => launch.Artifact).Should().OnlyHaveUniqueItems();
        foreach (var artifact in Matrix())
        {
            artifact.Launch.Implementation.Should().Be("dotnet");
            artifact.Launch.Release.Should().Be(artifact.Release);
            artifact.Launch.Deployment.Should().Be(artifact.Deployment.Id);
            artifact.Launch.Artifact.Should().Be(artifact.FileName);
            artifact.Launch.Platform.Should().Be(artifact.Platform?.Rid ?? "any");
        }
    }

    [Test]
    [Arguments("linux-musl-x64")]
    [Arguments("win-x86")]
    [Arguments("linux-arm")]
    [Arguments("osx")]
    [Arguments("")]
    public void AnUnsupportedPlatformIsReportedAndNotMapped(string rid)
    {
        foreach (var deployment in Deployment.All)
        {
            var resolution = ReleaseArtifacts.Resolve(deployment, rid, "1.2.3");
            resolution.Should().BeOfType<ArtifactResolution.NotDefined>();
            var unsupported = (ArtifactResolution.NotDefined)resolution;
            unsupported.Deployment.Should().Be(deployment);
            unsupported.Rid.Should().Be(rid);
            unsupported.Reason.Should().Contain(rid.Length == 0 ? "no platform" : rid);
            foreach (var platform in TargetPlatform.All) unsupported.Reason.Should().Contain(platform.Rid);
        }
    }

    [Test]
    public void AnInvalidReleaseIsRefused() =>
        FluentActions.Invoking(() => ReleaseArtifacts.Resolve(Deployment.NativeAot, "win-x64", "1.2")).Should().Throw<ArgumentException>().WithMessage("*exact SemVer*");

    [Test]
    public void TheLaunchPathIsTheMorphirExecutableOfThePlatform()
    {
        foreach (var deployment in new[] { Deployment.NativeAot, Deployment.SelfContained, Deployment.FrameworkDependent })
        {
            Defined(deployment, "win-arm64", "1.2.3").Launch.LaunchPath.Should().Be("morphir.exe");
            Defined(deployment, "linux-x64", "1.2.3").Launch.LaunchPath.Should().Be("morphir");
            Defined(deployment, "osx-arm64", "1.2.3").Launch.LaunchPath.Should().Be("morphir");
        }
    }

    [Test]
    public void EachDeploymentStatesHowItRuns()
    {
        var aot = Defined(Deployment.NativeAot, "win-x64", "1.2.3").Launch;
        aot.Execution.Should().Be("native");
        aot.BundledRuntime.Should().BeTrue();
        aot.RequiredRuntime.Should().BeNull();
        aot.RequiredSdk.Should().BeNull();
        aot.InProcessFSharpFrontend.Should().BeFalse();

        var selfContained = Defined(Deployment.SelfContained, "win-x64", "1.2.3").Launch;
        selfContained.Execution.Should().Be("managed");
        selfContained.BundledRuntime.Should().BeTrue();
        selfContained.RequiredRuntime.Should().BeNull();
        selfContained.InProcessFSharpFrontend.Should().BeTrue();

        var frameworkDependent = Defined(Deployment.FrameworkDependent, "win-x64", "1.2.3").Launch;
        frameworkDependent.Execution.Should().Be("managed");
        frameworkDependent.BundledRuntime.Should().BeFalse();
        frameworkDependent.RequiredRuntime.Should().Be("Microsoft.NETCore.App 11.0");
        frameworkDependent.RequiredSdk.Should().BeNull();
        frameworkDependent.InProcessFSharpFrontend.Should().BeTrue();

        var tool = Defined(Deployment.DotnetTool, "win-x64", "1.2.3").Launch;
        tool.Execution.Should().Be("managed");
        tool.BundledRuntime.Should().BeFalse();
        tool.RequiredRuntime.Should().Be("Microsoft.NETCore.App 11.0");
        tool.RequiredSdk.Should().Be(".NET SDK 11.0", "installing a .NET tool needs the SDK");
        tool.InProcessFSharpFrontend.Should().BeTrue();
    }

    [Test]
    public void EachDeploymentPublishesItsProject()
    {
        Deployment.NativeAot.Project.Should().Be("src/Morphir/Morphir.csproj");
        Deployment.SelfContained.Project.Should().Be("src/Morphir.Managed/Morphir.Managed.csproj");
        Deployment.FrameworkDependent.Project.Should().Be("src/Morphir.Managed/Morphir.Managed.csproj");
        Deployment.DotnetTool.Project.Should().Be("src/Morphir.Tool/Morphir.Tool.csproj");
    }

    [Test]
    public void ThePublishPropertiesMatchTheDeployment()
    {
        var aot = Deployment.NativeAot.BuildProperties;
        aot.Should().Contain("PublishAot", "true").And.Contain("PublishTrimmed", "true").And.Contain("SelfContained", "true");

        // The managed profiles hold FSharp.Compiler.Service: never trimmed, never AOT, and a folder, not one file.
        foreach (var managed in new[] { Deployment.SelfContained, Deployment.FrameworkDependent })
        {
            managed.BuildProperties.Should().Contain("PublishTrimmed", "false").And.Contain("PublishSingleFile", "false");
            managed.BuildProperties.Should().NotContainKey("PublishAot");
        }
        Deployment.SelfContained.BuildProperties.Should().Contain("SelfContained", "true");
        Deployment.FrameworkDependent.BuildProperties.Should().Contain("SelfContained", "false").And.Contain("UseAppHost", "true");

        Deployment.DotnetTool.BuildProperties.Should().Contain("PackAsTool", "true").And.Contain("ToolCommandName", "dotnet-morphir");

        // Archives ship no debug symbols or documentation files.
        foreach (var deployment in Deployment.All)
            deployment.BuildProperties.Should().Contain("DebugType", "none");
        foreach (var deployment in new[] { Deployment.NativeAot, Deployment.SelfContained, Deployment.FrameworkDependent })
            deployment.BuildProperties.Should().Contain("CopyOutputSymbolsToPublishDirectory", "false");
    }

    [Test]
    public void OnlyTheNativeAotProfileLinksTheEngineIn()
    {
        // Task 5.2/5.3: the released Native AOT morphir carries the WASM engine inside its one file
        // (build/wasmtime/WasmtimeStaticLink.targets); every other build keeps the package's library beside it.
        Deployment.NativeAot.BuildProperties.Should().Contain("MorphirWasmtimeStaticLink", "true");
        foreach (var deployment in Deployment.All.Where(deployment => deployment != Deployment.NativeAot))
            deployment.BuildProperties.Should().NotContainKey("MorphirWasmtimeStaticLink", deployment.Id);
        LegacyAsset.TrimmedSingleFile.BuildProperties.Should().NotContainKey("MorphirWasmtimeStaticLink");
        LegacyAsset.UntrimmedSingleFile.BuildProperties.Should().NotContainKey("MorphirWasmtimeStaticLink");
    }

    [Test]
    public void TheLegacyAssetsKeepTheirNamesAndSayWhatTheyAre()
    {
        var legacy = LegacyAsset.TrimmedSingleFile;
        legacy.ExecutableName("win-x64").Should().Be("morphir-win-x64.exe");
        legacy.ExecutableName("linux-arm64").Should().Be("morphir-linux-arm64");
        legacy.TarballName("osx-arm64", "0.4.0-alpha.3").Should().Be("morphir-osx-arm64-v0.4.0-alpha.3.tar.gz");
        legacy.ReleasedPlatforms.Select(platform => platform.Rid).Should().Equal("win-x64", "osx-arm64", "linux-x64", "linux-arm64");

        // The same settings as the PublishSingleFile target that releases have used.
        legacy.Project.Should().Be("src/Morphir/Morphir.csproj");
        legacy.BuildProperties.Should().Equal(new Dictionary<string, string>
        {
            ["SelfContained"] = "true",
            ["PublishSingleFile"] = "true",
            ["PublishTrimmed"] = "true",
            ["TrimMode"] = "partial",
            ["TreatWarningsAsErrors"] = "false",
        });

        legacy.Execution.Should().Be("managed");
        legacy.BundledRuntime.Should().BeTrue();
        legacy.Trimmed.Should().BeTrue();
        legacy.SingleFile.Should().BeTrue();
        legacy.InProcessFSharpFrontend.Should().BeFalse();
        Deployment.All.Select(deployment => deployment.Id).Should().NotContain(legacy.Id, "a legacy alias is not one of the four offerings");
    }

    [Test]
    public void TheLegacyAliasDescribesWhatTheReleaseWorkflowShips()
    {
        var workflow = File.ReadAllText(Path.Combine(PathHelper.GetRepositoryRoot(), ".github", "workflows", "deployment.yml"));
        var expected = LegacyAsset.TrimmedSingleFile.ReleasedPlatforms.Select(platform => platform.Rid).ToList();

        // The build-executables matrix builds the asset for these RIDs ...
        var matrix = Regex.Matches(workflow, @"^\s*- rid: (\S+)\s*$", RegexOptions.Multiline).Select(match => match.Groups[1].Value).ToList();
        matrix.Should().BeEquivalentTo(expected);
        workflow.Should().Contain("--target PublishSingleFile", "releases build the legacy asset with the PublishSingleFile target");

        // ... and the create-github-release loop picks the assets for these RIDs, under names that its templates write.
        var loop = Regex.Match(workflow, @"for rid in ([a-z0-9 -]+); do");
        loop.Success.Should().BeTrue("the create-github-release job loops over the released RIDs");
        loop.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Should().BeEquivalentTo(expected);

        var executable = Regex.Match(workflow, "release-assets/(morphir-\\$rid\\$suffix)\"").Groups[1].Value;
        var tarball = Regex.Match(workflow, "release-assets/(morphir-\\$rid-v\\$VERSION\\.tar\\.gz)").Groups[1].Value;
        executable.Should().NotBeEmpty();
        tarball.Should().NotBeEmpty();
        foreach (var rid in expected)
        {
            var suffix = rid.StartsWith("win-", StringComparison.Ordinal) ? ".exe" : "";
            executable.Replace("$rid", rid).Replace("$suffix", suffix).Should().Be(LegacyAsset.TrimmedSingleFile.ExecutableName(rid));
            tarball.Replace("$rid", rid).Replace("$VERSION", "1.2.3").Should().Be(LegacyAsset.TrimmedSingleFile.TarballName(rid, "1.2.3"));
        }
    }

    [Test]
    public void TheUntrimmedBaselineIsNeverReleased()
    {
        LegacyAsset.UntrimmedSingleFile.ReleasedPlatforms.Should().BeEmpty();
        LegacyAsset.UntrimmedSingleFile.BuildProperties.Should().Contain("PublishTrimmed", "false").And.NotContainKey("TrimMode");
    }

    [Test]
    public void TheLaunchInfoSaysItIsNotThePublicContract()
    {
        var json = JsonDocument.Parse(BuildLaunchInfo.ToJson(Defined(Deployment.FrameworkDependent, "win-arm64", "1.2.3").Launch, "ab12"));
        var root = json.RootElement;
        root.GetProperty("note").GetString().Should().Contain("not a public contract").And.Contain("tool release metadata");
        root.TryGetProperty("schemaVersion", out _).Should().BeFalse("the build-side file has no contract version");
        root.GetProperty("deployment").GetString().Should().Be("framework-dependent");
        root.GetProperty("platform").GetString().Should().Be("win-arm64");
        root.GetProperty("launchPath").GetString().Should().Be("morphir.exe");
        root.GetProperty("requiredRuntime").GetString().Should().Be("Microsoft.NETCore.App 11.0");
        root.GetProperty("sha256").GetString().Should().Be("ab12");
        BuildLaunchInfo.FileNameFor("x.zip").Should().Be("x.zip.build-launch.json");
    }

    [Test]
    [Arguments("1.2.3+b.1")]
    [Arguments("1.2.3-alpha.1+sha.5114f85")]
    public void BuildMetadataIsRefused(string release) =>
        FluentActions.Invoking(() => ReleaseArtifacts.Resolve(Deployment.SelfContained, "linux-x64", release))
            .Should().Throw<ArgumentException>().WithMessage("*build metadata*");

    [Test]
    [Arguments("1.2.3-01")]
    [Arguments("1.2.3-alpha..1")]
    [Arguments("01.2.3")]
    [Arguments("1.2.3.4")]
    [Arguments("*")]
    [Arguments("1.2.3\n")]
    public void AnInexactReleaseIsRefused(string release) =>
        FluentActions.Invoking(() => ReleaseArtifacts.Resolve(Deployment.SelfContained, "linux-x64", release))
            .Should().Throw<ArgumentException>().WithMessage("*exact SemVer*");

    [Test]
    public void ThePortableToolNeedsNoPlatform()
    {
        var resolution = ReleaseArtifacts.Resolve(Deployment.DotnetTool, null, "1.2.3");
        var artifact = resolution.Should().BeOfType<ArtifactResolution.Defined>().Subject.Artifact;
        artifact.Platform.Should().BeNull();
        artifact.FileName.Should().Be("Morphir.Tool.1.2.3.nupkg");
        artifact.Launch.Platform.Should().Be("any");
    }

    [Test]
    public void APlatformSpecificDeploymentNeedsAPlatform()
    {
        foreach (var deployment in Deployment.All.Where(deployment => !deployment.IsPortable))
            ReleaseArtifacts.Resolve(deployment, null, "1.2.3").Should().BeOfType<ArtifactResolution.NotDefined>()
                .Which.Reason.Should().Contain("needs a platform");
    }

    [Test]
    public void ArtifactsAreMadeOnlyByResolve()
    {
        foreach (var type in new[] { typeof(ReleaseArtifact), typeof(BuildLaunchInfo), typeof(ArtifactResolution.Defined), typeof(ArtifactResolution.NotDefined) })
            type.GetConstructors().Should().BeEmpty($"{type.Name} has no public constructor");
    }

    [Test]
    public void BuildPropertiesAreImmutable()
    {
        foreach (var properties in Deployment.All.Select(deployment => deployment.BuildProperties)
                     .Concat([LegacyAsset.TrimmedSingleFile.BuildProperties, LegacyAsset.UntrimmedSingleFile.BuildProperties]))
            properties.Should().BeAssignableTo<System.Collections.Immutable.IImmutableDictionary<string, string>>();
    }

    [Test]
    public void TheLaunchMetadataStatesWhatIsNotDeclaredAndTheLayout()
    {
        string Json(Deployment deployment, string? rid) => BuildLaunchInfo.ToJson(Defined(deployment, rid, "1.2.3").Launch, "ab12");

        using var aot = JsonDocument.Parse(Json(Deployment.NativeAot, "linux-x64"));
        aot.RootElement.GetProperty("layout").GetString().Should().Be("single-file", "native-aot is one native file");
        var prerequisites = aot.RootElement.GetProperty("prerequisites");
        prerequisites.GetProperty("runtime").GetString().Should().Contain("none").And.Contain("carries");
        prerequisites.GetProperty("installation").GetString().Should().Contain("none");
        prerequisites.GetProperty("operatingSystem").GetString().Should().Contain("not declared yet");

        using var fd = JsonDocument.Parse(Json(Deployment.FrameworkDependent, "win-arm64"));
        fd.RootElement.GetProperty("layout").GetString().Should().Be("folder");
        fd.RootElement.GetProperty("prerequisites").GetProperty("runtime").GetString().Should().Be("Microsoft.NETCore.App 11.0");

        using var tool = JsonDocument.Parse(Json(Deployment.DotnetTool, null));
        tool.RootElement.GetProperty("layout").GetString().Should().Be("tool-package");
        tool.RootElement.GetProperty("prerequisites").GetProperty("installation").GetString().Should().Be(".NET SDK 11.0");

        foreach (var document in new[] { aot, fd, tool })
            Regex.IsMatch(document.RootElement.GetProperty("note").GetString()!, @"\btask\b|\b\d+\.\d+\b").Should().BeFalse("a build output names no plan tasks");
    }

    [Test]
    public void TheLegacyTargetsUseTheLegacyAliasAndNotTheLeanProperties()
    {
        TargetSettings.PublishSingleFile.Should().BeSameAs(LegacyAsset.TrimmedSingleFile.BuildProperties);
        TargetSettings.PublishSingleFileUntrimmed.Should().BeSameAs(LegacyAsset.UntrimmedSingleFile.BuildProperties);
        foreach (var legacy in new[] { TargetSettings.PublishSingleFile, TargetSettings.PublishSingleFileUntrimmed })
            legacy.Keys.Should().NotContain(["DebugType", "DebugSymbols", "CopyOutputSymbolsToPublishDirectory", "PublishDocumentationFile", "PublishReferencesDocumentationFiles"]);
        TargetSettings.PublishExecutable.Should().BeSameAs(Deployment.NativeAot.BuildProperties);
        TargetSettings.PackTool.Should().BeSameAs(Deployment.DotnetTool.BuildProperties);
    }

    [Test]
    public void ARidSpecificPublishRedirectsTheLockFileAndKeepsTheProfile()
    {
        var properties = TargetSettings.ForRidSpecificPublish(Deployment.SelfContained.BuildProperties);
        properties.Should().Contain(TargetSettings.RidSpecificPublishProperty, "true");
        properties.Where(pair => pair.Key != TargetSettings.RidSpecificPublishProperty).Should().BeEquivalentTo(Deployment.SelfContained.BuildProperties);
    }

    private static IEnumerable<ReleaseArtifact> Matrix() =>
        from release in Releases
        from deployment in Deployment.All
        from platform in TargetPlatform.All
        select Defined(deployment, platform.Rid, release);

    private static string Name(Deployment deployment, string rid, string release) => Defined(deployment, rid, release).FileName;

    private static ReleaseArtifact Defined(Deployment deployment, string? rid, string release)
    {
        var resolution = ReleaseArtifacts.Resolve(deployment, rid, release);
        resolution.Should().BeOfType<ArtifactResolution.Defined>($"the build defines {deployment.Id} on {rid}");
        return ((ArtifactResolution.Defined)resolution).Artifact;
    }
}
