// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Packaging;
using Morphir.TestSupport;

namespace Morphir.TestSupport.Tests.Packaging;

/// <summary>
/// A tool installed with <c>dotnet tool install --tool-path</c> keeps the package it installed in the tool store,
/// byte for byte (observed with the .NET 10 SDK). The installation checks that it is the packed file.
/// </summary>
[Category("PlatformSpecific")]
public sealed class PackagedInstallationTests
{
    [Test]
    public void TheStoredPackageIsThePackedFile()
    {
        using var tool = new ToolPath();
        var packed = tool.Packed("package bytes");
        tool.Store("0.4.0-alpha.3", "package bytes");

        PackagedInstallation.StoredPackageProblem(tool.Root, "Morphir.Tool", "0.4.0-alpha.3", packed).Should().BeNull();
    }

    [Test]
    public void ADifferentStoredPackageIsReported()
    {
        using var tool = new ToolPath();
        var packed = tool.Packed("package bytes");
        tool.Store("0.4.0-alpha.3", "other bytes");

        PackagedInstallation.StoredPackageProblem(tool.Root, "Morphir.Tool", "0.4.0-alpha.3", packed)
            .Should().Contain("morphir.tool.0.4.0-alpha.3.nupkg").And.Contain("not the packed");
    }

    [Test]
    public void AMissingStoredPackageIsReported()
    {
        using var tool = new ToolPath();
        var packed = tool.Packed("package bytes");

        PackagedInstallation.StoredPackageProblem(tool.Root, "Morphir.Tool", "0.4.0-alpha.3", packed).Should().Contain("no stored package");
    }

    private sealed class ToolPath : IDisposable
    {
        private readonly TestSandbox _sandbox = TestSandbox.Create("morphir tool ");

        private string Scratch => _sandbox.Root;

        public string Root => Path.Combine(Scratch, "tool path");

        public string Packed(string content)
        {
            var path = Path.Combine(Scratch, "Morphir.Tool.0.4.0-alpha.3.nupkg");
            File.WriteAllText(path, content);
            return path;
        }

        public void Store(string version, string content)
        {
            var folder = Directory.CreateDirectory(Path.Combine(Root, ".store", "morphir.tool", version, "morphir.tool", version)).FullName;
            File.WriteAllText(Path.Combine(folder, $"morphir.tool.{version}.nupkg"), content);
        }

        public void Dispose() => _sandbox.Dispose();
    }
}
