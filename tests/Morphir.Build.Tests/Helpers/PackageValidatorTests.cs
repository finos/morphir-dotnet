using FluentAssertions;
using Morphir.Build.Helpers;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// Tests for PackageValidator helper class
/// </summary>
public class PackageValidatorTests
{
    [Test]
    public async Task ValidateToolPackage_ValidPackage_ReturnsSuccess()
    {
        // Arrange
        var package = TestFixture.FindLatestPackage("Morphir.Tool.*.nupkg");
        package.Should().NotBeNull("Tool package should exist after build");

        // Act
        var result = PackageValidator.ValidateToolPackage(package!);

        // Assert
        result.IsValid.Should().BeTrue("Tool package should be valid");
        result.Errors.Should().BeEmpty("Valid package should have no errors");

        await Task.CompletedTask;
    }

    [Test]
    public async Task ValidateToolPackage_NonExistentPackage_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentPath = "/tmp/nonexistent-package.nupkg";

        // Act & Assert
        var act = () => PackageValidator.ValidateToolPackage(nonExistentPath);
        act.Should().Throw<FileNotFoundException>()
            .WithMessage($"*{nonExistentPath}*");

        await Task.CompletedTask;
    }

    [Test]
    public async Task ValidateLibraryPackage_ValidPackage_ReturnsSuccess()
    {
        // Arrange - Test with Morphir.Core package
        var package = TestFixture.FindLatestPackage("Morphir.Core.*.nupkg");
        package.Should().NotBeNull("Morphir.Core package should exist after build");

        // Act
        var result = PackageValidator.ValidateLibraryPackage(package!);

        // Assert
        result.IsValid.Should().BeTrue("Library package should be valid");
        result.Errors.Should().BeEmpty("Valid package should have no errors");

        await Task.CompletedTask;
    }

    [Test]
    public async Task ValidateLibraryPackage_NonExistentPackage_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentPath = "/tmp/nonexistent-library.nupkg";

        // Act & Assert
        var act = () => PackageValidator.ValidateLibraryPackage(nonExistentPath);
        act.Should().Throw<FileNotFoundException>()
            .WithMessage($"*{nonExistentPath}*");

        await Task.CompletedTask;
    }

    [Test]
    public async Task ValidateDotnetToolSettings_ValidXml_ReturnsSuccess()
    {
        // Arrange
        var validXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<DotNetCliTool Version=""1.0"">
  <Commands>
    <Command Name=""dotnet-morphir"" EntryPoint=""dotnet-morphir.dll"" />
  </Commands>
</DotNetCliTool>";

        // Act
        var result = PackageValidator.ValidateDotnetToolSettings(validXml);

        // Assert
        result.IsValid.Should().BeTrue("Valid XML should pass validation");
        result.Errors.Should().BeEmpty();

        await Task.CompletedTask;
    }

    [Test]
    public async Task ValidateDotnetToolSettings_MissingRootElement_ReturnsError()
    {
        // Arrange
        var invalidXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<InvalidRoot>
  <Commands>
    <Command Name=""test"" EntryPoint=""test.dll"" />
  </Commands>
</InvalidRoot>";

        // Act
        var result = PackageValidator.ValidateDotnetToolSettings(invalidXml);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("DotNetCliTool"));

        await Task.CompletedTask;
    }

    [Test]
    public async Task ValidateDotnetToolSettings_MissingCommandsElement_ReturnsError()
    {
        // Arrange
        var invalidXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<DotNetCliTool Version=""1.0"">
</DotNetCliTool>";

        // Act
        var result = PackageValidator.ValidateDotnetToolSettings(invalidXml);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Commands"));

        await Task.CompletedTask;
    }

    [Test]
    public async Task ValidateDotnetToolSettings_MissingNameAttribute_ReturnsError()
    {
        // Arrange
        var invalidXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<DotNetCliTool Version=""1.0"">
  <Commands>
    <Command EntryPoint=""test.dll"" />
  </Commands>
</DotNetCliTool>";

        // Act
        var result = PackageValidator.ValidateDotnetToolSettings(invalidXml);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Name"));

        await Task.CompletedTask;
    }

    [Test]
    public async Task ValidateDotnetToolSettings_MissingEntryPointAttribute_ReturnsError()
    {
        // Arrange
        var invalidXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<DotNetCliTool Version=""1.0"">
  <Commands>
    <Command Name=""test"" />
  </Commands>
</DotNetCliTool>";

        // Act
        var result = PackageValidator.ValidateDotnetToolSettings(invalidXml);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("EntryPoint"));

        await Task.CompletedTask;
    }

    [Test]
    public async Task ValidateDotnetToolSettings_EntryPointNotDll_ReturnsError()
    {
        // Arrange
        var invalidXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<DotNetCliTool Version=""1.0"">
  <Commands>
    <Command Name=""test"" EntryPoint=""test.exe"" />
  </Commands>
</DotNetCliTool>";

        // Act
        var result = PackageValidator.ValidateDotnetToolSettings(invalidXml);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains(".dll"));

        await Task.CompletedTask;
    }

    [Test]
    public async Task ValidatePackageMetadata_ValidNuspec_ReturnsSuccess()
    {
        // Arrange
        var validNuspec = @"<?xml version=""1.0"" encoding=""utf-8""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"">
  <metadata>
    <id>TestPackage</id>
    <version>1.0.0</version>
    <authors>Test Author</authors>
    <description>Test Description</description>
    <license type=""expression"">Apache-2.0</license>
  </metadata>
</package>";

        // Act
        var result = PackageValidator.ValidatePackageMetadata(validNuspec);

        // Assert
        result.IsValid.Should().BeTrue("Valid nuspec should pass validation");
        result.Errors.Should().BeEmpty();

        await Task.CompletedTask;
    }

    [Test]
    public async Task ValidatePackageMetadata_MissingId_ReturnsError()
    {
        // Arrange
        var invalidNuspec = @"<?xml version=""1.0"" encoding=""utf-8""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"">
  <metadata>
    <version>1.0.0</version>
    <authors>Test Author</authors>
    <description>Test Description</description>
  </metadata>
</package>";

        // Act
        var result = PackageValidator.ValidatePackageMetadata(invalidNuspec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("'id'"));

        await Task.CompletedTask;
    }

    [Test]
    public async Task ValidatePackageMetadata_MissingVersion_ReturnsError()
    {
        // Arrange
        var invalidNuspec = @"<?xml version=""1.0"" encoding=""utf-8""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"">
  <metadata>
    <id>TestPackage</id>
    <authors>Test Author</authors>
    <description>Test Description</description>
  </metadata>
</package>";

        // Act
        var result = PackageValidator.ValidatePackageMetadata(invalidNuspec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("'version'"));

        await Task.CompletedTask;
    }

    [Test]
    public async Task ValidatePackageMetadata_MissingAuthors_ReturnsError()
    {
        // Arrange
        var invalidNuspec = @"<?xml version=""1.0"" encoding=""utf-8""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"">
  <metadata>
    <id>TestPackage</id>
    <version>1.0.0</version>
    <description>Test Description</description>
  </metadata>
</package>";

        // Act
        var result = PackageValidator.ValidatePackageMetadata(invalidNuspec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("'authors'"));

        await Task.CompletedTask;
    }

    [Test]
    public async Task ValidatePackageMetadata_MissingDescription_ReturnsError()
    {
        // Arrange
        var invalidNuspec = @"<?xml version=""1.0"" encoding=""utf-8""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"">
  <metadata>
    <id>TestPackage</id>
    <version>1.0.0</version>
    <authors>Test Author</authors>
  </metadata>
</package>";

        // Act
        var result = PackageValidator.ValidatePackageMetadata(invalidNuspec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("'description'"));

        await Task.CompletedTask;
    }
}
