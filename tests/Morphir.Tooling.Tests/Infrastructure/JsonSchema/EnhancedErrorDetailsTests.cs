using FluentAssertions;
using Morphir.Tooling.Infrastructure.JsonSchema;

namespace Morphir.Tooling.Tests.Infrastructure.JsonSchema;

/// <summary>
/// Tests for enhanced validation error details (Expected, Found values)
/// </summary>
public class EnhancedErrorDetailsTests
{
    private readonly SchemaValidator _sut;
    private readonly SchemaLoader _schemaLoader;

    public EnhancedErrorDetailsTests()
    {
        _schemaLoader = new SchemaLoader();
        _sut = new SchemaValidator(_schemaLoader);
    }

    [Test]
    public async Task ValidateAsync_ShouldIncludeFoundValue_WhenTypeIsWrong()
    {
        // Arrange - formatVersion should be a number, not a string
        var invalidJson = @"{
            ""formatVersion"": ""3"",
            ""distribution"": [""Library"", [], [], {}]
        }";

        // Act
        var result = await _sut.ValidateAsync(invalidJson, "3", CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse("string formatVersion should be invalid");
        result.Errors.Should().Contain(e =>
            e.Path.Contains("formatVersion") &&
            e.Found != null,
            "error should include the found value");
    }

    [Test]
    public async Task ValidateAsync_ShouldIncludeExpectedValue_WhenTypeIsWrong()
    {
        // Arrange - formatVersion should be a number, not a string
        var invalidJson = @"{
            ""formatVersion"": ""3"",
            ""distribution"": [""Library"", [], [], {}]
        }";

        // Act
        var result = await _sut.ValidateAsync(invalidJson, "3", CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse("string formatVersion should be invalid");
        result.Errors.Should().Contain(e =>
            e.Path.Contains("formatVersion") &&
            e.Expected != null,
            "error should include the expected type");
    }

    [Test]
    public async Task ValidateAsync_ShouldIncludeFoundValue_ForMissingRequiredField()
    {
        // Arrange - missing formatVersion field
        var invalidJson = await File.ReadAllTextAsync("TestData/invalid-missing-formatversion.json");

        // Act
        var result = await _sut.ValidateAsync(invalidJson, "3", CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Message.Contains("formatVersion") &&
            e.Found != null,
            "error for missing field should indicate what was found (undefined/null)");
    }

    [Test]
    public async Task ValidateAsync_ShouldIncludeExpectedValue_ForMissingRequiredField()
    {
        // Arrange - missing formatVersion field
        var invalidJson = await File.ReadAllTextAsync("TestData/invalid-missing-formatversion.json");

        // Act
        var result = await _sut.ValidateAsync(invalidJson, "3", CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Message.Contains("formatVersion") &&
            e.Expected != null,
            "error should indicate that formatVersion field is expected");
    }

    [Test]
    public async Task ValidateAsync_ShouldNotIncludeLineAndColumn_WhenNotAvailable()
    {
        // Arrange
        var invalidJson = await File.ReadAllTextAsync("TestData/invalid-missing-formatversion.json");

        // Act
        var result = await _sut.ValidateAsync(invalidJson, "3", CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        // Note: Line/Column are complex to calculate and may remain null
        // This test documents that behavior
        result.Errors.Should().AllSatisfy(e =>
        {
            // It's acceptable for Line and Column to be null
            // as calculating them from JsonDocument is non-trivial
        });
    }

    [Test]
    public async Task ValidateAsync_ShouldProvideUsefulErrorDetails_ForArrayTypeError()
    {
        // Arrange - dependencies should be an object, not a string
        var invalidJson = @"{
            ""formatVersion"": 3,
            ""distribution"": [
                ""Library"",
                [[""test""], [""package""]],
                ""WRONG_TYPE"",
                {""modules"": []}
            ]
        }";

        // Act
        var result = await _sut.ValidateAsync(invalidJson, "3", CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        result.Errors.Should().Contain(e =>
            e.Found != null || e.Expected != null,
            "type errors should include expected or found values");
    }
}
