// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using Morphir.Compilation;
using Morphir.Files;
using Morphir.IR.Trees;
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
/// <param name="ProjectFile">The absolute project file that supplied the sources, or null.</param>
/// <param name="Ir">The written IR, in the layout of the settings, with its format version.</param>
public sealed record CompilePublishRequest(string Language, string? ProjectFile, CompileIrOutput Ir);

/// <summary>
/// The written IR of one compile, in one of its layouts: one file or a JSON document tree. Each case has the format
/// version that its files declare, which the record names.
/// </summary>
public abstract record CompileIrOutput
{
    private CompileIrOutput(FormatVersion version) => Version = version;

    /// <summary>The format version that the files of the IR declare.</summary>
    public FormatVersion Version { get; }

    /// <summary>The exact bytes that the writer wrote, published as <c>compile.dest/morphir-ir.json</c>.</summary>
    public sealed record SingleFile(FormatVersion Version, ReadOnlyMemory<byte> Bytes) : CompileIrOutput(Version);

    /// <summary>
    /// The files of a document tree (<c>DocumentTreeStore.Write</c>), published as <c>compile.dest/morphir-ir</c>: an
    /// IR 4 tree declares the version of the writer, and a classic tree declares <c>3.1.0</c> in every file.
    /// </summary>
    public sealed record DocumentTree(FormatVersion Version, VFileTree Tree) : CompileIrOutput(Version);

    /// <summary>Calls the function for this layout.</summary>
    public TResult Match<TResult>(Func<SingleFile, TResult> singleFile, Func<DocumentTree, TResult> documentTree) => this switch
    {
        SingleFile file => singleFile(file),
        DocumentTree tree => documentTree(tree),
        _ => throw new System.Diagnostics.UnreachableException(),
    };
}

/// <summary>Splits the written single-file IR into the document tree of the same model (IR 3 and IR 4).</summary>
internal static class CompileDocumentTree
{
    /// <summary>The depth of JSON that the written IR may have; the codecs guard their own recursion.</summary>
    private const int MaxDepth = 4096;

    /// <summary>The version that every file of a classic tree declares (the parent's v3 tree specification).</summary>
    private static readonly FormatVersion ClassicTreeVersion = Morphir.IR.Versioning.parse("3.1.0").ResultValue;

    /// <summary>
    /// The tree of the model that <paramref name="written"/> holds: the bytes that the writer of <paramref name="version"/>
    /// wrote. The model is read back with the single-file codecs, so the tree holds exactly the written model, and is split
    /// with <c>V4DocumentTree.split</c> or <c>ClassicDocumentTree.split</c>. A refusal gives one
    /// <see cref="CompileCodes.TreeWriteFailed"/> error for each problem.
    /// </summary>
    public static Checked<CompileIrOutput> Split(ReadOnlyMemory<byte> written, FormatVersion version)
    {
        using var document = System.Text.Json.JsonDocument.Parse(written, new System.Text.Json.JsonDocumentOptions { MaxDepth = MaxDepth });
        if (!StoredModel.TryReadSingleFile(document.RootElement, out var model, out var problems)
            || !model.TryConvert(ModelLayout.DocumentTree, out var tree, out problems)
            || !StoredModel.TryGetWritten(DocumentTreeStore.Write(StoredModel.WriteOptions(false), tree.Tree), out var files, out problems))
            return Checked<CompileIrOutput>.Fail(problems.Select(problem => Diagnostic.Create(Severity.Error, CompileCodes.TreeWriteFailed,
                $"The written IR could not be split into a document tree: {problem}", null)));
        return Checked<CompileIrOutput>.Pass(new CompileIrOutput.DocumentTree(tree.IrVersion == "3" ? ClassicTreeVersion : version, files));
    }
}

/// <summary>A started compile run that holds the task's exclusive lock until it is disposed.</summary>
public interface ICompilePublication : IDisposable
{
    /// <summary>Writes the IR and the completed record, then installs the IR to <c>-o</c> when it is set.</summary>
    Task<Checked<PublishedCompile>> PublishAsync(CompilePublishRequest request, CancellationToken ct);
}

/// <summary>
/// Publishes through the compile task record store (REC-1): <c>&lt;out&gt;/[&lt;member&gt;/]compile.dest/morphir-ir.json</c>
/// (or the document tree <c>compile.dest/morphir-ir</c>),
/// the schema-1 record <c>compile.json</c> beside it, and the <c>-o</c> install through the <c>installed</c> ledger.
/// The run holds <c>compile.lock</c> from <see cref="BeginAsync"/> until the publication is disposed, so a reader
/// such as <c>generate</c> waits for the run and then reads its IR.
/// </summary>
public sealed class TaskRecordCompilePublisher(TaskStoreOptions options) : ICompilePublisher
{
    /// <summary>The file name of the published IR in <c>compile.dest</c>.</summary>
    public const string IrFile = "morphir-ir.json";

    /// <summary>The directory name of a published document tree in <c>compile.dest</c>.</summary>
    public const string IrDirectory = "morphir-ir";

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
            var (name, layout) = request.Ir.Match(_ => (IrFile, IrLayout.SingleFile), _ => (IrDirectory, IrLayout.DocumentTree));
            var target = Path.Combine(run.Dest, name);
            string? failure;
            try
            {
                failure = await request.Ir.Match(
                    async file =>
                    {
                        await File.WriteAllBytesAsync(target, file.Bytes, ct);
                        return (string?)null;
                    },
                    tree => Task.FromResult(VFileTreeModule.MatchPublish<string?>(new PublishOptions(target, ManifestMode.NoManifest), tree.Tree,
                        _ => null, TreeWriteErrorModule.describe)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                failure = error.Message;
            }
            if (failure is not null)
                return Checked<PublishedCompile>.Fail([Diagnostic.Create(Severity.Error, CompileCodes.PublishFailed,
                    $"The IR could not be written to '{target}': {failure}", null)]);

            var ir = IrDescriptor.Create(RecordPath.Parse(name), layout, IrStorageFormat.Json, request.Ir.Version);
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
