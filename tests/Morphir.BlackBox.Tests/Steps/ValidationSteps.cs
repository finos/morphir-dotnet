// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.Steps;

/// <summary>The steps of the <c>validate</c> command and of its report.</summary>
[Binding]
public sealed class ValidationSteps(IValidationDriver validation, DriverSet drivers, ScenarioRun run, OtherHostRun other)
{
    private ValidationReport Report => ValidationReport.Of(run.Result);

    [When("I validate the model")]
    public async Task ValidateTheModel() => run.Result = await validation.ValidateAsync(run.Model);

    [When("I validate the model as text")]
    public async Task ValidateTheModelAsText() => run.Result = await validation.ValidateAsync(run.Model, new ValidationOptions { Json = false });

    [When("I validate the model on the other host")]
    public async Task ValidateTheModelOnTheOtherHost() =>
        other.Result = await new ValidationDriver(drivers.CliOf(drivers.OtherHost)).ValidateAsync(run.Model);

    [Then("the validation report has the exit code {int}")]
    public void TheReportHasTheExitCode(int exitCode) => Report.ShouldHaveTheExitCode(exitCode);

    [Then("the validation report has no diagnostic")]
    public void TheReportHasNoDiagnostic() =>
        Expect(Report.Diagnostics.IsEmpty, $"The validation report has {Report.Diagnostics.Length} diagnostics.");

    [Then("the validation report has {int} {string} diagnostic with the code {string} from {string}")]
    public void TheReportHasDiagnosticsFrom(int count, string severity, string code, string extension) =>
        Expect(Report.Count(severity, code, extension) == count,
            $"The validation report has {Report.Count(severity, code, extension)} {severity} diagnostics with the code {code} from {extension}, not {count}.");

    [Then("the validation report has {int} {string} diagnostic with the code {string}")]
    public void TheReportHasDiagnostics(int count, string severity, string code) =>
        Expect(Report.Count(severity, code) == count,
            $"The validation report has {Report.Count(severity, code)} {severity} diagnostics with the code {code}, not {count}.");

    [Then("the validation report lists {string} as {string}")]
    public void TheReportListsTheAnalyzerAs(string id, string status)
    {
        var analyzers = Report.Analyzers.Where(analyzer => analyzer.Id == id).ToArray();
        Expect(analyzers is [{ } one] && one.Status == status,
            $"The validation report lists {id} as {string.Join(", ", analyzers.Select(analyzer => analyzer.Status))}, not {status}.");
    }

    [Then("the analyzers of the validation report are the validator entries of extension list")]
    public async Task TheAnalyzersAreTheValidatorEntries()
    {
        var analyzers = Report.Analyzers.Select(analyzer => analyzer.Id).ToArray();
        var list = (await drivers.Cli.RunAsync(ExtensionListReport.Arguments)).ShouldExitWith(0);
        var validators = ExtensionListReport.Of(list).Extensions.Where(entry => entry.Types.Contains("validator")).Select(entry => entry.Id).ToArray();
        Expect(analyzers.Length > 0 && analyzers.SequenceEqual(validators),
            $"The analyzers {string.Join(", ", analyzers)} are not the validators {string.Join(", ", validators)} of extension list.");
    }

    [Then("the validation reports of both hosts are equal apart from the host")]
    public void TheReportsOfBothHostsAreEqual()
    {
        if (ValidationReport.DifferenceApartFromTheHost(run.Result, other.Result) is { } difference)
            throw new CommandAssertionException(difference, other.Result);
    }

    private void Expect(bool condition, string reason)
    {
        if (!condition) throw new CommandAssertionException(reason, run.Result);
    }
}
