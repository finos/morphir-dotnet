// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using Morphir.Files;
using Morphir.Mep;
using Morphir.Tooling.Features.TaskRecords;

namespace Morphir.Tooling.Features.Generate;

/// <summary>
/// One recorded generation task, held inside the caller's shared compile lease. The persistent target lock
/// covers preparation, guest settlement, stage promotion, result replacement and the independent install.
/// </summary>
internal sealed class RecordedGenerationTask : IDisposable
{
    private readonly TaskLocation _location;
    private readonly string _output;
    private readonly TaskStoreOptions _options;
    private readonly TaskLock _lock;
    private ImmutableSortedDictionary<string, ImmutableArray<RecordPath>> _installed =
        ImmutableSortedDictionary.Create<string, ImmutableArray<RecordPath>>(StringComparer.Ordinal);
    private bool _prepared;

    private RecordedGenerationTask(TaskLocation location, string output, TaskStoreOptions options, TaskLock taskLock)
    {
        _location = location;
        _output = output;
        _options = options;
        _lock = taskLock;
    }

    public static async Task<RecordedGenerationTask> OpenAsync(TaskLocation location, string output,
        TaskStoreOptions options, CancellationToken ct)
    {
        Validate(location, output);
        var taskLock = await TaskLock.AcquireAsync(location.Lock, TaskLockMode.Exclusive, "this generation task",
            options.Diagnostics, options.PollInterval, ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            Validate(location, output);
            return new(location, output, options, taskLock);
        }
        catch { taskLock.Dispose(); throw; }
    }

    // Delayed until reader/cache admission, so an admission refusal needs no stage or cleared .dest.
    public void Prepare()
    {
        if (_prepared) throw new InvalidOperationException("The generation task is already prepared.");
        Validate(_location, _output);
        RejectLinksBelow(_location.Dest);
        switch (RecordFile.Read(_location.Record))
        {
            case RecordReading.Found found:
                if (!found.Record.Task.Equals(_location.Task) || !found.Record.Module.Equals(_location.Member))
                    throw new IOException("The generation record belongs to another task or member.");
                _installed = found.Record.Installed;
                // Preserve installed and completedAt exactly; the prior success must disappear before .dest.
                RecordFile.Write(_location.Record, found.Record.ToTombstone());
                break;
            case RecordReading.Unreadable unreadable:
                _options.Diagnostics.WriteLine($"warning: could not read previous task record at {_location.Record}: {unreadable.Reason}");
                File.Delete(_location.Record);
                break;
        }
        // TaskLocation is derived from the resolved out root/member and parsed task id, never guest input.
        if (Directory.Exists(_location.Dest)) Directory.Delete(_location.Dest, recursive: true);
        Directory.CreateDirectory(_location.Dest);
        _prepared = true;
    }

    /// <summary>
    /// Called only after the validated generation and guest shutdown/disposal. Renames declared files on the
    /// same filesystem, cleans the private stage, then commits the successful record. No internal file copy.
    /// </summary>
    public TaskRecord Commit(BoundedArtifactStage stage, StagedGenerationOutput staged, CancellationToken ct)
    {
        if (!_prepared) throw new InvalidOperationException("The generation task has not been prepared.");
        var values = ImmutableArray.CreateBuilder<RecordPath>();
        try
        {
            ct.ThrowIfCancellationRequested();
            Validate(_location, _output);
            if (!Path.GetFullPath(stage.Directory).Equals(Path.GetFullPath(staged.Directory), StringComparison.Ordinal)
                || !Path.GetDirectoryName(Path.GetFullPath(stage.Directory))!.Equals(_location.Dest, StringComparison.Ordinal))
                throw new IOException("The completed stage is not a private child of this task.");
            foreach (var artifact in staged.Artifacts)
            {
                ct.ThrowIfCancellationRequested();
                var path = RecordPath.Parse(artifact.Path);
                var source = path.Under(stage.Directory);
                var destination = path.Under(_location.Dest);
                if (GenerationPublisher.ContainsPath(stage.Directory, destination))
                    throw new IOException("An artifact conflicts with the private stage directory.");
                Ordinary(source);
                Ordinary(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(source, destination); // Refuse any collision; never replace another product.
                values.Add(path);
            }
            // Cleanup must be acknowledged before a record can name any product.
            if (stage.Cleanup() is { } cleanup) throw new IOException(cleanup.Message);
            ct.ThrowIfCancellationRequested();
            var record = new TaskRecord
            {
                Task = _location.Task,
                Module = _location.Member,
                Inputs = [TaskId.Compile.Value],
                Value = values.OrderBy(path => path.Value, StringComparer.Ordinal).ToImmutableArray(),
                Installed = _installed,
                CompletedAt = RecordTimestamp.At(_options.Time.GetUtcNow())
            };
            // Synchronous atomic record replacement is the working-result commit point. Cancellation after
            // this point can refuse installation, but does not turn a completed working product into a tombstone.
            RecordFile.Write(_location.Record, record);
            return record;
        }
        catch (Exception primary)
        {
            // The record is still a tombstone. Attempt to remove every promoted path, not installed files.
            foreach (var path in values)
            {
                try { File.Delete(path.Under(_location.Dest)); }
                catch (Exception cleanup) when (TaskFailures.IsIo(cleanup)) { primary.Data["generation.promotion_cleanup"] = cleanup; }
            }
            if (stage.Cleanup() is { } cleanupFailure) primary.Data["generation.stage_cleanup"] = cleanupFailure.Message;
            throw;
        }
    }

    public TaskRecord CommitFiles(VFileTree tree, CancellationToken ct)
    {
        if (!_prepared) throw new InvalidOperationException("The generation task has not been prepared.");
        var values = ImmutableArray.CreateBuilder<RecordPath>();
        string? stageDirectory = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            Validate(_location, _output);
            stageDirectory = Path.Combine(_location.Dest, "bounded-" + Guid.NewGuid().ToString("N"));
            if (Path.Exists(stageDirectory)) throw new IOException("The private generation stage already exists.");
            Directory.CreateDirectory(stageDirectory);

            var staged = VFileTreeModule.publish(new PublishOptions(stageDirectory, ManifestMode.NoManifest), tree)
                .Match<PublishResult, TreeWriteError, PublishResult>(
                    published => published,
                    failed => throw new IOException(TreeWriteErrorModule.describe(failed)));

            foreach (var relative in staged.Written)
            {
                ct.ThrowIfCancellationRequested();
                var path = RecordPath.Parse(relative);
                var source = path.Under(stageDirectory);
                var destination = path.Under(_location.Dest);
                if (GenerationPublisher.ContainsPath(stageDirectory, destination))
                    throw new IOException("An artifact conflicts with the private stage directory.");
                Ordinary(source);
                Ordinary(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(source, destination);
                values.Add(path);
            }

            Directory.Delete(stageDirectory, recursive: true);
            stageDirectory = null;
            ct.ThrowIfCancellationRequested();
            var record = new TaskRecord
            {
                Task = _location.Task,
                Module = _location.Member,
                Inputs = [TaskId.Compile.Value],
                Value = values.OrderBy(path => path.Value, StringComparer.Ordinal).ToImmutableArray(),
                Installed = _installed,
                CompletedAt = RecordTimestamp.At(_options.Time.GetUtcNow())
            };
            RecordFile.Write(_location.Record, record);
            return record;
        }
        catch (Exception primary)
        {
            foreach (var path in values)
            {
                try { File.Delete(path.Under(_location.Dest)); }
                catch (Exception cleanup) when (TaskFailures.IsIo(cleanup)) { primary.Data["generation.promotion_cleanup"] = cleanup; }
            }
            if (stageDirectory is not null && Directory.Exists(stageDirectory))
            {
                try { Directory.Delete(stageDirectory, recursive: true); }
                catch (Exception cleanup) when (TaskFailures.IsIo(cleanup)) { primary.Data["generation.stage_cleanup"] = cleanup.Message; }
            }
            throw;
        }
    }

    public ValueTask<Outcome<InstallReport>> InstallAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return ValueTask.FromResult(TaskFailures.Cancelled<InstallReport>());
        Validate(_location, _output);
        return TaskInstaller.InstallAsync(_location, _output, _options, ct);
    }

    private static void Validate(TaskLocation location, string output)
    {
        if (!GenerationPublisher.ContainsPath(location.Root.Path, location.Dest) || location.Dest == location.Root.Path)
            throw new IOException("The generation destination is outside the task output root.");
        if (GenerationPublisher.ContainsPath(location.Root.Path, output) || GenerationPublisher.ContainsPath(output, location.Root.Path))
            throw new IOException("The install target overlaps the workspace task output root.");
        foreach (var path in new[] { location.Record, location.Lock })
        {
            Ordinary(path);
            if (Directory.Exists(path)) throw new IOException($"The task file {path} is a directory.");
        }
        foreach (var path in new[] { location.Dest, output }) Ordinary(path);
        if (File.Exists(location.Dest)) throw new IOException("The generation destination is not a directory.");
    }

    private static void Ordinary(string path)
    {
        if (GenerationPublisher.LinkedPath(path) is { } link) throw new IOException($"The task publication path contains a link at {link}.");
        if (File.Exists(path) && (File.GetAttributes(path).HasFlag(FileAttributes.Device) || !UnixInputFile.IsRegular(path)))
            throw new IOException($"The task publication path {path} is not a regular file.");
    }

    private static void RejectLinksBelow(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            Ordinary(path);
            if (Directory.Exists(path)) RejectLinksBelow(path);
        }
    }

    public void Dispose() => _lock.Dispose();
}
