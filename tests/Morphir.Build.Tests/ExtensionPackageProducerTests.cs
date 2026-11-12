// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Morphir.Build.Helpers;
using Morphir.TestSupport;
using TUnit.Core;

namespace Morphir.Build.Tests;

public class ExtensionPackageProducerTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void PreparationRefusesALinkedPublishRootWithEitherSeparatorSpelling(bool trailingSeparator)
    {
        using var sandbox = TestSandbox.Create("package-producer-linked-");
        var target = sandbox.PathOf("target");
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "run.exe"), [1, 2, 3]);
        var link = sandbox.PathOf("published");
        Directory.CreateSymbolicLink(link, target);
        var archive = sandbox.PathOf("provider.zip");
        ReleaseArchive.Create(target, archive, ArchiveFormat.Zip, ReleaseArchive.ExecutablesFor("run.exe"));
        var folder = trailingSeparator ? link + Path.DirectorySeparatorChar : link;
        Action prepare = () => ExtensionPackageProducer.Prepare(Template(), folder, archive, "win-x64", "run.exe");
        prepare.Should().Throw<InvalidOperationException>().WithMessage("*reparse point*");
        File.ReadAllBytes(Path.Combine(target, "run.exe")).Should().Equal([1, 2, 3]);
    }

    [Test]
    public void PreparationRefusesAnEmptyLinkedDirectoryBeforeRecursiveFileEnumeration()
    {
        using var sandbox = TestSandbox.Create("package-producer-linked-empty-");
        var folder = sandbox.PathOf("published");
        var target = sandbox.PathOf("empty-target");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(folder, "run.exe"), [1, 2, 3]);
        Directory.CreateSymbolicLink(Path.Combine(folder, "linked"), target);
        var archive = sandbox.PathOf("provider.zip");
        ReleaseArchive.Create(folder, archive, ArchiveFormat.Zip, ReleaseArchive.ExecutablesFor("run.exe"));
        Action prepare = () => ExtensionPackageProducer.Prepare(Template(), folder, archive, "win-x64", "run.exe");
        prepare.Should().Throw<InvalidOperationException>().WithMessage("*reparse point*");
        Directory.EnumerateFileSystemEntries(target).Should().BeEmpty();
    }

    [Test]
    public void PreparationUsesTheInstallerManifestAndActualWholeArchive()
    {
        using var sandbox = TestSandbox.Create("package-producer-");
        var folder = sandbox.PathOf("published");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "run.exe"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(folder, "runtime.dll"), [4, 5]);
        var archive = sandbox.PathOf("provider.zip");
        ReleaseArchive.Create(folder, archive, ArchiveFormat.Zip, ReleaseArchive.ExecutablesFor("run.exe"));
        var prepared = ExtensionPackageProducer.Prepare(Template(), folder, archive, "win-x64", "run.exe");
        var expected = "morphir-package-tree 0.1.0-preview.1\n"
            + $"x 3 {ReleaseArchive.Sha256(Path.Combine(folder, "run.exe"))} run.exe\n"
            + $"f 2 {ReleaseArchive.Sha256(Path.Combine(folder, "runtime.dll"))} runtime.dll\n";
        Encoding.ASCII.GetString(prepared.Manifest).Should().Be(expected);
        using var release = JsonDocument.Parse(prepared.Release);
        var artifact = release.RootElement.GetProperty("artifacts")[0];
        artifact.GetProperty("sha256").GetString().Should().Be(ReleaseArchive.Sha256(archive));
        artifact.GetProperty("package").GetProperty("executables").GetArrayLength().Should().Be(0);
        artifact.GetProperty("package").TryGetProperty("root", out _).Should().BeFalse();
    }

    [Test]
    public void PreparationRefusesAnAbsentPublishedEntrypoint()
    {
        using var sandbox = TestSandbox.Create("package-producer-missing-");
        var folder = sandbox.PathOf("published");
        Directory.CreateDirectory(folder);
        var archive = sandbox.PathOf("provider.zip");
        File.WriteAllBytes(archive, []);
        Action prepare = () => ExtensionPackageProducer.Prepare(Template(), folder, archive, "win-x64", "absent.exe");
        prepare.Should().Throw<InvalidOperationException>().WithMessage("*package.entry_point_missing*");
    }

    private static byte[] Template()
    {
        var corpus = Path.Combine(TestFixture.GetRepositoryRoot(), "tests", "Fixtures", "ExtensionAcquisition", "parent-0439c4c0", "package-cases.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(corpus));
        var template = JsonNode.Parse(document.RootElement.GetProperty("cases")[0].GetProperty("schemaDocuments")[0].GetProperty("document").GetRawText())!;
        template["artifacts"]![0]!["platform"] = "x86_64-pc-windows-msvc";
        return Encoding.UTF8.GetBytes(template.ToJsonString());
    }
}
