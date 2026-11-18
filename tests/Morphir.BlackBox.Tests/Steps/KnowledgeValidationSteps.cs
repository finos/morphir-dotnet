// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.Steps;

[Binding]
public sealed class KnowledgeValidationSteps(IKnowledgeValidationDriver validation, IScenarioWorkspace workspace, ScenarioRun run)
{
    private WorkspaceDirectory _bundle = workspace.Directory("missing knowledge bundle");

    [Given("a knowledge bundle containing project documentation")]
    public void ProjectDocumentation() => _bundle = KnowledgeBundleMother.Write(workspace, KnowledgeBundleMother.ProjectDocumentation);

    [Given("a knowledge bundle with malformed frontmatter")]
    public void MalformedFrontmatter() => _bundle = KnowledgeBundleMother.Write(workspace, KnowledgeBundleMother.MalformedFrontmatter);

    [Given("a knowledge bundle with an unresolved local link")]
    public void UnresolvedLink() => _bundle = KnowledgeBundleMother.Write(workspace, KnowledgeBundleMother.UnresolvedLink);

    [Given("a knowledge bundle directory that is missing")]
    public void MissingDirectory() => _bundle = workspace.Directory("missing knowledge bundle");

    [When("I check the knowledge bundle as JSON")]
    public async Task ValidateAsJson() =>
        run.Result = await validation.ValidateAsync(_bundle, new KnowledgeValidationOptions { Json = JsonOutputArgument.Document });

    [When("I check the knowledge bundle as OKF {string}")]
    public async Task ValidateAsVersion(string version) =>
        run.Result = await validation.ValidateAsync(_bundle, new KnowledgeValidationOptions
        {
            Json = JsonOutputArgument.Document,
            Version = version,
        });

    [When("I check the knowledge bundle under Morphir policy")]
    public async Task ValidateWithMorphirPolicy() =>
        run.Result = await validation.ValidateAsync(_bundle, new KnowledgeValidationOptions
        {
            Json = JsonOutputArgument.Stream,
            MorphirPolicy = true,
        });

    [When("I ask how to validate a knowledge bundle")]
    public async Task ValidationHelp() => run.Result = await validation.HelpAsync();

    [Then("the knowledge report uses OKF {string} with conformance {string}")]
    public void ReportsVersionAndConformance(string version, string conformance)
    {
        var report = KnowledgeValidationReport.Of(run.Result);
        report.Check(report.Version == version, $"Expected OKF version {version}, got {report.Version}.");
        report.Check(report.Conformance == conformance, $"Expected conformance {conformance}, got {report.Conformance}.");
    }

    [Then("the knowledge report has the diagnostic {string}")]
    public void HasDiagnostic(string code) => KnowledgeValidationReport.Of(run.Result).ShouldHaveDiagnostic(code);

    [Then("the knowledge report has a located finding in the malformed concept")]
    public void HasLocatedFinding() => KnowledgeValidationReport.Of(run.Result).ShouldHaveLocatedFinding(KnowledgeBundleMother.MalformedConceptName);
}
