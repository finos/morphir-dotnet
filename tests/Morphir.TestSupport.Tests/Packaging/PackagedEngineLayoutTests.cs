// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Morphir.TestSupport.Packaging;
using Morphir.TestSupport;

namespace Morphir.TestSupport.Tests.Packaging;

/// <summary>
/// The engine layout check of the packaged feature (add-wasm-extension-host, task 5.2): the engine library files of an
/// installed artifact against the <c>engines</c> its build contents record. Each wrong layout here is one a pack could
/// produce: an extra library, another platform's library in its place, a library beside a linked engine.
/// </summary>
[Category("PlatformSpecific")]
public sealed class PackagedEngineLayoutTests
{
    private const string WindowsEngine = "windows engine bytes";
    private const string LinuxEngine = "linux engine bytes";

    [Test]
    public void AnArchiveWithTheRecordedLibraryAgrees()
    {
        using var install = new Install();
        install.Write("morphir.exe", "app");
        var engine = install.Write("wasmtime.dll", WindowsEngine);

        Check(install, "self-contained", "win-arm64", Separate("wasmtime.dll", "win-arm64", engine)).Should().BeEmpty();
    }

    [Test]
    public void AnExtraEngineLibraryIsNamed()
    {
        using var install = new Install();
        var engine = install.Write("wasmtime.dll", WindowsEngine);
        install.Write("libwasmtime.so", LinuxEngine);

        Check(install, "framework-dependent", "win-arm64", Separate("wasmtime.dll", "win-arm64", engine))
            .Should().ContainSingle().Which.Should().Contain("libwasmtime.so").And.Contain("not recorded");
    }

    [Test]
    public void AnotherPlatformsLibraryInPlaceOfTheRecordedOneIsRefusedByDigest()
    {
        using var install = new Install();
        install.Write("wasmtime.dll", "the win-x64 engine bytes");

        Check(install, "self-contained", "win-arm64", Separate("wasmtime.dll", "win-arm64", Sha256(WindowsEngine)))
            .Should().ContainSingle().Which.Should().Contain("wasmtime.dll").And.Contain(Sha256(WindowsEngine));
    }

    [Test]
    public void ARecordedLibraryThatIsNotInstalledIsNamed()
    {
        using var install = new Install();
        install.Write("morphir.exe", "app");

        Check(install, "self-contained", "win-arm64", Separate("wasmtime.dll", "win-arm64", Sha256(WindowsEngine)))
            .Should().ContainSingle().Which.Should().Contain("wasmtime.dll").And.Contain("not in the installed artifact");
    }

    [Test]
    public void ALinkedEngineHasNoLibraryFile()
    {
        using var install = new Install();
        install.Write("morphir.exe", "app");
        Check(install, "native-aot", "win-arm64", Linked("morphir.exe", "win-arm64")).Should().BeEmpty();

        install.Write("wasmtime.dll", WindowsEngine);
        Check(install, "native-aot", "win-arm64", Linked("morphir.exe", "win-arm64"))
            .Should().ContainSingle().Which.Should().Contain("wasmtime.dll").And.Contain("not recorded");
    }

    [Test]
    public void ALinkedEngineNamesAnExecutableOfTheArtifact()
    {
        using var install = new Install();
        install.Write("morphir.exe", "app");

        Check(install, "native-aot", "win-arm64", Linked("morphir", "win-arm64"))
            .Should().ContainSingle().Which.Should().Contain("morphir").And.Contain("linked into");
    }

    [Test]
    public void AnArchiveRecordedForAnotherPlatformIsRefused()
    {
        using var install = new Install();
        var engine = install.Write("wasmtime.dll", WindowsEngine);

        Check(install, "self-contained", "win-arm64", Separate("wasmtime.dll", "win-x64", engine))
            .Should().ContainSingle().Which.Should().Contain("win-x64").And.Contain("win-arm64");
    }

    [Test]
    public void AnArchiveCarriesOneEngine()
    {
        using var install = new Install();
        var engine = install.Write("wasmtime.dll", WindowsEngine);
        var other = install.Write("runtimes/win-arm64/native/wasmtime.dll", WindowsEngine);

        Check(install, "self-contained", "win-arm64",
                Separate("wasmtime.dll", "win-arm64", engine), Separate("runtimes/win-arm64/native/wasmtime.dll", "win-arm64", other))
            .Should().ContainSingle().Which.Should().Contain("2 engines");
    }

    [Test]
    public void NoEnginesRecordedIsAProblem()
    {
        using var install = new Install();
        install.Write("morphir.exe", "app");

        PackagedEngineLayout.Problems(install.Root, install.Root, "self-contained", "win-arm64", null)
            .Should().ContainSingle().Which.Should().Contain("record no engines");
    }

    [Test]
    public void TheToolCarriesEachRecordedPlatformUnderItsOwnFolder()
    {
        using var install = new Install();
        var store = ".store/morphir.tool/1.2.3/morphir.tool/1.2.3";
        install.Write("dotnet-morphir.exe", "shim");
        var windows = install.Write($"{store}/tools/net10.0/any/runtimes/win-arm64/native/wasmtime.dll", WindowsEngine);
        var linux = install.Write($"{store}/tools/net10.0/any/runtimes/linux-x64/native/libwasmtime.so", LinuxEngine);
        var root = Path.Combine(install.Root, store);
        RecordedEngine[] recorded =
        [
            Separate("tools/net10.0/any/runtimes/linux-x64/native/libwasmtime.so", "linux-x64", linux),
            Separate("tools/net10.0/any/runtimes/win-arm64/native/wasmtime.dll", "win-arm64", windows),
        ];

        PackagedEngineLayout.Problems(install.Root, root, "dotnet-tool", "win-arm64", recorded).Should().BeEmpty();
        PackagedEngineLayout.Problems(install.Root, root, "dotnet-tool", "osx-arm64", recorded)
            .Should().ContainSingle().Which.Should().Contain("osx-arm64");
    }

    [Test]
    public void ToolLibrariesOutsideTheirPlatformFolderOrTheStoreAreRefused()
    {
        using var install = new Install();
        var store = ".store/morphir.tool/1.2.3/morphir.tool/1.2.3";
        var windows = install.Write($"{store}/tools/net10.0/any/runtimes/win-x64/native/wasmtime.dll", WindowsEngine);
        install.Write("wasmtime.dll", WindowsEngine);
        var root = Path.Combine(install.Root, store);

        var problems = PackagedEngineLayout.Problems(install.Root, root, "dotnet-tool", "win-arm64",
            [Separate("tools/net10.0/any/runtimes/win-x64/native/wasmtime.dll", "win-arm64", windows)]);

        problems.Should().Contain(problem => problem.Contains("outside the artifact's files", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.Contains("runtimes/win-arm64/native/", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> Check(Install install, string deployment, string rid, params RecordedEngine[] recorded) =>
        PackagedEngineLayout.Problems(install.Root, install.Root, deployment, rid, recorded);

    private static RecordedEngine Separate(string path, string platform, string sha256) =>
        new(path, platform, sha256, PackagedEngineLayout.SeparateLibrary, "50.0.1");

    private static RecordedEngine Linked(string path, string platform) =>
        new(path, platform, Sha256("c api archive"), PackagedEngineLayout.Linked, "50.0.1");

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private sealed class Install : IDisposable
    {
        private readonly TestSandbox _sandbox = TestSandbox.Create("morphir engine layout ");

        public Install() => Root = Directory.CreateDirectory(_sandbox.PathOf("install dir")).FullName;

        public string Root { get; }

        /// <summary>Writes a file below the installation and returns the SHA-256 of its text.</summary>
        public string Write(string relative, string text)
        {
            var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return Sha256(text);
        }

        public void Dispose() => _sandbox.Dispose();
    }
}
