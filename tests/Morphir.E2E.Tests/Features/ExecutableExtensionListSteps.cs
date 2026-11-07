using System.Text.Json;
using FluentAssertions;
using Morphir.E2E.Tests.Infrastructure;
using Reqnroll;

namespace Morphir.E2E.Tests.Features;

/// <summary>Steps for <c>extension list</c> in both hosts.</summary>
[Binding]
public sealed class ExecutableExtensionListSteps(ScenarioContext scenarioContext)
{
    private const string OtherResultKey = "OtherHostExecutionResult";

    private ExecutableExecutionResult Result =>
        SharedSteps.Last(scenarioContext) ?? throw new InvalidOperationException("No command has run in this scenario.");

    private static JsonElement Document(ExecutableExecutionResult result) => JsonDocument.Parse(result.StandardOutput).RootElement.Clone();

    [Then("stdout should be one JSON document")]
    public void ThenStdoutShouldBeOneJsonDocument()
    {
        var parse = () => JsonDocument.Parse(Result.StandardOutput).Dispose();
        parse.Should().NotThrow("stdout must hold only the JSON document: {0}", Result.StandardOutput);
    }

    [Then("the listed host should be the host of the executable under test")]
    public void ThenTheListedHostShouldBeTheHostOfTheExecutableUnderTest() =>
        Document(Result).GetProperty("host").GetString().Should().Be(ExecutableLocator.HostFor(ExecutableLocator.CurrentType));

    [Then("the listed extension ids should be \"(.*)\"")]
    public void ThenTheListedExtensionIdsShouldBe(string ids) =>
        Document(Result).GetProperty("extensions").EnumerateArray().Select(entry => entry.GetProperty("id").GetString())
            .Should().Equal(ids.Split(", "));

    [Then("every listed extension should be available in this host")]
    public void ThenEveryListedExtensionShouldBeAvailableInThisHost() =>
        Document(Result).GetProperty("extensions").EnumerateArray().Should().OnlyContain(entry => entry.GetProperty("available").GetBoolean());

    [When("I run \"(.*)\" with the other host")]
    public async Task WhenIRunWithTheOtherHost(string command)
    {
        var locator = new ExecutableLocator();
        var other = ExecutableLocator.CurrentType == "tool" ? locator.GetNativeHostPath() : locator.GetToolPath();
        other.Should().NotBeNull("the other host must be built: build src/Morphir.Tool, or publish or build morphir");
        scenarioContext[OtherResultKey] = await new ExecutableRunner(other!).ExecuteCommandAsync(command);
    }

    [Then("the extensions available in both hosts should be equal in both results")]
    public void ThenTheExtensionsAvailableInBothHostsShouldBeEqualInBothResults()
    {
        var other = (ExecutableExecutionResult)scenarioContext[OtherResultKey];
        other.ExitCode.Should().Be(0, other.CombinedOutput);
        static string[] Shared(JsonElement document) => document.GetProperty("extensions").EnumerateArray()
            .Where(entry => entry.GetProperty("hosts").GetArrayLength() == 2)
            .Select(entry => entry.GetRawText()).ToArray();
        var mine = Document(Result);
        var theirs = Document(other);
        mine.GetProperty("host").GetString().Should().NotBe(theirs.GetProperty("host").GetString());
        Shared(mine).Should().NotBeEmpty();
        Shared(mine).Should().Equal(Shared(theirs));
    }
}
