// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.Steps;

/// <summary>The steps of the <c>generate</c> command, of its report and of its output directory.</summary>
[Binding]
public sealed class GenerationSteps(IGenerationDriver generation, IScenarioWorkspace workspace, ScenarioRun run)
{
    // The first moment of the year 2000: a time that no run of the command gives to a file.
    private static readonly DateTime Year2000 = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private IReadOnlyDictionary<string, string>? _remembered;

    private WorkspaceDirectory Output => workspace.Directory("output");

    private GenerationReport Report => GenerationReport.Of(run.Result);

    private async Task Generate(string target, GenerationOptions options) =>
        run.Result = run.ModelTree is { } tree
            ? await generation.GenerateAsync(tree, target, Output, options)
            : await generation.GenerateAsync(run.Model, target, Output, options);

    [When("I generate {string} from the model")]
    public Task GenerateFromTheModel(string target) => Generate(target, new GenerationOptions());

    [When("I generate {string} from the model as text")]
    public Task GenerateFromTheModelAsText(string target) => Generate(target, new GenerationOptions { Json = false });

    [When("I generate {string} from the model with the option {string}")]
    public Task GenerateWithTheOption(string target, string option) => Generate(target, new GenerationOptions { Options = [option] });

    [When("I generate {string} from the model with the options")]
    public Task GenerateWithTheOptions(string target, Table options) =>
        Generate(target, new GenerationOptions { Options = [.. options.Rows.Select(row => row["option"])] });

    [Then("the generation report says that the generation succeeded")]
    public void TheGenerationSucceeded() => Report.ShouldSucceed();

    [Then("the generation report says that the generation failed with the kind {string}")]
    public void TheGenerationFailedWithTheKind(string kind) => Report.ShouldFail(kind);

    [Then("the generation report has the backend {string}")]
    public void TheReportHasTheBackend(string backend) => Check(Report.Backend == backend, $"The backend of the generation report is {Report.Backend ?? "null"}, not {backend}.");

    [Then("the generation report has the source version {string}")]
    public void TheReportHasTheSourceVersion(string version) =>
        Check(Report.SourceVersion == version, $"The source version of the generation report is {Report.SourceVersion ?? "null"}, not {version}.");

    [Then("the generation report has diagnostics")]
    public void TheReportHasDiagnostics() => Check(Report.Diagnostics.Count > 0, "The generation report has no diagnostic.");

    [Then("the first diagnostic of the generation report has the code {string} and a location")]
    public void TheFirstDiagnosticHasTheCodeAndALocation(string code)
    {
        var diagnostics = Report.Diagnostics;
        Check(diagnostics.Count > 0, "The generation report has no diagnostic.");
        Check(diagnostics[0].Code == code, $"The code of the first diagnostic is {diagnostics[0].Code}, not {code}.");
        Check(!string.IsNullOrEmpty(diagnostics[0].Location), "The first diagnostic has no location.");
    }

    [Then("the generation report has no artifact")]
    public void TheReportHasNoArtifact() => Check(Report.Artifacts.Count == 0, $"The generation report has the artifacts: {string.Join(", ", Report.Artifacts)}.");

    [Then("the generation report has no written file and has unchanged files")]
    public void TheReportHasNoWrittenFileAndHasUnchangedFiles()
    {
        Check(Report.Written.Count == 0, $"The generation report has the written files: {string.Join(", ", Report.Written)}.");
        Check(Report.Unchanged.Count > 0, "The generation report has no unchanged file.");
    }

    [Then("the generation report says that it removed only {string}")]
    public void TheReportRemovedOnly(string file) =>
        Check(Report.Removed.SequenceEqual([file]), $"The generation report removed: {string.Join(", ", Report.Removed)}. Expected only {file}.");

    [Then("each artifact of the generation report is a file of the output")]
    public void EachArtifactIsAFileOfTheOutput()
    {
        var artifacts = Report.Artifacts;
        Check(artifacts.Count > 0, "The generation report has no artifact.");
        var missing = artifacts.Where(artifact => !workspace.Exists(Output.File(artifact))).ToArray();
        Check(missing.Length == 0, $"The output has no file for the artifacts: {string.Join(", ", missing)}.");
    }

    [Then("the first artifact of the generation report contains {string} and not {string}")]
    public void TheFirstArtifactContainsAndNot(string text, string absent)
    {
        var artifacts = Report.Artifacts;
        Check(artifacts.Count > 0, "The generation report has no artifact.");
        var source = workspace.ReadText(Output.File(artifacts[0]));
        Check(source.Contains(text, StringComparison.Ordinal), $"The artifact {artifacts[0]} does not contain '{text}'.");
        Check(!source.Contains(absent, StringComparison.Ordinal), $"The artifact {artifacts[0]} contains '{absent}'.");
    }

    [Then("the output has one C# file for the module {string} of the package {string}")]
    public void TheOutputHasOneFileForTheModule(string module, string package)
    {
        var files = workspace.FilesIn(Output).Where(file => file.EndsWith(".cs", StringComparison.Ordinal)).ToArray();
        var expected = $"p_{package}/m_{char.ToUpperInvariant(module[0])}{module[1..]}Module.g.cs";
        Check(files.SequenceEqual([expected]), $"The output has the C# files: {string.Join(", ", files)}. Expected only {expected}.");
    }

    [Given("I remember the files of the output")]
    public void RememberTheFilesOfTheOutput() => _remembered = Snapshot();

    [Then("the files of the output are the same as I remembered")]
    public void TheFilesOfTheOutputAreTheSame()
    {
        var before = _remembered ?? throw new InvalidOperationException("The scenario did not remember the files of the output.");
        var after = Snapshot();
        var changed = before.Keys.Union(after.Keys)
            .Where(file => !before.TryGetValue(file, out var was) || !after.TryGetValue(file, out var now) || was != now)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Check(changed.Length == 0, $"The command changed the files of the output: {string.Join(", ", changed)}.");
    }

    [Then("the output directory is not there")]
    public void TheOutputDirectoryIsNotThere() => Check(!workspace.Exists(Output), $"The output directory {Output.Path} is there.");

    [Given("the output has the file {string} with the text {string}")]
    public void TheOutputHasTheFileWithTheText(string file, string text) => workspace.WriteText(Output.File(file), text);

    [Then("the output file {string} has the text {string}")]
    public void TheOutputFileHasTheText(string file, string text)
    {
        var actual = workspace.ReadText(Output.File(file));
        Check(actual == text, $"The output file {file} has the text '{actual}', not '{text}'.");
    }

    [Then("the output has no file {string}")]
    public void TheOutputHasNoFile(string file) => Check(!workspace.Exists(Output.File(file)), $"The output has the file {file}.");

    [Given("each file of the output has the time of the year 2000")]
    public void EachFileOfTheOutputHasTheTimeOfTheYear2000()
    {
        foreach (var file in workspace.FilesIn(Output)) workspace.SetLastWriteTimeUtc(Output.File(file), Year2000);
    }

    [Then("each file of the output still has the time of the year 2000")]
    public void EachFileOfTheOutputStillHasTheTimeOfTheYear2000()
    {
        var changed = workspace.FilesIn(Output).Where(file => workspace.LastWriteTimeUtc(Output.File(file)) != Year2000).ToArray();
        Check(changed.Length == 0, $"The command wrote these files of the output again: {string.Join(", ", changed)}.");
    }

    // Each file of the output with its content, so that a file that the command removed or changed is found.
    private Dictionary<string, string> Snapshot() =>
        workspace.FilesIn(Output).ToDictionary(file => file, file => Convert.ToBase64String(workspace.ReadBytes(Output.File(file))));

    private void Check(bool condition, string reason)
    {
        if (!condition) throw new CommandAssertionException(reason, run.Result);
    }
}
