using FluentAssertions;
using Morphir.Tooling.Infrastructure.JsonSchema;
using System.Text.Json;

namespace Morphir.Tooling.Tests.Features.VerifyIR;

[Binding]
public class VerifyIRSteps
{
    private SchemaLoader? _schemaLoader;
    private SchemaValidator? _validator;
    private string? _jsonContent;
    private SchemaValidationResult? _validationResult;
    private Exception? _caughtException;

    [Given(@"a SchemaLoader instance")]
    public void GivenASchemaLoaderInstance()
    {
        _schemaLoader = new SchemaLoader();
    }

    [Given(@"a SchemaValidator instance")]
    public void GivenASchemaValidatorInstance()
    {
        _validator = new SchemaValidator(_schemaLoader!);
    }

    [Given(@"a valid IR v(\d+) JSON file")]
    public async Task GivenAValidIRJsonFile(int version)
    {
        var testDataPath = $"TestData/valid-ir-v{version}.json";
        _jsonContent = await File.ReadAllTextAsync(testDataPath);
    }

    [Given(@"an IR JSON file missing the ""(.*)"" field")]
    public async Task GivenAnIRJsonFileMissingTheField(string fieldName)
    {
        _jsonContent = await File.ReadAllTextAsync("TestData/invalid-missing-formatversion.json");
    }

    [Given(@"an IR JSON file with dependencies as a string instead of an object")]
    public async Task GivenAnIRJsonFileWithDependenciesAsAString()
    {
        _jsonContent = await File.ReadAllTextAsync("TestData/invalid-wrong-type.json");
    }

    [Given(@"a malformed JSON file")]
    public void GivenAMalformedJsonFile()
    {
        _jsonContent = "{invalid json";
    }

    [When(@"I validate the IR against schema version ""(.*)""")]
    public async Task WhenIValidateTheIRAgainstSchemaVersion(string version)
    {
        try
        {
            _validationResult = await _validator!.ValidateAsync(_jsonContent!, version, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _caughtException = ex;
        }
    }

    [When(@"I attempt to validate the IR against schema version ""(.*)""")]
    public async Task WhenIAttemptToValidateTheIRAgainstSchemaVersion(string version)
    {
        try
        {
            _validationResult = await _validator!.ValidateAsync(_jsonContent!, version, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _caughtException = ex;
        }
    }

    [Then(@"the validation should succeed")]
    public void ThenTheValidationShouldSucceed()
    {
        _validationResult.Should().NotBeNull();
        _validationResult!.IsValid.Should().BeTrue("the JSON should be valid according to the schema");
    }

    [Then(@"there should be no validation errors")]
    public void ThenThereShouldBeNoValidationErrors()
    {
        _validationResult!.Errors.Should().BeEmpty("valid JSON should not produce any validation errors");
    }

    [Then(@"the validation should fail")]
    public void ThenTheValidationShouldFail()
    {
        _validationResult.Should().NotBeNull();
        _validationResult!.IsValid.Should().BeFalse("the JSON should be invalid according to the schema");
    }

    [Then(@"there should be validation errors")]
    public void ThenThereShouldBeValidationErrors()
    {
        _validationResult!.Errors.Should().NotBeEmpty("invalid JSON should produce validation errors");
    }

    [Then(@"the errors should mention ""(.*)""")]
    public void ThenTheErrorsShouldMention(string expectedText)
    {
        _validationResult!.Errors.Should().Contain(
            e => e.Message.Contains(expectedText, StringComparison.OrdinalIgnoreCase),
            $"at least one error should mention '{expectedText}'"
        );
    }

    [Then(@"the validation should throw a FileNotFoundException")]
    public void ThenTheValidationShouldThrowAFileNotFoundException()
    {
        _caughtException.Should().NotBeNull();
        _caughtException.Should().BeOfType<FileNotFoundException>();
    }

    [Then(@"the error message should contain ""(.*)""")]
    public void ThenTheErrorMessageShouldContain(string expectedText)
    {
        _caughtException.Should().NotBeNull();
        _caughtException!.Message.Should().Contain(expectedText);
    }

    [Then(@"the validation should throw a JsonException")]
    public void ThenTheValidationShouldThrowAJsonException()
    {
        _caughtException.Should().NotBeNull();
        _caughtException.Should().BeAssignableTo<JsonException>("JsonReaderException derives from JsonException");
    }
}
