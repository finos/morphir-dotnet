// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Morphir.Build.Helpers;
using Morphir.TestSupport;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// PackRelease checks the publish folder against the launch metadata it is about to write, and fails on a
/// contradiction: the metadata must describe the bytes that ship.
/// </summary>
public class PublishedLayoutTests
{
    [Test]
    public async Task ASelfContainedFolderCarriesTheRuntime()
    {
        using var folder = new Folder();
        await folder.Write("morphir.exe", "coreclr.dll", "FSharp.Compiler.Service.dll");
        Check(Deployment.SelfContained, "win-x64", folder).Should().BeEmpty();

        File.Delete(folder.Path("coreclr.dll"));
        Check(Deployment.SelfContained, "win-x64", folder).Should().ContainSingle().Which.Should().Contain("coreclr");
    }

    [Test]
    [Arguments("linux-x64", "libcoreclr.so")]
    [Arguments("osx-arm64", "libcoreclr.dylib")]
    public async Task TheRuntimeIsNamedForThePlatform(string rid, string coreclr)
    {
        using var folder = new Folder();
        await folder.Write("morphir", coreclr, "FSharp.Compiler.Service.dll");
        Check(Deployment.SelfContained, rid, folder).Should().BeEmpty();
    }

    [Test]
    public async Task AFrameworkDependentFolderNamesItsFrameworkAndCarriesNoRuntime()
    {
        using var folder = new Folder();
        await folder.Write("morphir.exe", "FSharp.Compiler.Service.dll");
        await folder.RuntimeConfig("""{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}""");
        Check(Deployment.FrameworkDependent, "win-arm64", folder).Should().BeEmpty();

        await folder.Write("coreclr.dll");
        Check(Deployment.FrameworkDependent, "win-arm64", folder).Should().ContainSingle().Which.Should().Contain("coreclr");
    }

    [Test]
    public async Task AFrameworkDependentFolderWithoutAFrameworkReferenceIsAContradiction()
    {
        using var folder = new Folder();
        await folder.Write("morphir.exe", "FSharp.Compiler.Service.dll");
        Check(Deployment.FrameworkDependent, "win-arm64", folder).Should().ContainSingle().Which.Should().Contain("morphir.runtimeconfig.json");

        await folder.RuntimeConfig("""{"runtimeOptions":{"tfm":"net10.0","includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"}]}}""");
        Check(Deployment.FrameworkDependent, "win-arm64", folder).Should().ContainSingle().Which.Should().Contain("Microsoft.NETCore.App");
    }

    [Test]
    public async Task TheFSharpCompilerShipsExactlyWithTheInProcessFrontend()
    {
        using var managed = new Folder();
        await managed.Write("morphir", "libcoreclr.so");
        Check(Deployment.SelfContained, "linux-x64", managed).Should().ContainSingle().Which.Should().Contain("FSharp.Compiler.Service.dll");

        using var aot = new Folder();
        await aot.Write("morphir", "FSharp.Compiler.Service.dll");
        Check(Deployment.NativeAot, "linux-x64", aot).Should().Contain(problem => problem.Contains("FSharp.Compiler.Service.dll"));
    }

    [Test]
    public async Task ANativeAotFolderShipsNoManagedAssembly()
    {
        using var folder = new Folder();
        await folder.Write("morphir.exe");
        Check(Deployment.NativeAot, "win-x64", folder).Should().BeEmpty("a native file is not a managed assembly");

        File.Copy(typeof(PublishedLayout).Assembly.Location, folder.Path("Morphir.Build.dll"));
        Check(Deployment.NativeAot, "win-x64", folder).Should()
            .Contain(problem => problem.Contains("Morphir.Build.dll") && problem.Contains("managed"));
    }

    [Test]
    public async Task ASingleFileArtifactHoldsOnlyItsLaunchPath()
    {
        // native-aot is declared single-file (task 5.3): one file, the launch path, and nothing beside it.
        using var folder = new Folder();
        await folder.Write("morphir.exe");
        Check(Deployment.NativeAot, "win-arm64", folder).Should().BeEmpty();

        // The native-aot publish before the engine was linked in: the executable and wasmtime.dll beside it.
        await folder.Write("wasmtime.dll");
        Check(Deployment.NativeAot, "win-arm64", folder).Should().ContainSingle()
            .Which.Should().Contain("single-file").And.Contain("wasmtime.dll").And.Contain("morphir.exe");

        Directory.CreateDirectory(folder.Path("sub"));
        await folder.Write("sub/notes.txt");
        Check(Deployment.NativeAot, "win-arm64", folder).Should().HaveCount(2)
            .And.Contain(problem => problem.Contains("sub/notes.txt"));
    }

    [Test]
    public async Task AFolderArtifactMayHoldFilesBesideItsLaunchPath()
    {
        using var folder = new Folder();
        await folder.Write("morphir.exe", "coreclr.dll", "FSharp.Compiler.Service.dll", "wasmtime.dll");
        Check(Deployment.SelfContained, "win-arm64", folder).Should().BeEmpty();
    }

    [Test]
    public async Task TheLaunchPathMustExist()
    {
        using var folder = new Folder();
        await folder.Write("other.exe");
        Check(Deployment.NativeAot, "win-x64", folder).Should()
            .Contain(problem => problem.Contains("the launch path morphir.exe is not in the publish folder"));
    }

    [Test]
    public void TheToolPackageIsNotAPublishFolder() =>
        FluentActions.Invoking(() => PublishedLayout.Check(Artifact(Deployment.DotnetTool, null), Path.GetTempPath()))
            .Should().Throw<ArgumentException>();

    private static IReadOnlyList<string> Check(Deployment deployment, string rid, Folder folder) =>
        PublishedLayout.Check(Artifact(deployment, rid), folder.Root);

    private static ReleaseArtifact Artifact(Deployment deployment, string? rid) =>
        ((ArtifactResolution.Defined)ReleaseArtifacts.Resolve(deployment, rid, "1.2.3")).Artifact;

    /// <summary>A publish folder inside a <see cref="TestSandbox"/>, which cleans up with TolerantDirectory.</summary>
    private sealed class Folder : IDisposable
    {
        private readonly TestSandbox _sandbox = TestSandbox.Create("morphir-published-layout-");

        public Folder() => Root = Directory.CreateDirectory(_sandbox.PathOf("publish")).FullName;

        public string Root { get; }

        public string Path(string name) => System.IO.Path.Combine(Root, name);

        public async Task Write(params string[] names)
        {
            foreach (var name in names) await File.WriteAllTextAsync(Path(name), name);
        }

        public Task RuntimeConfig(string json)
        {
            JsonDocument.Parse(json).Dispose();
            return File.WriteAllTextAsync(Path("morphir.runtimeconfig.json"), json);
        }

        public void Dispose() => _sandbox.Dispose();
    }
}
