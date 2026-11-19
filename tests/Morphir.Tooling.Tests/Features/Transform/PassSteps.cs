// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Text.Json;
using FluentAssertions;
using Microsoft.FSharp.Collections;
using Morphir.Compilation;
using Morphir.Extensions;
using Morphir.Files;
using Morphir.Frontend.FSharp;
using Morphir.IR.Model;
using Morphir.MiddleEnd;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.Transform;
using Morphir.Tooling.Tests.Features.Compile;
using Morphir.Tooling.Tests.Features.Generate;
using Diagnostic = Morphir.Compilation.Diagnostic;
using DiagnosticCode = Morphir.Compilation.DiagnosticCode;
using SourceFormatVersions = Morphir.Extensions.SourceFormatVersions;

namespace Morphir.Tooling.Tests.Features.Transform;

/// <summary>
/// The steps of <c>Passes.feature</c>: test passes and test backends in the test process, a compile with the F# frontend,
/// and a generate from the daily-rate fixture. Each scenario has its own folder.
/// </summary>
[Binding]
public sealed class PassSteps : IDisposable
{
    private readonly TestSandbox _sandbox = TestSandbox.Create("morphir-passes-");
    private readonly List<IModelTransform> _passes = [];
    private readonly List<string> _log = [];
    private CompileWorkspace? _workspace;
    private List<CompileEvent> _events = [];
    private CompileResult? _compiled;
    private Outcome<GenerationSuccess>? _generated;
    private string _output = "";

    private ModelPasses Passes => new(_passes, new Dictionary<string, ImmutableArray<IrFeature>>());

    private CompileWorkspace Workspace => _workspace ?? throw new InvalidOperationException("The scenario compiled nothing before this step.");

    private CompileResult Compiled => _compiled ?? throw new InvalidOperationException("The scenario compiled nothing before this step.");

    private Outcome<GenerationSuccess> Generated => _generated ?? throw new InvalidOperationException("The scenario generated nothing before this step.");

    /// <summary>A pass that records each module that it reads, and fails with a code when the scenario says so.</summary>
    private sealed class TestPass(string id, PassStage stage, List<string> log, string? failure) : IModelTransform
    {
        public string Id => id;
        public string Name => id;
        public string Description => "A test pass.";
        public PassStage Stage => stage;
        public FSharpSet<IrFeature> Removes => SetModule.Empty<IrFeature>();

        public Checked<PassResult> Run(ModuleUnit unit, IPackageView view)
        {
            log.Add($"{id}:{Morphir.IR.CanonicalNames.pathToString(unit.Path)}");
            return failure is null
                ? Checked<PassResult>.Pass(new PassResult(unit.Definition, FSharpList<PassApplication>.Empty))
                : Checked<PassResult>.Fail([Diagnostic.Create(DiagnosticSeverity.Error, DiagnosticCode.Create(failure), "The test pass failed.", null)]);
        }
    }

    private sealed record Options : ValidatedBackendOptions;

    /// <summary>A backend of one target that records its run and writes one file.</summary>
    private sealed class TestBackend(string target, List<string> log, BackendCapability capability) : IGenerationBackend
    {
        public BackendDescriptor Descriptor { get; } =
            new(ExtensionId.Create("test-" + target), ExtensionName.Create("Test backend"), TargetId.Create(target), [capability]);

        public Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options) => new Outcome<ValidatedBackendOptions>.Success(new Options());

        public ValueTask<Outcome<GenerationArtifacts>> GenerateAsync(BackendGenerationRequest request, CancellationToken ct)
        {
            log.Add("backend");
            var tree = VFileTreeModule.fromFileMap(MapModule.OfSeq([Tuple.Create("Out.txt", VFileModule.ofText("Out.txt", "generated"))]));
            return ValueTask.FromResult<Outcome<GenerationArtifacts>>(new Outcome<GenerationArtifacts>.Success(new GenerationArtifacts(tree, [])));
        }
    }

    [Given("a pass {string} of the stage after a frontend")]
    public void APassAfterAFrontend(string id) => _passes.Add(new TestPass(id, PassStage.AfterFrontend, _log, null));

    [Given("a pass {string} of the stage after a frontend that fails with {string}")]
    public void AFailingPassAfterAFrontend(string id, string code) => _passes.Add(new TestPass(id, PassStage.AfterFrontend, _log, code));

    [Given("a pass {string} of the stage before the backend {string}")]
    public void APassBeforeABackend(string id, string target) => _passes.Add(new TestPass(id, PassStage.NewBeforeBackend(target), _log, null));

    [Given("a pass {string} of the stage before the backend {string} that fails with {string}")]
    public void AFailingPassBeforeABackend(string id, string target, string code) =>
        _passes.Add(new TestPass(id, PassStage.NewBeforeBackend(target), _log, code));

    /// <summary>Compiles two F# documents (the modules <c>rates</c> and <c>pricing</c>) with the F# frontend in the test process.</summary>
    [When("I compile the F# sources at the IR version {string}")]
    public async Task CompileTheFSharpSources(string version)
    {
        _workspace = new CompileWorkspace();
        _workspace.Write("src/Rates.fs", CompileRunnerTests.Rates);
        _workspace.Write("src/Pricing.fs", CompileRunnerTests.Pricing);
        var overrides = new CompileOverrides { Language = "fsharp", Inputs = ["src/Rates.fs", "src/Pricing.fs"], PackageName = "acme/car-rental", IrVersion = version };
        var runner = CompileRunners.Create([FSharpFrontend.Registration], passes: Passes);
        _events = await CompileRunners.Events(runner, overrides, _workspace.Context());
        _compiled = CompileHandler.Fold(_events);
    }

    /// <summary>
    /// Generates from the daily-rate fixture with a test backend of the target: the target <c>classic</c> reads classic models,
    /// and each other target reads the current IR only.
    /// </summary>
    [When("I generate {string} from the daily-rate model at the version {string}")]
    public async Task GenerateFromTheDailyRateModel(string target, string version)
    {
        var input = GenerationInputFixtures.DailyRate(_sandbox.PathOf("model"), version, "json");
        _output = _sandbox.PathOf("out");
        var capability = target == "classic"
            ? new BackendCapability(ModelFamily.Classic, SourceFormatVersions.Create("1", "2", "3", "3.0.0", "3.1.0"), DistributionKind.Library)
            : new BackendCapability(ModelFamily.V4, SourceFormatVersions.Create("4", "4.0.0"), DistributionKind.Library);
        var resolution = await new CliGenerationSettingsResolver().ResolveAsync(new(input, target, _output), new(_sandbox.Work), default);
        var settings = resolution.Should().BeOfType<Outcome<GenerationSettings>.Success>().Subject.Value;
        var loaded = await new GenerationModelLoader(new BoundedModelCapture()).LoadAsync(settings.Input, settings.Limits, default);
        var model = loaded.Should().BeOfType<Outcome<LoadedModel>.Success>().Subject.Value;
        _generated = await GenerationStages.RunFrom(model, settings, new RegisteredBackendResolver([new TestBackend(target, _log, capability)]),
            new GenerationPublisher(), default, null, Passes);
    }

    [Then("the compile exits with {int}")]
    public void TheCompileExitsWith(int code) =>
        Compiled.ExitCode.Should().Be(code, string.Join("\n", Compiled.Diagnostics.Select(d => d.Diagnostic)));

    [Then("the pass {string} ran on the modules {string}")]
    public void ThePassRanOnTheModules(string id, string modules) =>
        _log.Should().Equal(modules.Split(", ").Select(module => $"{id}:{module}"));

    [Then("the stage {string} ran after the stage {string} and before the stage {string}")]
    public void TheStageRanBetween(string stage, string before, string after)
    {
        var started = _events.OfType<CompileEvent.StageStarted>().Select(e => e.Stage.WireValue()).ToList();
        started.Should().Contain([before, stage, after]);
        started.IndexOf(stage).Should().Be(started.IndexOf(before) + 1, "the stage {0} starts right after the stage {1}", stage, before);
        started.IndexOf(after).Should().Be(started.IndexOf(stage) + 1, "the stage {0} starts right after the stage {1}", after, stage);
    }

    [Then("the compile report lists the pass {string}")]
    public void TheCompileReportListsThePass(string id)
    {
        using var document = JsonDocument.Parse(CompileReport.From(Compiled).ToJson());
        document.RootElement.GetProperty("passes").GetProperty("ran").EnumerateArray().Select(item => item.GetString()).Should().Equal(id);
    }

    [Then("no pass ran")]
    public void NoPassRan() => _log.Should().NotContain(entry => entry != "backend");

    [Then("the compile has one info diagnostic {string} in the stage {string}")]
    public void TheCompileHasOneInfoDiagnostic(string code, string stage)
    {
        var notes = Compiled.Diagnostics.Where(d => d.Diagnostic.CodeOrNull?.Value == code).ToArray();
        notes.Should().ContainSingle();
        notes[0].Diagnostic.Severity.Should().Be(DiagnosticSeverity.Info);
        notes[0].Stage.WireValue().Should().Be(stage);
    }

    [Then("the IR is published")]
    public void TheIrIsPublished() => File.Exists(Workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json")).Should().BeTrue();

    [Then("no IR is published")]
    public void NoIrIsPublished() => File.Exists(Workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json")).Should().BeFalse();

    [Then("the compile has the error {string}")]
    public void TheCompileHasTheError(string code) => CompileRunners.ErrorCodes(Compiled).Should().Equal(code);

    [Then("the stage {string} did not start")]
    public void TheStageDidNotStart(string stage) =>
        _events.OfType<CompileEvent.StageStarted>().Select(e => e.Stage.WireValue()).Should().NotContain(stage);

    [Then("the generation succeeds")]
    public void TheGenerationSucceeds() => Generated.Should().BeOfType<Outcome<GenerationSuccess>.Success>(
        Generated is Outcome<GenerationSuccess>.Failure failure ? string.Join("\n", failure.Error.Diagnostics.Select(d => $"{d.Code}: {d.Message}")) : "");

    [Then("the pass {string} ran before the backend")]
    public void ThePassRanBeforeTheBackend(string id)
    {
        var first = _log.FindIndex(entry => entry.StartsWith(id + ":", StringComparison.Ordinal));
        first.Should().BeGreaterThanOrEqualTo(0, "the pass {0} ran", id);
        _log.IndexOf("backend").Should().BeGreaterThan(first);
    }

    [Then("the pass {string} did not run")]
    public void ThePassDidNotRun(string id) => _log.Should().NotContain(entry => entry.StartsWith(id + ":", StringComparison.Ordinal));

    [Then("the generation report lists the pass {string}")]
    public void TheGenerationReportListsThePass(string id)
    {
        var success = Generated.Should().BeOfType<Outcome<GenerationSuccess>.Success>().Subject.Value;
        success.Passes.Should().NotBeNull();
        success.Passes!.Ran.Should().Equal(id);
    }

    [Then("the generation has one info diagnostic {string}")]
    public void TheGenerationHasOneInfoDiagnostic(string code)
    {
        var success = Generated.Should().BeOfType<Outcome<GenerationSuccess>.Success>().Subject.Value;
        var notes = success.Diagnostics.Where(d => d.Code == code).ToArray();
        notes.Should().ContainSingle();
        notes[0].Severity.Should().Be("info");
        success.Passes.Should().BeNull();
    }

    [Then("the generation fails with the error {string}")]
    public void TheGenerationFailsWithTheError(string code)
    {
        var failure = Generated.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Subject;
        failure.Error.Kind.Should().Be(GenerationFailureKind.Semantic);
        failure.Error.Diagnostics.Select(d => d.Code).Should().Contain(code);
    }

    [Then("the backend did not run")]
    public void TheBackendDidNotRun() => _log.Should().NotContain("backend");

    [Then("no generated file is published")]
    public void NoGeneratedFileIsPublished() => Directory.Exists(_output).Should().BeFalse();

    public void Dispose()
    {
        _workspace?.Dispose();
        _sandbox.Dispose();
    }
}
