// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Formats.Tar;
using System.IO.Compression;
using FluentAssertions;
using Morphir.Build.Helpers;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// The archive of a published process folder follows the shared archive convention: files at the archive root with no
/// top folder, ordinal entry order, one fixed time stamp, mode 0755 for the launch file and createdump and 0644 for
/// every other file, and the same bytes when the same folder is archived again.
/// </summary>
public class ProcessArchiveTests
{
    private const string Launcher = "morphir-fsharp-frontend";

    [Test]
    [Arguments("linux-x64")]
    [Arguments("win-x64")]
    public async Task ArchivingTheSameFolderTwiceGivesTheSameBytes(string rid)
    {
        using var scratch = new Scratch();
        var folder = scratch.Folder();
        var first = scratch.PathOf("first" + ProcessArchive.ExtensionFor(rid));
        var second = scratch.PathOf("second" + ProcessArchive.ExtensionFor(rid));

        ProcessArchive.Create(folder, first, rid, ProcessArchive.ExecutablesFor(Launcher));
        // The file times of the folder change between the runs; the archive does not record them.
        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-3));
        ProcessArchive.Create(folder, second, rid, ProcessArchive.ExecutablesFor(Launcher));

        (await File.ReadAllBytesAsync(second)).Should().Equal(await File.ReadAllBytesAsync(first));
    }

    [Test]
    [Arguments("linux-x64")]
    [Arguments("win-x64")]
    public async Task TheFilesAreAtTheRootInOrdinalOrderWithTheFixedTimeStamp(string rid)
    {
        using var scratch = new Scratch();
        var folder = scratch.Folder();
        var archive = scratch.PathOf("archive" + ProcessArchive.ExtensionFor(rid));

        ProcessArchive.Create(folder, archive, rid, ProcessArchive.ExecutablesFor(Launcher));

        var entries = Entries(archive);
        entries.Select(entry => entry.Name).Should().Equal(
            "Morphir.Mep.dll", "createdump", "lib/System.Runtime.dll", Launcher, "Ä-resources/de/x.resources.dll");
        entries.Select(entry => entry.Name).Should().Equal(ProcessArchive.Files(folder), "the archive holds exactly the folder's files");
        entries.Should().OnlyContain(entry => entry.Time == ProcessArchive.Timestamp);
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("linux-x64")]
    [Arguments("win-x64")]
    public async Task TheLaunchFileAndCreatedumpAre0755AndEveryOtherFileIs0644(string rid)
    {
        using var scratch = new Scratch();
        var folder = scratch.Folder();
        var archive = scratch.PathOf("archive" + ProcessArchive.ExtensionFor(rid));

        ProcessArchive.Create(folder, archive, rid, ProcessArchive.ExecutablesFor(Launcher));

        foreach (var (name, mode, _) in Entries(archive))
        {
            var expected = name is Launcher or "createdump" ? ProcessArchive.ExecutableMode : ProcessArchive.RegularMode;
            mode.Should().Be(expected, name);
        }

        ((int)ProcessArchive.ExecutableMode).Should().Be(Convert.ToInt32("755", 8));
        ((int)ProcessArchive.RegularMode).Should().Be(Convert.ToInt32("644", 8));
        await Task.CompletedTask;
    }

    [Test]
    public async Task APathLongerThan100CharactersIsKeptInATar()
    {
        using var scratch = new Scratch();
        var folder = scratch.Folder();
        var longPath = $"{new string('d', 60)}/{new string('f', 60)}.dll";
        Write(folder, longPath);

        var archive = scratch.PathOf("archive.tar.gz");
        ProcessArchive.Create(folder, archive, "linux-x64", ProcessArchive.ExecutablesFor(Launcher));

        longPath.Length.Should().BeGreaterThan(100);
        Entries(archive).Select(entry => entry.Name).Should().Contain(longPath);
        await Task.CompletedTask;
    }

    [Test]
    public async Task APathThatATarCannotHoldFailsAndNamesThePath()
    {
        using var scratch = new Scratch();
        var folder = scratch.Folder();
        // Ustar keeps at most 100 bytes after the last split at '/', so a 120-character file name cannot be stored.
        var tooLong = $"lib/{new string('n', 120)}.dll";
        Write(folder, tooLong);

        var archive = scratch.PathOf("archive.tar.gz");
        var create = () => ProcessArchive.Create(folder, archive, "linux-x64", ProcessArchive.ExecutablesFor(Launcher));

        create.Should().Throw<InvalidOperationException>().WithMessage($"*{tooLong}*");
        File.Exists(archive).Should().BeFalse("no partial archive is left");
        await Task.CompletedTask;
    }

    private static List<(string Name, UnixFileMode Mode, DateTimeOffset Time)> Entries(string archive)
    {
        var entries = new List<(string, UnixFileMode, DateTimeOffset)>();
        if (archive.EndsWith(".zip", StringComparison.Ordinal))
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries)
                entries.Add((entry.FullName, (UnixFileMode)((entry.ExternalAttributes >> 16) & 0xFFF), new DateTimeOffset(entry.LastWriteTime.DateTime, TimeSpan.Zero)));
            return entries;
        }

        using var stream = File.OpenRead(archive);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        while (tar.GetNextEntry() is { } entry)
            entries.Add((entry.Name, entry.Mode, entry.ModificationTime));
        return entries;
    }

    private static void Write(string folder, string relative)
    {
        var path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relative);
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("morphir-archive-").FullName;

        public string PathOf(string name) => Path.Combine(root, name);

        /// <summary>A small publish folder: the launch file, createdump, a nested assembly and a non-ASCII path.</summary>
        public string Folder()
        {
            var folder = Directory.CreateDirectory(PathOf("folder")).FullName;
            foreach (var file in new[] { Launcher, "createdump", "Morphir.Mep.dll", "lib/System.Runtime.dll", "Ä-resources/de/x.resources.dll" })
                Write(folder, file);
            return folder;
        }

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
