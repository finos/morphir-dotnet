using System.Text.Json;
using FluentAssertions;
using Morphir.E2E.Tests.Infrastructure;
using Reqnroll;

namespace Morphir.E2E.Tests.Features;

/// <summary>Steps for <c>extension list</c> in both hosts.</summary>
[Binding]
public sealed class ExecutableExtensionListSteps(ScenarioContext scenarioContext)
{
    /// <summary>The scenario key for the result of the same command in the other host.</summary>
    internal const string OtherResultKey = "OtherHostExecutionResult";

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

    private JsonElement Extension(string id) =>
        Document(Result).GetProperty("extensions").EnumerateArray().Single(entry => entry.GetProperty("id").GetString() == id);

    [Then("the extension \"(.*)\" should have the types \"(.*)\"")]
    public void ThenTheExtensionShouldHaveTheTypes(string id, string types) =>
        Extension(id).GetProperty("types").EnumerateArray().Select(type => type.GetString()).Should().Equal(types.Split(", "));

    [Then("the extension \"(.*)\" should have the delivery \"(.*)\" and the hosts \"(.*)\"")]
    public void ThenTheExtensionShouldHaveTheDeliveryAndTheHosts(string id, string delivery, string hosts)
    {
        var entry = Extension(id);
        entry.GetProperty("delivery").GetString().Should().Be(delivery);
        entry.GetProperty("hosts").EnumerateArray().Select(host => host.GetString()).Should().Equal(hosts.Split(", "));
    }

    [Then("the extension \"(.*)\" should claim the validator IR versions \"(.*)\"")]
    public void ThenTheExtensionShouldClaimTheValidatorIrVersions(string id, string versions) =>
        Extension(id).GetProperty("capabilities").GetProperty("validator").GetProperty("irVersions").EnumerateArray()
            .Select(version => version.GetString()).Should().Equal(versions.Split(", "));

    [Then("every listed extension except \"(.*)\" should be available in this host")]
    public void ThenEveryListedExtensionExceptShouldBeAvailableInThisHost(string except) =>
        Document(Result).GetProperty("extensions").EnumerateArray()
            .Where(entry => entry.GetProperty("id").GetString() != except)
            .Should().OnlyContain(entry => entry.GetProperty("available").GetBoolean());

    /// <summary>
    /// The in-process F# frontend is in the managed composition only: the tool and the packaged managed offerings have
    /// it; the AOT-compatible <c>morphir</c>, published as Native AOT or as the legacy single file, lists it as
    /// unavailable with the reason.
    /// </summary>
    [Then("the in-process F# frontend should be available exactly when the executable under test has it")]
    public void ThenTheInProcessFSharpFrontendShouldBeAvailableExactlyWhenTheExecutableHasIt()
    {
        var entry = Extension("morphir-dotnet.fsharp");
        var expected = ExecutableLocator.CurrentType is "tool" or "self-contained" or "framework-dependent" or "dotnet-tool";
        entry.GetProperty("available").GetBoolean().Should().Be(expected);
        if (expected)
            entry.TryGetProperty("reason", out _).Should().BeFalse();
        else
            entry.GetProperty("reason").GetString().Should().Contain("morphir-fsharp-frontend");
    }

    /// <summary>
    /// The deployment comes from the build: <c>aot</c> is published by PublishExecutable with the native-aot profile's
    /// settings, and each packaged type (<see cref="ExecutableLocator.PackagedTypes"/>) is its own release profile's
    /// output, so it reports its own name; the legacy single-file builds and the development build of the tool come from
    /// no named profile and state no deployment.
    /// </summary>
    [Then("the listed deployment should be the deployment of the executable under test")]
    public void ThenTheListedDeploymentShouldBeTheDeploymentOfTheExecutableUnderTest()
    {
        var root = Document(Result);
        var type = ExecutableLocator.CurrentType;
        if (type == "aot" || ExecutableLocator.PackagedTypes.Contains(type))
        {
            var deployment = type == "aot" ? "native-aot" : type;
            root.GetProperty("deployment").GetString().Should().Be(deployment);
            root.GetProperty("execution").GetString().Should().Be(deployment == "native-aot" ? "native-aot" : "jit");
        }
        else
        {
            root.TryGetProperty("deployment", out _).Should().BeFalse("no named release profile built this executable");
            root.GetProperty("execution").GetString().Should().Be("jit");
        }
    }

    [When("I run \"(.*)\" with the other host")]
    public async Task WhenIRunWithTheOtherHost(string command)
    {
        var locator = new ExecutableLocator();
        var other = ExecutableLocator.IsToolHost(ExecutableLocator.CurrentType) ? locator.GetNativeHostPath() : locator.GetToolPath();
        other.Should().NotBeNull("the other host must be built: build src/Morphir.Tool, or publish or build morphir");
        scenarioContext[OtherResultKey] = await new ExecutableRunner(other!).ExecuteCommandAsync(new TestDataResolver().ResolvePath(command));
    }

    [Then("the extensions available in both hosts should be equal in both results")]
    public void ThenTheExtensionsAvailableInBothHostsShouldBeEqualInBothResults()
    {
        var other = (ExecutableExecutionResult)scenarioContext[OtherResultKey];
        other.ExitCode.Should().Be(0, other.CombinedOutput);
        // The entries available in both results; an entry that one offering lacks differs in available and reason.
        static Dictionary<string, string> Available(JsonElement document) => document.GetProperty("extensions").EnumerateArray()
            .Where(entry => entry.GetProperty("available").GetBoolean())
            .ToDictionary(entry => entry.GetProperty("id").GetString()!, entry => entry.GetRawText());
        var mine = Available(Document(Result));
        var theirs = Available(Document(other));
        Document(Result).GetProperty("host").GetString().Should().NotBe(Document(other).GetProperty("host").GetString());
        var shared = mine.Keys.Intersect(theirs.Keys).ToList();
        shared.Should().NotBeEmpty();
        foreach (var id in shared)
            mine[id].Should().Be(theirs[id], id);
    }
}
