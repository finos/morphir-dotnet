// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Compilation;
using Morphir.Extensions;
using Morphir.Frontend.FSharp;
using Morphir.IR.Model;
using Morphir.IR.Pipeline;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Scenarios;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>
/// Task 6.2: <c>compile --target</c>/<c>--generate-output</c>/<c>--option</c> (CLI-2). The compiled IR goes to the
/// generation stages as the <see cref="LoadedModel"/> of the exact bytes that the compile wrote.
/// </summary>
[NotInParallel(nameof(CompileTargetTests))]
public sealed class CompileTargetTests
{
    private static CompileWorkspace Project()
    {
        var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml", "[project]\nname = \"acme/car-rental\"\nexposed_modules = [\"Pricing\"]\n");
        workspace.Write("src/Rates.fs", CompileRunnerTests.Rates);
        workspace.Write("src/Pricing.fs", CompileRunnerTests.Pricing);
        return workspace;
    }

    private const string Inputs = "--input src/Rates.fs src/Pricing.fs";

    private static async Task<(int Exit, CompileCommandTests.CapturedOutput Output)> Compile(CompileWorkspace workspace, string arguments,
        Action<IServiceCollection>? configure = null)
    {
        var output = new CompileCommandTests.CapturedOutput();
        var args = ("compile " + arguments).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        await using var app = MorphirCli.CreateBuilder(args)
            .AddScenario<GenerationScenario>()
            .AddScenario<CompilationScenario>()
            .ConfigureServices(services =>
            {
                services.AddSingleton(FSharpFrontend.Registration);
                services.AddSingleton<ICompileOutput>(output);
                services.AddSingleton(new CompileContextSource(() => workspace.Context()));
                configure?.Invoke(services);
            })
            .Build();
        return (await app.RunAsync(), output);
    }

    /// <summary>Runs <c>generate --input</c> through the same handler and services as the command.</summary>
    private static async Task<GenerationReport> Generate(string input, string target, string output, params string[] options)
    {
        var overrides = new GenerateOverrides(input, target, output, null, options);
        var outcome = await GenerateHandler.Handle(new GenerateRequest(overrides, new InvocationContext(Path.GetDirectoryName(output)!)),
            new CliGenerationSettingsResolver(), new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([new FSharpGenerationBackend(), new CSharpGenerationBackend()]), new GenerationPublisher(),
            CancellationToken.None);
        return GenerationReport.From(outcome, overrides);
    }

    private static string[] Tree(string root) =>
        [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/') + "\n" + Convert.ToHexString(File.ReadAllBytes(path)))];

    private static string Event(string line)
    {
        using var record = JsonDocument.Parse(line);
        return record.RootElement.GetProperty("event").GetString()!;
    }

    private static string? Stage(string line)
    {
        using var record = JsonDocument.Parse(line);
        return record.RootElement.TryGetProperty("stage", out var stage) ? stage.GetString() : null;
    }

    [Test]
    [Arguments("fsharp", "1")]
    [Arguments("fsharp", "3")]
    [Arguments("fsharp", "4")]
    [Arguments("csharp", "2")]
    [Arguments("csharp", "4")]
    public async Task CompileWithATargetEqualsCompileThenGenerate(string target, string version)
    {
        using var workspace = Project();
        var combined = await Compile(workspace, $"--json {Inputs} --ir-version {version} --target {target} --generate-output gen");
        combined.Exit.Should().Be(0, combined.Output.Text);
        using var report = JsonDocument.Parse(combined.Output.Text);
        var generation = report.RootElement.GetProperty("generation");

        var separate = await Compile(workspace, $"--json {Inputs} --ir-version {version}");
        separate.Exit.Should().Be(0, separate.Output.Text);
        using var compiled = JsonDocument.Parse(separate.Output.Text);
        var ir = compiled.RootElement.GetProperty("output").GetString()!;
        var generated = await Generate(ir, target, workspace.PathOf("gen2"));
        generated.Success.Should().BeTrue(generated.ToJson());

        Tree(workspace.PathOf("gen")).Should().NotBeEmpty().And.Equal(Tree(workspace.PathOf("gen2")));
        generation.GetProperty("backend").GetString().Should().Be(generated.Backend);
        generation.GetProperty("target").GetString().Should().Be(target);
        generation.GetProperty("sourceVersion").GetString().Should().Be(generated.SourceVersion);
        generation.GetProperty("family").GetString().Should().Be(generated.Family);
        generation.GetProperty("representation").GetString().Should().Be(generated.Representation);
        generation.GetProperty("artifacts").EnumerateArray().Select(a => a.GetString()).Should().Equal(generated.Artifacts);
    }

    [Test]
    public async Task TheBackendGetsTheExactWrittenBytesAsAJsonFileInput()
    {
        using var workspace = Project();
        var recorder = new RecordingBackend("fsharp", new BackendCapability(ModelFamily.Classic, Morphir.Extensions.SourceFormatVersions.Create("3"), DistributionKind.Library));
        var run = await Compile(workspace, $"--json {Inputs} --ir-version 3 --target fsharp --generate-output gen",
            services => services.AddSingleton<IGenerationBackendResolver>(new RegisteredBackendResolver([recorder])));
        run.Exit.Should().Be(0, run.Output.Text);
        using var report = JsonDocument.Parse(run.Output.Text);
        var ir = report.RootElement.GetProperty("output").GetString()!;

        var model = recorder.Requests.Should().ContainSingle().Subject.Model;
        model.Representation.Should().Be(ModelRepresentation.Json);
        model.Descriptor.Version.Text.Should().Be("3");
        model.Model.Family.IsClassic.Should().BeTrue("classic bytes give a classic model");
        var original = model.Original.Should().BeOfType<RetainedInput.File>().Subject;
        original.SourcePath.Should().Be(ir);
        original.Entry.CopyBytes().Should().Equal(File.ReadAllBytes(ir), "the backend sees the bytes that were published");
    }

    [Test]
    public async Task GenerationStagesAppearOnlyWhenATargetRuns()
    {
        using var workspace = Project();
        var plain = await Compile(workspace, $"--json-lines {Inputs}");
        plain.Exit.Should().Be(0, plain.Output.Text);
        plain.Output.Lines.Select(Stage).Should().NotContain(["generate", "generatePublish"]);
        using (var completed = JsonDocument.Parse(plain.Output.Lines[^1]))
            completed.RootElement.GetProperty("report").TryGetProperty("generation", out _).Should().BeFalse("a compile without a target has no generation member");

        var targeted = await Compile(workspace, $"--json-lines {Inputs} --target fsharp --generate-output gen");
        targeted.Exit.Should().Be(0, targeted.Output.Text);
        var records = targeted.Output.Lines.Select(line => $"{Event(line)}:{Stage(line)}").ToArray();
        records[^5..].Should().Equal("stageStarted:generate", "stageCompleted:generate", "stageStarted:generatePublish",
            "stageCompleted:generatePublish", "completed:");
        records.Should().ContainInOrder("stageCompleted:publish", "stageStarted:generate");
        using var report = JsonDocument.Parse(targeted.Output.Lines[^1]);
        var generation = report.RootElement.GetProperty("report").GetProperty("generation");
        generation.GetProperty("backend").GetString().Should().Be("morphir.fsharp");
        generation.GetProperty("written").GetArrayLength().Should().BeGreaterThan(0);
        File.Exists(Path.Combine(workspace.PathOf("gen"), GenerationDefaults.OwnershipManifest)).Should().BeTrue();
    }

    [Test]
    public async Task AnIncompatibleBackendFailsBeforeGenerationAndKeepsTheCompiledIr()
    {
        using var workspace = Project();
        workspace.Write("gen/keep.txt", "not generated");
        var narrow = new RecordingBackend("csharp", new BackendCapability(ModelFamily.V4, Morphir.Extensions.SourceFormatVersions.Create("4"), DistributionKind.Library));
        var run = await Compile(workspace, $"--json-lines {Inputs} --ir-version 1 --target csharp --generate-output gen",
            services => services.AddSingleton<IGenerationBackendResolver>(new RegisteredBackendResolver([narrow])));

        run.Exit.Should().Be(1, run.Output.Text);
        narrow.Requests.Should().BeEmpty("generation does not run");
        run.Output.Lines.Select(Stage).Should().Contain("generate").And.NotContain("generatePublish");
        using var completed = JsonDocument.Parse(run.Output.Lines[^1]);
        var report = completed.RootElement.GetProperty("report");
        report.GetProperty("success").GetBoolean().Should().BeFalse();
        report.GetProperty("failureKind").GetString().Should().Be("source");
        var diagnostic = report.GetProperty("diagnostics").EnumerateArray().Single(d => d.GetProperty("severity").GetString() == "error");
        diagnostic.GetProperty("code").GetString().Should().Be("incompatible-model");
        diagnostic.GetProperty("message").GetString().Should().Contain("fake.csharp", "the message names the backend")
            .And.Contain("V4 4 Library", "and the capability that it declares")
            .And.Contain("Classic 1 Library", "and the model");
        var ir = report.GetProperty("output").GetString();
        File.Exists(ir).Should().BeTrue("the compile output remains published");
        report.GetProperty("modules").GetArrayLength().Should().Be(2);
        report.GetProperty("generation").GetProperty("target").GetString().Should().Be("csharp");
        Directory.EnumerateFileSystemEntries(workspace.PathOf("gen")).Select(Path.GetFileName).Should().Equal("keep.txt");
    }

    [Test]
    public async Task OptionsReachTheBackendThroughTheSameValidationAsGenerate()
    {
        using var workspace = Project();
        var run = await Compile(workspace, $"--json {Inputs} --target csharp --generate-output gen --option namespace=Acme.Custom");
        run.Exit.Should().Be(0, run.Output.Text);
        Directory.EnumerateFiles(workspace.PathOf("gen"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText).Should().Contain(text => text.Contains("namespace Acme.Custom", StringComparison.Ordinal));

        var refused = await Compile(workspace, $"--json {Inputs} --target csharp --generate-output gen2 --option language-version=99");
        refused.Exit.Should().Be(2, refused.Output.Text);
        using var report = JsonDocument.Parse(refused.Output.Text);
        report.RootElement.GetProperty("failureKind").GetString().Should().Be("invocation");
        report.RootElement.GetProperty("generation").GetProperty("backend").GetString().Should().Be("morphir.csharp");
        report.RootElement.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("code").GetString())
            .Should().Contain("invalid-backend-option");
        Directory.Exists(workspace.PathOf("gen2")).Should().BeFalse();
    }

    [Test]
    [Arguments("--target fsharp --target csharp --generate-output gen", "--target")]
    [Arguments("--target fsharp", "--generate-output")]
    [Arguments("--generate-output gen", "--target")]
    [Arguments("--option namespace=Acme", "--target")]
    [Arguments("--target csharp --generate-output gen --option =x", "KEY")]
    public async Task InvalidGenerationOptionsAreRefusedWithExitCode2BeforeCompiling(string options, string named)
    {
        using var workspace = Project();
        var run = await Compile(workspace, $"--json {Inputs} {options}");
        run.Exit.Should().Be(2, run.Output.Text);
        using var report = JsonDocument.Parse(run.Output.Text);
        report.RootElement.GetProperty("failureKind").GetString().Should().Be("invocation");
        report.RootElement.GetProperty("output").ValueKind.Should().Be(JsonValueKind.Null);
        report.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Should().Contain(d => d.GetProperty("message").GetString()!.Contains(named, StringComparison.Ordinal));
        Directory.Exists(workspace.PathOf(".morphir")).Should().BeFalse("no compile ran");
        Directory.Exists(workspace.PathOf("gen")).Should().BeFalse();
    }

    [Test]
    public async Task GenerationDoesNotRunWhenCompilationFails()
    {
        using var workspace = Project();
        workspace.Write("src/Pricing.fs", CompileRunnerTests.MutablePricing);
        var run = await Compile(workspace, $"--json-lines {Inputs} --target fsharp --generate-output gen");
        run.Exit.Should().Be(1, run.Output.Text);
        run.Output.Lines.Select(Stage).Should().NotContain(["generate", "generatePublish"]);
        Directory.Exists(workspace.PathOf("gen")).Should().BeFalse();
    }

    [Test]
    public async Task ACodegenExtensionThatCompileTargetIgnoresIsANoteOnStderrAndTheBuiltInRuns()
    {
        using var workspace = Project();
        workspace.Write("morphir.toml",
            "[project]\nname = \"acme/car-rental\"\nexposed_modules = [\"Pricing\"]\n\n[codegen.fsharp]\nextension = \"morphir-fsharp-backend\"\n");

        var run = await Compile(workspace, $"{Inputs} --target fsharp --generate-output gen");
        run.Exit.Should().Be(0, run.Output.Error);
        run.Output.Error.Should().Contain("note:").And.Contain("[codegen.fsharp].extension").And.Contain("morphir generate");
        run.Output.Text.Should().NotContain("note:", "notes go to stderr only");
        run.Output.Text.Trim().Should().EndWith("morphir-ir.json");
        run.Output.Error.Should().Contain("morphir.fsharp", "the built-in backend ran");

        var json = await Compile(workspace, $"--json-lines {Inputs} --target fsharp --generate-output gen");
        json.Output.Text.Should().NotContain("note:", "JSON output stays clean");
        json.Output.Error.Should().Contain("[codegen.fsharp].extension");
    }

    [Test]
    public async Task HumanModeNamesTheGeneratedOutputInTheSummary()
    {
        using var workspace = Project();
        var run = await Compile(workspace, $"{Inputs} --target fsharp --generate-output gen");
        run.Exit.Should().Be(0, run.Output.Error);
        run.Output.Text.Trim().Should().EndWith("morphir-ir.json", "stdout holds only the published IR path");
        var summary = run.Output.Error.Split('\n').Single(line => line.StartsWith("compile succeeded", StringComparison.Ordinal));
        summary.Should().Contain("generated").And.Contain("morphir.fsharp").And.Contain(workspace.PathOf("gen"));
    }

    [Test]
    public async Task ARunnerWithoutGenerationServicesRefusesATargetAfterPublication()
    {
        using var workspace = Project();
        var runner = CompileRunners.Create([FSharpFrontend.Registration]);
        var target = CompileTarget.Parse("fsharp", "gen", [], workspace.Root);
        target.TryGetValue(out var parsed).Should().BeTrue();
        var events = await CompileRunners.Events(runner, new CompileOverrides
        {
            Inputs = [workspace.PathOf("src/Rates.fs"), workspace.PathOf("src/Pricing.fs")],
            Target = parsed
        }, workspace.Context());
        var result = CompileHandler.Fold(events);
        result.ExitCode.Should().Be(2);
        CompileRunners.ErrorCodes(result).Should().Equal("compile.generation_unavailable");
        File.Exists(CompileReport.From(result).Output).Should().BeTrue("the IR was published before the generation stages");
    }

    /// <summary>A backend that records its requests and generates one file.</summary>
    private sealed class RecordingBackend(string target, BackendCapability capability) : IGenerationBackend
    {
        private sealed record Options : ValidatedBackendOptions;

        public List<BackendGenerationRequest> Requests { get; } = [];

        public BackendDescriptor Descriptor { get; } = new(ExtensionId.Create("fake." + target), ExtensionName.Create("Fake " + target),
            TargetId.Create(target), [capability]);

        public Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options) =>
            new Outcome<ValidatedBackendOptions>.Success(new Options());

        public ValueTask<Outcome<GenerationArtifacts>> GenerateAsync(BackendGenerationRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            var tree = VFileTreeModule.fromFileMap(Microsoft.FSharp.Collections.MapModule.OfSeq(
                [Tuple.Create("Out.txt", VFileModule.ofText("Out.txt", "generated"))]));
            return ValueTask.FromResult<Outcome<GenerationArtifacts>>(new Outcome<GenerationArtifacts>.Success(new(tree, ImmutableArray<GenerationDiagnostic>.Empty)));
        }
    }
}
