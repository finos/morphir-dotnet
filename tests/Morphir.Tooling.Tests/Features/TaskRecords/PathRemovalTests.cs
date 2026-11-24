// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.Tooling.Features.TaskRecords;
using Path = System.IO.Path;

namespace Morphir.Tooling.Tests.Features.TaskRecords;

/// <summary>
/// The removal of a staged or rolled-back IR, against the file system itself. A path whose lookup the file system denies
/// is not a missing path: the removal fails and keeps the reason.
/// </summary>
[Category("PlatformSpecific")]
public sealed class PathRemovalTests
{
    [Test]
    public void ADeniedLookupIsARefusalWithItsReasonAndNotAMissingPath()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test("A Unix parent without search permission denies the lookup.");
            return;
        }
        using var sandbox = TestSandbox.Create("morphir-removal-");
        var parent = sandbox.PathOf("dest");
        var staged = Path.Combine(parent, ".morphir-ir.staging");
        Directory.CreateDirectory(staged);
        var searchable = File.GetUnixFileMode(parent);
        File.SetUnixFileMode(parent, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        bool removed;
        Exception? failure;
        try
        {
            try
            {
                File.GetAttributes(staged);
                Skip.Test("This user is not denied the lookup (for example root), so the case cannot be made.");
            }
            catch (UnauthorizedAccessException)
            {
                // The lookup is denied, as the case needs.
            }
            removed = PathRemoval.Default.TryRemove(staged, out failure);
        }
        finally
        {
            File.SetUnixFileMode(parent, searchable);
        }

        removed.Should().BeFalse("a path whose lookup is denied may still be there");
        failure.Should().NotBeNull().And.Match(error => error is UnauthorizedAccessException || error is IOException);
        Directory.Exists(staged).Should().BeTrue("nothing was removed");
    }
}
