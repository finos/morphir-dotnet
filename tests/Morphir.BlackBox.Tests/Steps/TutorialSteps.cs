// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps of the F# pricing tutorial (<c>docs/content/docs/guides/fsharp-pricing-tutorial.md</c>) and of its
/// example <c>examples/car-rental-pricing</c>. A marked code block of the tutorial has a first line that names a file
/// of the example, such as <c>// src/Rates.fs</c> or <c># morphir.toml</c>.
/// </summary>
[Binding]
public sealed partial class TutorialSteps(
    ICompilationDriver compilation, IGenerationDriver generation, IModelValidationDriver validation, IScenarioWorkspace workspace, ScenarioRun run)
{
    private const string Example = "Examples/car-rental-pricing";

    [GeneratedRegex("```(?<language>[a-z]+)\\n(?<body>.*?)\\n```", RegexOptions.Singleline)]
    private static partial Regex CodeBlock();

    [GeneratedRegex("^(?:(?://|#) (?<file>[A-Za-z./]+)|<!-- (?<file>[A-Za-z./]+) -->)$")]
    private static partial Regex FileMarker();

    /// <summary>The copy of the example in the work area, so that a run writes nothing in the repository.</summary>
    private WorkspaceDirectory Project => workspace.Directory("acme");

    private string Page(string page) => workspace.ReadFixtureText("Docs/" + page).Replace("\r\n", "\n");

    private string ExampleFile(string relative) => workspace.ReadFixtureText($"{Example}/{relative}").Replace("\r\n", "\n").TrimEnd('\n');

    [Then("each marked code block of the tutorial is the file of the example that it names")]
    public void EachMarkedCodeBlockIsAFileOfTheExample()
    {
        var blocks = CodeBlock().Matches(Page("guides/fsharp-pricing-tutorial.md"))
            .Select(match => match.Groups["body"].Value.Split('\n', 2))
            .Select(lines => (Marker: FileMarker().Match(lines[0]), Body: lines.Length > 1 ? lines[1] : ""))
            .Where(item => item.Marker.Success)
            .ToDictionary(item => item.Marker.Groups["file"].Value, item => item.Body);
        string[] marked = ["src/Rates.fs", "src/Quote.fs", "src/Discount.fs", "morphir.toml", "Acme.CarRental.fsproj"];
        Expect(blocks.Keys.Order(StringComparer.Ordinal).SequenceEqual(marked.Order(StringComparer.Ordinal)),
            $"The tutorial marks the files {string.Join(", ", blocks.Keys)}, not {string.Join(", ", marked)}.");

        foreach (var file in new[] { "src/Rates.fs", "src/Quote.fs", "src/Discount.fs", "morphir.toml" })
            Expect(blocks[file] == ExampleFile(file), $"The code block of {file} is not the file of the example.");

        // Step 1 shows the project file before step 4 adds Discount.fs.
        var withDiscount = blocks["Acme.CarRental.fsproj"].Replace(
            "    <Compile Include=\"src/Quote.fs\" />\n",
            "    <Compile Include=\"src/Quote.fs\" />\n    <Compile Include=\"src/Discount.fs\" />\n");
        Expect(withDiscount == ExampleFile("Acme.CarRental.fsproj"), "The project file of the tutorial, with Discount.fs, is not the project file of the example.");
    }

    [Then("the first F# code block of the page {string} is the file {string} of the example")]
    public void TheFirstFSharpCodeBlockIsTheFile(string page, string file)
    {
        var rule = CodeBlock().Matches(Page(page)).First(match => match.Groups["language"].Value == "fsharp").Groups["body"].Value;
        Expect(rule == ExampleFile(file), $"The first F# code block of {page} is not the file {file} of the example.");
    }

    [Given("a copy of the example of the tutorial")]
    public void ACopyOfTheExample() => workspace.CopyFixtureDirectory(Example, "acme");

    [Given("the guarded discount of the tutorial in the example")]
    public void TheGuardedDiscount()
    {
        var guarded = CodeBlock().Matches(Page("guides/fsharp-pricing-tutorial.md"))
            .Select(match => match.Groups["body"].Value)
            .Single(body => body.Contains("| d when d >= 7", StringComparison.Ordinal));
        workspace.WriteText(Project.File("src", "Discount.fs"), guarded + "\n");
    }

    [When("I compile the example as JSON")]
    public async Task CompileAsJson() => run.Result = await compilation.CompileAsync(new CompilationOptions { Project = Project });

    [When("I compile the example")]
    public async Task Compile() =>
        run.Result = await compilation.CompileAsync(new CompilationOptions { Output = CompilationOutput.Text, Project = Project });

    [Then("the compile of the example succeeds with the modules {string} and no diagnostic")]
    public void TheCompileSucceeds(string modules)
    {
        var report = CompilationReport.Of(run.Result.ShouldExitWith(0)).ShouldSucceed();
        report.Check(report.Modules.SequenceEqual(modules.Split(", ")), $"The modules are {string.Join(", ", report.Modules)}, not {modules}.");
        report.Check(report.Diagnostics.Count == 0, "The compilation report has diagnostics.");
    }

    [When("I generate {string} from the last compile of the example to {string}")]
    public async Task GenerateFromTheLastCompile(string target, string output) =>
        run.Result = await generation.GenerateAsync(new GenerationRequest
        {
            Target = target,
            Output = Project.Directory(output),
            Options = new GenerationOptions { Json = false, Project = Project },
        });

    [Then("the example has the generated C# files {string} in {string}")]
    public void TheExampleHasTheGeneratedFiles(string files, string output)
    {
        var generated = workspace.FilesIn(Project.Directory(output)).Where(file => file.EndsWith(".g.cs", StringComparison.Ordinal))
            .Select(file => file.Split('/')[^1]).Order(StringComparer.Ordinal).ToArray();
        Expect(generated.SequenceEqual(files.Split(", ").Order(StringComparer.Ordinal)), $"The generated C# files are {string.Join(", ", generated)}, not {files}.");
    }

    [When("I compile the example at the IR version {string} and install the IR to {string}")]
    public async Task CompileAndInstall(string version, string directory) =>
        run.Result = await compilation.CompileAsync(new CompilationOptions
        {
            Output = CompilationOutput.Text,
            Project = Project,
            IrVersion = version,
            Install = Project.Directory(directory),
        });

    [When("I validate the IR that the example installed in {string}")]
    public async Task ValidateTheInstalledIr(string directory) =>
        run.Result = await validation.ValidateAsync(Project.File(directory, "morphir-ir.json"));

    [When("I compile the example with the target {string} and the generated output {string}")]
    public async Task CompileWithTheTarget(string target, string output) =>
        run.Result = await compilation.CompileAsync(new CompilationOptions
        {
            Output = CompilationOutput.Text,
            Project = Project,
            Target = target,
            GenerateOutput = Project.Directory(output),
        });

    private void Expect(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }
}
