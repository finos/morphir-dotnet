// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;
using System.Text;
using FluentAssertions;
using Morphir.E2E.Tests.Infrastructure;

namespace Morphir.E2E.Tests.InfrastructureTests;

/// <summary>
/// Paths in a host trace are what the host resolved; the test's own paths may go through a link (macOS
/// <c>/var</c> → <c>/private/var</c>) or a Windows short name (<c>RUNNER~1</c>). Both sides are compared canonically.
/// </summary>
public sealed class CanonicalPathTests
{
    [Test]
    public void APathThroughALinkIsUnderTheLinkTarget()
    {
        var root = Directory.CreateTempSubdirectory("morphir canonical ").FullName;
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(root, "real install")).FullName;
            var link = Path.Combine(root, "linked install");
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                Skip.Test($"This account cannot create a symbolic link here: {exception.Message}");
            }

            File.WriteAllText(Path.Combine(target, "hostfxr.dll"), "");
            CanonicalPath.Of(Path.Combine(link, "hostfxr.dll")).Should().Be(CanonicalPath.Of(Path.Combine(target, "hostfxr.dll")));
            HostTrace.IsUnder(Path.Combine(link, "hostfxr.dll"), target).Should().BeTrue();
            HostTrace.IsUnder(Path.Combine(target, "hostfxr.dll"), link).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void AWindowsShortNameIsTheLongName()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Short names exist only on Windows.");
        var root = Directory.CreateTempSubdirectory("morphir canonical long name ").FullName;
        try
        {
            var buffer = new StringBuilder(1024);
            var length = GetShortPathName(root, buffer, buffer.Capacity);
            var shortName = buffer.ToString(0, (int)length);
            if (length == 0 || string.Equals(shortName, root, StringComparison.OrdinalIgnoreCase))
                Skip.Test("This volume has no 8.3 short names.");

            CanonicalPath.Of(Path.Combine(shortName, "hostfxr.dll")).Should().Be(CanonicalPath.Of(Path.Combine(root, "hostfxr.dll")));
            HostTrace.IsUnder(Path.Combine(root, "hostfxr.dll"), shortName).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void AMissingTailIsKept()
    {
        var root = Directory.CreateTempSubdirectory("morphir canonical ").FullName;
        try
        {
            CanonicalPath.Of(Path.Combine(root, "not there", "file.txt"))
                .Should().Be(Path.Combine(CanonicalPath.Of(root), "not there", "file.txt"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = CharSet.Unicode)]
    private static extern uint GetShortPathName(string longPath, StringBuilder shortPath, int length);
}
