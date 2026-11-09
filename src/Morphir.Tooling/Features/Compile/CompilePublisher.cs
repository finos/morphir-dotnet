// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using Morphir.Compilation;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.TaskRecords;
using Severity = Morphir.Compilation.DiagnosticSeverity;
using FormatVersion = Morphir.IR.Versioning.FormatVersion;

namespace Morphir.Tooling.Features.Compile;

/// <summary>Starts the publication of one compile. The runner starts it when the settings are resolved.</summary>
public interface ICompilePublisher
{
    /// <summary>
    /// Starts a compile run: it takes the task's exclusive lock, replaces the earlier record with a tombstone
    /// and clears <c>.dest</c>. Dispose the publication to release the lock.
    /// </summary>
    Task<Checked<ICompilePublication>> BeginAsync(CompileSettings settings, CancellationToken ct);
}

/// <summary>What a successful compile publishes.</summary>
/// <param name="Language">The source language, for example <c>fsharp</c>.</param>
/// <param name="IrVersion">The format version of the written IR.</param>
/// <param name="ProjectFile">The absolute project file that supplied the sources, or null.</param>
/// <param name="Ir">The exact bytes that the writer wrote.</param>
public sealed record CompilePublishRequest(string Language, FormatVersion IrVersion, string? ProjectFile, ReadOnlyMemory<byte> Ir);

/// <summary>A started compile run that holds the task's exclusive lock until it is disposed.</summary>
public interface ICompilePublication : IDisposable
{
    /// <summary>Writes the IR and the completed record, then installs the IR to <c>-o</c> when it is set.</summary>
    Task<Checked<PublishedCompile>> PublishAsync(CompilePublishRequest request, CancellationToken ct);
}

/// <summary>
/// Publishes through the compile task record store (REC-1): <c>&lt;out&gt;/[&lt;member&gt;/]compile.dest/morphir-ir.json</c>,
/// the schema-1 record <c>compile.json</c> beside it, and the <c>-o</c> install through the <c>installed</c> ledger.
/// The run holds <c>compile.lock</c> from <see cref="BeginAsync"/> until the publication is disposed, so a reader
/// such as <c>generate</c> waits for the run and then reads its IR.
/// </summary>
public sealed class TaskRecordCompilePublisher(TaskStoreOptions options) : ICompilePublisher
{
    /// <summary>The file name of the published IR in <c>compile.dest</c>.</summary>
    public const string IrFile = "morphir-ir.json";

    /// <summary>A publisher whose waiting line and warnings go to stderr.</summary>
    public TaskRecordCompilePublisher() : this(new TaskStoreOptions()) { }

    /// <inheritdoc />
    public async Task<Checked<ICompilePublication>> BeginAsync(CompileSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var place = new CompileTaskPlace(settings.OutRoot, settings.Member, settings.MorphirHome);
        return await place.Store(options).BeginAsync(ct) switch
        {
            Outcome<CompileTaskRun>.Success started => Checked<ICompilePublication>.Pass(new Publication(started.Value, settings)),
            Outcome<CompileTaskRun>.Failure failed => Checked<ICompilePublication>.Fail(TaskDiagnostics.Of(failed.Error, ct)),
            _ => throw new System.Diagnostics.UnreachableException(),
        };
    }

    private sealed class Publication(CompileTaskRun run, CompileSettings settings) : ICompilePublication
    {
        public async Task<Checked<PublishedCompile>> PublishAsync(CompilePublishRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(run.Dest, IrFile);
            try
            {
                await File.WriteAllBytesAsync(target, request.Ir, ct);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return Checked<PublishedCompile>.Fail([Diagnostic.Create(Severity.Error, CompileCodes.PublishFailed,
                    $"The IR could not be written to '{target}': {error.Message}", null)]);
            }

            var ir = IrDescriptor.Create(RecordPath.Parse(IrFile), IrLayout.SingleFile, IrStorageFormat.Json, request.IrVersion);
            if (run.Complete(new CompileProduct(request.Language, ir, request.ProjectFile)) is Outcome<TaskRecord>.Failure recordFailure)
                return Checked<PublishedCompile>.Fail(TaskDiagnostics.Of(recordFailure.Error, ct));

            if (settings.Output is not { } output) return Checked<PublishedCompile>.Pass(new PublishedCompile(target, []));
            return await run.InstallAsync(output.Value, ct) switch
            {
                Outcome<InstallReport>.Success installed => Checked<PublishedCompile>.Pass(new PublishedCompile(target,
                    [.. installed.Value.Copied.Select(entry => Path.Combine(installed.Value.Target, entry.Replace('/', Path.DirectorySeparatorChar)))])),
                Outcome<InstallReport>.Failure failed => Checked<PublishedCompile>.Fail(TaskDiagnostics.Of(failed.Error, ct)),
                _ => throw new System.Diagnostics.UnreachableException(),
            };
        }

        public void Dispose() => run.Dispose();
    }
}

/// <summary>Turns task record failures into compile diagnostics.</summary>
internal static class TaskDiagnostics
{
    /// <summary>
    /// One error for each failure diagnostic. A cancelled failure throws <see cref="OperationCanceledException"/>, so the
    /// runner reports it as a cancellation (exit code 130).
    /// </summary>
    public static ImmutableArray<Diagnostic> Of(GenerationFailure failure, CancellationToken ct)
    {
        if (failure.Kind == GenerationFailureKind.Cancelled) throw new OperationCanceledException(ct);
        return [.. failure.Diagnostics.Select(diagnostic => Diagnostic.Create(Severity.Error,
            DiagnosticCode.TryParse(diagnostic.Code, out var code, out _) ? code : CompileCodes.Failed, diagnostic.Message, null))];
    }
}
