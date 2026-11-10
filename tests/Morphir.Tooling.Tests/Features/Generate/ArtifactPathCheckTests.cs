// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Generate;

/// <summary>
/// The host's own check of the artifact paths that an external backend returns, before anything is published.
/// Single paths must be plain relative paths that name the same file on every platform; a set of paths must not
/// name one file twice or use one path as both a file and a directory.
/// </summary>
public class ArtifactPathCheckTests
{
    [Test]
    [Arguments("Pricing.fs")]
    [Arguments("Acme/Pricing.fs")]
    [Arguments(".gitignore")]
    [Arguments("a/.hidden/b.fs")]
    [Arguments("CONSOLE.fs")]
    [Arguments("Com10.fs")]
    [Arguments("LPT.fs")]
    [Arguments("nul-check.fs")]
    public async Task APlainRelativePathIsSafe(string path) =>
        await Assert.That(MepGenerationBackend.UnsafePath(path)).IsNull();

    [Test]
    [Arguments("", "empty")]
    [Arguments("../escape.fs", "'..'")]
    [Arguments("a/../b.fs", "'..'")]
    [Arguments("./a.fs", "'.'")]
    [Arguments("a//b.fs", "''")]
    [Arguments("/abs.fs", "''")]
    [Arguments("a\\b.fs", "\\")]
    [Arguments("C:x.fs", ":")]
    [Arguments("\\\\server\\share\\x.fs", "\\")]
    [Arguments("a\u0001b.fs", "control")]
    [Arguments("A.fs.", "ends with")]
    [Arguments("A.fs ", "ends with")]
    [Arguments("dir./A.fs", "ends with")]
    [Arguments("dir /A.fs", "ends with")]
    [Arguments("CON", "reserved")]
    [Arguments("con.fs", "reserved")]
    [Arguments("NUL.tar.gz", "reserved")]
    [Arguments("aux/x.fs", "reserved")]
    [Arguments("PRN.txt", "reserved")]
    [Arguments("COM1", "reserved")]
    [Arguments("com9.fs", "reserved")]
    [Arguments("LPT1.fs", "reserved")]
    [Arguments("lpt9", "reserved")]
    [Arguments("COM¹.fs", "reserved")]
    [Arguments("LPT³", "reserved")]
    public async Task AnUnsafePathIsRefusedWithItsReason(string path, string reason)
    {
        var refused = MepGenerationBackend.UnsafePath(path);
        refused.Should().NotBeNull(path);
        refused.Should().Contain(reason, path);
        await Task.CompletedTask;
    }

    [Test]
    public async Task DistinctPathsPass() =>
        await Assert.That(MepGenerationBackend.UnsafePaths(["A.fs", "b/C.fs", "b/D.fs", "bb/E.fs"])).IsNull();

    [Test]
    [Arguments("Ab.fs", "AB.fs", "twice")]
    [Arguments("dir/A.fs", "DIR/a.fs", "twice")]
    [Arguments("a", "a/b.fs", "both a file and a directory")]
    [Arguments("a/b.fs", "A", "both a file and a directory")]
    [Arguments("x/y", "x/Y/z.fs", "both a file and a directory")]
    public async Task PathsThatNameOneFileTwiceOrAFileAndADirectoryAreRefused(string first, string second, string reason)
    {
        var refused = MepGenerationBackend.UnsafePaths([first, second]);
        refused.Should().NotBeNull();
        refused!.Value.Reason.Should().Contain(reason);
        await Task.CompletedTask;
    }

    [Test]
    public async Task TheFirstUnsafeSinglePathIsReportedFromTheSet()
    {
        var refused = MepGenerationBackend.UnsafePaths(["A.fs", "CON.fs", "../b.fs"]);
        refused!.Value.Path.Should().Be("CON.fs");
        await Task.CompletedTask;
    }
}
