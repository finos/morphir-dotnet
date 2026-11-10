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
/// Task 2.4: complete size reporting of packaged artifacts. Every file that an artifact contains, read from the
/// artifact itself, with its release, SHA-256, where it was packed, the archive size and the extracted total. Nothing
/// here is shipped, and there is no size target.
/// </summary>
public class PackagedContentsTests
{
    private static readonly PackOrigin Origin = new("0123456789abcdef0123456789abcdef01234567", WorkingTreeClean: true, new DateTimeOffset(2026, 11, 9, 20, 0, 0, TimeSpan.Zero));

    [Test]
    [Arguments(ArchiveFormat.Zip)]
    [Arguments(ArchiveFormat.TarGz)]
    public async Task TheContentsListEveryPackagedFileWithItsSize(ArchiveFormat format)
    {
        var root = Directory.CreateTempSubdirectory("morphir-packaged-");
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root.FullName, "publish dir"));
            await File.WriteAllBytesAsync(Path.Combine(source.FullName, "morphir"), new byte[1234]);
            Directory.CreateDirectory(Path.Combine(source.FullName, "de"));
            await File.WriteAllBytesAsync(Path.Combine(source.FullName, "de", "strings.resources.dll"), new byte[56]);
            await File.WriteAllBytesAsync(Path.Combine(source.FullName, "empty.txt"), []);
            var archive = Path.Combine(root.FullName, format == ArchiveFormat.Zip ? "a.zip" : "a.tar.gz");
            ReleaseArchive.Create(source.FullName, archive, format, ReleaseArchive.ExecutablesFor("morphir"));

            var contents = PackagedContents.Read(Artifact("linux-x64"), archive, Origin);

            contents.Files.Select(file => (file.Path, file.Bytes)).Should().Equal(("de/strings.resources.dll", 56L), ("empty.txt", 0L), ("morphir", 1234L));
            contents.FileCount.Should().Be(3);
            contents.ExtractedBytes.Should().Be(1290);
            contents.ArchiveBytes.Should().Be(new FileInfo(archive).Length);
            contents.Deployment.Should().Be("self-contained");
            contents.Platform.Should().Be("linux-x64");
            contents.Release.Should().Be("1.2.3");
            contents.Sha256.Should().Be(ReleaseArchive.Sha256(archive));
            contents.Origin.Should().Be(Origin);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Test]
    public async Task AToolPackageListsEveryPackageEntryAndCountsThePackageMetadata()
    {
        var root = Directory.CreateTempSubdirectory("morphir-packaged-");
        try
        {
            var package = Path.Combine(root.FullName, "Morphir.Tool.1.2.3.nupkg");
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                await using (var entry = zip.CreateEntry("tools/net10.0/any/dotnet-morphir.dll").Open()) await entry.WriteAsync(new byte[10]);
                await using (var entry = zip.CreateEntry("Morphir.Tool.nuspec").Open()) await entry.WriteAsync(new byte[3]);
                await using (var entry = zip.CreateEntry("_rels/.rels").Open()) await entry.WriteAsync(new byte[2]);
                zip.CreateEntry("tools/net10.0/any/"); // a folder entry is not a file
            }

            var tool = ((ArtifactResolution.Defined)ReleaseArtifacts.Resolve(Deployment.DotnetTool, null, "1.2.3")).Artifact;
            var contents = PackagedContents.Read(tool, package, Origin);

            contents.Files.Select(file => file.Path).Should().Equal("Morphir.Tool.nuspec", "_rels/.rels", "tools/net10.0/any/dotnet-morphir.dll");
            contents.ExtractedBytes.Should().Be(15);
            contents.Platform.Should().Be("any");
            contents.PackageMetadataEntries.Should().Be(2, "the nuspec and the package relationships are package plumbing, not tool files");
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Test]
    [Arguments(TarEntryType.SymbolicLink)]
    [Arguments(TarEntryType.HardLink)]
    public async Task ATarLinkEntryIsRefusedNotDropped(TarEntryType type)
    {
        var root = Directory.CreateTempSubdirectory("morphir-packaged-");
        try
        {
            var archive = Path.Combine(root.FullName, "a.tar.gz");
            await using (var output = File.Create(archive))
            await using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
            await using (var tar = new TarWriter(gzip, TarEntryFormat.Ustar))
            {
                await tar.WriteEntryAsync(new UstarTarEntry(TarEntryType.RegularFile, "morphir") { DataStream = new MemoryStream(new byte[4]) });
                await tar.WriteEntryAsync(new UstarTarEntry(type, "morphir-link") { LinkName = "morphir" });
            }

            FluentActions.Invoking(() => PackagedContents.Read(Artifact("linux-x64"), archive, Origin))
                .Should().Throw<InvalidDataException>().WithMessage("*morphir-link*link*");
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Test]
    public void TheContentsFileSaysItIsABuildOutputAndWhereItWasPacked()
    {
        var contents = Contents("a.zip", "native-aot", "win-arm64", "1.2.3", [new PackagedFile("morphir.exe", 28)]);
        using var json = JsonDocument.Parse(PackagedContents.ToJson(contents));
        var note = json.RootElement.GetProperty("note").GetString();
        note.Should().Contain("not a public contract").And.Contain("every file this artifact contains");
        note.Should().NotContain("ship");
        json.RootElement.TryGetProperty("schemaVersion", out _).Should().BeFalse();
        json.RootElement.GetProperty("release").GetString().Should().Be("1.2.3");
        json.RootElement.GetProperty("sha256").GetString().Should().Be("ab12");
        json.RootElement.GetProperty("origin").GetProperty("commit").GetString().Should().Be(Origin.Commit);
        json.RootElement.GetProperty("origin").GetProperty("packedAt").GetString().Should().Be("2026-11-09T20:00:00+00:00");
        json.RootElement.GetProperty("files")[0].GetProperty("path").GetString().Should().Be("morphir.exe");
        PackagedContents.FileNameFor("a.zip").Should().Be("a.zip.build-contents.json");
        PackagedContents.Parse(PackagedContents.ToJson(contents)).Should().BeEquivalentTo(contents);
    }

    [Test]
    public void TheSizeReportHasTotalsAndEveryFileForEachArtifact()
    {
        var aot = Contents("morphir-dotnet-1.2.3-native-aot-win-arm64.zip", "native-aot", "win-arm64", "1.2.3", [new PackagedFile("morphir.exe", 28_162_560)], archiveBytes: 10_494_982);
        var managed = Contents("morphir-dotnet-1.2.3-self-contained-win-arm64.zip", "self-contained", "win-arm64", "1.2.3",
            [new PackagedFile("morphir.exe", 10), new PackagedFile("de/x.resources.dll", 20)], archiveBytes: 56_278_921);

        var report = PackagedContents.SizeReport([managed, aot], "1.2.3");

        report.Should().StartWith("# Packaged sizes");
        report.Should().Contain("| Deployment | Platform | Release | Artifact | SHA-256 | Archive bytes | Files | Extracted bytes | Satellite resource folders |");
        report.Should().Contain("| native-aot | win-arm64 | 1.2.3 | `morphir-dotnet-1.2.3-native-aot-win-arm64.zip` | `ab12` | 10,494,982 | 1 | 28,162,560 | 0 |");
        report.Should().Contain("| self-contained | win-arm64 | 1.2.3 | `morphir-dotnet-1.2.3-self-contained-win-arm64.zip` | `ab12` | 56,278,921 | 2 | 30 | 1 |");
        report.IndexOf("| native-aot |", StringComparison.Ordinal).Should().BeLessThan(report.IndexOf("| self-contained |", StringComparison.Ordinal),
            "rows follow the deployment order");
        report.Should().Contain("| `de/x.resources.dll` | 20 |").And.Contain("| `morphir.exe` | 28,162,560 |");
        report.Should().Contain("not a size target");
        report.Should().NotContain("hipped");
    }

    [Test]
    public void TheToolRowSaysWhatItsCountsAre()
    {
        var tool = Contents("Morphir.Tool.1.2.3.nupkg", "dotnet-tool", "any", "1.2.3",
            [new PackagedFile("Morphir.Tool.nuspec", 3), new PackagedFile("tools/net10.0/any/dotnet-morphir.dll", 10)]);

        var report = PackagedContents.SizeReport([tool], "1.2.3");

        report.Should().Contain("| dotnet-tool (package) | any | 1.2.3 | `Morphir.Tool.1.2.3.nupkg` | `ab12` | 0 | 2 (1 package metadata) | 13 |");
        report.Should().Contain("not the installed size");
    }

    [Test]
    public void ArtifactsOfAnotherReleaseAreShownApart()
    {
        var current = Contents("morphir-dotnet-1.2.3-native-aot-win-arm64.zip", "native-aot", "win-arm64", "1.2.3", [new PackagedFile("morphir.exe", 1)]);
        var old = Contents("morphir-dotnet-1.2.2-native-aot-win-arm64.zip", "native-aot", "win-arm64", "1.2.2", [new PackagedFile("morphir.exe", 2)]);

        var report = PackagedContents.SizeReport([old, current], "1.2.3");

        var otherSection = report.IndexOf("## Other releases in this folder", StringComparison.Ordinal);
        otherSection.Should().BePositive("an artifact of another release is listed in its own section");
        report.IndexOf("`morphir-dotnet-1.2.2-native-aot-win-arm64.zip`", StringComparison.Ordinal).Should().BeGreaterThan(otherSection);
        report.IndexOf("`morphir-dotnet-1.2.3-native-aot-win-arm64.zip`", StringComparison.Ordinal).Should().BeLessThan(otherSection);
        report.Should().Contain("1.2.2");
    }

    [Test]
    public void SatelliteResourceFoldersAreTheFoldersOfResourceAssemblies()
    {
        var contents = Contents("a.zip", "self-contained", "linux-x64", "1.2.3",
        [
            new PackagedFile("de/FSharp.Compiler.Service.resources.dll", 1),
            new PackagedFile("de/FSharp.Core.resources.dll", 1),
            new PackagedFile("zh-Hans/FSharp.Core.resources.dll", 1),
            new PackagedFile("pt-BR/FSharp.Core.resources.dll", 1),
            new PackagedFile("runtimes/linux-x64/native/libx.so", 1),
            new PackagedFile("morphir", 1),
        ]);
        contents.SatelliteFolders.Should().Equal("de", "pt-BR", "zh-Hans");

        // A tool package holds its satellite folders below tools/<tfm>/any/.
        var tool = Contents("Morphir.Tool.1.2.3.nupkg", "dotnet-tool", "any", "1.2.3",
            [new PackagedFile("tools/net10.0/any/cs/FSharp.Core.resources.dll", 1), new PackagedFile("tools/net10.0/any/dotnet-morphir.dll", 1)]);
        tool.SatelliteFolders.Should().Equal("tools/net10.0/any/cs");
    }

    private static PackagedContents Contents(string artifact, string deployment, string platform, string release, IReadOnlyList<PackagedFile> files, long archiveBytes = 0) =>
        new(artifact, release, deployment, platform, "ab12", archiveBytes, files.Count, files.Sum(file => file.Bytes), files, Origin);

    private static ReleaseArtifact Artifact(string rid) =>
        ((ArtifactResolution.Defined)ReleaseArtifacts.Resolve(Deployment.SelfContained, rid, "1.2.3")).Artifact;
}
