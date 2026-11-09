using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Morphir.E2E.Tests.Infrastructure;
using Reqnroll;

namespace Morphir.E2E.Tests.Features;

/// <summary>Steps for <c>validate</c> in both hosts.</summary>
[Binding]
public sealed class ExecutableValidateSteps(ScenarioContext scenarioContext)
{
    private ExecutableExecutionResult Result =>
        SharedSteps.Last(scenarioContext) ?? throw new InvalidOperationException("No command has run in this scenario.");

    private JsonElement Report => JsonDocument.Parse(Result.StandardOutput).RootElement.Clone();

    private IEnumerable<JsonElement> Diagnostics => Report.GetProperty("diagnostics").EnumerateArray();

    [Then("the validate report should have the exit code (\\d+)")]
    public void ThenTheValidateReportShouldHaveTheExitCode(int exitCode)
    {
        Report.GetProperty("exitCode").GetInt32().Should().Be(exitCode);
        Report.GetProperty("success").GetBoolean().Should().Be(exitCode == 0);
    }

    [Then("the validate report should have no diagnostic")]
    public void ThenTheValidateReportShouldHaveNoDiagnostic() => Diagnostics.Should().BeEmpty();

    [Then("the validate report should have (\\d+) \"(.*)\" diagnostic with the code \"(.*)\" from \"(.*)\"")]
    public void ThenTheValidateReportShouldHaveDiagnosticsFrom(int count, string severity, string code, string extension) =>
        Diagnostics.Where(diagnostic => diagnostic.GetProperty("severity").GetString() == severity
                && diagnostic.GetProperty("code").GetString() == code
                && diagnostic.TryGetProperty("extension", out var id) && id.GetString() == extension)
            .Should().HaveCount(count, Result.StandardOutput);

    [Then("the validate report should have (\\d+) \"(.*)\" diagnostic with the code \"([^\"]*)\"$")]
    public void ThenTheValidateReportShouldHaveDiagnostics(int count, string severity, string code) =>
        Diagnostics.Where(diagnostic => diagnostic.GetProperty("severity").GetString() == severity && diagnostic.GetProperty("code").GetString() == code)
            .Should().HaveCount(count, Result.StandardOutput);

    [Then("the validate report should list \"(.*)\" as \"(.*)\"")]
    public void ThenTheValidateReportShouldListAs(string id, string status) =>
        Report.GetProperty("analyzers").EnumerateArray().Single(analyzer => analyzer.GetProperty("id").GetString() == id)
            .GetProperty("status").GetString().Should().Be(status);

    [Then("the analyzers of the validate report should be the validator entries of extension list")]
    public async Task ThenTheAnalyzersShouldBeTheValidatorEntries()
    {
        var analyzers = Report.GetProperty("analyzers").EnumerateArray().Select(analyzer => analyzer.GetProperty("id").GetString()).ToArray();
        var list = await new ExecutableRunner(new ExecutableLocator().GetExecutablePath()!).ExecuteCommandAsync("extension list --json");
        list.ExitCode.Should().Be(0, list.CombinedOutput);
        var validators = JsonDocument.Parse(list.StandardOutput).RootElement.GetProperty("extensions").EnumerateArray()
            .Where(entry => entry.GetProperty("types").EnumerateArray().Any(type => type.GetString() == "validator"))
            .Select(entry => entry.GetProperty("id").GetString()).ToArray();
        analyzers.Should().NotBeEmpty();
        analyzers.Should().Equal(validators);
    }

    [Then("the validate reports of both hosts should be equal apart from the host")]
    public void ThenTheValidateReportsOfBothHostsShouldBeEqualApartFromTheHost()
    {
        var other = (ExecutableExecutionResult)scenarioContext[ExecutableExtensionListSteps.OtherResultKey];
        other.ExitCode.Should().Be(Result.ExitCode, other.CombinedOutput);
        var mine = JsonNode.Parse(Result.StandardOutput)!.AsObject();
        var theirs = JsonNode.Parse(other.StandardOutput)!.AsObject();
        mine["host"]!.GetValue<string>().Should().NotBe(theirs["host"]!.GetValue<string>());
        mine.Remove("host");
        theirs.Remove("host");
        mine.ToJsonString().Should().Be(theirs.ToJsonString());
    }
}
