using System.Text.Json;
using FluentAssertions;
using Morphir.E2E.Tests.Infrastructure;
using Reqnroll;

namespace Morphir.E2E.Tests.Features;

[Binding]
public class ExecutableIRVerificationSteps
{
    [Given("test data files are available")]
    public void GivenTestDataFilesAreAvailable()
    {
        var testDataDir = new TestDataResolver().GetTestDataDirectory();
        Directory.Exists(testDataDir).Should().BeTrue(
            $"Test data directory should exist at: {testDataDir}");
    }

    [Then("the output should be valid JSON")]
    public void ThenTheOutputShouldBeValidJson()
    {
        SharedSteps.ExecutionResult.Should().NotBeNull("command should have been executed");
        // Use StandardOutput only - stderr contains logging which would break JSON parsing
        var output = SharedSteps.ExecutionResult!.StandardOutput.Trim();
        
        Action parseJson = () => JsonDocument.Parse(output);
        parseJson.Should().NotThrow($"output should be valid JSON. Actual output: {output}");
    }

    [Then("the JSON output should have field \"(.*)\" with value \"(.*)\"")]
    public void ThenTheJsonOutputShouldHaveFieldWithValue(string fieldPath, string expectedValue)
    {
        SharedSteps.ExecutionResult.Should().NotBeNull("command should have been executed");
        // Use StandardOutput only - stderr contains logging which would break JSON parsing
        var output = SharedSteps.ExecutionResult!.StandardOutput.Trim();
        
        using var doc = JsonDocument.Parse(output);
        var current = doc.RootElement;

        foreach (var segment in fieldPath.Split('.'))
        {
            current.TryGetProperty(segment, out var property).Should().BeTrue(
                $"JSON should have field '{segment}' in path '{fieldPath}'");
            current = property;
        }

        var actualValue = current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetInt32().ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };

        actualValue.Should().Be(expectedValue,
            $"field '{fieldPath}' should have value '{expectedValue}'");
    }

    [Then("the JSON output should have field \"(.*)\" that is not empty")]
    public void ThenTheJsonOutputShouldHaveFieldThatIsNotEmpty(string fieldPath)
    {
        SharedSteps.ExecutionResult.Should().NotBeNull("command should have been executed");
        // Use StandardOutput only - stderr contains logging which would break JSON parsing
        var output = SharedSteps.ExecutionResult!.StandardOutput.Trim();
        
        using var doc = JsonDocument.Parse(output);
        var current = doc.RootElement;

        foreach (var segment in fieldPath.Split('.'))
        {
            current.TryGetProperty(segment, out var property).Should().BeTrue(
                $"JSON should have field '{segment}' in path '{fieldPath}'");
            current = property;
        }

        if (current.ValueKind == JsonValueKind.Array)
        {
            current.GetArrayLength().Should().BeGreaterThan(0,
                $"field '{fieldPath}' should not be empty");
        }
        else if (current.ValueKind == JsonValueKind.Object)
        {
            current.EnumerateObject().Any().Should().BeTrue(
                $"field '{fieldPath}' should not be empty");
        }
    }

    [Then("the output should be empty")]
    public void ThenTheOutputShouldBeEmpty()
    {
        SharedSteps.ExecutionResult.Should().NotBeNull("command should have been executed");
        // Use StandardOutput only for empty check - stderr may contain logging
        SharedSteps.ExecutionResult!.StandardOutput.Should().BeEmpty(
            "stdout should be empty in quiet mode (logging goes to stderr)");
    }
}

