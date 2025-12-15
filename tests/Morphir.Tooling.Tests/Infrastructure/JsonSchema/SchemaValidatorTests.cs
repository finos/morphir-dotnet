using FluentAssertions;
using Morphir.Tooling.Infrastructure.JsonSchema;

namespace Morphir.Tooling.Tests.Infrastructure.JsonSchema;

public class SchemaValidatorTests
{
    private readonly SchemaValidator _sut;
    private readonly SchemaLoader _schemaLoader;

    public SchemaValidatorTests()
    {
        _schemaLoader = new SchemaLoader();
        _sut = new SchemaValidator(_schemaLoader);
    }

    [Test]
    public async Task ValidateAsync_ShouldReturnValid_WhenJsonMatchesSchema()
    {
        // Arrange
        var validJson = await File.ReadAllTextAsync("TestData/valid-ir-v3.json");

        // Act
        var result = await _sut.ValidateAsync(validJson, "3", CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue("valid JSON should pass validation");
        result.Errors.Should().BeEmpty("no errors should be present for valid JSON");
    }

    [Test]
    public async Task ValidateAsync_ShouldReturnInvalid_WhenRequiredFieldMissing()
    {
        // Arrange
        var invalidJson = await File.ReadAllTextAsync("TestData/invalid-missing-formatversion.json");

        // Act
        var result = await _sut.ValidateAsync(invalidJson, "3", CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse("JSON missing required field should fail validation");
        result.Errors.Should().NotBeEmpty("errors should be present for invalid JSON");
        result.Errors.Should().Contain(e => e.Message.Contains("formatVersion"),
            "error should mention the missing required field");
    }

    [Test]
    public async Task ValidateAsync_ShouldReturnInvalid_WhenTypeIsWrong()
    {
        // Arrange
        var invalidJson = @"{
            ""formatVersion"": 3,
            ""distribution"": [
                ""Library"",
                [[""test""], [""package""]],
                ""WRONG_TYPE_SHOULD_BE_OBJECT"",
                { ""modules"": [] }
            ]
        }";

        // Act
        var result = await _sut.ValidateAsync(invalidJson, "3", CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse("JSON with wrong type should fail validation");
        result.Errors.Should().NotBeEmpty("errors should be present for type mismatch");
    }

    [Test]
    public async Task ValidateAsync_ShouldThrowException_WhenInvalidSchemaVersion()
    {
        // Arrange
        var validJson = await File.ReadAllTextAsync("TestData/valid-ir-v3.json");

        // Act
        var act = () => _sut.ValidateAsync(validJson, "99", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*Schema file not found*");
    }

    [Test]
    public async Task ValidateAsync_ShouldThrowException_WhenJsonIsInvalid()
    {
        // Arrange
        var malformedJson = "{invalid json";

        // Act
        var act = () => _sut.ValidateAsync(malformedJson, "3", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<System.Text.Json.JsonException>();
    }

    [Test]
    public async Task ValidateAsync_ShouldIncludePathInErrors_WhenValidationFails()
    {
        // Arrange
        var invalidJson = await File.ReadAllTextAsync("TestData/invalid-missing-formatversion.json");

        // Act
        var result = await _sut.ValidateAsync(invalidJson, "3", CancellationToken.None);

        // Assert
        result.Errors.Should().AllSatisfy(e => e.Path.Should().NotBeNull(),
            "validation errors should include the path to the invalid element");
    }
}
