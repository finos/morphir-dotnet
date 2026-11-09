// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Morphir.Compilation;
using Morphir.Frontend.FSharp;
using Morphir.Tooling.Features.Compile;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>Task 4.6: the compile runner (STRM-1) and the handler's fold (CLI-1), with fakes and the real frontend.</summary>
public sealed class CompileRunnerTests
{
    public const string Rates = "namespace Acme.CarRental\n\nmodule Rates =\n    let dailyRate (seats: int) : int = if seats > 4 then 80 else 60\n";
    public const string Pricing = "namespace Acme.CarRental\n\nmodule Pricing =\n    let total (seats: int) (days: int) : int = Rates.dailyRate seats * days\n";
    public const string MutablePricing = "namespace Acme.CarRental\n\nmodule Pricing =\n    let total (seats: int) (days: int) : int =\n        let mutable sum = Rates.dailyRate seats * days\n        sum\n";

    private static readonly FrontendHint FSharpHint = new(LanguageId.Create("fsharp"), "The dotnet tool dotnet-morphir has a built-in F# frontend.");

    private static CompileWorkspace Project(string pricing = Pricing)
    {
        var workspace = new CompileWorkspace();
        workspace.Write("src/Rates.fs", Rates);
        workspace.Write("src/Pricing.fs", pricing);
        return workspace;
    }

    private static CompileOverrides Inputs(string version = "4") => new()
    {
        Language = "fsharp",
        Inputs = ["src/Rates.fs", "src/Pricing.fs"],
        PackageName = "acme/car-rental",
        IrVersion = version
    };

    [Test]
    public async Task TheToolPathCompilesWithTheRealFrontendAndPublishes()
    {
        using var workspace = Project();
        var events = await CompileRunners.Events(CompileRunners.Create([FSharpFrontend.Registration]), Inputs(), workspace.Context());
        var result = CompileHandler.Fold(events);
        result.ExitCode.Should().Be(0, string.Join("\n", result.Diagnostics.Select(d => d.Diagnostic)));
        var success = ((CompileConclusion.Succeeded)result.Conclusion).Value;
        success.Frontend.Should().Be("morphir-dotnet.fsharp (in-process)");
        success.Sources.Selection.Documents.Should().Equal("Rates.fs", "Pricing.fs");
        success.Modules.Should().Equal("Rates", "Pricing");
        success.Publication.IrPath.Should().Be(workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json"));
        using var ir = JsonDocument.Parse(File.ReadAllBytes(success.Publication.IrPath));
        ir.RootElement.GetProperty("formatVersion").GetRawText().Should().Be("4");
    }

    [Test]
    public async Task EveryStageStartsAndCompletesInOrderAndCompletedIsLast()
    {
        using var workspace = Project();
        var events = await CompileRunners.Events(CompileRunners.Create([FSharpFrontend.Registration]), Inputs(), workspace.Context());
        events.Where(e => e is not CompileEvent.DiagnosticsReported).Select(Describe).Should().Equal(
            "start settings", "end settings True", "start sources", "end sources True", "start capture", "end capture True",
            "start frontend", "end frontend True", "start write", "end write True", "start publish", "end publish True", "completed");
    }

    [Test]
    public async Task ASourceErrorFailsWithExitCodeOneAndPublishesNothing()
    {
        using var workspace = Project(MutablePricing);
        var result = CompileHandler.Fold(await CompileRunners.Events(CompileRunners.Create([FSharpFrontend.Registration]), Inputs(), workspace.Context()));
        result.ExitCode.Should().Be(1);
        CompileRunners.ErrorCodes(result).Should().Equal("fsharp.unsupported_mutable");
        result.Diagnostics.Single().Stage.Should().Be(CompileStage.Frontend);
        File.Exists(workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json")).Should().BeFalse();
    }

    [Test]
    public async Task AnUndeclaredPreReleaseFailsInTheSettingsStageWithItsSpelling()
    {
        using var workspace = Project();
        var frontend = new FakeFrontend("fake.fsharp", FakeFrontend.FSharp());
        var events = await CompileRunners.Events(CompileRunners.Create([frontend]), Inputs("4.1.0-draft.1"), workspace.Context());
        var result = CompileHandler.Fold(events);
        result.ExitCode.Should().Be(2);
        CompileRunners.ErrorCodes(result).Should().Equal("compile.unsupported_ir_version");
        var message = result.Diagnostics.Single().Diagnostic.Message;
        message.Should().Contain("4.1.0-draft.1").And.Contain("Declared versions: 1, 2, 3, 4");
        events.OfType<CompileEvent.StageStarted>().Select(e => e.Stage).Should().Equal(CompileStage.Settings);
        frontend.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task AnIrVersionThatTheFrontendDoesNotServeFailsBeforeCompilation()
    {
        using var workspace = Project();
        var frontend = new FakeFrontend("fake.classic-only", FakeFrontend.FSharp(FakeFrontend.Classic("3.0.0")));
        var result = CompileHandler.Fold(await CompileRunners.Events(CompileRunners.Create([frontend]), Inputs("4"), workspace.Context()));
        result.ExitCode.Should().Be(2);
        CompileRunners.ErrorCodes(result).Should().Equal("compile.ir_version_not_served");
        result.Diagnostics.Single().Diagnostic.Message.Should().Contain("fake.classic-only").And.Contain("classic 3.0.0");
        frontend.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task WithoutAnFSharpFrontendTheCompileFailsBeforeAnyProjectFileIsEvaluated()
    {
        using var workspace = Project();
        workspace.Write("morphir.toml",
            "[project]\nname = \"acme/car-rental\"\n\n[frontend]\nlanguage = \"fsharp\"\n\n[frontend.fsharp]\nproject_file = \"src/Acme.CarRental.fsproj\"\n");
        workspace.Write("src/Acme.CarRental.fsproj", "<Project />");
        var provider = new FakeProjectFileProvider("Rates.fs", "Pricing.fs");
        var events = await CompileRunners.Events(CompileRunners.Create([], [FSharpHint], [provider]), new CompileOverrides(), workspace.Context());
        var result = CompileHandler.Fold(events);
        result.ExitCode.Should().Be(2);
        CompileRunners.ErrorCodes(result).Should().Equal("compile.frontend_unavailable");
        result.Diagnostics.Single().Diagnostic.Message.Should().Contain("dotnet-morphir").And.Contain("--extension");
        provider.Expanded.Should().BeEmpty();
        CompileReport.From(result).Language.Should().Be("fsharp", "the report keeps the resolved settings");
    }

    [Test]
    public async Task AConfiguredExtensionReplacesTheRegisteredFrontendsThroughTheConnector()
    {
        using var workspace = Project();
        var registered = new FakeFrontend("fake.registered", FakeFrontend.FSharp());
        var extension = new DisposableFrontend(new FakeFrontend("fake.extension", FakeFrontend.FSharp()));
        var connector = new FakeExtensionConnector(extension);
        var result = CompileHandler.Fold(await CompileRunners.Events(CompileRunners.Create([registered], extensions: connector),
            Inputs() with { Extension = "morphir-fsharp-frontend" }, workspace.Context()));
        result.ExitCode.Should().Be(0, string.Join("\n", result.Diagnostics.Select(d => d.Diagnostic)));
        ((CompileConclusion.Succeeded)result.Conclusion).Value.Frontend.Should().Be("fake.extension (in-process)");
        connector.Requests.Single().Extension.Value.Should().Be("morphir-fsharp-frontend");
        registered.Requests.Should().BeEmpty();
        extension.Disposed.Should().BeTrue("the run disposes a connected extension");
    }

    /// <summary>
    /// Spec scenario "Guest writes a log line to stdout": the MEP frontend gives an operational failure with
    /// <c>extension.protocol_error</c> for non-framed guest output (MepFrontendTests, fake guest <c>stdout-log</c>,
    /// in Morphir.Compilation.Mep.Tests). This test gives the run that outcome and checks exit code 2.
    /// </summary>
    [Test]
    public async Task AnExtensionProtocolFailureFailsTheRunWithExitCode2()
    {
        using var workspace = Project();
        var protocol = Diagnostic.Create(DiagnosticSeverity.Error, Morphir.Compilation.Mep.MepFrontendCodes.ProtocolError,
            "Extension org.finos.morphir.fsharp-frontend: the output is not a valid frame.", null);
        var broken = new FakeFrontend("org.finos.morphir.fsharp-frontend", FakeFrontend.FSharp(),
            (_, _) => Task.FromResult(FrontendOutcome.Fail(new FrontendFailure(protocol.Message, [protocol]))), FrontendTransport.Mep);
        var result = CompileHandler.Fold(await CompileRunners.Events(CompileRunners.Create([], extensions: new FakeExtensionConnector(broken)),
            Inputs() with { Extension = "morphir-fsharp-frontend" }, workspace.Context()));
        result.ExitCode.Should().Be(2);
        ((CompileConclusion.Failed)result.Conclusion).Failure.Kind.Should().Be(CompileFailureKind.Operational);
        CompileRunners.ErrorCodes(result).Should().Equal("extension.protocol_error", "compile.frontend_failed");
        result.Diagnostics.Select(d => d.Diagnostic.Message).Should().Contain(message => message.Contains("org.finos.morphir.fsharp-frontend"));
    }

    [Test]
    public async Task AConfiguredExtensionThatCannotStartFailsBeforeSourceSelection()
    {
        using var workspace = Project();
        var events = await CompileRunners.Events(CompileRunners.Create([FSharpFrontend.Registration], extensions: new FailingExtensionConnector()),
            Inputs() with { Extension = "morphir-fsharp-frontend" }, workspace.Context());
        var result = CompileHandler.Fold(events);
        result.ExitCode.Should().Be(2);
        CompileRunners.ErrorCodes(result).Should().Equal("extension.unavailable");
        result.Diagnostics.Single().Diagnostic.Message.Should().Contain("morphir-fsharp-frontend").And.Contain("--extension");
        events.OfType<CompileEvent.StageStarted>().Select(e => e.Stage).Should().Equal(CompileStage.Settings);
    }

    [Test]
    public async Task CancellationDuringTheFrontendEndsWithACancelledCompletedEvent()
    {
        using var workspace = Project();
        using var cancellation = new CancellationTokenSource();
        var frontend = new FakeFrontend("fake.waiting", FakeFrontend.FSharp(), async (_, ct) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        var events = await CompileRunners.Events(CompileRunners.Create([frontend]), Inputs(), workspace.Context(), cancellation.Token);
        events.Last().Should().BeOfType<CompileEvent.Completed>();
        var result = CompileHandler.Fold(events);
        result.ExitCode.Should().Be(130);
        ((CompileConclusion.Failed)result.Conclusion).Failure.Kind.Should().Be(CompileFailureKind.Cancelled);
        CompileRunners.ErrorCodes(result).Should().Equal("compile.cancelled");
        events.OfType<CompileEvent.StageStarted>().Select(e => e.Stage).Should().NotContain(CompileStage.Publish);
        File.Exists(workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json")).Should().BeFalse();
    }

    [Test]
    public async Task TheHandlerGivesEveryEventToTheObserverAndFoldsTheSameResult()
    {
        using var workspace = Project();
        var runner = CompileRunners.Create([FSharpFrontend.Registration]);
        var observer = new RecordingObserver();
        var handled = await CompileHandler.Handle(new CompileCommandRequest(Inputs(), workspace.Context(), observer), runner, CancellationToken.None);
        observer.Events.Last().Should().BeOfType<CompileEvent.Completed>();
        handled.Should().BeEquivalentTo(CompileHandler.Fold(observer.Events), options => options.ComparingByMembers<CompileResult>());
    }

    [Test]
    public void TheFoldOrdersDiagnosticsByStageThenDocumentThenPosition()
    {
        Diagnostic At(string code, string? uri, int line) => Diagnostic.Create(DiagnosticSeverity.Warning, DiagnosticCode.Create(code), code,
            uri is null ? null : Location.Create(uri, Morphir.Compilation.Range.Create(Position.Create(line, 0), Position.Create(line, 1))));
        var result = CompileHandler.Fold([
            new CompileEvent.DiagnosticsReported(CompileStage.Frontend, "B.fs", [At("x.b2", "B.fs", 2), At("x.b1", "B.fs", 1)]),
            new CompileEvent.DiagnosticsReported(CompileStage.Frontend, "A.fs", [At("x.a", "A.fs", 9)]),
            new CompileEvent.DiagnosticsReported(CompileStage.Sources, null, [At("x.s", null, 0)]),
            new CompileEvent.Completed(new CompileConclusion.Failed(new CompileFailure(CompileFailureKind.Source, "failed")), null)
        ]);
        result.Diagnostics.Select(d => d.Diagnostic.CodeOrNull!.Value).Should().Equal("x.s", "x.a", "x.b1", "x.b2");
    }

    private static string Describe(CompileEvent compileEvent) => compileEvent switch
    {
        CompileEvent.StageStarted started => $"start {started.Stage.WireValue()}",
        CompileEvent.StageCompleted completed => $"end {completed.Stage.WireValue()} {completed.Passed}",
        CompileEvent.Completed => "completed",
        _ => compileEvent.ToString()
    };

    private sealed class RecordingObserver : ICompileEventObserver
    {
        public List<CompileEvent> Events { get; } = [];

        public ValueTask OnEventAsync(CompileEvent compileEvent, CancellationToken ct)
        {
            Events.Add(compileEvent);
            return ValueTask.CompletedTask;
        }
    }
}
