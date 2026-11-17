using FluentAssertions;
using Morphir.TestSupport;
using Morphir.TestSupport.Fixtures;
using Morphir.Tooling.Features.ModelVerify;
using Morphir.Tooling.Infrastructure.JsonSchema;

namespace Morphir.Tooling.Tests.Features.ModelVerify;

[Category("PlatformSpecific")]
public class VerifyModelHandlerTests
{
    private readonly SchemaLoader _schemaLoader;
    private readonly SchemaValidator _validator;

    public VerifyModelHandlerTests()
    {
        _schemaLoader = new SchemaLoader();
        _validator = new SchemaValidator(_schemaLoader);
    }

    [Test]
    public async Task Handle_ShouldReturnValid_WhenIRIsValid()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.ModelVerify.VerifyModel(
            FilePath: "TestData/valid-ir-v3.json",
            SchemaVersion: 3,
            JsonOutput: false,
            Quiet: false
        );

        // Act
        var result = await VerifyModelHandler.Handle(command, _validator, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue("valid IR should pass validation");
        result.SchemaVersion.Should().Be("3");
        result.DetectionMethod.Should().Be("manual", "schema version was explicitly specified");
        result.FilePath.Should().Be("TestData/valid-ir-v3.json");
        result.Errors.Should().BeEmpty("valid IR should have no errors");
        result.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.ExitCode.Should().Be(0, "the report states the exit code of a valid model");
    }

    [Test]
    public async Task Handle_ShouldReturnInvalid_WhenIRHasErrors()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.ModelVerify.VerifyModel(
            FilePath: "TestData/invalid-missing-formatversion.json",
            SchemaVersion: 3
        );

        // Act
        var result = await VerifyModelHandler.Handle(command, _validator, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse("invalid IR should fail validation");
        result.Errors.Should().NotBeEmpty("invalid IR should have errors");
        result.Errors.Should().Contain(e => e.Message.Contains("formatVersion"));
        result.ExitCode.Should().Be(1, "the report states the exit code of a model that is not valid");
    }

    [Test]
    public async Task Handle_ShouldAutoDetectVersion_WhenVersionNotSpecified()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.ModelVerify.VerifyModel(
            FilePath: "TestData/valid-ir-v3.json",
            SchemaVersion: null  // Auto-detect
        );

        // Act
        var result = await VerifyModelHandler.Handle(command, _validator, CancellationToken.None);

        // Assert
        result.DetectionMethod.Should().Be("auto", "version was auto-detected");
        result.SchemaVersion.Should().NotBeNullOrEmpty("version should be detected");
        result.IsValid.Should().BeTrue("auto-detection should work for valid IR");
    }

    [Test]
    public async Task Handle_ShouldWorkWithV1IR()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.ModelVerify.VerifyModel(
            FilePath: "TestData/valid-ir-v1.json",
            SchemaVersion: 1
        );

        // Act
        var result = await VerifyModelHandler.Handle(command, _validator, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue("valid v1 IR should pass validation");
        result.SchemaVersion.Should().Be("1");
    }

    [Test]
    public async Task Handle_ShouldWorkWithV2IR()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.ModelVerify.VerifyModel(
            FilePath: "TestData/valid-ir-v2.json",
            SchemaVersion: 2
        );

        // Act
        var result = await VerifyModelHandler.Handle(command, _validator, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue("valid v2 IR should pass validation");
        result.SchemaVersion.Should().Be("2");
    }

    [Test]
    public async Task Handle_ShouldIncludeTimestamp()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.ModelVerify.VerifyModel(
            FilePath: "TestData/valid-ir-v3.json",
            SchemaVersion: 3
        );
        var beforeExecution = DateTime.UtcNow;

        // Act
        var result = await VerifyModelHandler.Handle(command, _validator, CancellationToken.None);

        // Assert
        result.Timestamp.Should().BeOnOrAfter(beforeExecution);
        result.Timestamp.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    [Test]
    public async Task Handle_ShouldReturnErrorResult_WhenFileDoesNotExist()
    {
        // Arrange
        var command = new Morphir.Tooling.Features.ModelVerify.VerifyModel(
            FilePath: "TestData/nonexistent-file.json",
            SchemaVersion: 3
        );

        // Act
        var result = await VerifyModelHandler.Handle(command, _validator, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse("file not found should result in invalid");
        result.Errors.Should().NotBeEmpty("file not found should have errors");
        result.Errors.Should().Contain(e =>
            e.Message.Contains("does not exist") || e.Message.Contains("not found"),
            "error message should indicate file not found");
        result.Errors[0].Expected.Should().Be("Existing file");
        result.Errors[0].Found.Should().Be("File not found");
    }

    /// <summary>
    /// The parent's v4 schema checks the types and values of a module only in the wrapper <c>{ "access", "value" }</c>
    /// (see <c>Schemas/PROVENANCE.md</c>): in the flattened spelling that morphir-dotnet writes, a type node with an
    /// unknown tag passes the schema. When this test fails, the parent's schema closed the gap.
    /// </summary>
    [Test]
    public async Task TheV4SchemaAloneAcceptsAnUnknownTypeTagInAFlattenedModule()
    {
        var json = FixtureCatalog.ReadText(FixtureCatalog.Models.V4DailyRateUnknownTypeTag);

        var result = await _validator.ValidateAsync(json, "4", CancellationToken.None);

        result.IsValid.Should().BeTrue("the pinned v4 schema does not check the values of a flattened module");
    }

    [Test]
    public async Task AV4ModelIsDecodedSoAnUnknownTypeTagInAFlattenedModuleIsAnError()
    {
        await using var sandbox = TestSandbox.Create("morphir-verify-v4-");
        var faulty = FixtureCatalog.Models.V4WithAnUnknownTypeTag;
        var path = sandbox.PathOf("model.json");
        await File.WriteAllBytesAsync(path, FixtureCatalog.ReadBytes(faulty.Entry));

        var result = await VerifyModelHandler.Handle(new VerifyModel(path), _validator, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.SchemaVersion.Should().Be("4");
        var error = result.Errors.Should().ContainSingle().Which;
        error.Path.Should().Be(faulty.Fault.JsonPointer);
        error.Code.Should().Be("unknown_node");
        error.File.Should().BeNull("a single file has no file of a tree");
    }

    [Test]
    public async Task AValidV4ModelDecodesWithNoError()
    {
        await using var sandbox = TestSandbox.Create("morphir-verify-v4-");
        var path = sandbox.PathOf("model.json");
        await File.WriteAllBytesAsync(path, FixtureCatalog.ReadBytes(FixtureCatalog.Models.V4ConstantFolding));

        var result = await VerifyModelHandler.Handle(new VerifyModel(path), _validator, CancellationToken.None);

        result.Errors.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
        result.Layout.Should().Be(ModelLayouts.SingleFile);
        result.FilesChecked.Should().Be(1);
    }

    [Test]
    public void TheJsonReportKeepsIsValidAndStatesTheExitCode()
    {
        var usage = new VerifyModelResult(false, "unknown", "none", "", [], DateTime.UnixEpoch) { ExitCode = 2 };

        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(usage, VerifyModelResult.JsonInfo));

        json.RootElement.GetProperty("IsValid").GetBoolean().Should().BeFalse("the release check of finos/morphir reads IsValid");
        json.RootElement.GetProperty("exitCode").GetInt32().Should().Be(2, "a usage failure is told from a model that is not valid");
        new VerifyModelResult(true, "3", "auto", "m.json", [], DateTime.UnixEpoch).ExitCode.Should().Be(0);
    }
}
