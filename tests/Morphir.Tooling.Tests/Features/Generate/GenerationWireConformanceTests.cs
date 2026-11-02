using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Morphir.IR.Pipeline;
using Morphir.Testing;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Tests.Integration.CLI;
using System.Text.Json;
using static Morphir.Testing.GenerationConformance;
using Sources = Microsoft.FSharp.Collections.FSharpList<System.Tuple<string, string>>;
using Generated = Microsoft.FSharp.Core.FSharpResult<Microsoft.FSharp.Collections.FSharpList<System.Tuple<string, string>>, Microsoft.FSharp.Collections.FSharpList<string>>;

namespace Morphir.Tooling.Tests.Features.Generate;

public sealed class GenerationWireConformanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-wire-" + Guid.NewGuid().ToString("N"));
    public GenerationWireConformanceTests() => Directory.CreateDirectory(_root);
    private static readonly FSharpFunc<Generated, Generated> Identity = FSharpFunc<Generated, Generated>.FromConverter(value => value);

    private async Task<Generated> Generate(string path)
    {
        if (Environment.GetEnvironmentVariable("MORPHIR_GENERATION_CLI") is { } executable)
        {
            var output = Path.Combine(_root, "published-" + Guid.NewGuid().ToString("N"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var response = await new CliTestHelper(executable).ExecuteCommandAsync($"generate --input \"{path}\" --target fsharp --output \"{output}\" --json", timeout.Token);
            using var report = JsonDocument.Parse(response.StandardOutput);
            report.RootElement.GetProperty("exitCode").GetInt32().Should().Be(response.ExitCode);
            if (response.ExitCode != 0) return Generated.NewError(ListModule.OfSeq(report.RootElement.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("code").GetString()!)));
            report.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            var paths = report.RootElement.GetProperty("artifacts").EnumerateArray().Select(value => value.GetString()!).ToArray();
            paths.Should().NotBeEmpty();
            return Generated.NewOk(ListModule.OfSeq(paths.Select(location => Tuple.Create(location, File.ReadAllText(Path.Combine(output, location))))));
        }
        var result = await new GenerationModelLoader(new BoundedModelCapture()).LoadAsync(new(path), ModelReadLimits.Default, default);
        if (result is Outcome<LoadedModel>.Failure failed) return Generated.NewError(ListModule.OfSeq(failed.Error.Diagnostics.Select(d => d.Code)));
        var loaded = ((Outcome<LoadedModel>.Success)result).Value;
        var backend = new FSharpGenerationBackend();
        var options = ((Outcome<ValidatedBackendOptions>.Success)backend.ValidateOptions(BackendOptions.Empty)).Value;
        var generated = await backend.GenerateAsync(new(loaded, new("fsharp"), options), default);
        if (generated is Outcome<GenerationArtifacts>.Failure failure) return Generated.NewError(ListModule.OfSeq(failure.Error.Diagnostics.Select(d => d.Code)));
        var tree = ((Outcome<GenerationArtifacts>.Success)generated).Value.Tree;
        var files = VFileTreeModule.allFiles(tree).ToDictionary(file => file.Path.Value, file => VFileModule.readText(file).ResultValue);
        files.Should().NotBeEmpty();
        var order = VFileTreeModule.tryGet(VFileTreeModule.declaredOrder, tree).Value;
        return Generated.NewOk(ListModule.OfSeq(order.Select(path => Tuple.Create(path, files[path]))));
    }

    private static InputExpectation<Generated> Captured(Generated value, string? refusal = null)
    {
        var materialize = FSharpFunc<Unit, Generated>.FromConverter(_ => value);
        return refusal is null ? InputExpectation<Generated>.NewSupported(materialize) : InputExpectation<Generated>.NewRefused(materialize, refusal);
    }

    private async Task RunMatrix(BehaviorCase<Generated> template, string[] versions, Func<string, string, string, string> prepare, string? refusal = null)
    {
        var required = ListModule.OfSeq(from version in versions
                                        from format in new[] { "json", "text", "binary", "tree" }
                                        select new Representation(version, format, format == "tree" ? "tree" : "file", true));
        var variants = new List<Variant<Generated>>();
        var sources = new Dictionary<string, Sources>();
        foreach (var row in required)
        {
            if (row.Format != "json" && !row.Version.Contains('.'))
            {
                variants.Add(new(row, InputExpectation<Generated>.NewUnrepresentable("The Ion IR profile declares exact releases 3.0.0, 3.1.0 or 4.0.0; it has no integer-version spelling or v1/v2 profile.")));
                continue;
            }
            var path = prepare(Path.Combine(_root, row.Version, row.Format), row.Version, row.Format);
            var generated = await Generate(path);
            variants.Add(new(row, Captured(generated, refusal)));
            if (generated.IsOk)
            {
                if (sources.TryGetValue(row.Version, out var earlier)) generated.ResultValue.ToArray().Should().Equal(earlier.ToArray(), "same-version source and declared order must agree");
                else sources.Add(row.Version, generated.ResultValue);
            }
        }
        var caseRows = ListModule.OfSeq(variants);
        var behavior = new BehaviorCase<Generated>(template.Id, template.Consumer, template.ExpectedOutput, required, caseRows);
        foreach (var row in caseRows) GenerationConformance.run(Identity, behavior, row);
        var incomplete = new BehaviorCase<Generated>(template.Id, template.Consumer, template.ExpectedOutput, required, caseRows.Tail);
        GenerationConformance.validate(incomplete).ToArray().Should().NotBeEmpty("missing matrix rows must fail");
    }

    [Test]
    public async Task DailyRateRunsOneOracleAcrossAllApplicableWireRepresentations()
    {
        var template = GenerationCases.dailyRate<Generated>(FSharpList<Representation>.Empty, FSharpList<Variant<Generated>>.Empty);
        await RunMatrix(template, ["1", "2", "3", "3.0.0", "3.1.0", "4", "4.0.0"], GenerationInputFixtures.DailyRate);
    }

    [Test]
    [Arguments("literal.int-max")]
    [Arguments("literal.int-overflow")]
    [Arguments("literal.decimal-scale")]
    public async Task ScalarCasesUseTheSameConsumersAndRefusalAssertions(string id)
    {
        var scalar = GenerationScalars.cases.Single(value => value.Id == id);
        var template = new BehaviorCase<Generated>(scalar.Id, scalar.Consumer, scalar.Expected, FSharpList<Representation>.Empty, FSharpList<Variant<Generated>>.Empty);
        await RunMatrix(template, ["3.0.0", "3.1.0", "4.0.0"],
            (directory, version, format) => GenerationInputFixtures.Scalar(directory, version, format, scalar), scalar.Refusal?.Value);
    }
    public void Dispose() => Directory.Delete(_root, true);
}
