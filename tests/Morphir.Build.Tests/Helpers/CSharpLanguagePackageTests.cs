// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using FluentAssertions;
using Morphir.Build.Helpers;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>Tests for the check of the Morphir.Lang.CSharp package layout in PackageValidator (design.html, BLD-1).</summary>
[Category("PlatformSpecific")]
public class CSharpLanguagePackageTests
{
    private static string WritePackage(IEnumerable<string> entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"csharp-{Guid.NewGuid():N}.nupkg");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            archive.CreateEntry(entry);
        }

        return path;
    }

    private static PackageValidator.ValidationResult Validate(IEnumerable<string> entries)
    {
        var path = WritePackage(entries);
        try
        {
            return PackageValidator.ValidateCSharpLanguagePackage(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ValidateCSharpLanguagePackage_PackedPackage_ReturnsSuccess()
    {
        var package = TestFixture.FindLatestPackage("Morphir.Lang.CSharp.?.*.nupkg");
        package.Should().NotBeNull("the Morphir.Lang.CSharp package should exist after packing");

        var result = PackageValidator.ValidateCSharpLanguagePackage(package!);

        result.Errors.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("analyzers/dotnet/cs/Morphir.Lang.CSharp.Generator.dll")]
    [Arguments("analyzers/dotnet/cs/FSharp.Core.dll")]
    [Arguments("analyzers/dotnet/cs/Morphir.Lang.CSharp.CodeFixes.dll")]
    [Arguments("tasks/netstandard2.0/Morphir.Lang.CSharp.Build.dll")]
    [Arguments("build/Morphir.Lang.CSharp.targets")]
    [Arguments("lib/netstandard2.0/Morphir.Lang.CSharp.dll")]
    public async Task ValidateCSharpLanguagePackage_PackageWithoutAnEntry_ReturnsFailureNamingTheEntry(string missing)
    {
        var result = Validate(PackageValidator.CSharpLanguageEntries.Where(entry => entry != missing));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.Contains(missing));
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("analyzers/dotnet/cs/Microsoft.CodeAnalysis.dll", "the compiler host has this assembly")]
    [Arguments("analyzers/dotnet/cs/Microsoft.CodeAnalysis.CSharp.Workspaces.dll", "the compiler host has this assembly")]
    [Arguments("analyzers/dotnet/cs/System.Collections.Immutable.dll", "the compiler host has this assembly")]
    [Arguments("analyzers/dotnet/Morphir.Lang.CSharp.Generator.dll", "only for C#")]
    [Arguments("tasks/netstandard2.0/Microsoft.Build.Framework.dll", "MSBuild has this assembly")]
    [Arguments("analyzers/dotnet/cs/Morphir.Lang.CSharp.Generator.pdb", ".pdb")]
    public async Task ValidateCSharpLanguagePackage_PackageWithAWrongEntry_ReturnsFailureWithTheReason(string entry, string reason)
    {
        var result = Validate([.. PackageValidator.CSharpLanguageEntries, entry]);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.Contains(reason));
        await Task.CompletedTask;
    }
}
