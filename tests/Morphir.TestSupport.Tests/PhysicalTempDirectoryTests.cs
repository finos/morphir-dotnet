// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;

namespace Morphir.TestSupport.Tests;

[Category("PlatformSpecific")]
public class PhysicalTempDirectoryTests
{
    [Test]
    public void TemporaryDirectoriesHaveNoLinkedAncestor()
    {
        var path = Directory.CreateTempSubdirectory("morphir-physical-temp-").FullName;
        try
        {
            for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
                directory.Attributes.HasFlag(FileAttributes.ReparsePoint).Should().BeFalse(directory.FullName);
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
