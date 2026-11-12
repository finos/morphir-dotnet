// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.Steps;

/// <summary>The steps of the <c>ir verify</c> command and of its report.</summary>
[Binding]
public sealed class VerificationSteps(IModelVerificationDriver verification, ScenarioRun run)
{
    [When("I verify the model")]
    public async Task VerifyTheModel() => run.Result = await verification.VerifyAsync(run.Model);

    [When("I verify the model with the schema version {int}")]
    public async Task VerifyTheModelWithTheSchemaVersion(int version) =>
        run.Result = await verification.VerifyAsync(run.Model, new VerificationOptions { SchemaVersion = version });

    [When("I verify the model as JSON")]
    public async Task VerifyTheModelAsJson() =>
        run.Result = await verification.VerifyAsync(run.Model, new VerificationOptions { Json = true });

    [When("I verify the model in quiet mode")]
    public async Task VerifyTheModelInQuietMode() =>
        run.Result = await verification.VerifyAsync(run.Model, new VerificationOptions { Quiet = true });

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

    [Then("the verification report has errors")]
    public void TheReportHasErrors()
    {
        if (VerificationReport.Of(run.Result).Errors.Count == 0)
            throw new CommandAssertionException("The verification report has no error.", run.Result);
    }
}
