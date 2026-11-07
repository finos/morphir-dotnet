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
    /// <summary>The scenario key every step class uses for the last command's result.</summary>
    public const string LastExecutionResultKey = "LastExecutionResult";

    private readonly ScenarioContext _scenarioContext;

    public SharedSteps(ScenarioContext scenarioContext) => _scenarioContext = scenarioContext;

    // Locating the executable and test data is read-only and safe to share; results are per scenario.
    // Lazy<T> initializes once even when scenarios run in parallel.
    private static readonly Lazy<ExecutableLocator> Locator = new(() => new ExecutableLocator());
    private static readonly Lazy<TestDataResolver> Resolver = new(() => new TestDataResolver());
    private static readonly Lazy<ExecutableRunner> Runner = new(() => new ExecutableRunner(
        Locator.Value.GetExecutablePath()
        ?? throw new InvalidOperationException("Morphir executable not found. Build the executable first.")));

    private static ExecutableLocator ExecutableLocator => Locator.Value;
    private static ExecutableRunner ExecutableRunner => Runner.Value;
    private static TestDataResolver TestDataResolver => Resolver.Value;

    /// <summary>The last command result in this scenario. A [ThreadStatic] field is lost across awaits.</summary>
    public static ExecutableExecutionResult? Last(ScenarioContext scenarioContext) =>
        scenarioContext.TryGetValue(LastExecutionResultKey, out ExecutableExecutionResult result) ? result : null;

    private ExecutableExecutionResult? ExecutionResult => Last(_scenarioContext);

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
        _scenarioContext[LastExecutionResultKey] = await ExecutableRunner.ExecuteCommandAsync(resolvedCommand);
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

