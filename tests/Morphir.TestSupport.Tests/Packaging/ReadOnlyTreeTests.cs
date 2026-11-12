// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Packaging;
using Morphir.TestSupport;

namespace Morphir.TestSupport.Tests.Packaging;

/// <summary>Making an installation read-only for the packaged feature, and giving it back writable.</summary>
[Category("PlatformSpecific")]
public sealed class ReadOnlyTreeTests
{
    [Test]
    public void ATreeIsReadOnlyUntilItIsRestoredAndThenItCanBeDeleted()
    {
        if (!OperatingSystem.IsWindows() && Environment.UserName == "root")
            Skip.Test("root writes through any mode bits, so the tree cannot be made read-only for this user");

        using var sandbox = TestSandbox.Create("morphir read only ");
        var root = Directory.CreateDirectory(sandbox.PathOf("install dir")).FullName;
        var sub = Directory.CreateDirectory(Path.Combine(root, "sub folder")).FullName;
        File.WriteAllText(Path.Combine(root, "tool"), "x");
        File.WriteAllText(Path.Combine(sub, "library"), "y");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Path.Combine(root, "tool"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var before = ReadOnlyTree.Snapshot(root);

        using (var tree = ReadOnlyTree.Apply(root))
        {
            ReadOnlyTree.WriteProblems(root).Should().BeEmpty("no file can be added, changed or deleted anywhere in the tree");
            if (!OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(Path.Combine(root, "tool")).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserExecute);
                File.GetUnixFileMode(Path.Combine(sub, "library")).Should().NotHaveFlag(UnixFileMode.UserWrite);
            }
        }

        ReadOnlyTree.Snapshot(root).Should().Equal(before);
        File.WriteAllText(Path.Combine(sub, "new"), "z");
        File.AppendAllText(Path.Combine(root, "tool"), "x");
        if (!OperatingSystem.IsWindows())
            File.GetUnixFileMode(Path.Combine(root, "tool")).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Directory.Delete(root, recursive: true);
        Directory.Exists(root).Should().BeFalse();
    }

    [Test]
    public void AWritableTreeHasWriteProblemsToReport()
    {
        using var sandbox = TestSandbox.Create("morphir read only ");
        var root = Directory.CreateDirectory(sandbox.PathOf("install dir")).FullName;
        File.WriteAllText(Path.Combine(root, "tool"), "x");

        ReadOnlyTree.WriteProblems(root).Should().NotBeEmpty();
        ReadOnlyTree.Snapshot(root).Should().Equal(["tool 1"], "the probe leaves nothing behind");
    }
}
