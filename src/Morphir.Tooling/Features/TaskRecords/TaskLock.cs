// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Tooling.Features.TaskRecords;

/// <summary>How a run holds a task lock.</summary>
public enum TaskLockMode
{
    /// <summary>A run that changes the task's files. No other run or reader can hold the lock.</summary>
    Exclusive,

    /// <summary>A reader of the task's files. Other readers can hold the lock at the same time.</summary>
    Shared,
}

/// <summary>
/// An advisory lock on an empty lock file, held until it is disposed. The file stays on disk between runs.
/// </summary>
/// <remarks>
/// The lock is the operating system's file sharing: on Windows the share mode of the open handle, and on
/// Linux and macOS the <c>flock</c> lock that .NET takes for <see cref="FileShare.None"/> (exclusive) and for
/// a read-only handle with <see cref="FileShare.Read"/> (shared). A lock that is not free is tried again at a
/// fixed interval. The first time that the lock is not free, one line goes to the diagnostics writer.
/// </remarks>
public sealed class TaskLock : IDisposable
{
    private readonly FileStream _handle;

    private TaskLock(FileStream handle, TaskLockMode mode)
    {
        _handle = handle;
        Mode = mode;
    }

    /// <summary>How the lock is held.</summary>
    public TaskLockMode Mode { get; }

    /// <summary>The lock file.</summary>
    public string Path => _handle.Name;

    /// <summary>
    /// Takes the lock, and waits while another run holds it. <paramref name="subject"/> completes the waiting
    /// line "waiting for another Morphir run to finish &lt;subject&gt; (&lt;path&gt;)".
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled while the run waited.</exception>
    /// <exception cref="IOException">The lock file could not be created or opened for a reason other than a lock.</exception>
    public static async ValueTask<TaskLock> AcquireAsync(string path, TaskLockMode mode, string subject, TextWriter diagnostics,
        TimeSpan pollInterval, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var announced = false;
        while (true)
        {
            if (TryOpen(path, mode) is { } handle) return new TaskLock(handle, mode);
            if (!announced)
            {
                announced = true;
                await diagnostics.WriteLineAsync($"waiting for another Morphir run to finish {subject} ({path})");
                await diagnostics.FlushAsync(ct);
            }
            await Task.Delay(pollInterval, ct);
        }
    }

    private static FileStream? TryOpen(string path, TaskLockMode mode)
    {
        try
        {
            return mode == TaskLockMode.Exclusive
                ? new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)
                : new FileStream(path, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read);
        }
        catch (IOException error) when (IsContention(error))
        {
            return null;
        }
    }

    /// <summary>
    /// True when the error says that another handle holds the lock. Windows reports a sharing or lock
    /// violation. On Linux and macOS, .NET reports a held <c>flock</c> lock as a plain <see cref="IOException"/>.
    /// </summary>
    private static bool IsContention(IOException error)
    {
        if (error is FileNotFoundException or DirectoryNotFoundException or PathTooLongException) return false;
        if (!OperatingSystem.IsWindows()) return true;
        const int SharingViolation = 32, LockViolation = 33;
        var code = error.HResult & 0xFFFF;
        return code is SharingViolation or LockViolation;
    }

    /// <summary>Releases the lock.</summary>
    public void Dispose() => _handle.Dispose();
}
