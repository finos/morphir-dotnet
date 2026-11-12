// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Nodes;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>The steps of the <c>validate</c> command and of its report.</summary>
[Binding]
public sealed class ValidationSteps(IModelValidationDriver validation, DriverSet drivers, ScenarioRun run)
{
    private readonly Dictionary<MorphirHost, CommandResult> _byHost = [];

    [When("I validate the model")]
    public async Task ValidateTheModel() => run.Result = await validation.ValidateAsync(run.Model);

    [When("I validate the model as JSON")]
    public async Task ValidateTheModelAsJson() => run.Result = await validation.ValidateAsync(run.Model, new ValidationOptions { Json = true });

    /// <summary>The words of <paramref name="arguments"/> are more arguments of the command, after the input.</summary>
    [When("I validate the model as JSON with the arguments {string}")]
    public async Task ValidateTheModelWithTheArguments(string arguments) =>
        run.Result = await validation.ValidateAsync(run.Model, new ValidationOptions
        {
            Json = true,
            Arguments = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries),
        });

    [Then("validation exits with {int} and one JSON report")]
    public void ValidationExitsWithOneReport(int exitCode)
    {
        var report = ValidationReport.Of(run.Result.ShouldExitWith(exitCode));
        report.Check(report.Required("exitCode").GetInt32() == exitCode, $"The field exitCode of the validation report is not {exitCode}.");
    }

    [Then("the validation text is a plain table with the error {string}")]
    public void TheTextIsAPlainTable(string code)
    {
        var output = run.Result.StandardOutput;
        Check(!output.Contains("\u001b[", StringComparison.Ordinal), "Redirected output is not plain text: it has an ANSI code.");
        var lines = output.Trim().Replace("\r\n", "\n").Split('\n');
        Check(lines.Any(line => line.StartsWith("│ SEVERITY ", StringComparison.Ordinal)), "The table has no SEVERITY header.");
        Check(lines.Any(line => line.StartsWith("│ error ", StringComparison.Ordinal) && line.Contains(code, StringComparison.Ordinal)), $"The table has no error row {code}.");
        Check(lines[^1].StartsWith("1 error, 0 warnings", StringComparison.Ordinal), "The last line is not the summary of one error.");
    }

    [When("I validate the model as JSON on the native host and on the tool host")]
    public async Task ValidateOnBothHosts()
    {
        foreach (var host in new[] { MorphirHost.Native, MorphirHost.DotnetTool })
            _byHost[host] = await drivers.ValidationOf(host).ValidateAsync(run.Model, new ValidationOptions { Json = true });
        run.Result = _byHost[MorphirHost.DotnetTool];
    }

    [Then("both hosts write the same validation report apart from the host")]
    public void BothHostsWriteTheSameReport()
    {
        var native = _byHost[MorphirHost.Native];
        var tool = _byHost[MorphirHost.DotnetTool];
        Check(native.ExitCode == tool.ExitCode, $"The native host exits with {native.ExitCode}, and the tool host with {tool.ExitCode}.");
        var first = JsonNode.Parse(ValidationReport.Of(native).Document.GetRawText())!.AsObject();
        var second = JsonNode.Parse(ValidationReport.Of(tool).Document.GetRawText())!.AsObject();
        Check(first["host"]?.GetValue<string>() == "morphir", "The native host does not write the host morphir.");
        Check(second["host"]?.GetValue<string>() == "dotnet-morphir", "The tool host does not write the host dotnet-morphir.");
        first.Remove("host");
        second.Remove("host");
        Check(first.ToJsonString() == second.ToJsonString(), "The two hosts write other reports apart from the host.");
    }

    private void Check(bool condition, string reason)
    {
        if (!condition) throw new CommandAssertionException(reason, run.Result);
    }
}
