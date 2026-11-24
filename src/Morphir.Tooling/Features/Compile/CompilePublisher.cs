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
/// <param name="IrVersion">The format version of the written IR.</param>
/// <param name="ProjectFile">The absolute project file that supplied the sources, or null.</param>
/// <param name="Ir">The exact bytes that the writer wrote.</param>
/// <remarks>
/// A request from the constructor publishes <paramref name="Ir"/> as <c>compile.dest/morphir-ir.json</c>. A request from
/// <see cref="ForDocumentTree"/> publishes its <see cref="DocumentTree"/> as <c>compile.dest/morphir-ir</c> instead, and
/// the record names the version that the files of the tree declare.
/// </remarks>
public sealed record CompilePublishRequest(string Language, FormatVersion IrVersion, string? ProjectFile, ReadOnlyMemory<byte> Ir)
{
    /// <summary>
    /// The document tree to publish in place of the single file, or null for a single-file compile. Only
    /// <see cref="ForDocumentTree"/> sets it.
    /// </summary>
    public DocumentTreeStore.WrittenTree? DocumentTree { get; private init; }

    /// <summary>
    /// A request that publishes <paramref name="tree"/>: the document tree of the model that <paramref name="ir"/>, the bytes
    /// that the writer of <paramref name="irVersion"/> wrote, holds.
    /// </summary>
    public static CompilePublishRequest ForDocumentTree(string language, FormatVersion irVersion, string? projectFile,
        ReadOnlyMemory<byte> ir, DocumentTreeStore.WrittenTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        return new(language, irVersion, projectFile, ir) { DocumentTree = tree };
    }
}

/// <summary>Writes the single-file IR of a compile as the document tree of the same model (IR 3 and IR 4).</summary>
internal static class CompileDocumentTree
{
    /// <summary>The depth of JSON that the written IR may have; the codecs guard their own recursion.</summary>
    private const int MaxDepth = 4096;

    /// <summary>
    /// The tree of the model that <paramref name="written"/>, the bytes that the writer wrote, holds. The model is read back
    /// with the single-file codecs, so the tree holds exactly the written model, split and written by
    /// <see cref="StoredModel.TryWriteTree"/> with the version that its files declare. A refusal gives
    /// one <see cref="CompileCodes.TreeWriteFailed"/> error for each problem.
    /// </summary>
    public static Checked<DocumentTreeStore.WrittenTree> Write(ReadOnlyMemory<byte> written)
    {
        using var document = System.Text.Json.JsonDocument.Parse(written, new System.Text.Json.JsonDocumentOptions { MaxDepth = MaxDepth });
        if (StoredModel.TryReadSingleFile(document.RootElement, out var model, out var problems)
            && model.TryWriteTree(false, out var tree, out problems))
            return Checked<DocumentTreeStore.WrittenTree>.Pass(tree);
        return Checked<DocumentTreeStore.WrittenTree>.Fail(problems.Select(problem => Diagnostic.Create(Severity.Error, CompileCodes.TreeWriteFailed,
            $"The written IR could not be split into a document tree: {problem}", null)));
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
/// <remarks>
/// The IR is staged in a sibling of its place in <c>compile.dest</c> and moved into place whole, then the record is
/// written. A write failure, a cancellation or a record failure before the record is complete removes the staged and the
/// moved IR, so <c>compile.dest</c> holds no partial IR and the tombstone of the run stays.
/// </remarks>
public sealed class TaskRecordCompilePublisher : ICompilePublisher
{
    /// <summary>The file name of the published IR in <c>compile.dest</c>.</summary>
    public const string IrFile = "morphir-ir.json";

    /// <summary>The directory name of a published document tree in <c>compile.dest</c>.</summary>
    public const string IrDirectory = "morphir-ir";

    private readonly TaskStoreOptions _options;
    private readonly CompilePublicationHooks _hooks;

    /// <summary>A publisher with the given task store options.</summary>
    public TaskRecordCompilePublisher(TaskStoreOptions options) : this(options, CompilePublicationHooks.None) { }

    /// <summary>A publisher whose waiting line and warnings go to stderr.</summary>
    public TaskRecordCompilePublisher() : this(new TaskStoreOptions()) { }

    /// <summary>For tests: a publisher whose publications run <paramref name="hooks"/>.</summary>
    internal TaskRecordCompilePublisher(TaskStoreOptions options, CompilePublicationHooks hooks) =>
        (_options, _hooks) = (options, hooks);

    /// <inheritdoc />
    public async Task<Checked<ICompilePublication>> BeginAsync(CompileSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var place = new CompileTaskPlace(settings.OutRoot, settings.Member, settings.MorphirHome);
        return await place.Store(_options).BeginAsync(ct) switch
        {
            Outcome<CompileTaskRun>.Success started => Checked<ICompilePublication>.Pass(new Publication(started.Value, settings, _hooks, _options.Diagnostics)),
            Outcome<CompileTaskRun>.Failure failed => Checked<ICompilePublication>.Fail(TaskDiagnostics.Of(failed.Error, ct)),
            _ => throw new System.Diagnostics.UnreachableException(),
        };
    }

    private sealed class Publication(CompileTaskRun run, CompileSettings settings, CompilePublicationHooks hooks, TextWriter warnings)
        : ICompilePublication
    {
        public async Task<Checked<PublishedCompile>> PublishAsync(CompilePublishRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var tree = request.DocumentTree;
            var name = tree is null ? IrFile : IrDirectory;
            var target = Path.Combine(run.Dest, name);
            // One staging root for every run: the run holds compile.lock, so no other run stages here, and the tree
            // publication of Morphir.Files keeps one lock file for this root instead of one for each run.
            var staging = Path.Combine(run.Dest, $".{name}.staging");
            var removal = hooks.Removal ?? PathRemoval.Default;
            // The IR is staged only into an absent root: a root that a crashed run left and that cannot be cleared refuses it.
            if (!removal.TryRemove(staging, out var notCleared, ct))
            {
                // The run still holds compile.lock, so the warning reads compile.json as this run left it.
                CleanupWarnings.Write(warnings, [new CleanupRefusal(staging, notCleared)], run.Location.Record, "before the publication");
                return Checked<PublishedCompile>.Fail([Diagnostic.Create(Severity.Error, CompileCodes.PublishFailed,
                    $"The IR could not be written to '{target}': the staging path '{staging}' could not be cleared: {notCleared.Message}", null)]);
            }
            // Only what this attempt moved into place is rolled back: a target that was there before stays.
            var moved = false;
            var recorded = false;
            // The failure of an uncommitted publication waits for the rollback, which a cancellation can still overtake.
            Checked<PublishedCompile>? refused = null;
            var rolledBack = false;

            // The run holds compile.lock: no reader sees the IR of a run whose record is not complete. The removals retry
            // while another handle holds a file and never throw, so the failure or the cancellation of the run stands. A
            // path that the file system still refuses to remove is named in a warning with the reason of the refusal, and
            // the next compile clears compile.dest.
            void RollBack()
            {
                rolledBack = true;
                var paths = moved && !recorded ? new[] { staging, target } : [staging];
                // Every removal runs first, the staged record's included; the warnings follow, with what compile.json holds
                // then, and a warning that stderr refuses is dropped.
                var refusals = new List<CleanupRefusal>(run.TakeCleanupRefusals());
                foreach (var path in paths)
                    if (!removal.TryRemove(path, out var reason)) refusals.Add(new CleanupRefusal(path, reason));
                CleanupWarnings.Write(warnings, refusals, run.Location.Record, "after the publication failed");
            }
            try
            {
                string? failure;
                try
                {
                    if (tree is null)
                    {
                        await (hooks.StageFile ?? StageFileAsync)(staging, request.Ir, ct);
                        failure = null;
                    }
                    else failure = (hooks.StageTree ?? StageTree)(staging, tree.Files);
                    if (failure is null)
                    {
                        hooks.AfterStaged?.Invoke(staging);
                        ct.ThrowIfCancellationRequested();
                        MoveIntoPlace(staging, target, tree is not null, ct);
                        moved = true;
                        hooks.AfterMoved?.Invoke(target);
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    failure = error.Message;
                }
                // A cancellation that comes with the failure is the outcome of the run (exit code 130).
                ct.ThrowIfCancellationRequested();
                if (failure is not null)
                    refused = Checked<PublishedCompile>.Fail([Diagnostic.Create(Severity.Error, CompileCodes.PublishFailed,
                        $"The IR could not be written to '{target}': {failure}", null)]);
                else
                {
                    var ir = tree is null
                        ? IrDescriptor.Create(RecordPath.Parse(name), IrLayout.SingleFile, IrStorageFormat.Json, request.IrVersion)
                        : IrDescriptor.Create(RecordPath.Parse(name), IrLayout.DocumentTree, IrStorageFormat.Json, tree.FormatVersion);
                    if (run.Complete(new CompileProduct(request.Language, ir, request.ProjectFile), ct) is Outcome<TaskRecord>.Failure recordFailure)
                        refused = Checked<PublishedCompile>.Fail(TaskDiagnostics.Of(recordFailure.Error, ct));
                    else recorded = true;
                }
            }
            catch (Exception) when (!recorded)
            {
                // Whatever an uncommitted publication throws, its rollback runs first, and a cancellation during the
                // rollback is the outcome of the run (exit code 130).
                RollBack();
                ct.ThrowIfCancellationRequested();
                throw;
            }
            finally
            {
                if (!rolledBack) RollBack();
            }
            if (refused is not null)
            {
                // A cancellation during the rollback is the outcome of the run, not the failure that the rollback followed.
                ct.ThrowIfCancellationRequested();
                return refused;
            }

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

        private static Task StageFileAsync(string staging, ReadOnlyMemory<byte> bytes, CancellationToken ct) =>
            File.WriteAllBytesAsync(staging, bytes, ct);

        /// <summary>Writes the tree into the new directory <paramref name="staging"/>; the reason when it fails.</summary>
        private static string? StageTree(string staging, VFileTree files) =>
            VFileTreeModule.MatchPublish<string?>(new PublishOptions(staging, ManifestMode.NoManifest), files, _ => null, TreeWriteErrorModule.describe);

        /// <summary>
        /// Moves the staged IR to <paramref name="target"/>, which the cleared <c>compile.dest</c> does not hold. On Windows the
        /// move waits a bounded time while another process holds a staged file open.
        /// </summary>
        private static void MoveIntoPlace(string staging, string target, bool directory, CancellationToken ct)
        {
            Action move = directory ? () => Directory.Move(staging, target) : () => File.Move(staging, target, overwrite: false);
            // A cancellation during a wait for another handle ends the move before its next attempt.
            if (!FileReplacement.Replace(move, () =>
                {
                    ct.ThrowIfCancellationRequested();
                    return !File.Exists(target) && !Directory.Exists(target);
                }))
                throw new IOException($"Something appeared at '{target}' while the move of the IR waited for another handle.");
        }
    }
}

/// <summary>
/// Test seams of a compile publication: writers that replace the stage of the IR, and actions after the stage and after
/// the move into place. <see cref="None"/> has none.
/// </summary>
internal sealed record CompilePublicationHooks
{
    /// <summary>No seam: the publication as it runs in the product.</summary>
    public static CompilePublicationHooks None { get; } = new();

    /// <summary>Writes the staged tree in place of the tree writer: the staged path and the files; the reason of a failure, or null.</summary>
    public Func<string, VFileTree, string?>? StageTree { get; init; }

    /// <summary>Writes the staged single file in place of the file writer.</summary>
    public Func<string, ReadOnlyMemory<byte>, CancellationToken, Task>? StageFile { get; init; }

    /// <summary>Runs with the staged path, before the IR moves into place.</summary>
    public Action<string>? AfterStaged { get; init; }

    /// <summary>Runs with the target, after the IR moved into place and before the record is written.</summary>
    public Action<string>? AfterMoved { get; init; }

    /// <summary>Removes the staging root and a rolled-back IR in place of <see cref="PathRemoval.Default"/>.</summary>
    public PathRemoval? Removal { get; init; }
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
