// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.Tooling.Features.ModelConvert;

namespace Morphir.Tooling.Tests.Features.ModelConvert;

/// <summary>
/// The cleanup of the disk store's staging. A failed write deletes its staging in a <c>finally</c>; the delete is best
/// effort, so a file system that refuses it cannot replace the write's own result with an exception.
/// </summary>
[Category("PlatformSpecific")] // A file held open on the real file system.
public sealed class ModelConversionStoreTests
{
    [Test]
    public void AStagingDirectoryThatCannotBeDeletedDoesNotThrow()
    {
        using var sandbox = TestSandbox.Create("morphir-convert-cleanup-");
        var staging = Directory.CreateDirectory(sandbox.PathOf(".out.convert-held")).FullName;
        using (new FileStream(Path.Combine(staging, "manifest.json"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            var cleanup = () => DiskModelConversionStore.DeleteStaging(staging);

            cleanup.Should().NotThrow();
            // Windows refuses to delete a file that is open; other systems delete it.
            if (OperatingSystem.IsWindows()) Directory.Exists(staging).Should().BeTrue("the open file keeps the staging directory");
        }

        DiskModelConversionStore.DeleteStaging(staging);
        Directory.Exists(staging).Should().BeFalse();
    }

    [Test]
    public void AStagingFileThatCannotBeDeletedDoesNotThrow()
    {
        using var sandbox = TestSandbox.Create("morphir-convert-cleanup-");
        var staging = sandbox.PathOf(".out.json.convert-held.tmp");
        using (new FileStream(staging, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            var cleanup = () => DiskModelConversionStore.DeleteStaging(staging);

            cleanup.Should().NotThrow();
            if (OperatingSystem.IsWindows()) File.Exists(staging).Should().BeTrue("the open file is not deleted");
        }

        DiskModelConversionStore.DeleteStaging(staging);
        File.Exists(staging).Should().BeFalse();
    }
}
