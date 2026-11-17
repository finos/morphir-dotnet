// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;
using Morphir.TestSupport.Fixtures;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>The steps of the <c>model</c> command group, of <c>model verify</c> and of its report.</summary>
[Binding]
public sealed class VerificationSteps(IModelVerificationDriver verification, ICliDriver cli, IScenarioWorkspace workspace, ScenarioRun run)
{
    [When("I verify the model")]
    public async Task VerifyTheModel() => run.Result = await verification.VerifyAsync(run.ModelLocation);

    [When("I verify the model with the schema version {int}")]
    public async Task VerifyTheModelWithTheSchemaVersion(int version) =>
        run.Result = await verification.VerifyAsync(run.ModelLocation, new VerificationOptions { SchemaVersion = version });

    [When("I verify the model as JSON")]
    public async Task VerifyTheModelAsJson() =>
        run.Result = await verification.VerifyAsync(run.ModelLocation, new VerificationOptions { Json = JsonOutputArgument.Stream });

    [When("I verify the model with the schema version {int} as JSON")]
    public async Task VerifyTheModelWithTheSchemaVersionAsJson(int version) =>
        run.Result = await verification.VerifyAsync(run.ModelLocation, new VerificationOptions { SchemaVersion = version, Json = JsonOutputArgument.Stream });

    [When("I verify the model in quiet mode")]
    public async Task VerifyTheModelInQuietMode() =>
        run.Result = await verification.VerifyAsync(run.ModelLocation, new VerificationOptions { Quiet = true });

    [When("I verify the model in quiet mode as JSON")]
    public async Task VerifyTheModelInQuietModeAsJson() =>
        run.Result = await verification.VerifyAsync(run.ModelLocation, new VerificationOptions { Quiet = true, Json = JsonOutputArgument.Stream });

    [When("I verify the model as a JSON document")]
    public async Task VerifyTheModelAsAJsonDocument() =>
        run.Result = await verification.VerifyAsync(run.ModelLocation, new VerificationOptions { Json = JsonOutputArgument.Document });

    [When("I verify the model as a JSON document with an option that it does not have")]
    public async Task VerifyTheModelAsAJsonDocumentWithAnUnknownOption() =>
        run.Result = await verification.VerifyAsync(run.ModelLocation, new VerificationOptions { Json = JsonOutputArgument.Document.Then("--no-such-option") });

    [When("I ask for the help of the model commands")]
    public async Task AskForTheHelpOfTheModelCommands() => run.Result = await cli.RunAsync(["model", "--help"]);

    /// <summary>
    /// Runs the words of the former <c>ir verify</c>. That no <c>morphir-ir</c> tool takes the command is a scenario of
    /// <c>ToolRouting.feature</c>, "Another unknown command starts nothing".
    /// </summary>
    [When("I verify the model with the former command")]
    public async Task VerifyTheModelWithTheFormerCommand() =>
        run.Result = await cli.RunAsync(["ir", "verify", run.Model.Path]);

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

    [Then("the verification report has the layout {string}")]
    public void TheReportHasTheLayout(string layout)
    {
        var report = VerificationReport.Of(run.Result);
        if (report.Layout != layout)
            throw new CommandAssertionException($"The layout of the verification report is {report.Layout ?? "null"}, not {layout}.", run.Result);
    }

    /// <summary>The report counts each file of the tree of the scenario, as the workspace lists them.</summary>
    [Then("the verification report counts every file of the tree")]
    public void TheReportCountsEveryFile()
    {
        var tree = run.ModelTree ?? throw new InvalidOperationException("The scenario has no model tree before this step.");
        var files = workspace.FilesIn(tree).Count;
        var report = VerificationReport.Of(run.Result);
        if (report.FilesChecked != files)
            throw new CommandAssertionException($"The verification report checked {report.FilesChecked?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"} files, and the tree has {files}.", run.Result);
    }

    /// <summary>An error of the report is at the fault of the model: in its file, at its JSON location, naming its words.</summary>
    [Then("an error of the verification report names the place of the fault")]
    public void AnErrorNamesThePlaceOfTheFault() => VerificationReport.Of(run.Result).ShouldHaveAnErrorAt(Fault);

    [Then("an error of the verification report has the code {string} and names the place of the fault")]
    public void AnErrorWithTheCodeNamesThePlaceOfTheFault(string code) => VerificationReport.Of(run.Result).ShouldHaveAnErrorAt(Fault, code);

    /// <summary>An error of the report has the code, and its message names each of the two versions as a number of its own.</summary>
    [Then("an error of the verification report has the code {string} and names the versions {int} and {int}")]
    public void AnErrorWithTheCodeNamesTheVersions(string code, int first, int second)
    {
        var report = VerificationReport.Of(run.Result);
        var named = report.Errors.Any(error => error.Code == code
            && Version(first).IsMatch(error.Message) && Version(second).IsMatch(error.Message));
        if (!named)
            throw new CommandAssertionException($"No error of the verification report has the code {code} and names {first} and {second}.", run.Result);
    }

    /// <summary>The text of the result names the file of the fault, on a line of its own.</summary>
    [Then("the output names the file of the fault")]
    public void TheOutputNamesTheFileOfTheFault()
    {
        var file = Fault.File ?? throw new InvalidOperationException("The fault of the scenario is in no file of a tree.");
        if (!run.Result.StandardOutput.Contains($"File: {file}", StringComparison.Ordinal))
            throw new CommandAssertionException($"The output does not name the file {file}.", run.Result);
    }

    private FixtureFault Fault => run.Fault ?? throw new InvalidOperationException("The scenario has no faulty model before this step.");

    /// <summary>The number as a word of its own, not part of a longer number such as <c>4000</c> or <c>3.1</c>.</summary>
    private static Regex Version(int version) => new($@"(?<![\w.]){version}(?!\w|\.\d)", RegexOptions.CultureInvariant);

    [Then("the verification report states the exit code {int}")]
    public void TheReportStatesTheExitCode(int exitCode) => VerificationReport.Of(run.Result).ShouldStateTheExitCode(exitCode);

    /// <summary>The parser's words for a command that it does not know, with the command in quotes.</summary>
    private static Regex UnknownCommand(string command) =>
        new($@"Unrecognized command or argument '{Regex.Escape(command)}'", RegexOptions.CultureInvariant);
}
