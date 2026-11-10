using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Morphir.Build.Helpers;

/// <summary>
/// Archives a published process folder, such as a self-contained extension, by the shared archive convention: files
/// at the archive root with no top folder, <c>/</c> separators, ordinal entry order, one fixed time stamp, and explicit
/// modes (0755 for the launch file and createdump, 0644 for every other file). Windows runtime identifiers get a zip;
/// other runtime identifiers get a gzip ustar tar.
/// </summary>
/// <remarks>
/// Determinism is scoped: archiving the same folder again with the same build machine OS and .NET version gives the
/// same bytes, whatever the files' times and modes on disk. Archives made on different operating systems or .NET
/// versions can differ in bytes (the zip "version made by" field, and the deflate output of the runtime's zlib), but
/// not in entry names, order, times, modes or content.
/// </remarks>
public static class ProcessArchive
{
    /// <summary>The time stamp of every entry: 2000-01-01T00:00:00Z.</summary>
    public static readonly DateTimeOffset Timestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>0644.</summary>
    public const UnixFileMode RegularMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    /// <summary>0755.</summary>
    public const UnixFileMode ExecutableMode =
        RegularMode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    // The .NET host runs createdump to write a crash dump; it ships beside a self-contained runtime on Linux and macOS.
    private const string CreateDump = "createdump";

    // S_IFREG, the regular-file type bits of a Unix mode in a zip entry's external attributes.
    private const int RegularFileType = 0x8000;

    // Ustar fields: a name of at most 100 bytes, and a prefix of at most 155 bytes split off at a '/'.
    private const int UstarName = 100;
    private const int UstarPrefix = 155;

    /// <summary>The archive extension for a runtime identifier: .zip for win-*, otherwise .tar.gz.</summary>
    public static string ExtensionFor(string rid) =>
        rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase) ? ".zip" : ".tar.gz";

    /// <summary>The files that run as programs in a publish folder whose launch file is <paramref name="launchPath"/>.</summary>
    public static IReadOnlySet<string> ExecutablesFor(string launchPath) =>
        ImmutableSortedSet.Create(StringComparer.Ordinal, launchPath, CreateDump);

    /// <summary>The files of a folder as relative paths with forward slashes, in ordinal order.</summary>
    public static string[] Files(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    /// <summary>The number of files and their total size in bytes.</summary>
    public static (int Files, long Bytes) Measure(string folder)
    {
        var files = Files(folder);
        return (files.Length, files.Sum(file => new FileInfo(Path.Combine(folder, file)).Length));
    }

    /// <summary>
    /// Creates <paramref name="archive"/> from <paramref name="folder"/>, in the format of <paramref name="rid"/>. A file
    /// whose archive path is in <paramref name="executables"/> gets mode 0755; every other file gets 0644, whatever the
    /// build machine's modes. A path that a ustar tar cannot hold fails with <see cref="InvalidOperationException"/>
    /// naming the path, and no archive is left.
    /// </summary>
    public static void Create(string folder, string archive, string rid, IReadOnlySet<string> executables)
    {
        if (File.Exists(archive)) File.Delete(archive);
        var files = Files(folder);
        UnixFileMode ModeOf(string name) => executables.Contains(name) ? ExecutableMode : RegularMode;

        if (ExtensionFor(rid) == ".zip")
        {
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
            foreach (var file in files)
            {
                var entry = zip.CreateEntry(file, CompressionLevel.Optimal);
                entry.LastWriteTime = Timestamp;
                entry.ExternalAttributes = (RegularFileType | (int)ModeOf(file)) << 16;
                using var content = File.OpenRead(Path.Combine(folder, file));
                using var target = entry.Open();
                content.CopyTo(target);
            }
            return;
        }

        var unrepresentable = files.FirstOrDefault(file => !FitsUstar(file));
        if (unrepresentable is not null)
        {
            throw new InvalidOperationException(
                $"The path '{unrepresentable}' cannot be stored in a ustar tar: a ustar name holds at most {UstarName} bytes after a split at '/', with at most {UstarPrefix} bytes before it.");
        }

        using var output = File.Create(archive);
        using var gzip = new GZipStream(output, CompressionLevel.Optimal);
        // Ustar, not PAX: a PAX entry also records access and change times, which differ between runs.
        using var tar = new TarWriter(gzip, TarEntryFormat.Ustar, leaveOpen: false);
        foreach (var file in files)
        {
            using var content = File.OpenRead(Path.Combine(folder, file));
            tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, file)
            {
                DataStream = content,
                Mode = ModeOf(file),
                ModificationTime = Timestamp,
            });
        }
    }

    /// <summary>True when a ustar header can hold the path, in its name field or split into prefix and name at a '/'.</summary>
    static bool FitsUstar(string path)
    {
        var bytes = Encoding.UTF8.GetByteCount(path);
        if (bytes <= UstarName) return true;
        for (var at = path.IndexOf('/'); at >= 0; at = path.IndexOf('/', at + 1))
        {
            if (Encoding.UTF8.GetByteCount(path.AsSpan(0, at)) <= UstarPrefix
                && Encoding.UTF8.GetByteCount(path.AsSpan(at + 1)) <= UstarName)
                return true;
        }
        return false;
    }
}
