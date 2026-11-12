// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Morphir.TestSupport.Packaging;
using Morphir.TestSupport;

namespace Morphir.TestSupport.Tests.Packaging;

/// <summary>Finding the packaged artifact under test from the launch metadata that PackRelease writes.</summary>
[Category("PlatformSpecific")]
public sealed class PackagedArtifactTests
{
    [Test]
    public void TheArtifactOfTheDeploymentAndPlatformIsFound()
    {
        using var release = new ReleaseFolder();
        release.Write("self-contained", "linux-x64", "morphir-dotnet-1.2.3-self-contained-linux-x64.tar.gz");
        release.Write("self-contained", "win-arm64", "morphir-dotnet-1.2.3-self-contained-win-arm64.zip");
        release.Write("native-aot", "linux-x64", "morphir-dotnet-1.2.3-native-aot-linux-x64.tar.gz");

        var artifact = PackagedArtifact.Find(release.Root, "self-contained", "linux-x64");
        artifact.Artifact.Should().Be("morphir-dotnet-1.2.3-self-contained-linux-x64.tar.gz");
        artifact.ArchivePath.Should().Be(Path.Combine(release.Root, artifact.Artifact));
        artifact.Release.Should().Be("1.2.3");
        artifact.BundledRuntime.Should().BeTrue();
    }

    [Test]
    public void ThePortableToolServesEveryPlatform()
    {
        using var release = new ReleaseFolder();
        release.Write("dotnet-tool", "any", "Morphir.Tool.1.2.3.nupkg");
        PackagedArtifact.Find(release.Root, "dotnet-tool", "osx-arm64").Platform.Should().Be("any");
    }

    [Test]
    public void AMissingArtifactNamesTheCommandThatPacksItAndNeverFallsBack()
    {
        using var release = new ReleaseFolder();
        release.Write("self-contained", "win-arm64", "morphir-dotnet-1.2.3-self-contained-win-arm64.zip");

        FluentActions.Invoking(() => PackagedArtifact.Find(release.Root, "self-contained", "linux-x64"))
            .Should().Throw<InvalidOperationException>().WithMessage("*No packaged self-contained artifact for linux-x64*--target PackRelease --deployment self-contained --rid linux-x64*");
        FluentActions.Invoking(() => PackagedArtifact.Find(Path.Combine(release.Root, "absent"), "native-aot", "linux-x64"))
            .Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void TwoArtifactsForOnePairAreRefused()
    {
        using var release = new ReleaseFolder();
        release.Write("framework-dependent", "linux-x64", "morphir-dotnet-1.2.3-framework-dependent-linux-x64.tar.gz");
        release.Write("framework-dependent", "linux-x64", "morphir-dotnet-1.2.4-framework-dependent-linux-x64.tar.gz", "1.2.4");

        FluentActions.Invoking(() => PackagedArtifact.Find(release.Root, "framework-dependent", "linux-x64"))
            .Should().Throw<InvalidOperationException>().WithMessage("*2 packaged framework-dependent artifacts*")
            .Which.Message.Should().Contain("morphir-dotnet-1.2.3-framework-dependent-linux-x64.tar.gz")
            .And.Contain("morphir-dotnet-1.2.4-framework-dependent-linux-x64.tar.gz");
    }

    private sealed class ReleaseFolder : IDisposable
    {
        private readonly TestSandbox _sandbox = TestSandbox.Create("morphir release ");

        public ReleaseFolder() => Root = Directory.CreateDirectory(_sandbox.PathOf("release")).FullName;

        public string Root { get; }

        public void Write(string deployment, string platform, string artifact, string release = "1.2.3") =>
            File.WriteAllText(Path.Combine(Root, $"{artifact}.build-launch.json"), JsonSerializer.Serialize(new
            {
                note = "test",
                implementation = "dotnet",
                release,
                deployment,
                platform,
                artifact,
                sha256 = "ab",
                launchPath = deployment == "dotnet-tool" ? "dotnet-morphir" : "morphir",
                execution = deployment == "native-aot" ? "native" : "managed",
                layout = "folder",
                bundledRuntime = deployment is "native-aot" or "self-contained",
                requiredRuntime = deployment is "native-aot" or "self-contained" ? null : "Microsoft.NETCore.App 11.0",
                requiredSdk = deployment == "dotnet-tool" ? ".NET SDK 11.0" : null,
                inProcessFSharpFrontend = deployment != "native-aot",
            }));

        public void Dispose() => _sandbox.Dispose();
    }
}
