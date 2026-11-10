using System.Text.Json;
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.Testing;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Tests.Integration.CLI;

namespace Morphir.Tooling.Tests.Features.Generate;

[Binding]
public sealed class GenerateSteps : IDisposable
{
    private readonly TestSandbox _sandbox = TestSandbox.Create("morphir-generate-");
    private string _directory => _sandbox.PathOf("scenario");
    private readonly CliTestHelper _cli = new(Environment.GetEnvironmentVariable("MORPHIR_GENERATION_CLI"));
    private readonly CliRun _run;
    private string _input = "";
    private string _version = "";
    private string _format = "";
    private string[]? _firstSources;
    private string Output => _run.GenerationOutput ?? Path.Combine(_directory, "output");

    public GenerateSteps(CliRun run) => _run = run;

    [Given("a daily-rate model at version {string} in {string}")]
    public void Model(string version, string format)
    {
        _version = version;
        _format = format;
        Directory.CreateDirectory(_directory);
        if (format == "json")
        {
            _input = Path.Combine(_directory, "model.json");
            File.WriteAllText(_input, version == "3"
                ? File.ReadAllText(Path.Combine(RepositoryRoot(), "tests", "Fixtures", "MorphirIR", "daily-rate-v3.json"))
                : V4Model);
        }
        else
        {
            _input = Path.Combine(_directory, "model-tree");
            var fixture = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "MorphirIon", "trees", "v3-daily-rate");
            foreach (var file in Directory.EnumerateFiles(fixture, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(_input, Path.GetRelativePath(fixture, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            var manifest = Path.Combine(_input, "manifest.ion");
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("3.0.0", version));
        }
    }

    [When("I generate F# with explicit settings")]
    public async Task Generate() => await Invoke(null);

    [When("I generate F# without {string}")]
    public async Task GenerateWithout(string setting) => await Invoke(setting);

    private async Task Invoke(string? omit, params string[] extra)
    {
        var arguments = new List<string> { "generate", "--json" };
        if (omit != "input") arguments.Add($"--input \"{_input}\"");
        if (omit != "target") arguments.Add("--target fsharp");
        if (omit != "output") arguments.Add($"--output \"{Output}\"");
        arguments.AddRange(extra);
        _run.Result = await _cli.ExecuteCommandAsync(string.Join(" ", arguments), timeout: TimeSpan.FromSeconds(120));
    }

    /// <summary>The external F# backend guest, morphir-fsharp-backend, from the test output.</summary>
    private static string BackendGuest => Path.Combine(AppContext.BaseDirectory, "morphir-fsharp-backend.dll");

    /// <summary>The guest's own extension id, from the guest; the one place the tests take it from.</summary>
    private static string BackendGuestId => Morphir.CodeGeneration.FSharp.Extension.FSharpGuest.extension.Id;

    [When("I generate F# with the external F# backend extension")]
    public async Task GenerateWithExtension() => await Invoke(null, $"--extension \"{BackendGuest}\"");

    [Given("a project whose configuration selects the external F# backend")]
    public void ProjectSelectsExtension() => File.WriteAllText(Path.Combine(_directory, "morphir.toml"),
        $"[project]\nname = \"car/rental\"\n\n[codegen.fsharp]\nextension = '{BackendGuest}'\n");

    [When("I generate F# for the project")]
    public async Task GenerateForProject() => await Invoke(null, $"--project \"{_directory}\"");

    [When("I generate F# for the project with the extension {string}")]
    public async Task GenerateForProjectWith(string extension) => await Invoke(null, $"--project \"{_directory}\"", $"--extension {extension}");

    [Then("the report names the external F# backend over MEP")]
    public void NamesExternal()
    {
        using var json = JsonDocument.Parse(_run.Result!.StandardOutput);
        json.RootElement.GetProperty("backend").GetString().Should().Be(BackendGuestId, "the external backend's own id, not morphir.fsharp");
        json.RootElement.GetProperty("backendTransport").GetString().Should().Be("mep");
        json.RootElement.GetProperty("backendVersion").GetString().Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Compares with the managed built-in backend in this process, not with the CLI under test: the guest formats
    /// with Fantomas, as the managed built-in does, while the built-in backend of the Native AOT <c>morphir</c>
    /// (MORPHIR_GENERATION_CLI) writes the same declarations unformatted. The comparison does not normalize formatting.
    /// </summary>
    [Then("the generated files equal those of the built-in F# backend")]
    public async Task EqualsBuiltIn()
    {
        var external = Sources();
        var builtInOutput = Path.Combine(_directory, "built-in");
        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(_input, "fsharp", builtInOutput), new InvocationContext(_directory)),
            new CliGenerationSettingsResolver(), new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([new FSharpGenerationBackend()]), new GenerationPublisher(), default);
        var success = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString()).Subject.Value;
        success.Backend.Should().Be("morphir.fsharp");
        var builtIn = success.Publication.Artifacts
            .Select(path => Tuple.Create(path, File.ReadAllText(Path.Combine(builtInOutput, path))))
            .ToArray();
        builtIn.Should().NotBeEmpty();
        external.Should().Equal(builtIn, "the same paths in the same order, with the same text");
    }

    [Then("the report has the diagnostic {string} that names {string}")]
    public void HasDiagnostic(string code, string text)
    {
        using var json = JsonDocument.Parse(_run.Result!.StandardOutput);
        var diagnostics = json.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();
        diagnostics.Should().Contain(item => item.GetProperty("code").GetString() == code && item.GetProperty("message").GetString()!.Contains(text),
            _run.Result.StandardOutput);
    }

    [Then("the report names the built-in F# backend with the existing members only")]
    public void NamesBuiltIn()
    {
        using var json = JsonDocument.Parse(_run.Result!.StandardOutput);
        json.RootElement.GetProperty("backend").GetString().Should().Be("morphir.fsharp");
        json.RootElement.EnumerateObject().Select(member => member.Name).Should().Equal(
            "success", "exitCode", "target", "backend", "input", "output", "sourceVersion", "family", "distributionKind",
            "representation", "artifacts", "written", "unchanged", "removed", "diagnostics", "failureKind", "formatting");
    }

    [Then("generation exits with {int} and one JSON report")]
    public void Report(int exitCode) => _run.ShouldExitWithOneJsonReport(exitCode);

    private Tuple<string, string>[] Sources()
    {
        using var json = JsonDocument.Parse(_run.Result!.StandardOutput);
        return json.RootElement.GetProperty("artifacts").EnumerateArray()
            .Select(item => item.GetString()!)
            .Select(path => Tuple.Create(path, File.ReadAllText(Path.Combine(Output, path))))
            .ToArray();
    }

    [Then("the generated daily-rate behavior matches the common corpus")]
    public void ExecuteBehavior() => GenerationCases.checkDailyRate(_version, _format, _format == "json" ? "file" : "tree", Sources());

    [Given("previous generated output exists")]
    public void PreviousOutput()
    {
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, "previous.fs"), "previous content");
    }

    [Given("the generation input is {string}")]
    public void InvalidInput(string problem) => File.WriteAllText(_input,
        problem == "malformed" ? "{ broken" : V4Model.Replace("\"WholeNumberLiteral\": 5000", "\"DocumentLiteral\": {\"extra\":true}"));

    [Then("previous generated output is unchanged")]
    public void Preserved() => File.ReadAllText(Path.Combine(Output, "previous.fs")).Should().Be("previous content");

    [Then("no generation output is published")]
    public void NoOutput() => Directory.Exists(Output).Should().BeFalse();

    [When("I generate the equivalent Ion tree")]
    public async Task Equivalent()
    {
        _firstSources = Sources().Select(file => file.Item1 + "\n" + file.Item2).ToArray();
        Model("3.0.0", "ion-tree");
        await Generate();
    }

    [Then("the ordered source matches the first representation")]
    public void Parity() => Sources().Select(file => file.Item1 + "\n" + file.Item2).Should().Equal(_firstSources!);

    public void Dispose()
    {
        _sandbox.Dispose();
    }

    internal static string RepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null && !File.Exists(Path.Combine(current.FullName, "Morphir.slnx"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }

    internal static string V4Model => File.ReadAllText(Path.Combine(RepositoryRoot(), "tests", "Fixtures", "MorphirIR", "daily-rate-v4.json"));
}
