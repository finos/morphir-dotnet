using System.Collections.Immutable;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Infrastructure;

/// <summary>Why a bounded capture stopped.</summary>
internal enum CaptureFailureKind
{
    /// <summary>A budget is zero or negative.</summary>
    InvalidLimits,
    /// <summary>The input is larger than a budget, or it changed size during capture.</summary>
    Limit,
    /// <summary>The input or one of its ancestors is a link, a device or another special entry.</summary>
    UnsupportedEntry,
    /// <summary>The input could not be read.</summary>
    Read,
    /// <summary>The input path is not valid.</summary>
    InvalidPath,
    /// <summary>The capture was cancelled.</summary>
    Cancelled,
}

/// <summary>A capture failure, with the path that was in use when it occurred.</summary>
internal sealed record CaptureFailure(CaptureFailureKind Kind, string Message, string Path);

/// <summary>The budgets that one capture shares across all the files it reads.</summary>
internal sealed record CaptureLimits(long MaxBytes, int MaxEntries, int MaxPathLength)
{
    /// <summary>A failure when a budget is not positive; otherwise null.</summary>
    internal CaptureFailure? Check(string path) =>
        MaxBytes <= 0 || MaxEntries <= 0 || MaxPathLength <= 0
            ? new(CaptureFailureKind.InvalidLimits, BoundedCapture.InvalidLimitsMessage, path)
            : null;
}

/// <summary>
/// The shared core of bounded input capture. It reads owned bytes before any parser or decoder runs. Files and
/// directory trees share one byte budget. Links and special entries are refused, also in the ancestors of an
/// input, because a linked ancestor can redirect an ordinary file outside its lexical location. One instance
/// is one capture and is not thread-safe.
/// </summary>
/// <remarks>
/// The read methods throw. Callers catch with <see cref="TryClassify"/>, which gives the failure kind and the
/// path in use, and map it to their own diagnostics.
/// </remarks>
internal sealed class BoundedCapture
{
    internal const string InvalidLimitsMessage = "Input budgets must all be positive.";

    private readonly CaptureLimits _limits;
    private readonly Func<string, bool> _isRegularFile;
    private readonly CancellationToken _ct;
    private readonly HashSet<string> _checkedAncestors = new(StringComparer.Ordinal);
    private long _remainingBytes;
    private int _entries;

    internal BoundedCapture(CaptureLimits limits, Func<string, bool> isRegularFile, CancellationToken ct)
    {
        _limits = limits;
        _isRegularFile = isRegularFile;
        _ct = ct;
        _remainingBytes = limits.MaxBytes;
    }

    /// <summary>The path in use, for failure reports.</summary>
    internal string CurrentPath { get; private set; } = string.Empty;

    /// <summary>
    /// Makes <paramref name="path"/> absolute and checks its length, its ancestors and its own attributes.
    /// Returns the attributes, so the caller can tell a file from a directory.
    /// </summary>
    internal FileAttributes Open(string path, out string fullPath)
    {
        CurrentPath = path;
        _ct.ThrowIfCancellationRequested();
        fullPath = Path.GetFullPath(path);
        CurrentPath = fullPath;
        CheckPath(fullPath);
        CheckAncestors(fullPath);
        var attributes = File.GetAttributes(fullPath);
        CheckAttributes(attributes);
        return attributes;
    }

    /// <summary>Counts one more entry against the entry budget.</summary>
    internal void CountEntry(string message = "The tree exceeds the entry budget.")
    {
        if (++_entries > _limits.MaxEntries) throw Limit(message);
    }

    /// <summary>Reads one regular file into owned bytes, within the remaining byte budget.</summary>
    internal async Task<RetainedEntry> ReadFileAsync(string path, string relative)
    {
        CurrentPath = path;
        _ct.ThrowIfCancellationRequested();
        // Opening a Unix FIFO can block before FileStream can test CanSeek.
        if (!_isRegularFile(path))
            throw new CaptureException(CaptureFailureKind.UnsupportedEntry, "Only regular input files are supported.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        CheckAttributes(File.GetAttributes(stream.SafeFileHandle));
        if (!stream.CanSeek)
            throw new CaptureException(CaptureFailureKind.UnsupportedEntry, "Only regular seekable files are supported.");
        var length = stream.Length;
        if (length > _remainingBytes || length > Array.MaxLength) throw Limit("The input exceeds the aggregate byte budget.");
        var bytes = new byte[(int)length];
        await stream.ReadExactlyAsync(bytes, _ct);
        if (await stream.ReadAsync(new byte[1], _ct) != 0) throw Limit("The input changed size during capture.");
        _remainingBytes -= length;
        return new RetainedEntry(relative, bytes);
    }

    /// <summary>
    /// Reads every file below <paramref name="root"/>, an opened directory, within the entry and depth budgets.
    /// The entries are in ordinal order of their root-relative paths, with '/' separators.
    /// </summary>
    internal async Task<ImmutableArray<RetainedEntry>> ReadTreeAsync(string root, int maxDepth)
    {
        var entries = new List<RetainedEntry>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.TryPop(out var directory))
        {
            _ct.ThrowIfCancellationRequested();
            CurrentPath = directory.Path;
            CheckAttributes(File.GetAttributes(directory.Path));
            foreach (var path in Directory.EnumerateFileSystemEntries(directory.Path))
            {
                CurrentPath = path;
                _ct.ThrowIfCancellationRequested();
                CountEntry();
                if (directory.Depth + 1 > maxDepth) throw Limit("The tree exceeds the depth budget.");
                CheckPath(path);
                var attributes = File.GetAttributes(path);
                CheckAttributes(attributes);
                if (attributes.HasFlag(FileAttributes.Directory))
                    pending.Push((path, directory.Depth + 1));
                else
                    entries.Add(await ReadFileAsync(path, Path.GetRelativePath(root, path).Replace('\\', '/')));
            }
        }
        _ct.ThrowIfCancellationRequested();
        entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        return [.. entries];
    }

    /// <summary>
    /// The failure that <paramref name="error"/> stands for, at the path in use. False for an exception that
    /// capture does not expect; the caller lets it propagate.
    /// </summary>
    internal bool TryClassify(Exception error, out CaptureFailure failure)
    {
        var path = CurrentPath;
        CaptureFailure? classified = error switch
        {
            OperationCanceledException => new(CaptureFailureKind.Cancelled, "Input capture was cancelled.", path),
            CaptureException capture => new(capture.Kind, capture.Message, path),
            IOException or UnauthorizedAccessException or System.Security.SecurityException =>
                new(CaptureFailureKind.Read, "The input entry could not be read.", path),
            ArgumentException or NotSupportedException =>
                new(CaptureFailureKind.InvalidPath, "The input path is invalid.", path),
            _ => null,
        };
        failure = classified!;
        return classified is not null;
    }

    private void CheckPath(string path)
    {
        if (path.Length > _limits.MaxPathLength) throw Limit("The input exceeds the path-length budget.");
    }

    private void CheckAncestors(string fullPath)
    {
        // A directory is recorded only after it and all its ancestors pass, so the walk can stop at the first
        // recorded one.
        var passed = new List<string>();
        for (var ancestor = new DirectoryInfo(fullPath).Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (_checkedAncestors.Contains(ancestor.FullName)) break;
            CheckAttributes(ancestor.Attributes);
            passed.Add(ancestor.FullName);
        }
        _checkedAncestors.UnionWith(passed);
    }

    private static void CheckAttributes(FileAttributes attributes)
    {
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
            throw new CaptureException(CaptureFailureKind.UnsupportedEntry, "Links and special input entries are unsupported.");
    }

    private static CaptureException Limit(string message) => new(CaptureFailureKind.Limit, message);

    private sealed class CaptureException(CaptureFailureKind kind, string message) : Exception(message)
    {
        public CaptureFailureKind Kind { get; } = kind;
    }
}
