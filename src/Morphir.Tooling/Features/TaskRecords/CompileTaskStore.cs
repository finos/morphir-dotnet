// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Features.TaskRecords;

/// <summary>The effects that the task record store uses. Tests replace them.</summary>
public sealed record TaskStoreOptions
{
    /// <summary>Receives the waiting line and warnings. The default is stderr; nothing goes to stdout.</summary>
    public TextWriter Diagnostics { get; init; } = Console.Error;

    /// <summary>The clock for <c>completedAt</c>.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>
    /// The Morphir home (<c>MORPHIR_HOME</c>, else <c>~/.morphir</c>), which holds the install locks. When it is
    /// null, the install lock is below the out root, and install says so on the diagnostics writer.
    /// </summary>
    public string? MorphirHome { get; init; }

    /// <summary>How often a waiting run tries the lock again.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>For tests: runs before each attempt to replace the record of a completed compile with its staged record.</summary>
    internal Action? BeforeRecordReplace { get; init; }

    /// <summary>For tests: clears <c>.dest</c> when a run begins, in place of <see cref="PathRemoval.Default"/>.</summary>
    internal PathRemoval? Removal { get; init; }
}

/// <summary>What a successful compile produced.</summary>
/// <param name="Language">The source language, for example <c>fsharp</c>.</param>
/// <param name="Ir">Where the IR is in <c>.dest</c>. The IR is the compile's whole product (<c>value</c>).</param>
/// <param name="ProjectFile">The absolute project file that supplied the sources, or null.</param>
public sealed record CompileProduct(string Language, IrDescriptor Ir, string? ProjectFile);

/// <summary>
/// The compile task's out-directory files for one workspace member (REC-1): <c>compile.dest</c>,
/// <c>compile.json</c> and <c>compile.lock</c>. A compile starts a run with <see cref="BeginAsync"/>; a reader such as
/// <c>generate</c> opens the completed output with <see cref="OpenCompletedAsync"/>.
/// </summary>
public sealed class CompileTaskStore
{
    private readonly TaskStoreOptions _options;

    /// <summary>A store for the compile task of <paramref name="member"/> below <paramref name="root"/>.</summary>
    public CompileTaskStore(OutRoot root, MemberPath member, TaskStoreOptions? options = null)
    {
        Location = root.Task(member, TaskId.Compile);
        _options = options ?? new TaskStoreOptions();
    }

    /// <summary>The task's files.</summary>
    public TaskLocation Location { get; }

    /// <summary>
    /// Starts a compile run. The run takes the exclusive lock, replaces a readable earlier record with a
    /// tombstone (an unreadable one is removed, with one warning line), and clears <c>.dest</c>. The caller writes
    /// the IR into <see cref="CompileTaskRun.Dest"/>, calls <see cref="CompileTaskRun.Complete"/> on success, and
    /// disposes the run to release the lock.
    /// </summary>
    public async ValueTask<Outcome<CompileTaskRun>> BeginAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return TaskFailures.Cancelled<CompileTaskRun>();
        TaskLock taskLock;
        try
        {
            taskLock = await TaskLock.AcquireAsync(Location.Lock, TaskLockMode.Exclusive, "this task", _options.Diagnostics, _options.PollInterval, ct);
        }
        catch (OperationCanceledException)
        {
            return TaskFailures.Cancelled<CompileTaskRun>();
        }
        catch (Exception error) when (TaskFailures.IsIo(error))
        {
            return TaskFailures.Io<CompileTaskRun>("compile.lock_failed", "lock", Location.Lock, error);
        }

        try
        {
            var previous = ImmutableSortedDictionary.Create<string, ImmutableArray<RecordPath>>(StringComparer.Ordinal);
            switch (RecordFile.Read(Location.Record))
            {
                case RecordReading.Found found:
                    previous = found.Record.Installed;
                    // The tombstone goes first: if the run stops here, the earlier record still describes .dest.
                    RecordFile.Write(Location.Record, found.Record.ToTombstone());
                    break;
                case RecordReading.Unreadable unreadable:
                    await _options.Diagnostics.WriteLineAsync($"warning: could not read previous task record at {Location.Record}: {unreadable.Reason}");
                    File.Delete(Location.Record);
                    break;
            }
            // A run that crashed can leave a staged IR that a scanner still holds: the removal retries a bounded time, and a
            // cancellation during its waits ends the run before publication (exit code 130).
            ct.ThrowIfCancellationRequested();
            if (!(_options.Removal ?? PathRemoval.Default).TryRemove(Location.Dest, ct))
            {
                taskLock.Dispose();
                return TaskFailures.Io<CompileTaskRun>("compile.prepare_failed", "clear", Location.Dest, new IOException(
                    $"another handle still holds a file below it after {Morphir.Files.FileReplacement.Attempts} attempts. Close the program that holds it and compile again"));
            }
            Directory.CreateDirectory(Location.Dest);
            return new Outcome<CompileTaskRun>.Success(new CompileTaskRun(Location, previous, taskLock, _options));
        }
        catch (OperationCanceledException)
        {
            taskLock.Dispose();
            return TaskFailures.Cancelled<CompileTaskRun>();
        }
        catch (Exception error) when (TaskFailures.IsIo(error))
        {
            taskLock.Dispose();
            return TaskFailures.Io<CompileTaskRun>("compile.prepare_failed", "prepare", Location.Dest, error);
        }
    }

    /// <summary>
    /// Opens the output of the most recent successful compile. The reader holds the shared lock until it is
    /// disposed, so a compile cannot clear <c>.dest</c> while the IR is read. A missing record and a tombstone
    /// fail the same way: the user must run <c>morphir compile</c> first.
    /// </summary>
    public async ValueTask<Outcome<CompileTaskOutput>> OpenCompletedAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return TaskFailures.Cancelled<CompileTaskOutput>();
        if (!Directory.Exists(Path.GetDirectoryName(Location.Record)))
            return Missing("There is no compile record at " + Location.Record + ".");

        TaskLock taskLock;
        try
        {
            taskLock = await TaskLock.AcquireAsync(Location.Lock, TaskLockMode.Shared, "this task", _options.Diagnostics, _options.PollInterval, ct);
        }
        catch (OperationCanceledException)
        {
            return TaskFailures.Cancelled<CompileTaskOutput>();
        }
        catch (Exception error) when (TaskFailures.IsIo(error))
        {
            return TaskFailures.Io<CompileTaskOutput>("compile.lock_failed", "lock", Location.Lock, error);
        }

        Outcome<CompileTaskOutput> Release(Outcome<CompileTaskOutput> failure)
        {
            taskLock.Dispose();
            return failure;
        }

        switch (RecordFile.Read(Location.Record))
        {
            case RecordReading.Missing:
                return Release(Missing("There is no compile record at " + Location.Record + "."));
            case RecordReading.Unreadable unreadable:
                return Release(TaskFailures.Of<CompileTaskOutput>(GenerationFailureKind.Operational, "compile.record_unreadable",
                    $"The compile record at {Location.Record} could not be read: {unreadable.Reason}", Location.Record));
            case RecordReading.Found { Record.Tombstone: true }:
                return Release(Missing("The most recent compile did not complete, so there is no compiled IR at " + Location.Dest + "."));
            case RecordReading.Found { Record.Ir: null }:
                return Release(TaskFailures.Of<CompileTaskOutput>(GenerationFailureKind.Invocation, "compile.record_without_ir",
                    $"The compile record at {Location.Record} produced no IR descriptor.", Location.Record));
            case RecordReading.Found found:
                return new Outcome<CompileTaskOutput>.Success(new CompileTaskOutput(Location, found.Record, found.Record.Ir!, taskLock));
            default:
                return Release(Missing("There is no compile record at " + Location.Record + "."));
        }

        Outcome<CompileTaskOutput> Missing(string reason) =>
            TaskFailures.Of<CompileTaskOutput>(GenerationFailureKind.Invocation, "compile.record_missing",
                reason + " Run `morphir compile` first, or give --input.", Location.Record);
    }
}

/// <summary>
/// A compile run that holds the exclusive lock on <c>compile.lock</c>. Dispose it after the record and any
/// install are written. A run that is disposed without <see cref="Complete"/> leaves the tombstone.
/// </summary>
public sealed class CompileTaskRun : IDisposable
{
    private readonly TaskLock _lock;
    private readonly TaskStoreOptions _options;
    private bool _disposed;

    internal CompileTaskRun(TaskLocation location, ImmutableSortedDictionary<string, ImmutableArray<RecordPath>> previousInstalled,
        TaskLock taskLock, TaskStoreOptions options)
    {
        Location = location;
        PreviousInstalled = previousInstalled;
        _lock = taskLock;
        _options = options;
    }

    /// <summary>The task's files.</summary>
    public TaskLocation Location { get; }

    /// <summary>The empty <c>.dest</c> directory. The compile writes its IR here.</summary>
    public string Dest => Location.Dest;

    /// <summary>The install ledger of the earlier record. <see cref="Complete"/> carries it forward.</summary>
    public ImmutableSortedDictionary<string, ImmutableArray<RecordPath>> PreviousInstalled { get; }

    /// <summary>
    /// Writes the full record of a successful compile. It replaces the tombstone, keeps the earlier
    /// <c>installed</c> ledger, and lists the project file in <c>inputs</c>, relative to the workspace root.
    /// </summary>
    public Outcome<TaskRecord> Complete(CompileProduct product) => Complete(product, CancellationToken.None);

    /// <summary>
    /// Writes the full record of a successful compile, as <see cref="Complete(CompileProduct)"/> does. A cancellation of
    /// <paramref name="ct"/> before the staged record replaces the tombstone throws <see cref="OperationCanceledException"/>
    /// and leaves the tombstone; it is checked again before each attempt of the replacement.
    /// </summary>
    public Outcome<TaskRecord> Complete(CompileProduct product, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var record = new TaskRecord
        {
            Task = Location.Task,
            Module = Location.Member,
            Language = product.Language,
            Inputs = product.ProjectFile is { } projectFile ? [InputPath(projectFile)] : [],
            Value = [product.Ir.Path],
            Ir = product.Ir,
            Installed = PreviousInstalled,
            CompletedAt = RecordTimestamp.At(_options.Time.GetUtcNow()),
        };
        try
        {
            RecordFile.Write(Location.Record, record, afterWait: null, ct, _options.BeforeRecordReplace);
            return new Outcome<TaskRecord>.Success(record);
        }
        catch (Exception error) when (TaskFailures.IsIo(error))
        {
            return TaskFailures.Io<TaskRecord>("compile.record_write_failed", "write", Location.Record, error);
        }
    }

    /// <summary>
    /// Installs the completed record's <c>value</c> into <paramref name="target"/> (<c>-o</c>) and updates the
    /// <c>installed</c> ledger. Relative targets are relative to the current directory; callers resolve them first.
    /// </summary>
    public ValueTask<Outcome<InstallReport>> InstallAsync(string target, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return TaskInstaller.InstallAsync(Location, target, _options, ct);
    }

    private string InputPath(string projectFile)
    {
        var full = Path.GetFullPath(projectFile);
        var relative = Path.GetRelativePath(Location.Root.WorkspaceRoot, full).Replace('\\', '/');
        return relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? full.Replace('\\', '/')
            : relative;
    }

    /// <summary>Releases the lock.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
    }
}

/// <summary>
/// The output of the most recent successful compile, with the shared lock held. Dispose it after the IR is read.
/// </summary>
public sealed class CompileTaskOutput : IDisposable
{
    private readonly TaskLock _lock;

    internal CompileTaskOutput(TaskLocation location, TaskRecord record, IrDescriptor ir, TaskLock taskLock)
    {
        Location = location;
        Record = record;
        Ir = ir;
        _lock = taskLock;
    }

    /// <summary>The task's files.</summary>
    public TaskLocation Location { get; }

    /// <summary>The completed record.</summary>
    public TaskRecord Record { get; }

    /// <summary>The record's IR descriptor.</summary>
    public IrDescriptor Ir { get; }

    /// <summary>The absolute IR file or document-tree directory inside <c>.dest</c>.</summary>
    public string IrPath => Ir.Path.Under(Location.Dest);

    /// <summary>Releases the shared lock.</summary>
    public void Dispose() => _lock.Dispose();
}
