// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.TestSupport;

/// <summary>Options for <see cref="TolerantDirectory"/>.</summary>
public sealed record TolerantDeleteOptions
{
    /// <summary>Total delete attempts before giving up. Default 5.</summary>
    public int Attempts { get; init; } = 5;

    /// <summary>Delay before the second attempt; each later attempt waits one more step. Default 250 ms.</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Receives a message when the directory could not be deleted. Default: stderr.</summary>
    public Action<string> Log { get; init; } = message => Console.Error.WriteLine(message);

    /// <summary>The delete operation itself. Replaceable so tests can simulate a lock that never clears.</summary>
    public Action<string> DeleteDirectory { get; init; } = TolerantDirectory.DeleteTree;
}

/// <summary>
/// Best-effort cleanup of a test's temporary directory. On Windows a file can stay locked briefly after
/// the process that used it exits (antivirus, indexers, a child still dying), so retry with backoff and
/// never let cleanup fail a test whose assertions passed.
/// </summary>
public static class TolerantDirectory
{
    internal static void DeleteTree(string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (Path.GetDirectoryName(root) is null)
            throw new IOException("Refusing to clean a filesystem root.");

        MakeWritable(root);
        Directory.Delete(root, recursive: true);

        static void MakeWritable(string entry)
        {
            var attributes = File.GetAttributes(entry);
            // Directory.Delete removes the link itself. Do not traverse it or
            // change attributes on a target outside this owned cleanup tree.
            if (attributes.HasFlag(FileAttributes.ReparsePoint)) return;
            if (attributes.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
            if (attributes.HasFlag(FileAttributes.Directory))
                foreach (var child in Directory.EnumerateFileSystemEntries(entry))
                    MakeWritable(child);
        }
    }

    /// <summary>Deletes <paramref name="path"/> recursively. Returns false, after logging, if it could not.</summary>
    public static async Task<bool> TryDeleteAsync(string path, TolerantDeleteOptions? options = null)
    {
        options ??= new TolerantDeleteOptions();
        for (var attempt = 1; ; attempt++)
        {
            if (TryOnce(path, options, attempt, out var done)) return done;
            await Task.Delay(options.InitialDelay * attempt).ConfigureAwait(false);
        }
    }

    /// <summary>Synchronous form for <see cref="IDisposable.Dispose"/> implementations.</summary>
    public static bool TryDelete(string path, TolerantDeleteOptions? options = null)
    {
        options ??= new TolerantDeleteOptions();
        for (var attempt = 1; ; attempt++)
        {
            if (TryOnce(path, options, attempt, out var done)) return done;
            Thread.Sleep(options.InitialDelay * attempt);
        }
    }

    /// <summary>Returns true when finished, with <paramref name="deleted"/> holding the outcome.</summary>
    private static bool TryOnce(string path, TolerantDeleteOptions options, int attempt, out bool deleted)
    {
        if (!Directory.Exists(path))
        {
            deleted = true;
            return true;
        }

        try
        {
            options.DeleteDirectory(path);
            deleted = true;
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            deleted = true;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (attempt < options.Attempts)
            {
                deleted = false;
                return false;
            }

            options.Log($"Could not delete test directory {path} after {attempt} attempts: {ex.Message}");
            deleted = false;
            return true;
        }
    }
}
