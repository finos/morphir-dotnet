// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps of <c>compile</c> and of its report, on an F# project in the work area of the workspace. The host of the
/// scenario compiles: a scenario that needs the F# frontend in the host names the host with its tag.
/// </summary>
[Binding]
public sealed class CompilationSteps(ICompilationDriver compilation, IGenerationDriver generation, IScenarioWorkspace workspace, ScenarioRun run)
{
    private const string ProjectFile = "src/Acme.CarRental.fsproj";

    private const string Rates = """
        namespace Acme.CarRental

        module Rates =
            let dailyRate (seats: int) : int = if seats > 4 then 80 else 60
        """;

    private const string Pricing = """
        namespace Acme.CarRental

        module Pricing =
            let total (seats: int) (days: int) : int = Rates.dailyRate seats * days
        """;

    private const string MutablePricing = """
        namespace Acme.CarRental

        module Pricing =
            let total (seats: int) (days: int) : int =
                let mutable sum = 0
                sum <- Rates.dailyRate seats * days
                sum
        """;

    private const string FsProj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <Compile Include="Rates.fs" />
            <Compile Include="Pricing.fs" />
          </ItemGroup>
        </Project>
        """;

    private string? _targetReport;

    private WorkspaceDirectory Project => workspace.Work;

    private WorkspaceDirectory OutRoot => Project.Directory(".morphir", "out");

    private WorkspaceFile PublishedIr => OutRoot.File("compile.dest", "morphir-ir.json");

    private WorkspaceFile Record => OutRoot.File("compile.json");

    private WorkspaceFile[] InputFiles => [Project.File("src", "Rates.fs"), Project.File("src", "Pricing.fs")];

    private CompilationReport Report => CompilationReport.Of(run.Result);

    [Given("an F# project whose morphir.toml names its project file")]
    public void ConfiguredProject() => WriteProject(configureProjectFile: true);

    [Given("an F# project without a configured project file")]
    public void UnconfiguredProject() => WriteProject(configureProjectFile: false);

    [Given("the source {string} uses a mutable binding")]
    public void MutableSource(string uri)
    {
        if (uri != "Pricing.fs") throw new ArgumentException("The project has one mutable variant: Pricing.fs.", nameof(uri));
        workspace.WriteText(Project.File("src", uri), MutablePricing);
    }

    [When("I compile the project at the IR version {string}")]
    public Task CompileAtVersion(string version) => Compile(new CompilationOptions { IrVersion = version });

    [When("I compile its input files at the IR version {string}")]
    public Task CompileInputsAtVersion(string version) => Compile(new CompilationOptions { Inputs = InputFiles, IrVersion = version });

    [When("I compile the project with the project file {string}")]
    public Task CompileWithProjectFile(string projectFile) => Compile(new CompilationOptions { ProjectFile = Project.File(projectFile) });

    [When("I compile the project")]
    public Task CompileTheProject() => Compile(new CompilationOptions());

    [When("I compile the project and install the IR to {string}")]
    public Task CompileAndInstall(string directory) => Compile(new CompilationOptions { Install = Project.Directory(directory) });

    /// <summary>Generates F# to the directory <c>output</c> of the work area, from the IR of the last compile of the project.</summary>
    [When("I generate F# for the project from its last compile")]
    public async Task GenerateFromRecord() =>
        run.Result = await generation.GenerateAsync(new GenerationRequest
        {
            Target = "fsharp",
            Output = workspace.Directory("output"),
            Options = new GenerationOptions { Project = Project },
        });

    [When("I compile the project from its {word} {word} with the target {string}")]
    public async Task CompileWithTarget(string input, string noun, string target)
    {
        await Compile(Sources(input) with { Target = target, GenerateOutput = Project.Directory("with-target") });
        CompilationReport.Of(run.Result.ShouldExitWith(0));
        _targetReport = run.Result.StandardOutput;
    }

    [When("I compile the project from its {word} {word} and then generate {string} from the compiled IR")]
    public async Task CompileThenGenerate(string input, string noun, string target)
    {
        await Compile(Sources(input));
        CompilationReport.Of(run.Result.ShouldExitWith(0));
        run.Result = await generation.GenerateAsync(PublishedIr, target, Project.Directory("two-step"));
        GenerationReport.Of(run.Result.ShouldExitWith(0)).ShouldSucceed();
    }

    [When("I compile its input files with the options {string}")]
    public Task CompileInputsWithOptions(string options) =>
        Compile(new CompilationOptions { Inputs = InputFiles, Arguments = options.Split(' ', StringSplitOptions.RemoveEmptyEntries) });

    private (WorkspaceFile File, string Package)? _source;

    [Given("the F# source file {string} of the package {string}")]
    public void TheFSharpSourceFile(string relative, string package)
    {
        var file = Project.File(relative);
        workspace.WriteText(file, "module Acme.Demo.Rates\n\nlet dailyRate (days: int) : int = days * 40\n");
        _source = (file, package);
    }

    [When("I compile the F# source file")]
    public async Task CompileTheFSharpSourceFile()
    {
        var (file, package) = _source ?? throw new InvalidOperationException("The scenario has no F# source file before this step.");
        run.Result = await compilation.CompileAsync(new CompilationOptions { Inputs = [file], Language = "fsharp", PackageName = package });
    }

    [Then("the compilation report says that the compile succeeded with the frontend {string}")]
    public void TheCompileSucceededWithTheFrontend(string frontend)
    {
        var report = Report.ShouldSucceed();
        report.Check(report.Frontend == frontend, $"The frontend of the compilation report is {report.Frontend ?? "null"}, not {frontend}.");
    }

    [Then("compilation exits with {int} and one JSON report")]
    public void CompilationReportOf(int exitCode) => CompilationReport.Of(run.Result.ShouldExitWith(exitCode));

    [Then("the compilation report has the IR version {string}")]
    public void ReportIrVersion(string version) =>
        Report.Check(Report.IrVersion == version, $"The IR version of the compilation report is {Report.IrVersion ?? "null"}, not {version}.");

    [Then("the published IR declares format version {int}")]
    public void PublishedFormatVersion(int version)
    {
        Check(workspace.Exists(PublishedIr), $"No IR is published at {PublishedIr.Path}.");
        using var ir = JsonDocument.Parse(workspace.ReadText(PublishedIr));
        var formatVersion = ir.RootElement.GetProperty("formatVersion");
        Check(formatVersion.ValueKind == JsonValueKind.Number, "The published IR writes an integer release as a text, not as an integer.");
        Check(formatVersion.GetRawText() == version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"The published IR declares the format version {formatVersion.GetRawText()}, not {version}.");
    }

    [Then("the command report has the diagnostic {string}")]
    public void ReportDiagnostic(string code) => CommandReport.Of(run.Result).ShouldHaveDiagnostic(code);

    [Then("the compilation report has the diagnostic {string} in {string}")]
    public void ReportDiagnosticAt(string code, string uri) => Report.ShouldHaveDiagnosticIn(code, uri);

    [Then("no IR is published")]
    public void NoIr() => Check(!workspace.Exists(PublishedIr), $"An IR is published at {PublishedIr.Path}.");

    [Then("the compilation report lists the source documents {string} in that order")]
    public void SourceDocuments(string documents)
    {
        var listed = Report.SourceDocuments;
        Check(listed.SequenceEqual(documents.Split(", ")), $"The compilation report lists the source documents {string.Join(", ", listed)}, not {documents}.");
    }

    [Then("the generation input is the IR named by the compile record")]
    public void GenerationInputFromRecord()
    {
        using var record = JsonDocument.Parse(workspace.ReadText(Record));
        var irPath = record.RootElement.GetProperty("ir").GetProperty("path").GetString()!;
        var expected = ReportPath.Normalize(OutRoot.Directory("compile.dest").File(irPath).Path);
        var input = GenerationReport.Of(run.Result).Input;
        Check(input is not null && ReportPath.Normalize(input) == expected, $"The generation input is {input ?? "null"}, not the IR of the compile record, {expected}.");
    }

    [Then("both reports name the same backend")]
    public void SameBackend()
    {
        using var combined = JsonDocument.Parse(_targetReport ?? throw new InvalidOperationException("The scenario has no compile with a target."));
        var backend = combined.RootElement.GetProperty("generation").GetProperty("backend").GetString();
        var generated = GenerationReport.Of(run.Result).Backend;
        Check(!string.IsNullOrEmpty(backend) && backend == generated, $"The compile with a target names the backend {backend ?? "null"}, and the generation names {generated ?? "null"}.");
    }

    [Then("both generated outputs have the same paths and bytes")]
    public void SameGeneratedOutputs()
    {
        var withTarget = Files(Project.Directory("with-target"));
        var twoStep = Files(Project.Directory("two-step"));
        Check(withTarget.Length > 0 && withTarget.SequenceEqual(twoStep), "The two generated outputs do not have the same paths and bytes.");
    }

    [Then("the compile record is a tombstone that keeps the earlier install ledger")]
    public void Tombstone()
    {
        using var record = JsonDocument.Parse(workspace.ReadText(Record));
        var root = record.RootElement;
        Check(root.GetProperty("tombstone").GetBoolean(), "The compile record is not a tombstone.");
        Check(root.GetProperty("value").GetArrayLength() == 0, "The tombstone has a value.");
        Check(!root.TryGetProperty("ir", out _) && !root.TryGetProperty("language", out _), "The tombstone has an IR or a language.");
        var installed = root.GetProperty("installed").EnumerateObject().ToArray();
        Check(installed.Length == 1, "The tombstone does not keep exactly one install.");
        Check(ReportPath.Normalize(installed[0].Name) == ReportPath.Normalize(Project.Directory("dist").Path), $"The tombstone keeps the install {installed[0].Name}, not dist.");
    }

    private CompilationOptions Sources(string input) => input switch
    {
        "input" => new CompilationOptions { Inputs = InputFiles },
        "project" => new CompilationOptions { ProjectFile = Project.File(ProjectFile) },
        _ => throw new ArgumentOutOfRangeException(nameof(input), input, "Use its input files or its project file."),
    };

    private void WriteProject(bool configureProjectFile)
    {
        workspace.WriteText(Project.File("src", "Rates.fs"), Rates);
        workspace.WriteText(Project.File("src", "Pricing.fs"), Pricing);
        workspace.WriteText(Project.File(ProjectFile), FsProj);
        workspace.WriteText(Project.File("morphir.toml"), $"""
            [project]
            name = "acme/car-rental"
            source_directory = "src"

            [frontend]
            language = "fsharp"
            {(configureProjectFile ? $"\n[frontend.fsharp]\nproject_file = \"{ProjectFile}\"\n" : "")}
            """);
    }

    private async Task Compile(CompilationOptions options) => run.Result = await compilation.CompileAsync(options with { Project = Project });

    private string[] Files(WorkspaceDirectory root) =>
        [.. workspace.FilesIn(root).Select(file => file + "\n" + Convert.ToHexString(workspace.ReadBytes(root.File(file))))];

    private void Check(bool condition, string reason)
    {
        if (!condition) throw new CommandAssertionException(reason, run.Result);
    }
}
