using System.Text.Json;
using FluentAssertions;
using Morphir.Testing;
using Morphir.Tooling.Tests.Integration.CLI;

namespace Morphir.Tooling.Tests.Features.Generate;

[Binding]
public sealed class GenerateSteps : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "morphir-generate-" + Guid.NewGuid().ToString("N"));
    private readonly CliTestHelper _cli = new();
    private string _input = "";
    private string _version = "";
    private string _format = "";
    private CliExecutionResult? _result;
    private string[]? _firstSources;
    private string Output => Path.Combine(_directory, "output");

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

    private async Task Invoke(string? omit)
    {
        var arguments = new List<string> { "generate", "--json" };
        if (omit != "input") arguments.Add($"--input \"{_input}\"");
        if (omit != "target") arguments.Add("--target fsharp");
        if (omit != "output") arguments.Add($"--output \"{Output}\"");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _result = await _cli.ExecuteCommandAsync(string.Join(" ", arguments), timeout.Token);
    }

    [Then("generation exits with {int} and one JSON report")]
    public void Report(int exitCode)
    {
        _result.Should().NotBeNull();
        _result!.ExitCode.Should().Be(exitCode, _result.CombinedOutput);
        using var json = JsonDocument.Parse(_result.StandardOutput);
        json.RootElement.GetProperty("success").GetBoolean().Should().Be(exitCode == 0);
        json.RootElement.GetProperty("diagnostics").ValueKind.Should().Be(JsonValueKind.Array);
    }

    private Tuple<string, string>[] Sources()
    {
        using var json = JsonDocument.Parse(_result!.StandardOutput);
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
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null && !File.Exists(Path.Combine(current.FullName, "Morphir.slnx"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }

    private const string V4Model = """
        {"formatVersion":4,"distribution":{"Library":{"packageName":"car/rental","dependencies":{},"def":{"modules":{
          "inventory":{"access":"Public","types":{},"values":{
            "dailyRate":{"access":"Public","ExpressionBody":{"inputTypes":{},"outputType":"morphir/SDK:basics#int","body":{"Literal":{"WholeNumberLiteral": 5000}}}}
          }}
        }}}}}
        """;
}
