// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
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
            var artifact = Supported(Deployment.DotnetTool, platform.Rid, "1.2.3");
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
            resolution.Should().BeOfType<ArtifactResolution.Unsupported>();
            var unsupported = (ArtifactResolution.Unsupported)resolution;
            unsupported.Deployment.Should().Be(deployment);
            unsupported.Rid.Should().Be(rid);
            unsupported.Reason.Should().Contain(rid.Length == 0 ? "no platform" : rid);
            foreach (var platform in TargetPlatform.All) unsupported.Reason.Should().Contain(platform.Rid);
        }
    }

    [Test]
    public void AnInvalidReleaseIsRefused() =>
        FluentActions.Invoking(() => ReleaseArtifacts.Resolve(Deployment.NativeAot, "win-x64", "1.2")).Should().Throw<ArgumentException>();

    [Test]
    public void TheLaunchPathIsTheMorphirExecutableOfThePlatform()
    {
        foreach (var deployment in new[] { Deployment.NativeAot, Deployment.SelfContained, Deployment.FrameworkDependent })
        {
            Supported(deployment, "win-arm64", "1.2.3").Launch.LaunchPath.Should().Be("morphir.exe");
            Supported(deployment, "linux-x64", "1.2.3").Launch.LaunchPath.Should().Be("morphir");
            Supported(deployment, "osx-arm64", "1.2.3").Launch.LaunchPath.Should().Be("morphir");
        }
    }

    [Test]
    public void EachDeploymentStatesHowItRuns()
    {
        var aot = Supported(Deployment.NativeAot, "win-x64", "1.2.3").Launch;
        aot.Execution.Should().Be("native");
        aot.BundledRuntime.Should().BeTrue();
        aot.RequiredRuntime.Should().BeNull();
        aot.RequiredSdk.Should().BeNull();
        aot.InProcessFSharpFrontend.Should().BeFalse();

        var selfContained = Supported(Deployment.SelfContained, "win-x64", "1.2.3").Launch;
        selfContained.Execution.Should().Be("managed");
        selfContained.BundledRuntime.Should().BeTrue();
        selfContained.RequiredRuntime.Should().BeNull();
        selfContained.InProcessFSharpFrontend.Should().BeTrue();

        var frameworkDependent = Supported(Deployment.FrameworkDependent, "win-x64", "1.2.3").Launch;
        frameworkDependent.Execution.Should().Be("managed");
        frameworkDependent.BundledRuntime.Should().BeFalse();
        frameworkDependent.RequiredRuntime.Should().Be("Microsoft.NETCore.App 10.0");
        frameworkDependent.RequiredSdk.Should().BeNull();
        frameworkDependent.InProcessFSharpFrontend.Should().BeTrue();

        var tool = Supported(Deployment.DotnetTool, "win-x64", "1.2.3").Launch;
        tool.Execution.Should().Be("managed");
        tool.BundledRuntime.Should().BeFalse();
        tool.RequiredRuntime.Should().Be("Microsoft.NETCore.App 10.0");
        tool.RequiredSdk.Should().Be(".NET SDK 10.0", "installing a .NET tool needs the SDK");
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
        var workflow = File.ReadAllLines(Path.Combine(PathHelper.GetRepositoryRoot(), ".github", "workflows", "deployment.yml"));
        var rids = workflow.Select(line => line.Trim()).Where(line => line.StartsWith("- rid: ", StringComparison.Ordinal))
            .Select(line => line["- rid: ".Length..]).ToList();
        rids.Should().BeEquivalentTo(LegacyAsset.TrimmedSingleFile.ReleasedPlatforms.Select(platform => platform.Rid));

        var text = string.Join('\n', workflow);
        text.Should().Contain("--target PublishSingleFile", "releases build the legacy asset with the PublishSingleFile target");
        text.Should().Contain("morphir-$rid$suffix").And.Contain("morphir-$rid-v$VERSION.tar.gz");
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
        var json = JsonDocument.Parse(BuildLaunchInfo.ToJson(Supported(Deployment.FrameworkDependent, "win-arm64", "1.2.3").Launch, "ab12"));
        var root = json.RootElement;
        root.GetProperty("note").GetString().Should().Contain("not a public contract").And.Contain("tool release metadata");
        root.TryGetProperty("schemaVersion", out _).Should().BeFalse("the build-side file has no contract version");
        root.GetProperty("deployment").GetString().Should().Be("framework-dependent");
        root.GetProperty("platform").GetString().Should().Be("win-arm64");
        root.GetProperty("launchPath").GetString().Should().Be("morphir.exe");
        root.GetProperty("requiredRuntime").GetString().Should().Be("Microsoft.NETCore.App 10.0");
        root.GetProperty("sha256").GetString().Should().Be("ab12");
        BuildLaunchInfo.FileNameFor("x.zip").Should().Be("x.zip.build-launch.json");
    }

    [Test]
    public async Task ATarArchiveKeepsTheLaunchPathExecutable()
    {
        var root = Directory.CreateTempSubdirectory("morphir-release-archive-");
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root.FullName, "publish dir"));
            await File.WriteAllTextAsync(Path.Combine(source.FullName, "morphir"), "launcher");
            Directory.CreateDirectory(Path.Combine(source.FullName, "sub"));
            await File.WriteAllTextAsync(Path.Combine(source.FullName, "sub", "data.json"), "{}");
            var archive = Path.Combine(root.FullName, "a.tar.gz");

            ReleaseArchive.Create(source.FullName, archive, ArchiveFormat.TarGz, "morphir");

            await using var gzip = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);
            using var reader = new TarReader(gzip);
            var entries = new Dictionary<string, UnixFileMode>();
            while (reader.GetNextEntry() is { } entry) entries[entry.Name] = entry.Mode;
            entries.Keys.Should().BeEquivalentTo("morphir", "sub/data.json");
            entries["morphir"].Should().HaveFlag(UnixFileMode.UserExecute).And.HaveFlag(UnixFileMode.OtherExecute);
            entries["sub/data.json"].Should().NotHaveFlag(UnixFileMode.UserExecute);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Test]
    public async Task AZipArchiveHoldsThePublishFolderAtItsRoot()
    {
        var root = Directory.CreateTempSubdirectory("morphir-release-archive-");
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root.FullName, "publish dir"));
            await File.WriteAllTextAsync(Path.Combine(source.FullName, "morphir.exe"), "launcher");
            Directory.CreateDirectory(Path.Combine(source.FullName, "sub"));
            await File.WriteAllTextAsync(Path.Combine(source.FullName, "sub", "data.json"), "{}");
            var archive = Path.Combine(root.FullName, "a.zip");

            ReleaseArchive.Create(source.FullName, archive, ArchiveFormat.Zip, "morphir.exe");

            using var zip = ZipFile.OpenRead(archive);
            zip.Entries.Select(entry => entry.FullName).Should().BeEquivalentTo("morphir.exe", "sub/data.json");
        }
        finally
        {
            root.Delete(true);
        }
    }

    private static IEnumerable<ReleaseArtifact> Matrix() =>
        from release in Releases
        from deployment in Deployment.All
        from platform in TargetPlatform.All
        select Supported(deployment, platform.Rid, release);

    private static string Name(Deployment deployment, string rid, string release) => Supported(deployment, rid, release).FileName;

    private static ReleaseArtifact Supported(Deployment deployment, string rid, string release)
    {
        var resolution = ReleaseArtifacts.Resolve(deployment, rid, release);
        resolution.Should().BeOfType<ArtifactResolution.Supported>($"{deployment.Id} on {rid} is a first-delivery pair");
        return ((ArtifactResolution.Supported)resolution).Artifact;
    }
}
