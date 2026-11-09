// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.FSharp.Collections;
using Morphir.IR.Pipeline;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Features.TaskRecords;

/// <summary>What one <c>-o</c> install did. Paths are relative to the target.</summary>
/// <param name="Target">The absolute target directory.</param>
/// <param name="Copied">The <c>value</c> entries that were installed.</param>
/// <param name="Written">Files created or replaced.</param>
/// <param name="Unchanged">Files that were already byte-identical.</param>
/// <param name="Removed">Files from an earlier install that this run no longer produces, and that were removed.</param>
public sealed record InstallReport(string Target, ImmutableArray<string> Copied, ImmutableArray<string> Written,
    ImmutableArray<string> Unchanged, ImmutableArray<string> Removed);

/// <summary>
/// Copies a task's <c>value</c> from <c>.dest</c> into an <c>-o</c> target and keeps the record's
/// <c>installed</c> ledger, as the upstream out-directory contract describes. Install removes only files that
/// it wrote on an earlier run, never a whole directory, and refuses to overwrite a file that it did not write.
/// The copy itself is the owned-tree publication that <c>generate</c> uses, without a manifest in the target.
/// </summary>
internal static class TaskInstaller
{
    public static async ValueTask<Outcome<InstallReport>> InstallAsync(TaskLocation location, string target, TaskStoreOptions options,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return TaskFailures.Cancelled<InstallReport>();
        Outcome<InstallReport> Refuse(string code, string message, string? path = null) =>
            TaskFailures.Of<InstallReport>(GenerationFailureKind.Invocation, code, message, path);

        var record = RecordFile.Read(location.Record) switch
        {
            RecordReading.Found found => found.Record,
            _ => null,
        };
        if (record is null)
            return Refuse("install.no_record", $"There is no readable result record at {location.Record}; the task did not complete.", location.Record);
        if (record.Tombstone)
            return Refuse("install.tombstone", $"The result record at {location.Record} is a tombstone: the task has no completed product. Run the task first.", location.Record);

        string canonical;
        try
        {
            canonical = Canonical(target);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException || TaskFailures.IsIo(error))
        {
            return Refuse("install.invalid_target", $"The -o target {target} is not a valid directory path.", target);
        }
        if (File.Exists(canonical))
            return Refuse("install.target_not_directory",
                $"The -o target {target} exists and is not a directory. -o installs the task output into a directory; the output stays in {location.Dest}.", target);
        if (Inside(location.Dest, canonical))
            return Refuse("install.target_in_dest", $"The -o target {target} is inside the task's scratch directory {location.Dest}.", target);

        var caseInsensitive = IsCaseInsensitive(canonical);
        TaskLock targetLock;
        try
        {
            targetLock = await TaskLock.AcquireAsync(LockPath(location, canonical, caseInsensitive, options), TaskLockMode.Exclusive,
                "installing to this target", options.Diagnostics, options.PollInterval, ct);
        }
        catch (OperationCanceledException)
        {
            return TaskFailures.Cancelled<InstallReport>();
        }
        catch (Exception error) when (TaskFailures.IsIo(error))
        {
            return TaskFailures.Io<InstallReport>("install.lock_failed", "lock the install target", canonical, error);
        }

        using (targetLock)
        {
            try
            {
                return InstallLocked(location, record, canonical, caseInsensitive);
            }
            catch (Exception error) when (TaskFailures.IsIo(error))
            {
                return TaskFailures.Io<InstallReport>("install.failed", "install into", canonical, error);
            }
        }
    }

    private static Outcome<InstallReport> InstallLocked(TaskLocation location, TaskRecord record, string target, bool caseInsensitive)
    {
        Outcome<InstallReport> Refuse(string code, string message) =>
            TaskFailures.Of<InstallReport>(GenerationFailureKind.Invocation, code, message, target);

        var comparer = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var previousKey = record.Installed.Keys.FirstOrDefault(key => comparer.Equals(key, target));
        var previous = previousKey is null ? [] : record.Installed[previousKey];

        var produced = new List<string>();
        foreach (var entry in record.Value)
        {
            var source = entry.Under(location.Dest);
            if (Directory.Exists(source)) produced.AddRange(FilesBelow(source).Select(file => entry.Segments.Length == 0 ? file : string.Join('/', entry.Segments) + "/" + file));
            else if (File.Exists(source)) produced.Add(string.Join('/', entry.Segments));
            else
                return TaskFailures.Of<InstallReport>(GenerationFailureKind.Operational, "install.missing_product",
                    $"The value entry {entry} is not in {location.Dest}.", source);
        }
        produced.Sort(StringComparer.Ordinal);

        var links = produced.Concat(record.Value.Select(entry => string.Join('/', entry.Segments))).Concat(previous.Select(path => path.Value))
            .Select(path => LinkBelow(target, path)).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (links.Length > 0)
            return Refuse("install.link_below_target",
                $"The -o target {target} has a symbolic link below it: {string.Join(", ", links)}. Point -o at the real directory instead.");

        var collisions = produced.GroupBy(path => path, comparer).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (collisions.Length > 0)
            return Refuse("install.colliding_outputs",
                $"These outputs name the same file on the target filesystem: {string.Join(", ", collisions)}.");

        var owned = previous.Select(path => path.Value).ToHashSet(comparer);
        var conflicts = produced.Where(path => !owned.Contains(path) && Exists(Path.Combine(target, path))).ToArray();
        if (conflicts.Length > 0)
            return Refuse("install.foreign_content",
                $"The -o target {target} already contains files that Morphir did not write: {string.Join(", ", conflicts)}. Move them, or choose a different -o target.");

        var files = produced.Select(path => VFileModule.ofDiskFile(path, RecordPath.Parse(path).Under(location.Dest)));
        var tree = VFileTreeModule.fromFiles(ListModule.OfSeq(files));
        var options = new PublishOptions(target, ManifestMode.NoManifest);
        var validation = VFileTreeModule.validatePublish(options, tree);
        if (validation.IsError) return Refuse("install.invalid_outputs", TreeWriteErrorModule.describe(validation.ErrorValue));

        // Nothing can refuse the install after this point.
        Directory.CreateDirectory(target);
        var producedSet = produced.ToHashSet(comparer);
        var removed = new List<string>();
        foreach (var stale in previous.Where(path => !producedSet.Contains(path.Value)))
        {
            var path = stale.Under(target);
            if (File.Exists(path))
            {
                File.Delete(path);
                PruneEmptyParents(path, target);
                removed.Add(stale.Value);
            }
        }

        var published = VFileTreeModule.publish(options, tree);

        var ledger = produced.Select(RecordPath.Parse).ToImmutableArray();
        if (published.IsError)
        {
            // Remove what this run added; files that were already owned stay, and the ledger names what is on disk.
            var kept = previous.Where(path => !removed.Contains(path.Value, comparer) && File.Exists(path.Under(target))).Select(path => path.Value).ToList();
            foreach (var added in produced.Where(path => !owned.Contains(path)))
            {
                var path = Path.Combine(target, added);
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        PruneEmptyParents(path, target);
                    }
                }
                catch (Exception error) when (TaskFailures.IsIo(error))
                {
                    kept.Add(added);
                }
            }
            RecordFile.Write(location.Record, WithLedger(record, previousKey, target,
                kept.Distinct(comparer).Order(StringComparer.Ordinal).Select(RecordPath.Parse).ToImmutableArray()));
            return TaskFailures.Of<InstallReport>(GenerationFailureKind.Operational, "install.copy_failed",
                "Install failed while it copied files: " + TreeWriteErrorModule.describe(published.ErrorValue), target);
        }

        try
        {
            RecordFile.Write(location.Record, WithLedger(record, previousKey, target, ledger));
        }
        catch (Exception error) when (TaskFailures.IsIo(error))
        {
            // Without a ledger the next install would see these files as foreign content, so take back what this run added.
            foreach (var added in produced.Where(path => !owned.Contains(path)))
            {
                try { File.Delete(Path.Combine(target, added)); }
                catch (Exception cleanup) when (TaskFailures.IsIo(cleanup)) { }
            }
            return TaskFailures.Io<InstallReport>("install.record_write_failed", "write", location.Record, error);
        }

        var result = published.ResultValue;
        return new Outcome<InstallReport>.Success(new InstallReport(target, [.. record.Value.Select(entry => entry.Value)],
            [.. result.Written], [.. result.Unchanged], [.. removed]));
    }

    private static TaskRecord WithLedger(TaskRecord record, string? previousKey, string target, ImmutableArray<RecordPath> files)
    {
        var installed = record.Installed;
        if (previousKey is not null) installed = installed.Remove(previousKey);
        return record with { Installed = installed.SetItem(target, files) };
    }

    /// <summary>The full target path; when the target itself is a link, the directory that it leads to.</summary>
    private static string Canonical(string target)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        var info = new DirectoryInfo(full);
        if (info.Exists && info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is { } resolved)
            return Path.TrimEndingDirectorySeparator(resolved.FullName);
        return full;
    }

    private static bool Inside(string parent, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        return string.Equals(root, path, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;

    /// <summary>The first component below <paramref name="target"/> on the way to <paramref name="relative"/> that is a link.</summary>
    private static string? LinkBelow(string target, string relative)
    {
        var current = target;
        foreach (var segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null) return Path.GetRelativePath(target, current).Replace('\\', '/');
            if (!info.Exists) return null;
        }
        return null;
    }

    private static IEnumerable<string> FilesBelow(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'));

    private static void PruneEmptyParents(string path, string target)
    {
        var stop = Path.TrimEndingDirectorySeparator(target);
        for (var directory = Path.GetDirectoryName(path); directory is not null
             && directory.Length > stop.Length
             && Directory.Exists(directory)
             && !Directory.EnumerateFileSystemEntries(directory).Any();
             directory = Path.GetDirectoryName(directory))
        {
            Directory.Delete(directory);
        }
    }

    /// <summary>
    /// True when the filesystem that holds <paramref name="path"/> does not tell letter case apart. The test
    /// looks for the nearest existing directory under another letter case.
    /// </summary>
    private static bool IsCaseInsensitive(string path)
    {
        var directory = path;
        while (!Directory.Exists(directory) && Path.GetDirectoryName(directory) is { } parent) directory = parent;
        var swapped = new string(directory.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)).ToArray());
        return swapped == directory ? OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() : Directory.Exists(swapped);
    }

    /// <summary>
    /// The install lock is keyed on the canonical target, so that two tasks, or two workspaces, that install
    /// into one directory take turns. It is in the Morphir home, never in the target.
    /// </summary>
    private static string LockPath(TaskLocation location, string target, bool caseInsensitive, TaskStoreOptions options)
    {
        var key = caseInsensitive ? target.ToLowerInvariant() : target;
        var name = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32] + ".lock";
        if (options.MorphirHome is { Length: > 0 } home) return Path.Combine(home, "locks", "install", name);
        options.Diagnostics.WriteLine($"warning: the Morphir home is not known, so the install lock for {target} is below the out root; another workspace does not share it");
        return Path.Combine(location.Root.Path, "install-locks", name);
    }
}
