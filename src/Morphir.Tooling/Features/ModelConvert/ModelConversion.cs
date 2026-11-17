// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.IR.Trees;

namespace Morphir.Tooling.Features.ModelConvert;

/// <summary>What <c>model convert</c> was asked for: the paths as given, the layout if given, and the compaction mode if given.</summary>
public sealed record ConvertRequest(string Input, string Output, ModelLayout? Layout, string? CompactMode);

/// <summary>The stages of a conversion, in order, with their wire names.</summary>
public static class ConvertStages
{
    /// <summary>The input is read and decoded.</summary>
    public const string Read = "read";

    /// <summary>The model is split into a tree or joined into a single file.</summary>
    public const string Convert = "convert";

    /// <summary>The output is written.</summary>
    public const string Write = "write";
}

/// <summary>
/// What a conversion reports while it runs. Each event is written before the work that follows it starts, so a
/// streaming reader sees a stage end before the next one begins.
/// </summary>
public interface IConvertObserver
{
    /// <summary>A stage starts.</summary>
    void StageStarted(string stage);

    /// <summary>
    /// Diagnostics, in a stage or, for the checks of the request before the first stage, with no stage. Together they
    /// are the report's diagnostics.
    /// </summary>
    void Diagnostics(string? stage, IReadOnlyList<ConvertDiagnostic> diagnostics);

    /// <summary>Notices of content that a stage dropped. Together they are the report's notices.</summary>
    void Notices(string stage, IReadOnlyList<ConvertDiagnostic> notices);

    /// <summary>A file of the output is in place, at its path as the report lists it.</summary>
    void FileWritten(string path);

    /// <summary>A stage ends.</summary>
    void StageCompleted(string stage, bool passed);
}

/// <summary>
/// One run of <c>model convert</c> (CLI-2): detect the input's layout, check the output, read, convert, write. The
/// output is checked before anything is read or written: <c>--output</c> that is the input, is inside it or holds it
/// is refused with <c>model.output_overlaps_input</c>, and one that already holds a file, or is a file where a
/// directory is needed or the reverse, with <c>model.output_not_empty</c>. A run that changes neither the layout nor
/// the encoding exits 2 with <c>model.nothing_to_do</c>, decided from the envelopes of the input's files before the
/// model is read: a tree with any file in an envelope is not plain, so it is read and written plain. A compacted input
/// is expanded as it is read, and the output is plain unless <c>--compact</c> is given. A
/// path that is not a path exits 2 with <c>model.invalid_arguments</c>. Every other refusal exits 1, and leaves nothing
/// at the output; a file-system failure is <c>model.read_failed</c> or <c>model.write_failed</c>, so every run ends with
/// a report.
/// </summary>
public sealed class ModelConversion(IModelConversionStore store)
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Runs <paramref name="request"/>, telling <paramref name="observer"/> what happens, and returns the report.</summary>
    public async Task<ConvertReport> RunAsync(ConvertRequest request, IConvertObserver observer, CancellationToken ct)
    {
        var state = new State(request);
        try
        {
            return await RunStagesAsync(state, observer, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            var cancelled = ConvertDiagnostic.Failure(ModelConvertCodes.Cancelled, "The conversion was cancelled; nothing was written to the output.", null);
            observer.Diagnostics(state.Stage, [cancelled]);
            return state.Report(ModelConvertCodes.CancelledExitCode, [cancelled]);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A store refuses what it can name; a file-system failure that escapes it still ends the run with a
            // report, so the stream ends with one completed record.
            var writing = state.Stage == ConvertStages.Write;
            var failure = ConvertDiagnostic.Failure(writing ? ModelConvertCodes.WriteFailed : ModelConvertCodes.ReadFailed, error.Message,
                writing ? state.Request.Output : state.Request.Input);
            return state.Stage is { } stage
                ? Fail(state, observer, stage, ModelConvertCodes.RefusedExitCode, [failure])
                : Refuse(state, observer, null, failure);
        }
    }

    private async Task<ConvertReport> RunStagesAsync(State state, IConvertObserver observer, CancellationToken ct)
    {
        var request = state.Request;
        if (InvalidPath(request.Input, "--input", out var inputPath) is { } invalidInput)
            return Usage(state, observer, invalidInput);
        if (InvalidPath(request.Output, "--output", out var outputPath) is { } invalidOutput)
            return Usage(state, observer, invalidOutput);
        ModelLayout inputLayout;
        if (File.Exists(inputPath)) inputLayout = ModelLayout.SingleFile;
        else if (Directory.Exists(inputPath)) inputLayout = ModelLayout.DocumentTree;
        else
            return Refuse(state, observer, null, ConvertDiagnostic.Failure(ModelConvertCodes.InputNotFound,
                "No file or directory is at the input.", request.Input));
        state.InputLayout = inputLayout;
        var outputLayout = request.Layout ?? inputLayout;
        state.OutputLayout = outputLayout;

        ConvertDiagnostic? outputProblem;
        try
        {
            outputProblem = OutputProblem(request, inputPath, outputPath, outputLayout);
        }
        catch (Exception error) when (DiskModelConversionStore.IsFileSystemFailure(error))
        {
            outputProblem = ConvertDiagnostic.Failure(ModelConvertCodes.WriteFailed, $"The output cannot be checked: {error.Message}", request.Output);
        }
        if (outputProblem is not null)
            return Refuse(state, observer, null, outputProblem);

        // Nothing to do is decided from the envelopes alone, before the model is decoded: a plain input that keeps its
        // layout and is not compacted would be written as it is, whether or not it decodes. A tree is plain only when
        // none of its files is in an envelope.
        var source = new ModelSource(inputPath, request.Input, inputLayout);
        if (outputLayout == inputLayout && request.CompactMode is null && !await store.HasEnvelopeAsync(source, ct))
        {
            state.InputEncoding = ModelConvertNames.Plain;
            return Usage(state, observer, ConvertDiagnostic.Failure(ModelConvertCodes.NothingToDo,
                $"The input is a plain {Describe(inputLayout)} and the output would be the same. Give --layout={ModelConvertNames.Of(Other(inputLayout))} to change the layout, or --compact to compact it.",
                request.Input));
        }

        // Read.
        state.Stage = ConvertStages.Read;
        observer.StageStarted(ConvertStages.Read);
        var read = await store.ReadAsync(source, ct);
        state.InputEncoding = read.Encoding;
        state.Notices.AddRange(read.Notices);
        if (read.Notices.Count > 0) observer.Notices(ConvertStages.Read, read.Notices);
        if (read.Model is not { } model)
            return Fail(state, observer, ConvertStages.Read, ModelConvertCodes.RefusedExitCode, read.Diagnostics);
        state.IrVersion = model.IrVersion;
        observer.StageCompleted(ConvertStages.Read, true);
        ct.ThrowIfCancellationRequested();

        // Convert.
        state.Stage = ConvertStages.Convert;
        observer.StageStarted(ConvertStages.Convert);
        if (!model.TryConvert(outputLayout, out var converted, out var problems))
            return Fail(state, observer, ConvertStages.Convert, ModelConvertCodes.RefusedExitCode,
                [.. problems.Select(problem => ConvertDiagnostic.Of(problem, request.Input))]);
        observer.StageCompleted(ConvertStages.Convert, true);
        ct.ThrowIfCancellationRequested();

        // Write.
        state.Stage = ConvertStages.Write;
        observer.StageStarted(ConvertStages.Write);
        var written = await store.WriteAsync(converted, new ModelTarget(outputPath, request.Output, outputLayout, request.CompactMode is not null), ct);
        if (written.Diagnostics.Count > 0)
            return Fail(state, observer, ConvertStages.Write, ModelConvertCodes.RefusedExitCode, written.Diagnostics);
        IReadOnlyList<string> files = outputLayout == ModelLayout.DocumentTree
            ? [.. written.Files.Select(file => ModelConvertPaths.Join(request.Output, file))]
            : [request.Output];
        foreach (var file in files) observer.FileWritten(file);
        state.FilesWritten.AddRange(files);
        observer.StageCompleted(ConvertStages.Write, true);
        return state.Report(0, []);
    }

    /// <summary>
    /// Why the output cannot be written, before anything is read: it overlaps the input, or it already holds content,
    /// or it is a file where a tree needs a directory, or a directory where a single file is written.
    /// </summary>
    private static ConvertDiagnostic? OutputProblem(ConvertRequest request, string input, string output, ModelLayout layout)
    {
        if (SameOrInside(output, input) || SameOrInside(input, output))
            return ConvertDiagnostic.Failure(ModelConvertCodes.OutputOverlapsInput,
                $"The output is the input, is inside it, or holds it; give an output outside the input '{request.Input}'.", request.Output);
        if (layout == ModelLayout.DocumentTree)
        {
            if (File.Exists(output))
                return ConvertDiagnostic.Failure(ModelConvertCodes.OutputNotEmpty,
                    "The output is a file; a document tree is written to a directory that does not exist or is empty.", request.Output);
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                return ConvertDiagnostic.Failure(ModelConvertCodes.OutputNotEmpty,
                    "The output directory already holds files; give a directory that does not exist or is empty.", request.Output);
            return null;
        }
        if (Directory.Exists(output))
            return ConvertDiagnostic.Failure(ModelConvertCodes.OutputNotEmpty,
                "The output is a directory; a single-file model is written to a file that does not exist.", request.Output);
        if (File.Exists(output))
            return ConvertDiagnostic.Failure(ModelConvertCodes.OutputNotEmpty,
                "The output file already exists; give a file that does not exist.", request.Output);
        return null;
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="other"/> or inside it.</summary>
    private static bool SameOrInside(string path, string other)
    {
        var a = Path.TrimEndingDirectorySeparator(path);
        var b = Path.TrimEndingDirectorySeparator(other);
        return a.Equals(b, PathComparison)
            || a.StartsWith(b + Path.DirectorySeparatorChar, PathComparison)
            || a.StartsWith(b + Path.AltDirectorySeparatorChar, PathComparison);
    }

    private static string Describe(ModelLayout layout) => layout == ModelLayout.DocumentTree ? "document tree" : "single file";

    private static ModelLayout Other(ModelLayout layout) =>
        layout == ModelLayout.DocumentTree ? ModelLayout.SingleFile : ModelLayout.DocumentTree;

    /// <summary>
    /// Why <paramref name="given"/>, the value of <paramref name="option"/>, is not a path, or null with its full path.
    /// </summary>
    private static ConvertDiagnostic? InvalidPath(string given, string option, out string fullPath)
    {
        try
        {
            fullPath = Path.GetFullPath(given);
            return null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            fullPath = "";
            return ConvertDiagnostic.Failure(ModelConvertCodes.InvalidArguments, $"The option {option} is not a valid path: {error.Message}", null);
        }
    }

    /// <summary>A refusal of the request before any stage (exit code 2).</summary>
    private static ConvertReport Usage(State state, IConvertObserver observer, ConvertDiagnostic diagnostic)
    {
        observer.Diagnostics(null, [diagnostic]);
        return state.Report(ModelConvertCodes.UsageExitCode, [diagnostic]);
    }

    private static ConvertReport Refuse(State state, IConvertObserver observer, string? stage, ConvertDiagnostic diagnostic)
    {
        observer.Diagnostics(stage, [diagnostic]);
        return state.Report(ModelConvertCodes.RefusedExitCode, [diagnostic]);
    }

    private static ConvertReport Fail(State state, IConvertObserver observer, string stage, int exitCode, IReadOnlyList<ConvertDiagnostic> diagnostics)
    {
        if (diagnostics.Count > 0) observer.Diagnostics(stage, diagnostics);
        observer.StageCompleted(stage, false);
        return state.Report(exitCode, diagnostics);
    }

    /// <summary>What the run has found so far, for its report.</summary>
    private sealed class State(ConvertRequest request)
    {
        public ConvertRequest Request { get; } = request;
        public string? Stage { get; set; }
        public ModelLayout? InputLayout { get; set; }
        public ModelLayout? OutputLayout { get; set; }
        public string? InputEncoding { get; set; }
        public string? IrVersion { get; set; }
        public List<ConvertDiagnostic> Notices { get; } = [];
        public List<string> FilesWritten { get; } = [];

        public ConvertReport Report(int exitCode, IReadOnlyList<ConvertDiagnostic> diagnostics) => new(
            exitCode == 0, exitCode, Request.Input, Request.Output,
            InputLayout is { } input ? ModelConvertNames.Of(input) : null,
            OutputLayout is { } output ? ModelConvertNames.Of(output) : null,
            InputEncoding,
            OutputLayout is null ? null : Request.CompactMode is null ? ModelConvertNames.Plain : ModelConvertNames.Compacted,
            IrVersion,
            Request.CompactMode,
            // The policy that writes compacted files comes from the parent with compacted writing (add-model-convert 8.3).
            null,
            [.. FilesWritten], [.. diagnostics], [.. Notices]);
    }
}
