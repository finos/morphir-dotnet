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

    /// <summary>
    /// A compacted single file is read as deep as a plain one, by the number of tuple types around its deepest type
    /// reference (two levels each): deeper than the readers of a tree (512), and near the depth of a plain single file
    /// (4096). It expands, on the store's large stack, and the read gives what the read of its plain twin gives.
    /// </summary>
    [Test]
    [Arguments(300)]
    [Arguments(2000)]
    public async Task ACompactedSingleFileIsReadToTheDepthOfAPlainOne(int tuples)
    {
        var (compacted, plain) = await ReadWithItsPlainTwin(tuples);

        plain.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(compacted.Diagnostics.Select(diagnostic => diagnostic.Code),
            "the compacted file reads as its plain twin");
        compacted.Diagnostics.Should().NotContain(diagnostic => diagnostic.Code == "invalid_json");
        compacted.Encoding.Should().Be(ModelConvertNames.Compacted);
        (compacted.Model is null).Should().Be(plain.Model is null);
    }

    /// <summary>A compacted single file just past the depth of a plain one is refused with <c>depth_limit_exceeded</c>.</summary>
    [Test]
    public async Task ACompactedSingleFileJustPastTheDepthLimitIsTooDeep()
    {
        var (compacted, _) = await ReadWithItsPlainTwin(2060);

        compacted.Model.Should().BeNull();
        compacted.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("depth_limit_exceeded");
        compacted.Encoding.Should().Be(ModelConvertNames.Compacted);
    }

    /// <summary>Reads the deeply nested compacted file of the mother and its plain twin with the disk store.</summary>
    private static async Task<(ModelRead Compacted, ModelRead Plain)> ReadWithItsPlainTwin(int tuples)
    {
        using var sandbox = TestSandbox.Create("morphir-convert-deep-");
        var (bytes, plainText) = Morphir.Models.TestSupport.NamePositionMother.deeplyNestedCompactedFile(tuples);
        var compacted = sandbox.PathOf("deep.json");
        var twin = sandbox.PathOf("deep-plain.json");
        await File.WriteAllBytesAsync(compacted, bytes);
        await File.WriteAllTextAsync(twin, plainText);
        var store = new DiskModelConversionStore();

        var read = await store.ReadAsync(new ModelSource(compacted, "deep.json", Morphir.IR.Trees.ModelLayout.SingleFile), CancellationToken.None);
        var plainRead = await store.ReadAsync(new ModelSource(twin, "deep-plain.json", Morphir.IR.Trees.ModelLayout.SingleFile), CancellationToken.None);
        return (read, plainRead);
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
