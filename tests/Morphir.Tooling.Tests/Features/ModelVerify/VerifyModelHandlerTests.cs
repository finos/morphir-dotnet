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
    /// The parent's v4 schema checks the types and values of a module in every spelling of the module, the flattened
    /// <c>{ "access", "types", "values" }</c> that morphir-dotnet writes among them (see <c>Schemas/PROVENANCE.md</c>).
    /// It refuses the module that holds a type node with an unknown tag, and accepts the same module without it.
    /// </summary>
    [Test]
    public async Task TheV4SchemaRefusesAnUnknownTypeTagInAFlattenedModule()
    {
        var faulty = await _validator.ValidateAsync(
            FixtureCatalog.ReadText(FixtureCatalog.Models.V4DailyRateUnknownTypeTag), "4", CancellationToken.None);
        var valid = await _validator.ValidateAsync(
            FixtureCatalog.ReadText(FixtureCatalog.Models.V4DailyRate), "4", CancellationToken.None);

        faulty.IsValid.Should().BeFalse("the pinned v4 schema checks the values of a flattened module");
        // The schema tries each distribution kind and each module spelling, so it reports the alternatives it tried
        // too; the module that holds the node is among the places it names.
        faulty.Errors.Should().Contain(error => error.Path.StartsWith("/distribution/Library/def/modules/inventory", StringComparison.Ordinal),
            "the schema names the module that holds the node");
        valid.IsValid.Should().BeTrue("the same flattened module with a known type node passes the schema");
    }

    /// <summary>
    /// The schema locates the fault only at the module, whose spellings it tries in turn; the v4 reader, which
    /// <c>model verify</c> runs after the schema, names the node itself.
    /// </summary>
    [Test]
    public async Task AV4ModelIsDecodedSoAnUnknownTypeTagIsNamedAtItsJsonPath()
    {
        await using var sandbox = TestSandbox.Create("morphir-verify-v4-");
        var faulty = FixtureCatalog.Models.V4WithAnUnknownTypeTag;
        var path = sandbox.PathOf("model.json");
        await File.WriteAllBytesAsync(path, FixtureCatalog.ReadBytes(faulty.Entry));

        var result = await VerifyModelHandler.Handle(new VerifyModel(path), _validator, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.SchemaVersion.Should().Be("4");
        result.Errors.Where(error => error.Code is null).Should().NotBeEmpty("the schema refuses the module too");
        var error = result.Errors.Should().ContainSingle(error => error.Code != null).Which;
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

    /// <summary>
    /// Verifies the bytes of the parent's name-context example <paramref name="id"/>, or of its plain twin, written to a
    /// file of a sandbox.
    /// </summary>
    private async Task<VerifyModelResult> VerifyContextExample(string id, int? schemaVersion = null, bool twin = false)
    {
        await using var sandbox = TestSandbox.Create("morphir-verify-compacted-");
        var path = sandbox.PathOf("model.json");
        var example = FixtureCatalog.Context.Example(id);
        await File.WriteAllBytesAsync(path, FixtureCatalog.ReadBytes(twin ? example.Expanded! : example.File));
        return await VerifyModelHandler.Handle(new VerifyModel(path, schemaVersion), _validator, CancellationToken.None);
    }

    [Test]
    [Arguments("v4-single-file", "4")]
    [Arguments("v3-single-file", "3")]
    public async Task ACompactedSingleFileIsVerifiedAfterExpansion(string id, string version)
    {
        var result = await VerifyContextExample(id);

        result.Errors.Should().BeEmpty($"{id} is valid once expanded");
        result.IsValid.Should().BeTrue();
        result.SchemaVersion.Should().Be(version, "the version is detected from the expanded document");
        result.DetectionMethod.Should().Be("auto");
        result.FilesChecked.Should().Be(1);
        result.CompactedFiles.Should().Equal(result.FilePath);
    }

    [Test]
    public async Task ACompactedSingleFileIsCheckedAgainstAGivenVersion()
    {
        var result = await VerifyContextExample("v3-single-file", schemaVersion: 4);

        result.IsValid.Should().BeFalse("the expanded document is a v3 model, which the v4 schema refuses");
        result.DetectionMethod.Should().Be("manual");
        result.CompactedFiles.Should().ContainSingle();
        // Only the expanded v3 document has a formatVersion and a classic distribution array; the envelope has neither.
        result.Errors.Should().Contain(error => error.Path == "/document/formatVersion" && error.Message.Contains("4"),
            "the v4 schema refuses the v3 document's formatVersion, located inside the envelope's document");
        result.Errors.Should().Contain(error => error.Path == "/document/distribution",
            "the classic distribution is an array, where v4 has an object");
        result.Errors.Should().OnlyContain(error => error.Path.StartsWith("/document/", StringComparison.Ordinal),
            "no error is at the envelope's top level");
    }

    [Test]
    public async Task AnErrorOfAnExpandedSingleFileIsLocatedInTheEnvelopesDocument()
    {
        var compacted = await VerifyContextExample("v3-single-file", schemaVersion: 4);
        var plain = await VerifyContextExample("v3-single-file", schemaVersion: 4, twin: true);

        plain.Errors.Should().NotBeEmpty();
        plain.CompactedFiles.Should().BeEmpty();
        compacted.Errors.Select(error => (error.Path, error.Message)).Should().Equal(
            plain.Errors.Select(error => ("/document" + error.Path, error.Message)),
            "the errors of the plain twin, each inside the envelope's document");
    }

    [Test]
    [Arguments("undeclared-schema", "context.undeclared_schema", false)]
    [Arguments("unknown-alias", "context.unknown_alias", true)]
    [Arguments("context-after-document", "context.after_document", true)]
    [Arguments("critical-not-a-list", "context.invalid_envelope", true)]
    [Arguments("mixed-case-entry", "context.invalid_entry", true)]
    public async Task AnInvalidContextOfASingleFileFailsVerification(string id, string code, bool declared)
    {
        var result = await VerifyContextExample(id);

        result.IsValid.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.FilesChecked.Should().Be(1);
        var error = result.Errors.Should().ContainSingle().Which;
        error.Code.Should().Be(code);
        error.File.Should().BeNull("a single file has no file of a tree");
        result.CompactedFiles.Should().HaveCount(declared ? 1 : 0, "a file is compacted when its envelope's schema is declared");
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
