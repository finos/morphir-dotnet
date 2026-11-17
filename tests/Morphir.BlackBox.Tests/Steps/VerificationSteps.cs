// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>The steps of the <c>model</c> command group, of <c>model verify</c> and of its report.</summary>
[Binding]
public sealed class VerificationSteps(IModelVerificationDriver verification, ICliDriver cli, IToolRoutingDriver routing, ScenarioRun run)
{
    [When("I verify the model")]
    public async Task VerifyTheModel() => run.Result = await verification.VerifyAsync(run.Model);

    [When("I verify the model with the schema version {int}")]
    public async Task VerifyTheModelWithTheSchemaVersion(int version) =>
        run.Result = await verification.VerifyAsync(run.Model, new VerificationOptions { SchemaVersion = version });

    [When("I verify the model as JSON")]
    public async Task VerifyTheModelAsJson() =>
        run.Result = await verification.VerifyAsync(run.Model, new VerificationOptions { Json = JsonOutputArgument.Stream });

    [When("I verify the model with the schema version {int} as JSON")]
    public async Task VerifyTheModelWithTheSchemaVersionAsJson(int version) =>
        run.Result = await verification.VerifyAsync(run.Model, new VerificationOptions { SchemaVersion = version, Json = JsonOutputArgument.Stream });

    [When("I verify the model in quiet mode")]
    public async Task VerifyTheModelInQuietMode() =>
        run.Result = await verification.VerifyAsync(run.Model, new VerificationOptions { Quiet = true });

    [When("I verify the model in quiet mode as JSON")]
    public async Task VerifyTheModelInQuietModeAsJson() =>
        run.Result = await verification.VerifyAsync(run.Model, new VerificationOptions { Quiet = true, Json = JsonOutputArgument.Stream });

    [When("I ask for the help of the model commands")]
    public async Task AskForTheHelpOfTheModelCommands() => run.Result = await cli.RunAsync(["model", "--help"]);

    /// <summary>
    /// Runs the words of the former <c>ir verify</c> with an empty search path, so no <c>morphir-ir</c> tool can take
    /// the command.
    /// </summary>
    [When("I verify the model with the former command, and no morphir-ir is on the search path")]
    public async Task VerifyTheModelWithTheFormerCommand() =>
        run.Result = await routing.RunAsync(["ir", "verify", run.Model.Path], new ToolRoutingOptions());

    /// <summary>
    /// The help has a row for the command, with text after its name: its one-line description. The host writes help to
    /// standard output or to standard error, so both are read.
    /// </summary>
    [Then("the help lists the command {string} with a description")]
    public void TheHelpListsTheCommand(string command)
    {
        var row = new Regex($@"(?m)^[ \t]+{Regex.Escape(command)}\b[^\r\n]*?[ \t]{{2,}}\S");
        if (!row.IsMatch(run.Result.StandardOutput + run.Result.StandardError))
            throw new CommandAssertionException($"The help has no row for the command {command} with a description.", run.Result);
    }

    [Then("the command fails and names {string} as an unknown command")]
    public void TheCommandFailsWithTheUnknownCommand(string command)
    {
        if (run.Result.ExitCode == 0)
            throw new CommandAssertionException("The command exits with 0.", run.Result);
        if (!UnknownCommand(command).IsMatch(run.Result.StandardOutput + run.Result.StandardError))
            throw new CommandAssertionException($"The output does not name '{command}' as an unknown command.", run.Result);
    }

    /// <summary>The run wrote no verification result: no text report and no JSON report.</summary>
    [Then("the model was not verified")]
    public void TheModelWasNotVerified()
    {
        var output = run.Result.StandardOutput + run.Result.StandardError;
        if (output.Contains("Validation Result", StringComparison.Ordinal) || output.Contains("\"IsValid\"", StringComparison.Ordinal))
            throw new CommandAssertionException("The run wrote a verification result.", run.Result);
    }

    [Then("the verification report says that the model is valid")]
    public void TheModelIsValid() => VerificationReport.Of(run.Result).ShouldBeValid();

    [Then("the verification report says that the model is not valid")]
    public void TheModelIsNotValid() => VerificationReport.Of(run.Result).ShouldBeInvalid();

    [Then("the verification report has the schema version {string}")]
    public void TheReportHasTheSchemaVersion(string version)
    {
        var report = VerificationReport.Of(run.Result);
        if (report.SchemaVersion != version)
            throw new CommandAssertionException($"The schema version of the verification report is {report.SchemaVersion ?? "null"}, not {version}.", run.Result);
    }

    [Then("the verification report has the detection method {string}")]
    public void TheReportHasTheDetectionMethod(string method)
    {
        var report = VerificationReport.Of(run.Result);
        if (report.DetectionMethod != method)
            throw new CommandAssertionException($"The detection method of the verification report is {report.DetectionMethod ?? "null"}, not {method}.", run.Result);
    }

    [Then("the verification report has errors")]
    public void TheReportHasErrors()
    {
        if (VerificationReport.Of(run.Result).Errors.Count == 0)
            throw new CommandAssertionException("The verification report has no error.", run.Result);
    }

    [Then("the verification report states the exit code {int}")]
    public void TheReportStatesTheExitCode(int exitCode) => VerificationReport.Of(run.Result).ShouldStateTheExitCode(exitCode);

    /// <summary>The parser's words for a command that it does not know, with the command in quotes.</summary>
    private static Regex UnknownCommand(string command) =>
        new($@"Unrecognized command or argument '{Regex.Escape(command)}'", RegexOptions.CultureInvariant);
}
