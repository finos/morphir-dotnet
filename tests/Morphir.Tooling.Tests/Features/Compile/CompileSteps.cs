using System.Text.Json;
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.Tooling.Tests.Integration.CLI;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>
/// Steps for the compile acceptance scenarios. They run the real CLI hosts: the JIT <c>dotnet-morphir</c>
/// tool, which has the in-process F# frontend, and the <c>morphir</c> executable, which has none.
/// </summary>
[Binding]
public sealed class CompileSteps : IDisposable
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

    private readonly string _project = Path.Combine(Path.GetTempPath(), "morphir-compile-" + Guid.NewGuid().ToString("N"));
    private readonly CliTestHelper _tool = new(Environment.GetEnvironmentVariable("MORPHIR_DOTNET_TOOL_CLI"), CliHost.DotnetTool);
    private readonly CliTestHelper _morphir = new(Environment.GetEnvironmentVariable("MORPHIR_GENERATION_CLI"), CliHost.Morphir);
    private readonly CliRun _run;

    public CompileSteps(CliRun run) => _run = run;

    private string OutRoot => Path.Combine(_project, ".morphir", "out");
    private string PublishedIr => Path.Combine(OutRoot, "compile.dest", "morphir-ir.json");
    private string Record => Path.Combine(OutRoot, "compile.json");

    [Given("an F# project whose morphir.toml names its project file")]
    public void ConfiguredProject() => WriteProject(configureProjectFile: true);

    [Given("an F# project without a configured project file")]
    public void UnconfiguredProject() => WriteProject(configureProjectFile: false);

    [Given("the source {string} uses a mutable binding")]
    public void MutableSource(string uri)
    {
        uri.Should().Be("Pricing.fs", "the fixture has one mutable variant");
        File.WriteAllText(Path.Combine(_project, "src", uri), MutablePricing);
    }

    [When("I compile it with the dotnet tool at IR version {string}")]
    public Task CompileAtVersion(string version) => Compile(_tool, $"--ir-version {version}");

    [When("I compile its input files with the dotnet tool at IR version {string}")]
    public Task CompileInputsAtVersion(string version) =>
        Compile(_tool, $"--input \"{Path.Combine(_project, "src", "Rates.fs")}\" \"{Path.Combine(_project, "src", "Pricing.fs")}\" --ir-version {version}");

    [When("I compile it with the dotnet tool using --project-file {string}")]
    public Task CompileWithProjectFile(string projectFile) =>
        Compile(_tool, $"--project-file \"{Path.Combine(_project, projectFile)}\"");

    [When("I compile it with the morphir executable")]
    public Task CompileWithMorphir() => Compile(_morphir, "");

    [When("I compile it with the dotnet tool and install the IR to {string}")]
    public Task CompileAndInstall(string directory) => Compile(_tool, $"-o \"{Path.Combine(_project, directory)}\"");

    [When("I generate F# for the project without an input")]
    public Task GenerateFromRecord()
    {
        _run.GenerationOutput = Path.Combine(_project, "generated");
        return Run(_tool, $"generate --json --project \"{_project}\" --target fsharp --output \"{_run.GenerationOutput}\"");
    }

    [When("I compile it with the dotnet tool with target {string}")]
    public Task CompileWithTarget(string target) =>
        Compile(_tool, $"--target {target} --generate-output \"{Path.Combine(_project, "with-target")}\"");

    [When("I compile it with the dotnet tool and then generate F# from the compiled IR")]
    public async Task CompileThenGenerate()
    {
        await Compile(_tool, "");
        _run.ShouldExitWithOneJsonReport(0);
        await Run(_tool, $"generate --json --input \"{PublishedIr}\" --target fsharp --output \"{Path.Combine(_project, "two-step")}\"");
        _run.ShouldExitWithOneJsonReport(0);
    }

    [Then("compilation exits with {int} and one JSON report")]
    public void CompilationReport(int exitCode) => _run.ShouldExitWithOneJsonReport(exitCode);

    [Then("the report IR version is {string}")]
    public void ReportIrVersion(string version)
    {
        using var report = _run.Report();
        report.RootElement.GetProperty("irVersion").GetString().Should().Be(version);
    }

    [Then("the published IR declares format version {int}")]
    public void PublishedFormatVersion(int version)
    {
        File.Exists(PublishedIr).Should().BeTrue(PublishedIr);
        using var ir = JsonDocument.Parse(File.ReadAllText(PublishedIr));
        var formatVersion = ir.RootElement.GetProperty("formatVersion");
        formatVersion.ValueKind.Should().Be(JsonValueKind.Number, "integer releases are written as integers");
        formatVersion.GetRawText().Should().Be(version.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Then("the report has the diagnostic {string}")]
    public void ReportDiagnostic(string code) => Diagnostics().Should().Contain(d => d.Code == code, _run.Result!.StandardOutput);

    [Then("the report has the diagnostic {string} in {string}")]
    public void ReportDiagnosticAt(string code, string uri) =>
        Diagnostics().Should().Contain(d => d.Code == code && d.Uri == uri, _run.Result!.StandardOutput);

    [Then("no IR is published")]
    public void NoIr() => File.Exists(PublishedIr).Should().BeFalse();

    [Then("the report lists the source documents {string} in that order")]
    public void SourceDocuments(string documents)
    {
        using var report = _run.Report();
        report.RootElement.GetProperty("sources").GetProperty("documents").EnumerateArray()
            .Select(document => document.GetString())
            .Should().Equal(documents.Split(", "));
    }

    [Then("the generation input is the IR named by the compile record")]
    public void GenerationInputFromRecord()
    {
        using var record = JsonDocument.Parse(File.ReadAllText(Record));
        var irPath = record.RootElement.GetProperty("ir").GetProperty("path").GetString()!;
        using var report = _run.Report();
        Path.GetFullPath(report.RootElement.GetProperty("input").GetString()!)
            .Should().Be(Path.GetFullPath(Path.Combine(OutRoot, "compile.dest", irPath)));
    }

    [Then("both generated outputs have the same paths and bytes")]
    public void SameGeneratedOutputs() =>
        Files(Path.Combine(_project, "with-target")).Should().Equal(Files(Path.Combine(_project, "two-step")));

    [Then("the compile record is a tombstone that keeps the earlier install ledger")]
    public void Tombstone()
    {
        using var record = JsonDocument.Parse(File.ReadAllText(Record));
        var root = record.RootElement;
        root.GetProperty("tombstone").GetBoolean().Should().BeTrue();
        root.GetProperty("value").GetArrayLength().Should().Be(0);
        root.TryGetProperty("ir", out _).Should().BeFalse();
        root.TryGetProperty("language", out _).Should().BeFalse();
        var installed = root.GetProperty("installed").EnumerateObject().ToArray();
        installed.Should().ContainSingle();
        Path.GetFullPath(installed[0].Name).Should().Be(Path.GetFullPath(Path.Combine(_project, "dist")));
    }

    private void WriteProject(bool configureProjectFile)
    {
        Directory.CreateDirectory(Path.Combine(_project, "src"));
        File.WriteAllText(Path.Combine(_project, "src", "Rates.fs"), Rates);
        File.WriteAllText(Path.Combine(_project, "src", "Pricing.fs"), Pricing);
        File.WriteAllText(Path.Combine(_project, ProjectFile), FsProj);
        File.WriteAllText(Path.Combine(_project, "morphir.toml"), $"""
            [project]
            name = "acme/car-rental"
            source_directory = "src"

            [frontend]
            language = "fsharp"
            {(configureProjectFile ? $"\n[frontend.fsharp]\nproject_file = \"{ProjectFile}\"\n" : "")}
            """);
    }

    private Task Compile(CliTestHelper host, string options) =>
        Run(host, $"compile --json --project \"{_project}\" {options}".TrimEnd());

    private async Task Run(CliTestHelper host, string arguments)
    {
        _run.Result = await host.ExecuteCommandAsync(arguments, timeout: TimeSpan.FromSeconds(120));
    }

    private (string Code, string? Uri)[] Diagnostics()
    {
        using var report = _run.Report();
        return report.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Select(diagnostic => (
                diagnostic.GetProperty("code").GetString()!,
                diagnostic.TryGetProperty("location", out var location) ? location.GetProperty("uri").GetString() : null))
            .ToArray();
    }

    private static string[] Files(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/') + "\n" + Convert.ToHexString(File.ReadAllBytes(path)))
            .ToArray();

    public void Dispose()
    {
        TolerantDirectory.TryDelete(_project);
    }
}
