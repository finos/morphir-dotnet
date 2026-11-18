// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Morphir.Compilation;
using Morphir.Extensions;
using Morphir.Files;
using Morphir.Frontend.FSharp;
using Morphir.IR.Model;
using Morphir.IR.V4;
using Morphir.MiddleEnd;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.Transform;
using Diagnostic = Morphir.Compilation.Diagnostic;
using DiagnosticCode = Morphir.Compilation.DiagnosticCode;
using Value = Morphir.IR.V4.Value;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>
/// The compile command runs the passes of the stage after a frontend, between the frontend and the writer. A pass error stops
/// the run before publication, and a model of an older family skips the passes with one <c>info</c> diagnostic.
/// </summary>
[Category("PlatformSpecific")]
public sealed class CompilePassesTests
{
    private sealed class RecordingPass(string id, List<string> log, Func<ModuleUnit, Checked<PassResult>>? run = null) : IModelTransform
    {
        public string Id => id;
        public string Name => id;
        public string Description => "A test pass.";
        public PassStage Stage => PassStage.AfterFrontend;
        public FSharpSet<IrFeature> Removes => SetModule.Empty<IrFeature>();

        public Checked<PassResult> Run(ModuleUnit unit, IPackageView view)
        {
            log.Add(Morphir.IR.CanonicalNames.pathToString(unit.Path));
            return run?.Invoke(unit) ?? Checked<PassResult>.Pass(new PassResult(unit.Definition, FSharpList<PassApplication>.Empty));
        }
    }

    private static ModelPasses Passes(params IModelTransform[] passes) =>
        new(passes, new Dictionary<string, ImmutableArray<IrFeature>>());

    /// <summary>A pass of a rule that changes the literal <paramref name="from"/> to the literal <paramref name="to"/>.</summary>
    private static IModelTransform LiteralPass(string id, PassStage stage, int from, int to)
    {
        var rule = new RewriteRule(RuleId.Create($"https://example.org/rules/{id}"), id, FSharpOption<RuleExample>.None,
            SetModule.Empty<IrFeature>(), SetModule.Empty<IrFeature>(),
            FuncConvert.FromFunc<RewriteContext, Value, FSharpOption<Value>?>((_, value) =>
                value is Value.Literal { literal: Literal.IntegerLiteral literal } && literal.Item == new BigInteger(from)
                    ? FSharpOption<Value>.Some(Value.NewLiteral(ValueAttributesModule.empty, Literal.NewIntegerLiteral(new BigInteger(to))))
                    : FSharpOption<Value>.None));
        return RewritePass.create(id, id, "A test pass.", stage, Strategy.BottomUp, ListModule.OfSeq([rule]));
    }

    /// <summary>A pass of the stage before the backend of <paramref name="target"/> that changes nothing and gives one warning.</summary>
    private sealed class WarningPass(string target) : IModelTransform
    {
        public string Id => "warning";
        public string Name => "warning";
        public string Description => "A test pass that warns.";
        public PassStage Stage => PassStage.NewBeforeBackend(target);
        public FSharpSet<IrFeature> Removes => SetModule.Empty<IrFeature>();

        public Checked<PassResult> Run(ModuleUnit unit, IPackageView view) =>
            Checked<PassResult>.Of(new PassResult(unit.Definition, FSharpList<PassApplication>.Empty),
                [Diagnostic.Create(DiagnosticSeverity.Warning, DiagnosticCode.Create("test.warning"), "The pass warns.", null)]);
    }

    private sealed class RecordingBackend(string target) : IGenerationBackend
    {
        private sealed record Options : ValidatedBackendOptions;

        public List<BackendGenerationRequest> Requests { get; } = [];

        public BackendDescriptor Descriptor { get; } = new(ExtensionId.Create("fake." + target), ExtensionName.Create("Fake " + target),
            TargetId.Create(target), [new BackendCapability(ModelFamily.V4, Morphir.Extensions.SourceFormatVersions.Create("4"), DistributionKind.Library)]);

        public Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options) =>
            new Outcome<ValidatedBackendOptions>.Success(new Options());

        public ValueTask<Outcome<GenerationArtifacts>> GenerateAsync(BackendGenerationRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            var tree = VFileTreeModule.fromFileMap(MapModule.OfSeq([Tuple.Create("Out.txt", VFileModule.ofText("Out.txt", "generated"))]));
            return ValueTask.FromResult<Outcome<GenerationArtifacts>>(new Outcome<GenerationArtifacts>.Success(new(tree, ImmutableArray<GenerationDiagnostic>.Empty)));
        }
    }

    private static Task<(List<CompileEvent> Events, CompileResult Result, CompileWorkspace Workspace)> Run(IModelTransform pass, string version = "4") =>
        Run(Passes(pass), version);

    private static async Task<(List<CompileEvent> Events, CompileResult Result, CompileWorkspace Workspace)> Run(ModelPasses passes, string version = "4",
        IGenerationBackend? backend = null)
    {
        var workspace = new CompileWorkspace();
        workspace.Write("src/Rates.fs", CompileRunnerTests.Rates);
        workspace.Write("src/Pricing.fs", CompileRunnerTests.Pricing);
        var overrides = new CompileOverrides { Language = "fsharp", Inputs = ["src/Rates.fs", "src/Pricing.fs"], PackageName = "acme/car-rental", IrVersion = version };
        CompileGenerationStages? generation = null;
        if (backend is not null)
        {
            var parsed = CompileTarget.Parse(backend.Descriptor.Target.Value, "gen", null, workspace.Root);
            parsed.TryGetValue(out var target).Should().BeTrue();
            overrides = overrides with { Target = target };
            generation = new CompileGenerationStages(new RegisteredBackendResolver([backend]), new GenerationPublisher());
        }
        var runner = CompileRunners.Create([FSharpFrontend.Registration], passes: passes, generation: generation);
        var events = await CompileRunners.Events(runner, overrides, workspace.Context());
        return (events, CompileHandler.Fold(events), workspace);
    }

    private static string Describe(CompileEvent e) => e switch
    {
        CompileEvent.StageStarted started => "start " + started.Stage.WireValue(),
        CompileEvent.StageCompleted completed => "end " + completed.Stage.WireValue() + " " + completed.Passed,
        _ => "completed",
    };

    [Test]
    public async Task ThePassesRunBetweenTheFrontendAndTheWriterAndAreReported()
    {
        var log = new List<string>();
        var (events, result, workspace) = await Run(new RecordingPass("after-frontend", log));
        using var _ = workspace;

        result.ExitCode.Should().Be(0, string.Join("\n", result.Diagnostics.Select(d => d.Diagnostic)));
        log.Should().Equal("rates", "pricing");
        events.Where(e => e is not CompileEvent.DiagnosticsReported).Select(Describe).Should().Equal(
            "start settings", "end settings True", "start sources", "end sources True", "start capture", "end capture True",
            "start frontend", "end frontend True", "start transform", "end transform True", "start write", "end write True",
            "start publish", "end publish True", "completed");

        var json = CompileReport.From(result).ToJson();
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("passes").GetProperty("ran")[0].GetString().Should().Be("after-frontend");
    }

    [Test]
    public async Task APassAfterTheFrontendThatRewritesANodeChangesTheWrittenIrAndTheJsonListsTheApplication()
    {
        var (_, result, workspace) = await Run(LiteralPass("eighty", PassStage.AfterFrontend, 80, 81));
        using var _w = workspace;

        result.ExitCode.Should().Be(0, string.Join("\n", result.Diagnostics.Select(d => d.Diagnostic)));
        var written = File.ReadAllText(workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json"));
        var literals = System.Text.RegularExpressions.Regex.Matches(written, "\"IntegerLiteral\"\\s*:\\s*(\\d+)").Select(m => m.Groups[1].Value);
        literals.Should().Contain("81").And.NotContain("80");

        using var document = JsonDocument.Parse(CompileReport.From(result).ToJson());
        var application = document.RootElement.GetProperty("passes").GetProperty("applications").EnumerateArray().Should().ContainSingle().Subject;
        application.GetProperty("rule").GetString().Should().Be("https://example.org/rules/eighty");
        application.GetProperty("before").GetString().Should().Contain("/value/daily-rate/body");
    }

    [Test]
    public async Task CompileWithATargetReportsThePassesBeforeTheBackendTheirWarningsAndOneLineage()
    {
        var backend = new RecordingBackend("current-only");
        var passes = Passes(LiteralPass("eighty", PassStage.AfterFrontend, 80, 81),
            LiteralPass("eighty-one", PassStage.NewBeforeBackend("current-only"), 81, 82), new WarningPass("current-only"));
        var (_, result, workspace) = await Run(passes, backend: backend);
        using var _w = workspace;

        result.ExitCode.Should().Be(0, string.Join("\n", result.Diagnostics.Select(d => d.Diagnostic)));
        result.Diagnostics.Should().Contain(d => d.Diagnostic.CodeOrNull != null && d.Diagnostic.CodeOrNull.Value == "test.warning"
            && d.Stage == CompileStage.Generate);
        backend.Requests.Should().ContainSingle();

        using var document = JsonDocument.Parse(CompileReport.From(result).ToJson());
        var first = document.RootElement.GetProperty("passes").GetProperty("applications").EnumerateArray().Should().ContainSingle().Subject;
        var generation = document.RootElement.GetProperty("generation").GetProperty("passes");
        generation.GetProperty("ran").EnumerateArray().Select(id => id.GetString()).Should().Equal("eighty-one", "warning");

        // The node that both stages rewrote has one record, from the input of the compile pipeline to the last replacement.
        var last = generation.GetProperty("applications").EnumerateArray().Should().ContainSingle().Subject;
        last.GetProperty("rule").GetString().Should().Be("https://example.org/rules/eighty-one");
        last.GetProperty("before").GetString().Should().Be(first.GetProperty("before").GetString());
    }

    [Test]
    public async Task APassErrorStopsBeforeThePublicationOfTheModel()
    {
        var failing = new RecordingPass("failing", [], _ =>
            Checked<PassResult>.Fail([Diagnostic.Create(DiagnosticSeverity.Error, DiagnosticCode.Create("test.failed"), "The pass failed.", null)]));
        var (events, result, workspace) = await Run(failing);
        using var _ = workspace;

        result.ExitCode.Should().Be(1);
        CompileRunners.ErrorCodes(result).Should().Equal("test.failed");
        events.OfType<CompileEvent.StageStarted>().Select(e => e.Stage).Should().NotContain(CompileStage.Write).And.Contain(CompileStage.Transform);
        File.Exists(workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json")).Should().BeFalse();
    }

    [Test]
    public async Task AnOlderFamilySkipsThePassesWithOneInfoDiagnostic()
    {
        var log = new List<string>();
        var (_, result, workspace) = await Run(new RecordingPass("after-frontend", log), "3");
        using var _w = workspace;

        result.ExitCode.Should().Be(0, string.Join("\n", result.Diagnostics.Select(d => d.Diagnostic)));
        log.Should().BeEmpty();
        var notes = result.Diagnostics.Where(d => d.Diagnostic.CodeOrNull?.Value == "transform.skipped").ToArray();
        notes.Should().ContainSingle();
        notes[0].Diagnostic.Severity.Should().Be(DiagnosticSeverity.Info);
        notes[0].Stage.Should().Be(CompileStage.Transform);
    }
}
