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
    private readonly CliTestHelper _cli = CliTestHelper.ForTarget();
    private readonly CliRun _run;
    private string _input = "";
    private string _version = "";
    private string _format = "";
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
    public async Task Generate() => await Invoke();

    private async Task Invoke(params string[] extra)
    {
        var arguments = new List<string> { "generate", "--json", $"--input \"{_input}\"", "--target fsharp", $"--output \"{Output}\"" };
        arguments.AddRange(extra);
        _run.Result = await _cli.ExecuteCommandAsync(string.Join(" ", arguments), timeout: TimeSpan.FromSeconds(120));
    }

    /// <summary>The external F# backend guest, morphir-fsharp-backend, from the test output.</summary>
    private static string BackendGuest => Path.Combine(AppContext.BaseDirectory, "morphir-fsharp-backend.dll");

    /// <summary>The guest's own extension id, from the guest; the one place the tests take it from.</summary>
    private static string BackendGuestId => Morphir.CodeGeneration.FSharp.Extension.FSharpGuest.extension.Id;

    [When("I generate F# with the external F# backend extension")]
    public async Task GenerateWithExtension() => await Invoke($"--extension \"{BackendGuest}\"");

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
    /// (an executable target of the testing library) writes the same declarations unformatted. The comparison does not normalize formatting.
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
