// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Morphir.Compilation;
using Morphir.Tooling.Features.Generate;
using Severity = Morphir.Compilation.DiagnosticSeverity;

namespace Morphir.Tooling.Features.Compile;

/// <summary>
/// The generation request of <c>compile --target &lt;id&gt; --generate-output &lt;dir&gt; [--option KEY=VALUE]...</c>
/// (CLI-2). Build one with <see cref="Parse"/>; a value of this type always has a target and an output directory.
/// </summary>
public sealed record CompileTarget
{
    private CompileTarget(RequestedTarget target, string outputDirectory, BackendOptions options) =>
        (Target, OutputDirectory, Options) = (target, outputDirectory, options);

    /// <summary>The requested target. A malformed id reaches the backend resolver as an unknown target, as in <c>generate</c>.</summary>
    public RequestedTarget Target { get; }

    /// <summary>The absolute generation output directory.</summary>
    public string OutputDirectory { get; }

    /// <summary>The backend options, parsed by the parser of <c>generate --option</c>.</summary>
    public BackendOptions Options { get; }

    /// <summary>
    /// Parses the command-line values. No value gives null (no generation). <c>--target</c> needs
    /// <c>--generate-output</c>, and <c>--generate-output</c> and <c>--option</c> need <c>--target</c>. A refusal
    /// is an invocation error (exit code 2), before any stage runs.
    /// </summary>
    public static Checked<CompileTarget?> Parse(string? target, string? outputDirectory, IReadOnlyList<string>? options, string workingDirectory)
    {
        var hasOptions = options is { Count: > 0 };
        if (target is null)
        {
            if (outputDirectory is null && !hasOptions) return Checked<CompileTarget?>.Pass(null);
            var given = outputDirectory is not null ? "--generate-output" : "--option";
            return Invalid($"The option {given} needs --target <id>. Give a target, or remove {given}.");
        }
        if (string.IsNullOrWhiteSpace(target)) return Invalid("The option --target needs a target id, for example fsharp or csharp.");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return Invalid("The option --target needs --generate-output <directory>. The option -o installs the IR, not the generated files.");

        var parsed = BackendOptionText.Parse(options);
        if (parsed is Outcome<BackendOptions>.Failure failure)
            return Invalid($"The option --option is not valid: {failure.Error.Diagnostics[0].Message}");
        try
        {
            return Checked<CompileTarget?>.Pass(new CompileTarget(RequestedTarget.Parse(target),
                Path.GetFullPath(outputDirectory, workingDirectory), ((Outcome<BackendOptions>.Success)parsed).Value));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Invalid($"The option --generate-output is not a valid path: {error.Message}");
        }
    }

    private static Checked<CompileTarget?> Invalid(string message) =>
        Checked<CompileTarget?>.Fail([Diagnostic.Create(Severity.Error, CompileCodes.InvalidArguments, message, null)]);
}

/// <summary>
/// The generation of one compile, for the report: the target, the selected backend (null when selection failed),
/// the output directory, and the generation result when it succeeded.
/// </summary>
public sealed record CompileGeneration(string Target, string? Backend, string OutputDirectory, GenerationSuccess? Result);

/// <summary>
/// The generation stages of <c>compile --target</c> (CLI-2). After a successful compile and publication, the
/// exact bytes that the compile wrote go to the model loader of <c>generate --input</c>, and the loaded model
/// goes to <see cref="GenerationStages.RunFrom"/>, the unchanged tail of <c>generate</c>. The stages report as
/// <see cref="CompileStage.Generate"/> (loading, backend selection, option validation and generation) and
/// <see cref="CompileStage.GeneratePublish"/> (owned publication).
/// </summary>
public sealed class CompileGenerationStages(IGenerationBackendResolver backends, IGenerationPublisher publisher,
    ILogger<CompileGenerationStages>? logger = null)
{
    /// <summary>The budgets of the loaded model: the defaults of <c>generate --input</c>.</summary>
    public static ModelReadLimits Limits { get; } = ModelReadLimits.Default;

    /// <summary>
    /// The events of the generation stages, ending with the run's <see cref="CompileEvent.Completed"/> event.
    /// <paramref name="ir"/> is the exact bytes that <paramref name="compiled"/> published.
    /// </summary>
    public async IAsyncEnumerable<CompileEvent> RunAsync(CompileTarget target, CompileSuccess compiled, CompileSettings settings,
        ReadOnlyMemory<byte> ir, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var irPath = compiled.Publication.IrPath;
        var input = new ModelInput(irPath, InputFormat.Auto);
        var generationSettings = new GenerationSettings(input, target.Target, target.Options, new PublicationPolicy(target.OutputDirectory), Limits);
        var gate = new StagedPublisher(publisher);

        yield return new CompileEvent.StageStarted(CompileStage.Generate);
        Task<Outcome<GenerationSuccess>> run;
        if (ir.Length > Limits.MaxBytes)
            run = Task.FromResult<Outcome<GenerationSuccess>>(new Outcome<GenerationSuccess>.Failure(GenerationFailure.One(GenerationFailureKind.Semantic,
                "input-limit", $"The written IR has {ir.Length} bytes, more than the generation input limit of {Limits.MaxBytes} bytes.", irPath)));
        else
        {
            var original = new RetainedInput.File(irPath, new RetainedEntry(Path.GetFileName(irPath), ir.Span));
            var loaded = GenerationModelLoader.Decode(original, input, Limits, ct);
            run = loaded switch
            {
                Outcome<LoadedModel>.Success model => GenerationStages.RunFrom(model.Value, generationSettings, backends, gate, ct, logger),
                Outcome<LoadedModel>.Failure failed => Task.FromResult<Outcome<GenerationSuccess>>(new Outcome<GenerationSuccess>.Failure(failed.Error)),
                _ => throw new System.Diagnostics.UnreachableException()
            };
        }

        try
        {
            // The generate stage ends when the stages reach publication, or when they end without it.
            var first = await Task.WhenAny(gate.Reached.Task, run);
            if (first != gate.Reached.Task)
            {
                var ended = await run;
                foreach (var e in Report(CompileStage.Generate, ended, [])) yield return e;
                yield return Conclude(ended, target, compiled, settings);
                yield break;
            }

            var artifacts = await gate.Reached.Task;
            foreach (var e in Diagnostics(CompileStage.Generate, artifacts.Diagnostics)) yield return e;
            yield return new CompileEvent.StageCompleted(CompileStage.Generate, true);

            yield return new CompileEvent.StageStarted(CompileStage.GeneratePublish);
            gate.Proceed.TrySetResult();
            var outcome = await run;
            foreach (var e in Report(CompileStage.GeneratePublish, outcome, artifacts.Diagnostics)) yield return e;
            yield return Conclude(outcome, target, compiled, settings);
        }
        finally
        {
            // A consumer that stops reading never lets publication start.
            gate.Proceed.TrySetCanceled();
        }
    }

    /// <summary>The diagnostics and completion of a stage. Diagnostics already reported by the generate stage are not repeated.</summary>
    private static IEnumerable<CompileEvent> Report(CompileStage stage, Outcome<GenerationSuccess> outcome, ImmutableArray<GenerationDiagnostic> reported)
    {
        var (passed, diagnostics) = outcome switch
        {
            Outcome<GenerationSuccess>.Success => (true, ImmutableArray<GenerationDiagnostic>.Empty),
            Outcome<GenerationSuccess>.Failure failure => (false, failure.Error.Diagnostics.Where(d => !reported.Contains(d)).ToImmutableArray()),
            _ => throw new System.Diagnostics.UnreachableException()
        };
        foreach (var e in Diagnostics(stage, diagnostics)) yield return e;
        yield return new CompileEvent.StageCompleted(stage, passed);
    }

    private static IEnumerable<CompileEvent> Diagnostics(CompileStage stage, ImmutableArray<GenerationDiagnostic> diagnostics)
    {
        if (diagnostics.IsDefaultOrEmpty) yield break;
        var converted = diagnostics.Select(Convert).ToImmutableArray();
        foreach (var group in converted.GroupBy(diagnostic => diagnostic.LocationOrNull?.Uri))
            yield return new CompileEvent.DiagnosticsReported(stage, group.Key, [.. group]);
    }

    private static CompileEvent.Completed Conclude(Outcome<GenerationSuccess> outcome, CompileTarget target, CompileSuccess compiled, CompileSettings settings) =>
        outcome switch
        {
            Outcome<GenerationSuccess>.Success success => new(new CompileConclusion.Succeeded(compiled with
            {
                Generation = new CompileGeneration(success.Value.Target, success.Value.Backend, target.OutputDirectory, success.Value)
            }), settings),
            Outcome<GenerationSuccess>.Failure failure => new(new CompileConclusion.Failed(new CompileFailure(Kind(failure.Error.Kind),
                failure.Error.Diagnostics.FirstOrDefault(d => d.Severity == "error")?.Message ?? "Generation failed.")
            {
                Sources = compiled.Sources,
                Frontend = compiled.Frontend,
                Compiled = compiled,
                Generation = new CompileGeneration(target.Target.Text, failure.Error.Backend, target.OutputDirectory, null)
            }), settings),
            _ => throw new System.Diagnostics.UnreachableException()
        };

    /// <summary>The compile failure kind of a generation failure. The exit codes are those of <c>generate</c>.</summary>
    public static CompileFailureKind Kind(GenerationFailureKind kind) => kind switch
    {
        GenerationFailureKind.Semantic => CompileFailureKind.Source,
        GenerationFailureKind.Invocation => CompileFailureKind.Invocation,
        GenerationFailureKind.Operational => CompileFailureKind.Operational,
        GenerationFailureKind.Cancelled => CompileFailureKind.Cancelled,
        _ => throw new System.Diagnostics.UnreachableException()
    };

    /// <summary>
    /// A generation diagnostic in the compile (MEP) shape. The code and message are kept. The file path and the
    /// JSON location of the generation diagnostic go to <c>data</c>; a source range goes to the location.
    /// </summary>
    public static Diagnostic Convert(GenerationDiagnostic diagnostic)
    {
        var severity = diagnostic.Severity switch
        {
            "warning" => Severity.Warning,
            "info" => Severity.Info,
            "hint" => Severity.Hint,
            _ => Severity.Error
        };
        DiagnosticCode.TryParse(diagnostic.Code, out var code, out _);
        var location = diagnostic is { Range: { } range, Path: { } path }
            ? Location.Create(path, Morphir.Compilation.Range.Create(Position.Create(range.Start.Line, range.Start.Character),
                Position.Create(range.End.Line, range.End.Character)))
            : null;
        var created = Diagnostic.Create(severity, code, string.IsNullOrEmpty(diagnostic.Message) ? diagnostic.Code : diagnostic.Message, location);
        if (diagnostic.Path is null && diagnostic.Location is null && code is not null) return created;
        return created.WithData(Data(diagnostic, code is null));
    }

    private static JsonElement Data(GenerationDiagnostic diagnostic, bool keepCode)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (keepCode) writer.WriteString("code", diagnostic.Code);
            if (diagnostic.Path is { } path) writer.WriteString("path", path);
            if (diagnostic.Location is { } location) writer.WriteString("location", location);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// A publisher that signals when the generation stages reach publication and then waits until the compile
    /// stream has reported the generate stage. It does not change what is published.
    /// </summary>
    private sealed class StagedPublisher(IGenerationPublisher inner) : IGenerationPublisher
    {
        public TaskCompletionSource<GenerationArtifacts> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Proceed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<Outcome<GenerationPublication>> PublishAsync(GenerationArtifacts artifacts, ModelInput input, PublicationPolicy policy,
            CancellationToken ct)
        {
            Reached.TrySetResult(artifacts);
            await Proceed.Task.WaitAsync(ct);
            return await inner.PublishAsync(artifacts, input, policy, ct);
        }
    }
}
