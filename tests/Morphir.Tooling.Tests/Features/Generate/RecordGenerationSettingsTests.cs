// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Compilation;
using Morphir.Frontend.FSharp;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.TaskRecords;
using Morphir.Tooling.Tests.Features.Compile;
using Morphir.Tooling.Tests.Features.TaskRecords;

namespace Morphir.Tooling.Tests.Features.Generate;

/// <summary>
/// Task 5.4 (spec: compile-task-records, "Generate reads the most recent compile" and "Runs of one task are
/// serialized"): <c>generate</c> without <c>--input</c> reads the IR through the compile record and holds the
/// shared lock until model loading ends. Each wait has a bound and waits on a signal, never on a sleep.
/// </summary>
[Category("PlatformSpecific")]
public sealed class RecordGenerationSettingsTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private static CompileWorkspace Project()
    {
        var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml", "[project]\nname = \"acme/car-rental\"\n\n[frontend]\nlanguage = \"fsharp\"\n");
        workspace.Write("src/Rates.fs", CompileRunnerTests.Rates);
        workspace.Write("src/Pricing.fs", CompileRunnerTests.Pricing);
        return workspace;
    }

    private static readonly CompileOverrides Ordered = new() { Inputs = ["src/Rates.fs", "src/Pricing.fs"] };

    private static async Task<CompileResult> Compile(CompileWorkspace workspace, IFrontend? frontend = null, string version = "4") =>
        CompileHandler.Fold(await CompileRunners.Events(CompileRunners.Create([frontend ?? FSharpFrontend.Registration]),
            Ordered with { IrVersion = version }, workspace.Context()));

    private static InvocationContext Invocation(CompileWorkspace workspace) =>
        new(workspace.Root) { Environment = workspace.Context().Environment };

    private static RecordGenerationSettingsResolver Resolver(TextWriter? diagnostics = null) =>
        new(new CliGenerationSettingsResolver(), new FileSystemCompileConfigurationLoader(),
            new TaskStoreOptions { Diagnostics = diagnostics ?? TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) });

    private static GenerateOverrides FromRecord(string output = "gen") => new(Target: "fsharp", Output: output);

    private static Task<Outcome<GenerationSuccess>> Generate(CompileWorkspace workspace, GenerateOverrides overrides,
        IGenerationSettingsResolver? resolver = null, IModelLoader? loader = null) =>
        GenerateHandler.Handle(new GenerateRequest(overrides, Invocation(workspace)), resolver ?? Resolver(),
            loader ?? new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([new FSharpGenerationBackend()]), new GenerationPublisher(), default);

    private static string RecordIr(CompileWorkspace workspace) => workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json");

    [Test]
    public async Task GenerateWithoutAnInputUsesTheIrAndVersionOfTheCompileRecord()
    {
        using var workspace = Project();
        (await Compile(workspace, version: "3")).ExitCode.Should().Be(0);

        var generated = (await Generate(workspace, FromRecord())).ShouldSucceed();
        generated.InputPath.Should().Be(RecordIr(workspace));
        generated.SourceVersion.Should().Be("3", "the record's ir.version is v3");
        generated.OutputDirectory.Should().Be(workspace.PathOf("gen"));
        generated.Publication.Artifacts.Should().NotBeEmpty();
    }

    [Test]
    public async Task WithoutAPriorCompileGenerateFailsWithExitCodeTwoAndPublishesNothing()
    {
        using var workspace = Project();
        var outcome = await Generate(workspace, FromRecord());
        var failure = outcome.ShouldFail("compile.record_missing");
        failure.Kind.Should().Be(GenerationFailureKind.Invocation);
        GenerationReport.From(outcome, FromRecord()).ExitCode.Should().Be(2);
        failure.Diagnostics.Single().Message.Should().Contain("Run `morphir compile` first");
        Directory.Exists(workspace.PathOf("gen")).Should().BeFalse();
        Directory.Exists(workspace.PathOf(".morphir")).Should().BeFalse("a reader creates no out directory");
    }

    [Test]
    public async Task ATombstoneFailsLikeAMissingRecord()
    {
        using var workspace = Project();
        (await Compile(workspace)).ExitCode.Should().Be(0);
        workspace.Write("src/Pricing.fs", CompileRunnerTests.MutablePricing);
        (await Compile(workspace)).ExitCode.Should().Be(1);

        (await Generate(workspace, FromRecord())).ShouldFail("compile.record_missing");
        Directory.Exists(workspace.PathOf("gen")).Should().BeFalse();
    }

    [Test]
    public async Task AnExplicitInputNeverReadsARecordOrWaitsForTheLock()
    {
        using var workspace = Project();
        (await Compile(workspace)).ExitCode.Should().Be(0);
        var explicitIr = workspace.PathOf("explicit.json");
        File.Copy(RecordIr(workspace), explicitIr);
        File.WriteAllText(workspace.PathOf(".morphir/out/compile.json"), "{ not a record");

        // A compile holds the exclusive lock; an explicit input does not wait for it.
        var store = new CompileTaskStore(OutRootOf(workspace), MemberPath.Root, new TaskStoreOptions { Diagnostics = TextWriter.Null });
        using var running = (await store.BeginAsync(default)).ShouldSucceed();
        var waiting = new LineWriter();
        var resolution = await Resolver(waiting).ResolveAsync(new GenerateOverrides("explicit.json", "fsharp", "gen"), Invocation(workspace), default)
            .AsTask().WaitAsync(Bound);
        var settings = resolution.ShouldSucceed();
        settings.Input.Path.Should().Be(explicitIr);
        settings.InputLease.Should().BeNull();
        waiting.Lines.Should().BeEmpty();
    }

    [Test]
    public async Task TheSharedLockIsHeldUntilModelLoadingEnds()
    {
        using var workspace = Project();
        (await Compile(workspace)).ExitCode.Should().Be(0);
        var compileWaits = new LineWriter();
        Task<Outcome<CompileTaskRun>>? starting = null;
        var loader = new HookedLoader(new GenerationModelLoader(new BoundedModelCapture()), async () =>
        {
            // While the model loads, a compile must wait for the reader.
            var waitingStore = new CompileTaskStore(OutRootOf(workspace), MemberPath.Root,
                new TaskStoreOptions { Diagnostics = compileWaits, PollInterval = TimeSpan.FromMilliseconds(10) });
            starting = waitingStore.BeginAsync(default).AsTask();
            await compileWaits.FirstLine.WaitAsync(Bound);
            starting.IsCompleted.Should().BeFalse("the reader holds the shared lock while it loads the model");
        });

        (await Generate(workspace, FromRecord(), loader: loader)).ShouldSucceed();
        starting.Should().NotBeNull();
        using var run = (await starting!.WaitAsync(Bound)).ShouldSucceed();
        compileWaits.Lines.Should().ContainSingle();
    }

    [Test]
    public async Task GenerateWaitsForARunningCompileAndThenReadsItsIr()
    {
        using var workspace = Project();
        (await Compile(workspace)).ExitCode.Should().Be(0);

        // The second compile holds the exclusive lock while its frontend waits at a gate.
        workspace.Write("src/Rates.fs", CompileRunnerTests.Rates.Replace("80", "97"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gated = new FakeFrontend("morphir-dotnet.fsharp", FSharpFrontend.Registration.Capability, async (request, ct) =>
        {
            entered.TrySetResult();
            await gate.Task;
            return await FSharpFrontend.Registration.CompileAsync(request, ct);
        });
        var compiling = Compile(workspace, gated);
        await entered.Task.WaitAsync(Bound);

        var generateWaits = new LineWriter();
        var generating = Generate(workspace, FromRecord(), Resolver(generateWaits));
        await generateWaits.FirstLine.WaitAsync(Bound);
        generating.IsCompleted.Should().BeFalse("generate waits while the compile holds the lock");

        gate.SetResult();
        (await compiling.WaitAsync(Bound)).ExitCode.Should().Be(0);
        var generated = (await generating.WaitAsync(Bound)).ShouldSucceed();
        generateWaits.Lines.Should().ContainSingle().Which.Should().StartWith("waiting for another Morphir run to finish this task");
        var texts = generated.Publication.Artifacts.Select(path => File.ReadAllText(Path.Combine(generated.OutputDirectory, path)));
        string.Concat(texts).Should().Contain("97", "generate reads the IR of the compile that it waited for");
    }

    [Test]
    public async Task AnIrThatDoesNotDeclareTheRecordedVersionIsRefused()
    {
        using var workspace = Project();
        (await Compile(workspace)).ExitCode.Should().Be(0);
        var ir = File.ReadAllBytes(RecordIr(workspace));
        var store = new CompileTaskStore(OutRootOf(workspace), MemberPath.Root, new TaskStoreOptions { Diagnostics = TextWriter.Null });
        using (var run = (await store.BeginAsync(default)).ShouldSucceed())
        {
            File.WriteAllBytes(Path.Combine(run.Dest, "morphir-ir.json"), ir);
            run.Complete(new CompileProduct("fsharp", RecordWorkspace.SingleFileIr("3"), null)).ShouldSucceed();
        }

        (await Generate(workspace, FromRecord())).ShouldFail("compile.record_version_mismatch");
        Directory.Exists(workspace.PathOf("gen")).Should().BeFalse();
    }

    [Test]
    public async Task AnInputFormatThatDiffersFromTheRecordedFormatIsRefused()
    {
        using var workspace = Project();
        (await Compile(workspace)).ExitCode.Should().Be(0);
        (await Generate(workspace, FromRecord() with { InputFormat = "ion" })).ShouldFail("generate.input_format_conflict");
    }

    [Test]
    public async Task TheProjectOptionSelectsTheProjectWhoseRecordIsRead()
    {
        using var workspace = Project();
        (await Compile(workspace)).ExitCode.Should().Be(0);
        Directory.CreateDirectory(workspace.PathOf("elsewhere"));
        var generated = (await GenerateHandler.Handle(
            new GenerateRequest(FromRecord(workspace.PathOf("gen")) with { Project = workspace.Root },
                new InvocationContext(workspace.PathOf("elsewhere")) { Environment = workspace.Context().Environment }),
            Resolver(), new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([new FSharpGenerationBackend()]), new GenerationPublisher(), default)).ShouldSucceed();
        generated.InputPath.Should().Be(RecordIr(workspace));
    }

    private static OutRoot OutRootOf(CompileWorkspace workspace) =>
        ((Outcome<OutRoot>.Success)OutRoot.Resolve(new OutRootRequest(workspace.Root, workspace.Root))).Value;

    /// <summary>Runs a hook while the model loads, then loads the model.</summary>
    private sealed class HookedLoader(IModelLoader inner, Func<Task> hook) : IModelLoader
    {
        public async ValueTask<Outcome<LoadedModel>> LoadAsync(ModelInput input, ModelReadLimits limits, CancellationToken ct)
        {
            await hook();
            return await inner.LoadAsync(input, limits, ct);
        }
    }
}
