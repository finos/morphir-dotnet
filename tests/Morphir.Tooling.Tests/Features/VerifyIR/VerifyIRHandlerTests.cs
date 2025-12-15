using FluentAssertions;
using Morphir.Tooling.Features.VerifyIR;
using Morphir.Tooling.Infrastructure.JsonSchema;

namespace Morphir.Tooling.Tests.Features.VerifyIR;

public class VerifyIRHandlerTests
{
    private readonly SchemaLoader _schemaLoader;
    private readonly SchemaValidator _validator;

    public VerifyIRHandlerTests()
    {
        _schemaLoader = new SchemaLoader();
        _validator = new SchemaValidator(_schemaLoader);
    }

    [Test]
    public async Task Handle_ShouldReturnValid_WhenIRIsValid()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.VerifyIR.VerifyIR(
            FilePath: "TestData/valid-ir-v3.json",
            SchemaVersion: 3,
            JsonOutput: false,
            Quiet: false
        );

        // Act
        var result = await VerifyIRHandler.Handle(command, _validator, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue("valid IR should pass validation");
        result.SchemaVersion.Should().Be("3");
        result.DetectionMethod.Should().Be("manual", "schema version was explicitly specified");
        result.FilePath.Should().Be("TestData/valid-ir-v3.json");
        result.Errors.Should().BeEmpty("valid IR should have no errors");
        result.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Handle_ShouldReturnInvalid_WhenIRHasErrors()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.VerifyIR.VerifyIR(
            FilePath: "TestData/invalid-missing-formatversion.json",
            SchemaVersion: 3
        );

        // Act
        var result = await VerifyIRHandler.Handle(command, _validator, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse("invalid IR should fail validation");
        result.Errors.Should().NotBeEmpty("invalid IR should have errors");
        result.Errors.Should().Contain(e => e.Message.Contains("formatVersion"));
    }

    [Test]
    public async Task Handle_ShouldAutoDetectVersion_WhenVersionNotSpecified()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.VerifyIR.VerifyIR(
            FilePath: "TestData/valid-ir-v3.json",
            SchemaVersion: null  // Auto-detect
        );

        // Act
        var result = await VerifyIRHandler.Handle(command, _validator, CancellationToken.None);

        // Assert
        result.DetectionMethod.Should().Be("auto", "version was auto-detected");
        result.SchemaVersion.Should().NotBeNullOrEmpty("version should be detected");
        result.IsValid.Should().BeTrue("auto-detection should work for valid IR");
    }

    [Test]
    public async Task Handle_ShouldWorkWithV1IR()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.VerifyIR.VerifyIR(
            FilePath: "TestData/valid-ir-v1.json",
            SchemaVersion: 1
        );

        // Act
        var result = await VerifyIRHandler.Handle(command, _validator, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue("valid v1 IR should pass validation");
        result.SchemaVersion.Should().Be("1");
    }

    [Test]
    public async Task Handle_ShouldWorkWithV2IR()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.VerifyIR.VerifyIR(
            FilePath: "TestData/valid-ir-v2.json",
            SchemaVersion: 2
        );

        // Act
        var result = await VerifyIRHandler.Handle(command, _validator, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue("valid v2 IR should pass validation");
        result.SchemaVersion.Should().Be("2");
    }

    [Test]
    public async Task Handle_ShouldIncludeTimestamp()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.VerifyIR.VerifyIR(
            FilePath: "TestData/valid-ir-v3.json",
            SchemaVersion: 3
        );
        var beforeExecution = DateTime.UtcNow;

        // Act
        var result = await VerifyIRHandler.Handle(command, _validator, CancellationToken.None);

        // Assert
        result.Timestamp.Should().BeOnOrAfter(beforeExecution);
        result.Timestamp.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    [Test]
    public async Task Handle_ShouldReturnErrorResult_WhenFileDoesNotExist()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.VerifyIR.VerifyIR(
            FilePath: "TestData/nonexistent-file.json",
            SchemaVersion: 3
        );

        // Act
        var result = await VerifyIRHandler.Handle(command, _validator, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse("file not found should result in invalid");
        result.Errors.Should().NotBeEmpty("file not found should have errors");
        result.Errors.Should().Contain(e =>
            e.Message.Contains("does not exist") || e.Message.Contains("not found"),
            "error message should indicate file not found");
        result.Errors[0].Expected.Should().Be("Existing file");
        result.Errors[0].Found.Should().Be("File not found");
    }
}
