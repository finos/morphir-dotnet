// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Formats.Tar;
using System.IO.Compression;
using FluentAssertions;
using Morphir.Build.Helpers;
using Morphir.TestSupport;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// The one archive writer of the build, for release profiles and extension process folders alike. It follows the
/// shared archive convention: files at the archive root with no top folder, <c>/</c> separators, ordinal entry order,
/// one fixed time stamp, mode 0755 for the launch file and createdump and 0644 for every other file, and the same bytes
/// when the same folder is archived again. A path that a ustar tar cannot hold fails before anything is written.
/// </summary>
public class ReleaseArchiveTests
{
    private const string Launcher = "morphir-fsharp-frontend";

    [Test]
    [Category("PlatformSpecific")]
    public async Task ATarArchiveKeepsTheLaunchPathExecutable()
    {
        await using var sandbox = TestSandbox.Create("morphir-release-archive-");
        var source = Directory.CreateDirectory(sandbox.PathOf("publish dir"));
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "morphir"), "launcher");
        Directory.CreateDirectory(Path.Combine(source.FullName, "sub"));
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "sub", "data.json"), "{}");
        var archive = sandbox.PathOf("a.tar.gz");

        ReleaseArchive.Create(source.FullName, archive, ArchiveFormat.TarGz, ReleaseArchive.ExecutablesFor("morphir"));

        var entries = Entries(archive).ToDictionary(entry => entry.Name, entry => entry.Mode);
        entries.Keys.Should().BeEquivalentTo("morphir", "sub/data.json");
        entries["morphir"].Should().HaveFlag(UnixFileMode.UserExecute).And.HaveFlag(UnixFileMode.OtherExecute);
        entries["sub/data.json"].Should().NotHaveFlag(UnixFileMode.UserExecute);
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task AZipArchiveHoldsThePublishFolderAtItsRoot()
    {
        await using var sandbox = TestSandbox.Create("morphir-release-archive-");
        var source = Directory.CreateDirectory(sandbox.PathOf("publish dir"));
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "morphir.exe"), "launcher");
        Directory.CreateDirectory(Path.Combine(source.FullName, "sub"));
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "sub", "data.json"), "{}");
        var archive = sandbox.PathOf("a.zip");

        ReleaseArchive.Create(source.FullName, archive, ArchiveFormat.Zip, ReleaseArchive.ExecutablesFor("morphir.exe"));

        Entries(archive).Select(entry => entry.Name).Should().BeEquivalentTo("morphir.exe", "sub/data.json");
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task ATarArchiveKeepsCreatedumpExecutable()
    {
        await using var sandbox = TestSandbox.Create("morphir-release-archive-");
        var source = Directory.CreateDirectory(sandbox.PathOf("publish dir"));
        foreach (var name in new[] { "morphir", "createdump", "libcoreclr.so", "morphir.dll" })
            await File.WriteAllTextAsync(Path.Combine(source.FullName, name), name);
        var archive = sandbox.PathOf("a.tar.gz");

        ReleaseArchive.Create(source.FullName, archive, ArchiveFormat.TarGz, ReleaseArchive.ExecutablesFor("morphir"));

        var modes = Entries(archive).ToDictionary(entry => entry.Name, entry => entry.Mode);
        modes["morphir"].Should().Be(ReleaseArchive.ExecutableMode);
        modes["createdump"].Should().Be(ReleaseArchive.ExecutableMode, "the runtime runs createdump to write crash dumps");
        modes["libcoreclr.so"].Should().Be(ReleaseArchive.RegularMode, "a shared library is loaded, not run");
        modes["morphir.dll"].Should().Be(ReleaseArchive.RegularMode);
    }

    [Test]
    [Category("PlatformSpecific")]
    [Arguments(ArchiveFormat.Zip)]
    [Arguments(ArchiveFormat.TarGz)]
    public async Task ArchivingTheSameFolderTwiceGivesTheSameBytes(ArchiveFormat format)
    {
        using var scratch = new Scratch();
        var folder = scratch.Folder();
        var first = scratch.PathOf("first" + ReleaseArchive.ExtensionFor(format));
        var second = scratch.PathOf("second" + ReleaseArchive.ExtensionFor(format));

        ReleaseArchive.Create(folder, first, format, ReleaseArchive.ExecutablesFor(Launcher));
        // The file times of the folder change between the runs; the archive does not record them.
        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-3));
        ReleaseArchive.Create(folder, second, format, ReleaseArchive.ExecutablesFor(Launcher));

        (await File.ReadAllBytesAsync(second)).Should().Equal(await File.ReadAllBytesAsync(first), "one folder names one set of bytes");
    }

    [Test]
    [Category("PlatformSpecific")]
    [Arguments(ArchiveFormat.Zip)]
    [Arguments(ArchiveFormat.TarGz)]
    public void TheFilesAreAtTheRootInOrdinalOrderWithTheFixedTimeStamp(ArchiveFormat format)
    {
        using var scratch = new Scratch();
        var folder = scratch.Folder();
        var archive = scratch.PathOf("archive" + ReleaseArchive.ExtensionFor(format));

        ReleaseArchive.Create(folder, archive, format, ReleaseArchive.ExecutablesFor(Launcher));

        var entries = Entries(archive);
        entries.Select(entry => entry.Name).Should().Equal(
            "Morphir.Mep.dll", "createdump", "lib/System.Runtime.dll", Launcher, "Ä-resources/de/x.resources.dll");
        entries.Select(entry => entry.Name).Should().Equal(ReleaseArchive.Files(folder), "the archive holds exactly the folder's files");
        entries.Should().OnlyContain(entry => entry.Time == ReleaseArchive.Timestamp);
    }

    [Test]
    [Category("PlatformSpecific")]
    [Arguments(ArchiveFormat.Zip)]
    [Arguments(ArchiveFormat.TarGz)]
    public void TheLaunchFileAndCreatedumpAre0755AndEveryOtherFileIs0644(ArchiveFormat format)
    {
        using var scratch = new Scratch();
        var folder = scratch.Folder();
        var archive = scratch.PathOf("archive" + ReleaseArchive.ExtensionFor(format));

        ReleaseArchive.Create(folder, archive, format, ReleaseArchive.ExecutablesFor(Launcher));

        foreach (var (name, mode, _) in Entries(archive))
        {
            var expected = name is Launcher or "createdump" ? ReleaseArchive.ExecutableMode : ReleaseArchive.RegularMode;
            mode.Should().Be(expected, name);
        }

        ((int)ReleaseArchive.ExecutableMode).Should().Be(Convert.ToInt32("755", 8));
        ((int)ReleaseArchive.RegularMode).Should().Be(Convert.ToInt32("644", 8));
    }

    [Test]
    [Category("PlatformSpecific")]
    public void APathLongerThan100CharactersIsKeptInATar()
    {
        using var scratch = new Scratch();
        var folder = scratch.Folder();
        var longPath = $"{new string('d', 60)}/{new string('f', 60)}.dll";
        Write(folder, longPath);

        var archive = scratch.PathOf("archive.tar.gz");
        ReleaseArchive.Create(folder, archive, ArchiveFormat.TarGz, ReleaseArchive.ExecutablesFor(Launcher));

        longPath.Length.Should().BeGreaterThan(100);
        Entries(archive).Select(entry => entry.Name).Should().Contain(longPath);
    }

    [Test]
    [Category("PlatformSpecific")]
    public void APathThatATarCannotHoldFailsNamesThePathAndLeavesNoArchive()
    {
        using var scratch = new Scratch();
        var folder = scratch.Folder();
        // Ustar keeps at most 100 bytes after the last split at '/', so a 120-character file name cannot be stored.
        var tooLong = $"lib/{new string('n', 120)}.dll";
        Write(folder, tooLong);
        var archive = scratch.PathOf("archive.tar.gz");
        File.WriteAllText(archive, "an older archive");

        var create = () => ReleaseArchive.Create(folder, archive, ArchiveFormat.TarGz, ReleaseArchive.ExecutablesFor(Launcher));

        create.Should().Throw<InvalidOperationException>().WithMessage($"*{tooLong}*");
        File.Exists(archive).Should().BeFalse("no partial or older archive is left");
    }

    [Test]
    [Arguments("win-x64", ArchiveFormat.Zip)]
    [Arguments("win-arm64", ArchiveFormat.Zip)]
    [Arguments("linux-x64", ArchiveFormat.TarGz)]
    [Arguments("linux-musl-arm64", ArchiveFormat.TarGz)]
    [Arguments("osx-arm64", ArchiveFormat.TarGz)]
    public void ARuntimeIdentifierNamesItsFormat(string rid, ArchiveFormat format)
    {
        ReleaseArchive.FormatFor(rid).Should().Be(format);
        ReleaseArchive.ExtensionFor(format).Should().Be(format == ArchiveFormat.Zip ? ".zip" : ".tar.gz");
    }

    private static List<(string Name, UnixFileMode Mode, DateTimeOffset Time)> Entries(string archive)
    {
        var entries = new List<(string, UnixFileMode, DateTimeOffset)>();
        if (archive.EndsWith(".zip", StringComparison.Ordinal))
        {
            using var zip = ZipFile.OpenRead(archive);
            // A zip entry holds a clock time with no time zone, so the clock time is compared with the stamp's.
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

    /// <summary>A test sandbox; disposing it removes the folder with <see cref="TolerantDirectory"/>.</summary>
    private sealed class Scratch : IDisposable
    {
        private readonly TestSandbox _sandbox = TestSandbox.Create("morphir-archive-");

        public string PathOf(string name) => _sandbox.PathOf(name);

        /// <summary>A small publish folder: the launch file, createdump, a nested assembly and a non-ASCII path.</summary>
        public string Folder()
        {
            var folder = Directory.CreateDirectory(PathOf("folder")).FullName;
            foreach (var file in new[] { Launcher, "createdump", "Morphir.Mep.dll", "lib/System.Runtime.dll", "Ä-resources/de/x.resources.dll" })
                Write(folder, file);
            return folder;
        }

        public void Dispose() => _sandbox.Dispose();
    }
}
