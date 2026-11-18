using FluentAssertions;
using Morphir.Tooling.Features.ModelVerify;

namespace Morphir.Tooling.Tests.Features.ModelVerify;

public class VerifyModelValidatorTests
{
    private readonly VerifyModelValidator _sut;

    public VerifyModelValidatorTests()
    {
        _sut = new VerifyModelValidator();
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task Validate_ShouldSucceed_WhenFileExistsAndVersionValid()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.ModelVerify.VerifyModel(
            FilePath: "TestData/valid-ir-v3.json",
            SchemaVersion: 3
        );

        // Act
        var result = await _sut.ValidateAsync(command);

        // Assert
        result.IsValid.Should().BeTrue("command with valid file path and version should pass validation");
        result.Errors.Should().BeEmpty();
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task Validate_ShouldSucceed_WhenFileExistsAndVersionNotSpecified()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.ModelVerify.VerifyModel(
            FilePath: "TestData/valid-ir-v3.json",
            SchemaVersion: null  // Auto-detect
        );

        // Act
        var result = await _sut.ValidateAsync(command);

        // Assert
        result.IsValid.Should().BeTrue("command without version should pass validation");
    }

    [Test]
    public async Task Validate_ShouldFail_WhenFilePathEmpty()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.ModelVerify.VerifyModel(
            FilePath: "",
            SchemaVersion: 3
        );

        // Act
        var result = await _sut.ValidateAsync(command);

        // Assert
        result.IsValid.Should().BeFalse("empty file path should fail validation");
        result.Errors.Should().Contain(e => e.PropertyName == "FilePath",
            "should have at least one FilePath error");
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task Validate_ShouldFail_WhenFileDoesNotExist()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.ModelVerify.VerifyModel(
            FilePath: "TestData/nonexistent-file.json",
            SchemaVersion: 3
        );

        // Act
        var result = await _sut.ValidateAsync(command);

        // Assert
        result.IsValid.Should().BeFalse("non-existent file should fail validation");
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "FilePath" &&
            e.ErrorMessage.Contains("File does not exist"));
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task Validate_ShouldFail_WhenSchemaVersionLessThan1()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.ModelVerify.VerifyModel(
            FilePath: "TestData/valid-ir-v3.json",
            SchemaVersion: 0
        );

        // Act
        var result = await _sut.ValidateAsync(command);

        // Assert
        result.IsValid.Should().BeFalse("schema version < 1 should fail validation");
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "SchemaVersion" &&
            e.ErrorMessage.Contains("must be 1, 2, 3 or 4"));
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task Validate_ShouldFail_WhenSchemaVersionGreaterThan4()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.ModelVerify.VerifyModel(
            FilePath: "TestData/valid-ir-v3.json",
            SchemaVersion: 5
        );

        // Act
        var result = await _sut.ValidateAsync(command);

        // Assert
        result.IsValid.Should().BeFalse("schema version > 4 should fail validation");
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "SchemaVersion" &&
            e.ErrorMessage.Contains("must be 1, 2, 3 or 4"));
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task Validate_ShouldSucceed_ForAllValidVersions()
    {
        // Arrange & Act & Assert
        foreach (var version in new[] { 1, 2, 3, 4 })
        {
            var command = new Morphir.Tooling.Features.ModelVerify.VerifyModel(
                FilePath: "TestData/valid-ir-v3.json",
                SchemaVersion: version
            );

            var result = await _sut.ValidateAsync(command);

            result.IsValid.Should().BeTrue($"version {version} should be valid");
        }
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task Validate_ShouldSucceed_WhenThePathIsTheDirectoryOfATree()
    {
        var command = new Morphir.Tooling.Features.ModelVerify.VerifyModel(FilePath: "TestData", SchemaVersion: 4);

        var result = await _sut.ValidateAsync(command);

        result.IsValid.Should().BeTrue("a document tree is a directory");
    }

    [Test]
    public async Task Validate_ShouldIncludeMultipleErrors_WhenMultipleRulesViolated()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.ModelVerify.VerifyModel(
            FilePath: "",  // Invalid: empty
            SchemaVersion: 99  // Invalid: out of range
        );

        // Act
        var result = await _sut.ValidateAsync(command);

        // Assert
        result.IsValid.Should().BeFalse("multiple validation errors should fail");
        result.Errors.Should().HaveCountGreaterThan(1, "should have multiple validation errors");
        result.Errors.Should().Contain(e => e.PropertyName == "FilePath");
        result.Errors.Should().Contain(e => e.PropertyName == "SchemaVersion");
    }
}
