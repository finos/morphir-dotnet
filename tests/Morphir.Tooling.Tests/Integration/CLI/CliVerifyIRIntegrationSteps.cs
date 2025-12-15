using FluentAssertions;
using Reqnroll;
using System.Text.Json;

namespace Morphir.Tooling.Tests.Integration.CLI;

[Binding]
public class CliVerifyIRIntegrationSteps
{
    private readonly CliTestHelper _cliHelper;
    private CliExecutionResult? _lastResult;

    public CliVerifyIRIntegrationSteps()
    {
        _cliHelper = new CliTestHelper();
    }

    [Given(@"the Morphir CLI is available")]
    public void GivenTheMorphirCLIIsAvailable()
    {
        // No-op: CLI availability is checked in CliTestHelper constructor
        // If the CLI project doesn't exist, constructor will throw
    }

    [When(@"I run the CLI with command ""(.*)""")]
    public async Task WhenIRunTheCLIWithCommand(string command)
    {
        _lastResult = await _cliHelper.ExecuteCommandAsync(command);
    }

    [Then(@"the exit code should be (.*)")]
    public void ThenTheExitCodeShouldBe(int expectedExitCode)
    {
        _lastResult.Should().NotBeNull("CLI command should have been executed");
        _lastResult!.ExitCode.Should().Be(expectedExitCode,
            $"because command should return exit code {expectedExitCode}. Output: {_lastResult.CombinedOutput}");
    }

    [Then(@"the output should contain ""([^""]*)""$")]
    public void ThenTheOutputShouldContain(string expectedText)
    {
        _lastResult.Should().NotBeNull("CLI command should have been executed");
        _lastResult!.CombinedOutput.Should().Contain(expectedText,
            $"because output should include '{expectedText}'. Actual output: {_lastResult.CombinedOutput}");
    }

    [Then(@"the output should contain ""([^""]*)"" or ""([^""]*)""$")]
    public void ThenTheOutputShouldContainEither(string text1, string text2)
    {
        _lastResult.Should().NotBeNull("CLI command should have been executed");
        var output = _lastResult!.CombinedOutput;

        var containsEither = output.Contains(text1, StringComparison.OrdinalIgnoreCase) ||
                            output.Contains(text2, StringComparison.OrdinalIgnoreCase);

        containsEither.Should().BeTrue(
            $"because output should contain either '{text1}' or '{text2}'. Actual output: {output}");
    }

    [Then(@"the output should be empty")]
    public void ThenTheOutputShouldBeEmpty()
    {
        _lastResult.Should().NotBeNull("CLI command should have been executed");
        _lastResult!.CombinedOutput.Should().BeEmpty(
            "because quiet mode should produce no output");
    }

    [Then(@"the output should be valid JSON")]
    public void ThenTheOutputShouldBeValidJSON()
    {
        _lastResult.Should().NotBeNull("CLI command should have been executed");
        var isValid = CliTestHelper.IsValidJson(_lastResult!.StandardOutput);
        isValid.Should().BeTrue(
            $"because output should be valid JSON. Actual output: {_lastResult.StandardOutput}");
    }

    [Then(@"the JSON output should have field ""(.*)"" with value ""(.*)""")]
    public void ThenTheJSONOutputShouldHaveFieldWithValue(string fieldPath, string expectedValue)
    {
        _lastResult.Should().NotBeNull("CLI command should have been executed");

        var actualValue = CliTestHelper.GetJsonFieldValue(_lastResult!.StandardOutput, fieldPath);
        actualValue.Should().NotBeNull($"because JSON should have field '{fieldPath}'");

        // Parse expected value based on type
        if (expectedValue == "true" || expectedValue == "false")
        {
            // Boolean comparison
            actualValue.Should().Be(bool.Parse(expectedValue),
                $"because field '{fieldPath}' should be {expectedValue}");
        }
        else if (int.TryParse(expectedValue, out var intValue))
        {
            // Integer comparison - handle both int and string representations
            if (actualValue is int intActual)
            {
                intActual.Should().Be(intValue,
                    $"because field '{fieldPath}' should be {intValue}");
            }
            else if (actualValue is string strActual)
            {
                strActual.Should().Be(expectedValue,
                    $"because field '{fieldPath}' should be '{expectedValue}'");
            }
        }
        else
        {
            // String comparison
            actualValue.Should().Be(expectedValue,
                $"because field '{fieldPath}' should be '{expectedValue}'");
        }
    }

    [Then(@"the JSON output should have field ""(.*)"" that is not empty")]
    public void ThenTheJSONOutputShouldHaveFieldThatIsNotEmpty(string fieldPath)
    {
        _lastResult.Should().NotBeNull("CLI command should have been executed");

        var actualValue = CliTestHelper.GetJsonFieldValue(_lastResult!.StandardOutput, fieldPath);
        actualValue.Should().NotBeNull($"because JSON should have field '{fieldPath}'");

        // Check based on value type
        if (actualValue is int count)
        {
            count.Should().BeGreaterThan(0,
                $"because field '{fieldPath}' should not be empty");
        }
        else if (actualValue is string str)
        {
            str.Should().NotBeNullOrEmpty(
                $"because field '{fieldPath}' string should not be empty");
        }
    }
}
