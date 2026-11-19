// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Morphir.Build.Helpers;
using Morphir.TestSupport;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// Tasks 5.2 and 5.3 of add-wasm-extension-host: what WASM engine each packaged offering carries, read from the
/// artifact and checked against the pinned engine. A separate library must be byte for byte the Wasmtime package's file
/// for its platform; a linked engine is recorded by the pinned C API archive it was linked from; an artifact that
/// carries a separate library cannot claim single-file delivery.
/// </summary>
[Category("PlatformSpecific")]
public class PackagedEngineTests
{
    private static readonly IReadOnlyDictionary<string, byte[]> LibraryBytes = TargetPlatform.All.ToDictionary(
        platform => platform.Rid, platform => System.Text.Encoding.UTF8.GetBytes($"engine library of {platform.Rid}"));

    private static readonly EnginePin Pin = new(
        "50.0.1",
        TargetPlatform.All.ToDictionary(platform => platform.Rid, platform => new PinnedLibrary(
            platform.Rid, PackagedEngines.LibraryFileName(platform.Rid), Sha(LibraryBytes[platform.Rid]))),
        "50.0.1",
        TargetPlatform.All.ToDictionary(platform => platform.Rid, platform => new PinnedArchive(
            platform.Rid, $"https://example.invalid/wasmtime-v50.0.1-{platform.Rid}-c-api.zip", Sha([.. LibraryBytes[platform.Rid], 1]))));

    [Test]
    public void AMatchingLibraryIsRecordedAsASeparateLibraryOfItsPlatform()
    {
        var check = Check(Deployment.SelfContained, "win-arm64", Library("wasmtime.dll", "win-arm64"));

        check.Problems.Should().BeEmpty();
        check.Engines.Should().ContainSingle().Which.Should().Be(new PackagedEngine(
            "wasmtime.dll", "win-arm64", Pin.PackageLibraries["win-arm64"].Sha256, EngineDelivery.SeparateLibrary, "50.0.1",
            "Wasmtime 50.0.1 package, runtimes/win-arm64/native/wasmtime.dll"));
    }

    [Test]
    public void ALibraryWithOneFlippedByteFailsWithBothDigests()
    {
        var bytes = LibraryBytes["linux-x64"].ToArray();
        bytes[3] ^= 0x01;

        var check = Check(Deployment.FrameworkDependent, "linux-x64", new EngineLibraryEntry("libwasmtime.so", Sha(bytes)));

        check.Problems.Should().ContainSingle().Which.Should()
            .Contain("libwasmtime.so").And.Contain(Sha(bytes)).And.Contain(Pin.PackageLibraries["linux-x64"].Sha256);
    }

    [Test]
    public void ALibraryOfAnotherPlatformIsRefused()
    {
        // In the folder of another platform ...
        var nested = Check(Deployment.SelfContained, "win-arm64",
            Library("wasmtime.dll", "win-arm64"), Library("runtimes/win-x64/native/wasmtime.dll", "win-x64"));
        nested.Problems.Should().ContainSingle().Which.Should().Contain("runtimes/win-x64/native/wasmtime.dll").And.Contain("win-x64").And.Contain("win-arm64");

        // ... or at the root, with another platform's bytes: the message names the platform the bytes are for.
        var swapped = Check(Deployment.SelfContained, "win-arm64", new EngineLibraryEntry("wasmtime.dll", Pin.PackageLibraries["win-x64"].Sha256));
        swapped.Problems.Should().ContainSingle().Which.Should().Contain("win-x64").And.Contain(Pin.PackageLibraries["win-arm64"].Sha256);

        // ... or under the other operating system's file name.
        var foreign = Check(Deployment.SelfContained, "linux-x64", Library("libwasmtime.so", "linux-x64"), new EngineLibraryEntry("wasmtime.dll", Pin.PackageLibraries["win-x64"].Sha256));
        foreign.Problems.Should().ContainSingle().Which.Should().Contain("wasmtime.dll").And.Contain("linux-x64");
    }

    [Test]
    [Arguments("self-contained")]
    [Arguments("framework-dependent")]
    public void AnOfferingThatShouldCarryALibraryAndDoesNotIsRefused(string deployment)
    {
        Deployment.TryParse(deployment, out var offering).Should().BeTrue();

        var check = Check(offering!, "osx-arm64");

        check.Engines.Should().BeEmpty();
        check.Problems.Should().ContainSingle().Which.Should().Contain("libwasmtime.dylib").And.Contain("osx-arm64");
    }

    [Test]
    public void ALinkedEngineIsRecordedByItsPinnedCApiArchive()
    {
        var check = Check(Deployment.NativeAot, "win-arm64");

        check.Problems.Should().BeEmpty();
        var archive = Pin.CApiArchives["win-arm64"];
        check.Engines.Should().ContainSingle().Which.Should().Be(new PackagedEngine(
            "morphir.exe", "win-arm64", archive.Sha256, EngineDelivery.Linked, "50.0.1", archive.Url));
    }

    [Test]
    public void ALinkedEngineNeedsAPinnedArchiveForItsPlatform()
    {
        var pin = Pin with { CApiArchives = Pin.CApiArchives.Where(pair => pair.Key != "osx-x64").ToDictionary() };

        var check = PackagedEngines.Check(Artifact(Deployment.NativeAot, "osx-x64"), [], pin);

        check.Problems.Should().ContainSingle().Which.Should().Contain("osx-x64").And.Contain("C API archive");
    }

    [Test]
    public void ASingleFileArtifactWithASeparateLibraryIsRefused()
    {
        // The native-aot artifact before the engine was linked in: morphir.exe and wasmtime.dll. A platform that ships a
        // separate library does not claim single-file delivery.
        var check = Check(Deployment.NativeAot, "win-arm64", Library("wasmtime.dll", "win-arm64"));

        Artifact(Deployment.NativeAot, "win-arm64").Launch.Layout.Should().Be("single-file");
        check.Problems.Should().Contain(problem => problem.Contains("single-file") && problem.Contains("wasmtime.dll") && problem.Contains("separate library"));
        check.Problems.Should().Contain(problem => problem.Contains("links the engine into morphir.exe"));
        check.Engines.Should().ContainSingle().Which.Delivery.Should().Be(EngineDelivery.SeparateLibrary);
    }

    [Test]
    public void TheDeliveryFollowsTheStaticLinkSwitch()
    {
        PackagedEngines.DeliveryFor(Deployment.NativeAot).Should().Be(EngineDelivery.Linked);
        foreach (var deployment in Deployment.All.Where(deployment => deployment != Deployment.NativeAot))
            PackagedEngines.DeliveryFor(deployment).Should().Be(EngineDelivery.SeparateLibrary, deployment.Id);
    }

    [Test]
    public void ThePortableToolRecordsTheLibraryOfEveryPlatform()
    {
        var libraries = TargetPlatform.All
            .Select(platform => Library($"tools/net10.0/any/runtimes/{platform.Rid}/native/{PackagedEngines.LibraryFileName(platform.Rid)}", platform.Rid))
            .ToArray();

        var check = Check(Deployment.DotnetTool, null, libraries);

        check.Problems.Should().BeEmpty();
        check.Engines.Select(engine => engine.Platform).Should().Equal(TargetPlatform.All.Select(platform => platform.Rid));
        check.Engines.Should().OnlyContain(engine => engine.Delivery == EngineDelivery.SeparateLibrary);

        var missing = Check(Deployment.DotnetTool, null, libraries.Where(library => !library.Path.Contains("osx-x64")).ToArray());
        missing.Problems.Should().ContainSingle().Which.Should().Contain("osx-x64");
    }

    [Test]
    [Arguments(ArchiveFormat.Zip)]
    [Arguments(ArchiveFormat.TarGz)]
    public async Task TheEngineLibrariesAreReadFromTheArtifact(ArchiveFormat format)
    {
        await using var sandbox = TestSandbox.Create("morphir-packaged-engine-");
        var source = Directory.CreateDirectory(sandbox.PathOf("publish dir")).FullName;
        await File.WriteAllTextAsync(Path.Combine(source, "morphir"), "executable");
        await File.WriteAllBytesAsync(Path.Combine(source, "libwasmtime.so"), LibraryBytes["linux-arm64"]);
        Directory.CreateDirectory(Path.Combine(source, "runtimes", "osx-x64", "native"));
        await File.WriteAllBytesAsync(Path.Combine(source, "runtimes", "osx-x64", "native", "libwasmtime.dylib"), LibraryBytes["osx-x64"]);
        var archive = sandbox.PathOf(format == ArchiveFormat.Zip ? "a.zip" : "a.tar.gz");
        ReleaseArchive.Create(source, archive, format, ReleaseArchive.ExecutablesFor("morphir"));

        PackagedEngines.Read(archive).Should().Equal(
            new EngineLibraryEntry("libwasmtime.so", Sha(LibraryBytes["linux-arm64"])),
            new EngineLibraryEntry("runtimes/osx-x64/native/libwasmtime.dylib", Sha(LibraryBytes["osx-x64"])));
    }

    [Test]
    public async Task ThePinIsReadFromTheRestoreAndTheCApiPins()
    {
        await using var sandbox = TestSandbox.Create("morphir-engine-pin-");
        var packages = Directory.CreateDirectory(sandbox.PathOf("nuget packages")).FullName;
        var package = Path.Combine(packages, "wasmtime", "50.0.1");
        foreach (var platform in TargetPlatform.All)
        {
            var native = Directory.CreateDirectory(Path.Combine(package, "runtimes", platform.Rid, "native")).FullName;
            await File.WriteAllBytesAsync(Path.Combine(native, PackagedEngines.LibraryFileName(platform.Rid)), LibraryBytes[platform.Rid]);
        }

        var assets = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["version"] = 3,
            ["libraries"] = new Dictionary<string, object> { ["Wasmtime/50.0.1"] = new { type = "package", path = "wasmtime/50.0.1" } },
            ["packageFolders"] = new Dictionary<string, object> { [packages + Path.DirectorySeparatorChar] = new { } },
        });
        var props = Path.Combine(PathHelper.GetRepositoryRoot(), "build", "wasmtime", "WasmtimeCApi.props");

        var pin = EnginePin.Read(assets, File.ReadAllText(props));

        pin.PackageVersion.Should().Be("50.0.1");
        pin.PackageLibraries.Keys.Should().BeEquivalentTo(TargetPlatform.All.Select(platform => platform.Rid));
        pin.PackageLibraries["win-arm64"].Should().Be(new PinnedLibrary("win-arm64", "wasmtime.dll", Sha(LibraryBytes["win-arm64"])));

        // The C API pins are the committed ones, read from the props file, not repeated here.
        var pinned = XDocument.Load(props).Descendants("MorphirWasmtimeCApiArchive")
            .ToDictionary(element => (string)element.Attribute("Include")!, element => (Url: (string)element.Element("Url")!, Sha256: (string)element.Element("Sha256")!));
        pin.CApiVersion.Should().Be(XDocument.Load(props).Descendants("MorphirWasmtimeCApiVersion").Single().Value);
        pin.CApiArchives.Keys.Should().BeEquivalentTo(TargetPlatform.All.Select(platform => platform.Rid));
        foreach (var (rid, archive) in pin.CApiArchives)
            (archive.Url, archive.Sha256).Should().Be(pinned[rid]);
    }

    [Test]
    public void ARestoreWithoutTheWasmtimePackageIsRefused()
    {
        var assets = """{"version":3,"libraries":{},"packageFolders":{"/nowhere/":{}}}""";
        var props = File.ReadAllText(Path.Combine(PathHelper.GetRepositoryRoot(), "build", "wasmtime", "WasmtimeCApi.props"));

        FluentActions.Invoking(() => EnginePin.Read(assets, props)).Should().Throw<InvalidOperationException>().WithMessage("*Wasmtime*");
    }

    [Test]
    public void TheEngineRecordIsWrittenIntoTheContentsFile()
    {
        var engine = new PackagedEngine("morphir.exe", "win-arm64", "cd34", EngineDelivery.Linked, "50.0.1", "https://example.invalid/a.zip");
        var contents = new PackagedContents("a.zip", "1.2.3", "native-aot", "win-arm64", "ab12", 1, 1, 1, [new PackagedFile("morphir.exe", 1)], null)
        {
            Engines = [engine],
        };

        using var json = JsonDocument.Parse(PackagedContents.ToJson(contents));
        var written = json.RootElement.GetProperty("engines").EnumerateArray().Should().ContainSingle().Subject;
        written.GetProperty("path").GetString().Should().Be("morphir.exe");
        written.GetProperty("platform").GetString().Should().Be("win-arm64");
        written.GetProperty("sha256").GetString().Should().Be("cd34");
        written.GetProperty("delivery").GetString().Should().Be("linked");
        written.GetProperty("version").GetString().Should().Be("50.0.1");
        json.RootElement.GetProperty("files")[0].GetProperty("path").GetString().Should().Be("morphir.exe", "the existing members keep their names");
        PackagedContents.Parse(PackagedContents.ToJson(contents)).Engines.Should().Equal(engine);

        var separate = engine with { Delivery = EngineDelivery.SeparateLibrary };
        JsonDocument.Parse(PackagedContents.ToJson(contents with { Engines = [separate] })).RootElement
            .GetProperty("engines")[0].GetProperty("delivery").GetString().Should().Be("separate-library");
    }

    private static EngineCheck Check(Deployment deployment, string? rid, params EngineLibraryEntry[] libraries) =>
        PackagedEngines.Check(Artifact(deployment, rid), libraries, Pin);

    private static EngineLibraryEntry Library(string path, string rid) => new(path, Pin.PackageLibraries[rid].Sha256);

    private static ReleaseArtifact Artifact(Deployment deployment, string? rid) =>
        ((ArtifactResolution.Defined)ReleaseArtifacts.Resolve(deployment, rid, "1.2.3")).Artifact;

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
