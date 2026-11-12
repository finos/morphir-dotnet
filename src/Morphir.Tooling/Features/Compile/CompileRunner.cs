// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Morphir.Compilation;
using Morphir.Tooling.Features.Compile.Sources;
using Severity = Morphir.Compilation.DiagnosticSeverity;

namespace Morphir.Tooling.Features.Compile;

/// <summary>Runs one compile as a stream of events (STRM-1).</summary>
public interface ICompileRunner
{
    /// <summary>
    /// The events of one compile, as they occur. The last event is always <see cref="CompileEvent.Completed"/>.
    /// Cancellation before publication completes the stream with a cancelled failure.
    /// </summary>
    IAsyncEnumerable<CompileEvent> RunAsync(CompileOverrides overrides, CompileContext context, CancellationToken ct = default);
}

/// <summary>
/// The compile pipeline at its orchestration edge. Each stage returns a <c>Checked</c> value (DIAG-1); the runner
/// reports its diagnostics, grouped by document, when the stage completes, and stops at the first failed stage.
/// The stages are settings, sources, capture, frontend (which builds the requested family), write and publish, and,
/// with <c>--target</c>, generate and generatePublish (CLI-2).
/// </summary>
public sealed class CompileRunner(
    ICompileSettingsResolver settingsResolver,
    WriterRegistry writers,
    IFrontendResolver frontends,
    ISourceSelector selector,
    ISourceCapture capture,
    ICompilePublisher publisher,
    IFrontendExtensionConnector extensions,
    CompileGenerationStages? generation = null,
    ILogger<CompileRunner>? logger = null) : ICompileRunner
{
    /// <summary>The writer options of the published IR.</summary>
    public static WriteOptions PublishedLayout { get; } = WriteOptions.Compact.WithIndented(true);

    /// <inheritdoc />
    public async IAsyncEnumerable<CompileEvent> RunAsync(CompileOverrides overrides, CompileContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        IFrontend? connected = null;
        ICompilePublication? publication = null;
        try
        {
            // Settings, writer, and the frontends of this run: everything that needs no source.
            yield return new CompileEvent.StageStarted(CompileStage.Settings);
            CompileSettings? partial = null;
            var settingsRun = await Guard(CompileStage.Settings, () => ResolveSettingsAsync(overrides, context, value => partial = value, ct), ct);
            foreach (var e in Report(CompileStage.Settings, settingsRun)) yield return e;
            if (!settingsRun.Passed(out var resolved))
            {
                // The report keeps the resolved settings when a later check of the stage failed.
                yield return Failed(settingsRun, CompileFailureKind.Configuration, partial);
                yield break;
            }
            var (settings, writer, runFrontends) = resolved;
            connected = resolved.Extension;
            publication = resolved.Publication;

            yield return new CompileEvent.StageStarted(CompileStage.Sources);
            var claimed = runFrontends.ClaimedExtensions(settings.Language?.Value);
            var sourcesRun = await Guard(CompileStage.Sources, () => selector.SelectAsync(settings, claimed, ct), ct);
            foreach (var e in Report(CompileStage.Sources, sourcesRun)) yield return e;
            if (!sourcesRun.Passed(out var sources))
            {
                yield return Failed(sourcesRun, CompileFailureKind.Configuration, settings);
                yield break;
            }

            yield return new CompileEvent.StageStarted(CompileStage.Capture);
            var captureRun = await Guard(CompileStage.Capture, () => capture.CaptureAsync(sources, ct), ct);
            foreach (var e in Report(CompileStage.Capture, captureRun)) yield return e;
            if (!captureRun.Passed(out var documents))
            {
                yield return Failed(captureRun, CompileFailureKind.Operational, settings, sources);
                yield break;
            }

            yield return new CompileEvent.StageStarted(CompileStage.Frontend);
            var frontendRun = await Guard(CompileStage.Frontend, () => CompileAsync(runFrontends, settings, writer, sources, documents, ct), ct);
            foreach (var e in Report(CompileStage.Frontend, frontendRun)) yield return e;
            if (!frontendRun.Passed(out var compiled))
            {
                yield return Failed(frontendRun, frontendRun.Kind ?? CompileFailureKind.Source, settings, sources, frontendRun.Frontend);
                yield break;
            }

            yield return new CompileEvent.StageStarted(CompileStage.Write);
            var writeRun = await Guard(CompileStage.Write,
                () => Task.FromResult(writer.Writer.Write(writer.Target, PublishedLayout, compiled.Package.Model)), ct);
            foreach (var e in Report(CompileStage.Write, writeRun)) yield return e;
            if (!writeRun.Passed(out var bytes))
            {
                yield return Failed(writeRun, CompileFailureKind.Source, settings, sources, compiled.Frontend.Label);
                yield break;
            }

            yield return new CompileEvent.StageStarted(CompileStage.Publish);
            var publishRun = await Guard(CompileStage.Publish, () => publication.PublishAsync(new CompilePublishRequest(
                compiled.Frontend.Language.Value, writer.Target.Version, sources.Selection.ProjectFileOrNull, bytes), ct), ct);
            foreach (var e in Report(CompileStage.Publish, publishRun)) yield return e;
            if (!publishRun.Passed(out var published))
            {
                yield return Failed(publishRun, CompileFailureKind.Operational, settings, sources, compiled.Frontend.Label);
                yield break;
            }

            var success = new CompileSuccess(compiled.Frontend.Language.Value, compiled.Frontend.Label, settings.IrVersion.Value, sources,
                [.. compiled.Package.Modules], published);
            if (overrides.Target is not { } target)
            {
                yield return new CompileEvent.Completed(new CompileConclusion.Succeeded(success), settings);
                yield break;
            }
            // CLI-2: the generation stages run after publication, on the exact bytes that were written.
            if (generation is null)
            {
                yield return new CompileEvent.StageStarted(CompileStage.Generate);
                var unavailable = Checked<CompileGeneration>.Fail([Error(CompileCodes.GenerationUnavailable,
                    "This build registers no generation backends, so compile --target cannot run. Use morphir generate.")]);
                foreach (var e in Report(CompileStage.Generate, new StageResult<CompileGeneration>(unavailable))) yield return e;
                yield return new CompileEvent.Completed(new CompileConclusion.Failed(new CompileFailure(CompileFailureKind.Configuration,
                    "This build registers no generation backends.")
                { Sources = sources, Frontend = success.Frontend, Compiled = success }), settings);
                yield break;
            }
            await foreach (var e in generation.RunAsync(target, success, settings, bytes, ct)) yield return e;
        }
        finally
        {
            // The record and any install are written: release the task lock (REC-1).
            publication?.Dispose();
            if (connected is IAsyncDisposable disposable) await disposable.DisposeAsync();
        }
    }

    /// <summary>The settings stage's result: the settings, the writer and the frontends of this run.</summary>
    private sealed record ResolvedRun(CompileSettings Settings, ResolvedWriter Writer, IFrontendResolver Frontends, IFrontend? Extension,
        ICompilePublication Publication)
    {
        public void Deconstruct(out CompileSettings settings, out ResolvedWriter writer, out IFrontendResolver resolver) =>
            (settings, writer, resolver) = (Settings, Writer, Frontends);
    }

    private async Task<Checked<ResolvedRun>> ResolveSettingsAsync(CompileOverrides overrides, CompileContext context,
        Action<CompileSettings> resolved, CancellationToken ct)
    {
        var settings = settingsResolver.Resolve(overrides, context);
        if (!settings.TryGetValue(out var value)) return Checked<ResolvedRun>.Fail(settings.Diagnostics);
        resolved(value);
        var diagnostics = new List<Diagnostic>(settings.Diagnostics);
        var writer = writers.Resolve(value.IrVersion.Value.Version);
        diagnostics.AddRange(writer.Diagnostics);
        if (!writer.TryGetValue(out var resolvedWriter)) return Checked<ResolvedRun>.Fail(diagnostics);

        // A configured extension replaces the registered frontends for this run (FE-2).
        if (value.Extension is { } extension)
        {
            var connection = await extensions.ConnectAsync(
                new FrontendExtensionRequest(extension, value.Language?.Value, value.WorkingDirectory) { WasmModule = value.WasmModule }, ct);
            diagnostics.AddRange(connection.Diagnostics);
            if (!connection.TryGetValue(out var frontend)) return Checked<ResolvedRun>.Fail(diagnostics);
            ICompilePublication? started = null;
            try
            {
                started = await BeginAsync(value, diagnostics, ct);
            }
            finally
            {
                // The run does not own the connected extension until the settings stage passes.
                if (started is null && frontend is IAsyncDisposable disposable) await disposable.DisposeAsync();
            }
            return started is null
                ? Checked<ResolvedRun>.Fail(diagnostics)
                : Checked<ResolvedRun>.Of(new ResolvedRun(value, resolvedWriter, frontends.Only(frontend), frontend, started), diagnostics);
        }

        if (value.Language is { } language) diagnostics.AddRange(frontends.CheckAvailable(language.Value).Diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.IsError)) return Checked<ResolvedRun>.Fail(diagnostics);
        var publication = await BeginAsync(value, diagnostics, ct);
        return publication is null
            ? Checked<ResolvedRun>.Fail(diagnostics)
            : Checked<ResolvedRun>.Of(new ResolvedRun(value, resolvedWriter, frontends, null, publication), diagnostics);
    }

    /// <summary>
    /// Starts the record run when everything that needs no source passed (REC-1): the task lock, the tombstone and
    /// the cleared <c>.dest</c>. A compile that fails later leaves the tombstone. Null when the start failed.
    /// </summary>
    private async Task<ICompilePublication?> BeginAsync(CompileSettings settings, List<Diagnostic> diagnostics, CancellationToken ct)
    {
        var started = await publisher.BeginAsync(settings, ct);
        diagnostics.AddRange(started.Diagnostics);
        return started.TryGetValue(out var publication) ? publication : null;
    }

    private sealed record Compiled(ResolvedFrontend Frontend, CompiledPackage Package);

    private static async Task<StageResult<Compiled>> CompileAsync(IFrontendResolver resolver, CompileSettings settings, ResolvedWriter writer,
        SelectedSources sources, CapturedSources captured, CancellationToken ct)
    {
        var resolution = resolver.Resolve(settings.Language?.Value, sources.Selection.Documents, writer.Target);
        if (!resolution.TryGetValue(out var frontend))
            return new(Checked<Compiled>.Fail(resolution.Diagnostics), CompileFailureKind.Configuration);

        var request = captured.Documents.Select(document => new SourceDocument(document.Path, frontend.Language, 0UL, document.Text));
        if (!CompileRequest.TryParse(frontend.Language, null, request, settings.Package.Value.Name,
                settings.ExposedModules?.Value, writer.Target, "", out var compileRequest, out var invalid))
            return new(Checked<Compiled>.Fail([Error(CompileCodes.InvalidSetting, invalid.ToString())]), CompileFailureKind.Invocation, frontend.Label);

        var outcome = await frontend.Frontend.CompileAsync(compileRequest, ct);
        return outcome.Match(
            completed => new StageResult<Compiled>(completed.Match(
                (package, diagnostics) => Checked<Compiled>.Of(new Compiled(frontend, package), diagnostics),
                Checked<Compiled>.Fail), CompileFailureKind.Source, frontend.Label),
            failed => new StageResult<Compiled>(Checked<Compiled>.Fail(failed.Diagnostics.Append(
                Error(CompileCodes.FrontendFailed, $"The frontend {frontend.Label} failed: {failed.Message}"))), CompileFailureKind.Operational, frontend.Label));
    }

    /// <summary>A stage's checked result, the failure kind when it fails, or its cancellation or crash.</summary>
    private readonly record struct StageResult<T>(Checked<T> Result, CompileFailureKind? Kind = null, string? Frontend = null)
    {
        public bool Passed(out T value) => Result.TryGetValue(out value!);
        public static implicit operator StageResult<T>(Checked<T> result) => new(result);
    }

    private async Task<StageResult<T>> Guard<T>(CompileStage stage, Func<Task<Checked<T>>> work, CancellationToken ct) =>
        await Guard(stage, async () => (StageResult<T>)await work(), ct);

    private async Task<StageResult<T>> Guard<T>(CompileStage stage, Func<Task<StageResult<T>>> work, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            return await work();
        }
        catch (OperationCanceledException)
        {
            return new(Checked<T>.Fail([Error(CompileCodes.Cancelled,
                $"The compile was cancelled in the {stage.WireValue()} stage, before publication.")]), CompileFailureKind.Cancelled);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
        {
            logger?.LogError(error, "The compile {Stage} stage failed unexpectedly.", stage.WireValue());
            return new(Checked<T>.Fail([Error(CompileCodes.Failed,
                $"The {stage.WireValue()} stage failed unexpectedly: {error.GetType().Name}: {error.Message}")]), CompileFailureKind.Operational);
        }
    }

    private static IEnumerable<CompileEvent> Report<T>(CompileStage stage, StageResult<T> run)
    {
        foreach (var group in run.Result.Diagnostics.GroupBy(diagnostic => diagnostic.LocationOrNull?.Uri))
            yield return new CompileEvent.DiagnosticsReported(stage, group.Key, [.. group]);
        yield return new CompileEvent.StageCompleted(stage, run.Result.IsPassed);
    }

    private static CompileEvent.Completed Failed<T>(StageResult<T> run, CompileFailureKind kind, CompileSettings? settings,
        SelectedSources? sources = null, string? frontend = null)
    {
        var failure = run.Kind ?? kind;
        var message = run.Result.Diagnostics.FirstOrDefault(diagnostic => diagnostic.IsError)?.Message ?? "The compile failed.";
        return new(new CompileConclusion.Failed(new CompileFailure(failure, message) { Sources = sources, Frontend = frontend }), settings);
    }

    private static Diagnostic Error(DiagnosticCode code, string message) => Diagnostic.Create(Severity.Error, code, message, null);
}
