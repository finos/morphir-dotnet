using FluentAssertions;
using Morphir.E2E.Tests.Infrastructure;
using Reqnroll;

namespace Morphir.E2E.Tests.Features;

/// <summary>
/// Shared step definitions used across multiple feature files
/// </summary>
[Binding]
public class SharedSteps
{
    // Use instance fields to avoid race conditions in parallel test execution
    // The ScenarioContext ensures each scenario gets its own instance
    private static ExecutableLocator? _executableLocator;
    private static ExecutableRunner? _executableRunner;
    private static TestDataResolver? _testDataResolver;
    
    // Thread-local storage for execution result to avoid parallel test interference
    [ThreadStatic]
    private static ExecutableExecutionResult? _executionResult;

    private static ExecutableLocator ExecutableLocator
    {
        get
        {
            _executableLocator ??= new ExecutableLocator();
            return _executableLocator;
        }
    }

    private static ExecutableRunner ExecutableRunner
    {
        get
        {
            if (_executableRunner == null)
            {
                var executablePath = ExecutableLocator.GetExecutablePath();
                if (executablePath == null)
                {
                    throw new InvalidOperationException("Morphir executable not found. Build the executable first.");
                }
                _executableRunner = new ExecutableRunner(executablePath);
            }
            return _executableRunner;
        }
    }

    private static TestDataResolver TestDataResolver
    {
        get
        {
            _testDataResolver ??= new TestDataResolver();
            return _testDataResolver;
        }
    }

    public static ExecutableExecutionResult? ExecutionResult
    {
        get => _executionResult;
        set => _executionResult = value;
    }

    [Given("a Morphir executable is available")]
    public void GivenAMorphirExecutableIsAvailable()
    {
        var executablePath = ExecutableLocator.GetExecutablePath();
        ExecutableLocator.VerifyExecutable(executablePath).Should().BeTrue(
            "Morphir executable should be available for E2E testing");
    }

    [When("I run the command \"(.*)\"")]
    public async Task WhenIRunTheCommand(string command)
    {
        var resolvedCommand = TestDataResolver.ResolvePath(command);
        ExecutionResult = await ExecutableRunner.ExecuteCommandAsync(resolvedCommand);
    }

    [Then("the exit code should be (\\d+)")]
    public void ThenTheExitCodeShouldBe(int expectedExitCode)
    {
        ExecutionResult.Should().NotBeNull("command should have been executed");
        ExecutionResult!.ExitCode.Should().Be(expectedExitCode,
            $"expected exit code {expectedExitCode} but got {ExecutionResult.ExitCode}");
    }

    [Then(@"the output should contain ""([^""]*)"" or ""([^""]*)""")]
    public void ThenTheOutputShouldContainOr(string option1, string option2)
    {
        ExecutionResult.Should().NotBeNull("command should have been executed");
        ExecutionResult!.OutputContainsAny(option1, option2).Should().BeTrue(
            $"output should contain '{option1}' or '{option2}'");
    }

    [Then(@"the output should contain ""([^""]*)""$")]
    public void ThenTheOutputShouldContain(string expectedText)
    {
        ExecutionResult.Should().NotBeNull("command should have been executed");
        ExecutionResult!.CombinedOutput.Should().Contain(expectedText,
            $"output should contain '{expectedText}'");
    }
}

