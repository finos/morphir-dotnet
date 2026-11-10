using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Morphir.Build.Helpers;

/// <summary>
/// Archives a published process folder, such as a self-contained extension, so that the same folder gives the same
/// bytes: entries in ordinal path order under one root directory, with a fixed time stamp. Windows runtime
/// identifiers get a zip; other runtime identifiers get a gzip tar that keeps the executable bit of the executable.
/// </summary>
public static class ProcessArchive
{
    /// <summary>The time stamp of every entry.</summary>
    static readonly DateTimeOffset Stamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The archive extension for a runtime identifier: .zip for win-*, otherwise .tar.gz.</summary>
    public static string ExtensionFor(string rid) =>
        rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase) ? ".zip" : ".tar.gz";

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
    /// Writes the archive of <paramref name="folder"/> with every entry under <paramref name="root"/>/. For a tar,
    /// <paramref name="executables"/> names the files (relative paths) that get mode 0755; the others get 0644.
    /// </summary>
    public static void Create(string folder, string archive, string root, string rid, params string[] executables)
    {
        if (File.Exists(archive)) File.Delete(archive);

        if (ExtensionFor(rid) == ".zip")
        {
            using var stream = File.Create(archive);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

            foreach (var file in Files(folder))
            {
                var entry = zip.CreateEntry($"{root}/{file}", CompressionLevel.Optimal);
                entry.LastWriteTime = Stamp;
                using var source = File.OpenRead(Path.Combine(folder, file));
                using var target = entry.Open();
                source.CopyTo(target);
            }
        }
        else
        {
            using var stream = File.Create(archive);
            using var gzip = new GZipStream(stream, CompressionLevel.Optimal);
            using var tar = new TarWriter(gzip, TarEntryFormat.Ustar, leaveOpen: false);

            foreach (var file in Files(folder))
            {
                using var source = File.OpenRead(Path.Combine(folder, file));
                // Ustar: PAX entries would add access and change times of the moment.
                var entry = new UstarTarEntry(TarEntryType.RegularFile, $"{root}/{file}")
                {
                    ModificationTime = Stamp,
                    Mode = executables.Contains(file, StringComparer.Ordinal)
                        ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                          UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute
                        : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                    DataStream = source,
                };
                tar.WriteEntry(entry);
            }
        }
    }
}
