// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>The steps for one command line of the product and for the general checks of its result.</summary>
[Binding]
public sealed class CliSteps(ICliDriver cli, ScenarioRun run)
{
    [When("I run the command {string}")]
    public async Task RunTheCommand(string commandLine) => run.Result = await cli.RunCommandLineAsync(commandLine);

    [Then("the exit code is {int}")]
    public void TheExitCodeIs(int exitCode) => run.Result.ShouldExitWith(exitCode);

    [Then("the output contains {string}")]
    public void TheOutputContains(string text) => run.Result.OutputShouldContain(text);

    [Then("the output contains {string} or {string}")]
    public void TheOutputContainsOneOf(string first, string second) => run.Result.OutputShouldContainOneOf(first, second);

    [Then("the output does not contain {string}")]
    public void TheOutputDoesNotContain(string text) => run.Result.OutputShouldNotContain(text);

    [Then("the output is empty")]
    public void TheOutputIsEmpty() => run.Result.OutputShouldBeEmpty();

    [Then("standard output is empty")]
    public void StandardOutputIsEmpty() => run.Result.StandardOutputShouldBeEmpty();

    [Then("standard error contains {string}")]
    public void StandardErrorContains(string text) => run.Result.StandardErrorShouldContain(text);

    [Then("standard output is one JSON document")]
    public void StandardOutputIsOneJsonDocument() => run.Result.ShouldWriteOneJsonDocument();

    [Then("standard output is a version")]
    public void StandardOutputIsAVersion()
    {
        if (!Regex.IsMatch(run.Result.StandardOutput.Trim(), @"^\d+\.\d+\.\d+\S*$"))
            throw new CommandAssertionException("Standard output is not one version.", run.Result);
    }

    [Then("the command ran on the {string} host")]
    public void TheCommandRanOnTheHost(string host)
    {
        if (cli.Host.Name() != host)
            throw new CommandAssertionException($"The command ran on the {cli.Host.Name()} host, not on the {host} host.", run.Result);
    }
}
