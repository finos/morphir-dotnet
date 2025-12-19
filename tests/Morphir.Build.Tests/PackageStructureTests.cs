using FluentAssertions;
using TUnit.Core;

namespace Morphir.Build.Tests;

/// <summary>
/// Tests for validating NuGet package structure (directory layout, files, etc.)
/// </summary>
public class PackageStructureTests
{
    [Test]
    [Skip("Requires packages to be built first - run after PackAll target")]
    public async Task ToolPackage_HasCorrectStructure()
    {
        // Arrange
        var package = TestFixture.FindLatestPackage("Morphir.Tool.*.nupkg");
        package.Should().NotBeNull("Tool package should exist after build");
        
        // Act
        var entries = TestFixture.GetPackageEntries(package!);
        
        // Assert - Tool packages must have tools/ directory structure
        entries.Should().Contain(e => e.StartsWith("tools/net10.0/any/"),
            "Tool package must have tools/net10.0/any/ directory");
        
        // Check for required tool files
        entries.Should().Contain("tools/net10.0/any/morphir.dll",
            "Tool package must contain morphir.dll in tools directory");
        entries.Should().Contain("tools/net10.0/any/DotnetToolSettings.xml",
            "Tool package must contain DotnetToolSettings.xml");
        
        // Check for required dependencies
        entries.Should().Contain(e => e.Contains("Morphir.Core.dll"),
            "Tool package must include Morphir.Core.dll");
        entries.Should().Contain(e => e.Contains("Morphir.Tooling.dll"),
            "Tool package must include Morphir.Tooling.dll");
        
        await Task.CompletedTask;
    }
    
    [Test]
    [Skip("Requires packages to be built first - run after PackAll target")]
    public async Task ToolPackage_HasCorrectToolSettings()
    {
        // Arrange
        var package = TestFixture.FindLatestPackage("Morphir.Tool.*.nupkg");
        package.Should().NotBeNull("Tool package should exist after build");
        
        // Act
        var toolSettings = await TestFixture.ReadPackageEntry(package!, "tools/net10.0/any/DotnetToolSettings.xml");
        
        // Assert
        toolSettings.Should().Contain("<Command Name=\"morphir\"",
            "Tool command should be named 'morphir'");
        toolSettings.Should().Contain("EntryPoint=\"morphir.dll\"",
            "Tool entry point should be morphir.dll");
    }
    
    [Test]
    [Skip("Requires packages to be built first - run after PackAll target")]
    public async Task LibraryPackages_HaveCorrectStructure()
    {
        // Arrange - Check Morphir.Core package
        var corePackage = TestFixture.FindLatestPackage("Morphir.Core.*.nupkg");
        corePackage.Should().NotBeNull("Morphir.Core package should exist after build");
        
        // Act
        var entries = TestFixture.GetPackageEntries(corePackage!);
        
        // Assert - Library packages must have lib/ directory structure
        entries.Should().Contain(e => e.StartsWith("lib/net10.0/"),
            "Library package must have lib/net10.0/ directory");
        entries.Should().Contain(e => e.Contains("lib/net10.0/Morphir.Core.dll"),
            "Library package must contain the assembly in lib directory");
        
        // Library packages should NOT have tools/ directory
        entries.Should().NotContain(e => e.StartsWith("tools/"),
            "Library package should not contain tools/ directory");
        
        await Task.CompletedTask;
    }
    
    [Test]
    [Skip("Requires packages to be built first - run after PackAll target")]
    public async Task Packages_DoNotContainUnnecessaryFiles()
    {
        // Arrange
        var packages = new[]
        {
            TestFixture.FindLatestPackage("Morphir.Core.*.nupkg"),
            TestFixture.FindLatestPackage("Morphir.Tooling.*.nupkg"),
            TestFixture.FindLatestPackage("Morphir.*.nupkg"), // Morphir main library
            TestFixture.FindLatestPackage("Morphir.Tool.*.nupkg"),
        }.Where(p => p != null).ToList();
        
        packages.Should().NotBeEmpty("At least one package should exist after build");
        
        foreach (var package in packages)
        {
            // Act
            var entries = TestFixture.GetPackageEntries(package!);
            
            // Assert - No .pdb files in release packages (should be in symbols package)
            entries.Where(e => e.EndsWith(".pdb")).Should().BeEmpty(
                $"Package {Path.GetFileName(package)} should not contain .pdb files in main package");
            
            // No test files
            entries.Should().NotContain(e => e.Contains("Tests"),
                $"Package {Path.GetFileName(package)} should not contain test files");
        }
        
        await Task.CompletedTask;
    }
}
