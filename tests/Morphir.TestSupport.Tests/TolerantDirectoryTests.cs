// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;

namespace Morphir.TestSupport.Tests;

[Category("PlatformSpecific")]
public class TolerantDirectoryTests
{
    [Test]
    public async Task Delete_ReleasedDuringRetries_Succeeds()
    {
        var root = Directory.CreateTempSubdirectory("morphir-tolerant-").FullName;
        var file = Path.Combine(root, "locked.bin");
        var handle = new FileStream(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var logged = new List<string>();

        // Release the handle while the delete is retrying (on Windows the first attempt fails).
        var release = Task.Run(async () =>
        {
            await Task.Delay(150);
            await handle.DisposeAsync();
        });

        var deleted = await TolerantDirectory.TryDeleteAsync(root, new TolerantDeleteOptions
        {
            Attempts = 10,
            InitialDelay = TimeSpan.FromMilliseconds(50),
            Log = logged.Add,
        });
        await release;

        deleted.Should().BeTrue();
        Directory.Exists(root).Should().BeFalse();
        logged.Should().BeEmpty("a delete that eventually succeeds is not reported");
    }

    [Test]
    public async Task Delete_LockNeverReleased_LogsInsteadOfThrowing()
    {
        var root = Directory.CreateTempSubdirectory("morphir-tolerant-").FullName;
        var attempts = 0;
        var logged = new List<string>();

        try
        {
            var deleted = await TolerantDirectory.TryDeleteAsync(root, new TolerantDeleteOptions
            {
                Attempts = 3,
                InitialDelay = TimeSpan.FromMilliseconds(1),
                Log = logged.Add,
                DeleteDirectory = _ =>
                {
                    attempts++;
                    throw new IOException("The process cannot access the file because it is being used by another process.");
                },
            });

            deleted.Should().BeFalse();
            attempts.Should().Be(3);
            logged.Should().ContainSingle().Which.Should().Contain(root).And.Contain("being used by another process");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Delete_MissingDirectory_IsSuccess()
    {
        var missing = Path.Combine(Path.GetTempPath(), "morphir-tolerant-missing-" + Guid.NewGuid().ToString("N"));

        (await TolerantDirectory.TryDeleteAsync(missing)).Should().BeTrue();
        TolerantDirectory.TryDelete(missing).Should().BeTrue();
    }

    [Test]
    public async Task Delete_Synchronous_RemovesTree()
    {
        var root = Directory.CreateTempSubdirectory("morphir-tolerant-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        await File.WriteAllTextAsync(Path.Combine(root, "a", "b", "c.txt"), "x");

        TolerantDirectory.TryDelete(root).Should().BeTrue();
        Directory.Exists(root).Should().BeFalse();
    }
}
