// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Morphir.Build.Helpers;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// Task 2.4: complete shipped-size reporting. Every file that an artifact ships, read from the artifact itself, with the
/// archive size and the extracted total. No size target.
/// </summary>
public class ShippedContentsTests
{
    [Test]
    [Arguments(ArchiveFormat.Zip)]
    [Arguments(ArchiveFormat.TarGz)]
    public async Task TheContentsListEveryShippedFileWithItsSize(ArchiveFormat format)
    {
        var root = Directory.CreateTempSubdirectory("morphir-shipped-");
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root.FullName, "publish dir"));
            await File.WriteAllBytesAsync(Path.Combine(source.FullName, "morphir"), new byte[1234]);
            Directory.CreateDirectory(Path.Combine(source.FullName, "de"));
            await File.WriteAllBytesAsync(Path.Combine(source.FullName, "de", "strings.resources.dll"), new byte[56]);
            await File.WriteAllBytesAsync(Path.Combine(source.FullName, "empty.txt"), []);
            var archive = Path.Combine(root.FullName, format == ArchiveFormat.Zip ? "a.zip" : "a.tar.gz");
            ReleaseArchive.Create(source.FullName, archive, format, ReleaseArchive.ExecutablesFor("morphir"));

            var contents = ShippedContents.Read(Artifact("linux-x64"), archive);

            contents.Files.Select(file => (file.Path, file.Bytes)).Should().Equal(("de/strings.resources.dll", 56L), ("empty.txt", 0L), ("morphir", 1234L));
            contents.FileCount.Should().Be(3);
            contents.ExtractedBytes.Should().Be(1290);
            contents.ArchiveBytes.Should().Be(new FileInfo(archive).Length);
            contents.Deployment.Should().Be("self-contained");
            contents.Platform.Should().Be("linux-x64");
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Test]
    public async Task AToolPackageListsEveryPackageEntry()
    {
        var root = Directory.CreateTempSubdirectory("morphir-shipped-");
        try
        {
            var package = Path.Combine(root.FullName, "Morphir.Tool.1.2.3.nupkg");
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                await using (var entry = zip.CreateEntry("tools/net10.0/any/dotnet-morphir.dll").Open()) await entry.WriteAsync(new byte[10]);
                await using (var entry = zip.CreateEntry("Morphir.Tool.nuspec").Open()) await entry.WriteAsync(new byte[3]);
                zip.CreateEntry("tools/net10.0/any/"); // a folder entry is not a file
            }

            var tool = ((ArtifactResolution.Defined)ReleaseArtifacts.Resolve(Deployment.DotnetTool, null, "1.2.3")).Artifact;
            var contents = ShippedContents.Read(tool, package);

            contents.Files.Select(file => file.Path).Should().Equal("Morphir.Tool.nuspec", "tools/net10.0/any/dotnet-morphir.dll");
            contents.ExtractedBytes.Should().Be(13);
            contents.Platform.Should().Be("any");
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Test]
    public void TheContentsFileSaysItIsABuildOutput()
    {
        var contents = new ShippedContents("a.zip", "native-aot", "win-arm64", 10, 1, 28, [new ShippedFile("morphir.exe", 28)]);
        using var json = JsonDocument.Parse(ShippedContents.ToJson(contents));
        json.RootElement.GetProperty("note").GetString().Should().Contain("not a public contract");
        json.RootElement.TryGetProperty("schemaVersion", out _).Should().BeFalse();
        json.RootElement.GetProperty("files")[0].GetProperty("path").GetString().Should().Be("morphir.exe");
        ShippedContents.FileNameFor("a.zip").Should().Be("a.zip.build-contents.json");
        ShippedContents.Parse(ShippedContents.ToJson(contents)).Should().BeEquivalentTo(contents);
    }

    [Test]
    public void TheSizeReportHasTotalsAndEveryFileForEachArtifact()
    {
        var aot = new ShippedContents("morphir-dotnet-1.2.3-native-aot-win-arm64.zip", "native-aot", "win-arm64", 10_494_982, 1, 28_162_560,
            [new ShippedFile("morphir.exe", 28_162_560)]);
        var managed = new ShippedContents("morphir-dotnet-1.2.3-self-contained-win-arm64.zip", "self-contained", "win-arm64", 56_278_921, 2, 30,
            [new ShippedFile("morphir.exe", 10), new ShippedFile("de/x.dll", 20)]);

        var report = ShippedContents.SizeReport([managed, aot]);

        report.Should().Contain("| native-aot | win-arm64 | `morphir-dotnet-1.2.3-native-aot-win-arm64.zip` | 10,494,982 | 1 | 28,162,560 |");
        report.Should().Contain("| self-contained | win-arm64 | `morphir-dotnet-1.2.3-self-contained-win-arm64.zip` | 56,278,921 | 2 | 30 |");
        report.IndexOf("| native-aot |", StringComparison.Ordinal).Should().BeLessThan(report.IndexOf("| self-contained |", StringComparison.Ordinal),
            "rows follow the deployment order");
        report.Should().Contain("| `de/x.dll` | 20 |").And.Contain("| `morphir.exe` | 28,162,560 |");
        report.Should().Contain("not a size target");
    }

    private static ReleaseArtifact Artifact(string rid) =>
        ((ArtifactResolution.Defined)ReleaseArtifacts.Resolve(Deployment.SelfContained, rid, "1.2.3")).Artifact;
}
