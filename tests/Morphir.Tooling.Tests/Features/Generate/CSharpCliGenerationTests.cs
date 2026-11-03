using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Morphir.CodeGeneration.CSharp;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Tests.Integration.CLI;

namespace Morphir.Tooling.Tests.Features.Generate;

public sealed class CSharpCliGenerationTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-csharp-cli-" + Guid.NewGuid().ToString("N"));
    private string Output => Path.Combine(_root, "output");

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private async Task<CliExecutionResult> Invoke(string input, bool json = true, string? output = null, params string[] options)
    {
        var arguments = new List<string> { "generate", "--input", input, "--target", "csharp", "--output", output ?? Output };
        if (json) arguments.Add("--json");
        foreach (var option in options) { arguments.Add("--option"); arguments.Add(option); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        return await new CliTestHelper(Environment.GetEnvironmentVariable("MORPHIR_GENERATION_CLI"))
            .ExecuteCommandAsync(string.Join(" ", arguments.Select(Quote)), timeout.Token);
    }

    [Test]
    [Arguments("1", "json")]
    [Arguments("2", "json")]
    [Arguments("3", "json")]
    [Arguments("3.0.0", "json")]
    [Arguments("3.1.0", "json")]
    [Arguments("4", "json")]
    [Arguments("4.0.0", "json")]
    [Arguments("3.0.0", "text")]
    [Arguments("3.0.0", "binary")]
    [Arguments("3.0.0", "tree")]
    [Arguments("3.1.0", "text")]
    [Arguments("3.1.0", "binary")]
    [Arguments("3.1.0", "tree")]
    [Arguments("4.0.0", "text")]
    [Arguments("4.0.0", "binary")]
    [Arguments("4.0.0", "tree")]
    public async Task EveryAdmittedWireFormUsesTheRealProviderAndMatchesCoreBytes(string version, string format)
    {
        var input = GenerationInputFixtures.DailyRate(_root, version, format);
        var result = await Invoke(input, options: ["namespace=Pricing.Generated"]);
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        using var report = JsonDocument.Parse(result.StandardOutput);
        report.RootElement.GetProperty("backend").GetString().Should().Be("morphir.csharp");
        report.RootElement.GetProperty("sourceVersion").GetString().Should().Be(version);
        var loaded = ((Outcome<LoadedModel>.Success)await new GenerationModelLoader(new BoundedModelCapture()).LoadAsync(new(input), ModelReadLimits.Default, default)).Value;
        var direct = CSharpGenerator.Generate(loaded.Model, GenerationOptions.Default, NamespaceOverride.TryCreate("Pricing.Generated").ResultValue, default);
        direct.Status.IsSuccess.Should().BeTrue();
        report.RootElement.GetProperty("artifacts").EnumerateArray().Select(x => x.GetString()).Should().Equal(direct.Sources.Select(x => x.RelativePath));
        foreach (var source in direct.Sources)
            File.ReadAllBytes(Path.Combine(Output, source.RelativePath)).Should().Equal(Encoding.UTF8.GetBytes(source.Text));
    }

    [Test]
    public async Task LastOptionsWinBeforeBackendValidationAndNamespaceEmission()
    {
        var input = GenerationInputFixtures.DailyRate(_root, "4", "json");
        var result = await Invoke(input, options: ["language-version=preview", "language-version=15", "language-version=14", "namespace=First.Generated", "namespace=Second.Generated"]);
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        using var report = JsonDocument.Parse(result.StandardOutput);
        var source = report.RootElement.GetProperty("artifacts")[0].GetString()!;
        File.ReadAllText(Path.Combine(Output, source)).Should().Contain("namespace Second.Generated;").And.NotContain("First.Generated");
        var refused = await Invoke(input, options: ["language-version=14", "language-version=preview"]);
        refused.ExitCode.Should().Be(2);
    }

    [Test]
    [Arguments("language-version=preview")]
    [Arguments("language-version=latest")]
    [Arguments("language-version=\"14\"")]
    [Arguments("language-version=14.5")]
    [Arguments("language-version=16")]
    [Arguments("language-version=null")]
    [Arguments("namespace=true")]
    [Arguments("namespace=14")]
    [Arguments("namespace=[]")]
    [Arguments("namespace=Bad;class Injected{}")]
    [Arguments("namespace=Bad..Name")]
    [Arguments("unknown=14")]
    public async Task InvalidOptionsAreInvocationReportsAndPreservePriorOutput(string option)
    {
        var input = GenerationInputFixtures.DailyRate(_root, "4", "json");
        (await Invoke(input)).ExitCode.Should().Be(0);
        var before = Snapshot();
        var result = await Invoke(input, options: [option]);
        result.ExitCode.Should().Be(2, result.CombinedOutput);
        using var report = JsonDocument.Parse(result.StandardOutput);
        report.RootElement.GetProperty("failureKind").GetString().Should().Be("invocation");
        report.RootElement.GetProperty("backend").GetString().Should().Be("morphir.csharp");
        report.RootElement.GetProperty("diagnostics").GetArrayLength().Should().BeGreaterThan(0);
        report.RootElement.GetProperty("artifacts").GetArrayLength().Should().Be(0);
        Snapshot().Should().BeEquivalentTo(before);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SemanticFailuresUseJsonDataOrHumanStderr(bool json)
    {
        var input = GenerationInputFixtures.DailyRate(_root, "4", "json");
        (await Invoke(input)).ExitCode.Should().Be(0);
        var before = Snapshot();
        File.WriteAllText(input, File.ReadAllText(input).Replace("\"WholeNumberLiteral\": 5000", "\"DocumentLiteral\": {\"unsupported\":true}"));
        var result = await Invoke(input, json);
        result.ExitCode.Should().Be(1, result.CombinedOutput);
        if (json)
        {
            using var report = JsonDocument.Parse(result.StandardOutput);
            report.RootElement.GetProperty("failureKind").GetString().Should().Be("semantic");
            report.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString().Should().Be("document-literal");
            report.RootElement.GetProperty("diagnostics")[0].GetProperty("location").GetString().Should().NotBeNullOrEmpty();
            report.RootElement.GetProperty("artifacts").GetArrayLength().Should().Be(0);
        }
        else
        {
            result.StandardOutput.Should().BeEmpty();
            result.StandardError.Should().Contain("document-literal");
        }
        Snapshot().Should().BeEquivalentTo(before);
    }

    [Test]
    public async Task MissingInputIsAnOperationalJsonReport()
    {
        Directory.CreateDirectory(_root);
        var input = Path.Combine(_root, "missing.json");
        var result = await Invoke(input);
        result.ExitCode.Should().Be(2, result.CombinedOutput);
        using var report = JsonDocument.Parse(result.StandardOutput);
        report.RootElement.GetProperty("failureKind").GetString().Should().Be("operational");
        Directory.Exists(Output).Should().BeFalse();
    }

    [Test]
    public async Task RerunsKeepTimestampsAndRemoveOnlyStaleOwnedModules()
    {
        Directory.CreateDirectory(_root);
        var input = Path.Combine(_root, "car.json");
        File.Copy(Path.Combine(GenerateSteps.RepositoryRoot(), "examples", "car-rental", "models", "car-rental-v3.json"), input);
        (await Invoke(input)).ExitCode.Should().Be(0);
        File.WriteAllText(Path.Combine(Output, "User.cs"), "// user-owned content");
        var stamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var path in Directory.EnumerateFiles(Output, "*", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(path, stamp);
        var repeated = await Invoke(input);
        repeated.ExitCode.Should().Be(0, repeated.CombinedOutput);
        using (var report = JsonDocument.Parse(repeated.StandardOutput))
        {
            report.RootElement.GetProperty("written").GetArrayLength().Should().Be(0);
            report.RootElement.GetProperty("unchanged").GetArrayLength().Should().BeGreaterThan(0);
        }
        foreach (var path in Directory.EnumerateFiles(Output, "*", SearchOption.AllDirectories)) File.GetLastWriteTimeUtc(path).Should().Be(stamp);
        var node = JsonNode.Parse(File.ReadAllText(input))!;
        var modules = node["distribution"]![3]!["modules"]!.AsArray();
        var workflow = modules.Single(x => x![0]!.ToJsonString().Contains("workflow", StringComparison.Ordinal));
        modules.Remove(workflow);
        File.WriteAllText(input, node.ToJsonString());
        var reduced = await Invoke(input);
        reduced.ExitCode.Should().Be(0, reduced.CombinedOutput);
        using var receipt = JsonDocument.Parse(reduced.StandardOutput);
        receipt.RootElement.GetProperty("removed").EnumerateArray().Select(x => x.GetString()).Should().Equal("p_Car.Rental/m_WorkflowModule.g.cs");
        File.Exists(Path.Combine(Output, "p_Car.Rental", "m_WorkflowModule.g.cs")).Should().BeFalse();
        File.ReadAllText(Path.Combine(Output, "User.cs")).Should().Be("// user-owned content");
    }

    private Dictionary<string, string> Snapshot() => Directory.EnumerateFiles(Output, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(Output, path), path => Convert.ToBase64String(File.ReadAllBytes(path)));
}
