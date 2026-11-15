// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Text.Json;
using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Morphir.Compilation;
using Morphir.Frontend.FSharp;
using Morphir.IR.V4;
using Morphir.MiddleEnd;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Transform;

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

    private static ModelPasses Passes(IModelTransform pass) =>
        new([pass], new Dictionary<string, ImmutableArray<IrFeature>>());

    private static async Task<(List<CompileEvent> Events, CompileResult Result, CompileWorkspace Workspace)> Run(IModelTransform pass, string version = "4")
    {
        var workspace = new CompileWorkspace();
        workspace.Write("src/Rates.fs", CompileRunnerTests.Rates);
        workspace.Write("src/Pricing.fs", CompileRunnerTests.Pricing);
        var overrides = new CompileOverrides { Language = "fsharp", Inputs = ["src/Rates.fs", "src/Pricing.fs"], PackageName = "acme/car-rental", IrVersion = version };
        var events = await CompileRunners.Events(CompileRunners.Create([FSharpFrontend.Registration], passes: Passes(pass)), overrides, workspace.Context());
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
