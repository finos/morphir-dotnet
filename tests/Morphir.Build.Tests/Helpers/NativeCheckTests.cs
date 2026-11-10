// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Build.Helpers;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// The shared check of a Native AOT executable: what it needs from a Linux machine by name, the commands that each
/// executable supplies with their expected outcomes, and the table a report-mode probe writes.
/// </summary>
public class NativeCheckTests
{
    [Test]
    public void EveryNeededSharedLibraryIsReportedByName()
    {
        const string dynamic = """

            Dynamic section at offset 0x5e3a10 contains 30 entries:
              Tag        Type                         Name/Value
             0x0000000000000001 (NEEDED)             Shared library: [libm.so.6]
             0x0000000000000001 (NEEDED)             Shared library: [libgcc_s.so.1]
             0x0000000000000001 (NEEDED)             Shared library: [libc.so.6]
             0x0000000000000001 (NEEDED)             Shared library: [ld-linux-x86-64.so.2]
             0x000000000000000c (INIT)               0x1d000
             0x000000006ffffffe (VERNEED)            0x1c40
            """;

        NativeCheck.NeededLibraries(dynamic).Should().Equal("libm.so.6", "libgcc_s.so.1", "libc.so.6", "ld-linux-x86-64.so.2");
        NativeCheck.NeededLibraries("There is no dynamic section in this file.").Should().BeEmpty();
    }

    [Test]
    public void TheGlibcFloorAndBuildImageAreTheOnesMorphirupReleasesWith()
    {
        NativeCheck.GlibcFloor.Should().Be(new Version(2, 35));
        MorphirupRelease.GlibcFloor.Should().Be(NativeCheck.GlibcFloor);
        MorphirupRelease.LinuxBuildImage.Should().Be(NativeCheck.LinuxBuildImage);
        NativeCheck.CleanImages.Should().Equal("debian:12-slim", NativeCheck.LinuxBuildImage);
    }

    [Test]
    public void ACommandFillsInItsInputAndScratchFolders()
    {
        var command = new CheckCommand("generate", ["ICU title-casing"],
            ["generate", "--input", "{inputs}/model.json", "--output", "{scratch}/out"]);

        command.Expand("/check/probe inputs", "/scratch").Should().Equal("generate", "--input", "/check/probe inputs/model.json", "--output", "/scratch/out");
        command.Expect.Should().Be(CheckExpectation.Succeeds, "a command expects success unless it says otherwise");
    }

    [Test]
    [Arguments(0, null, null, "", "", true)]
    [Arguments(1, "was stopped", null, "", "MSBuild evaluation wrote more than 1048576 bytes and was stopped.", true)]
    [Arguments(1, null, "not implemented", "Install a distribution (not implemented yet)", "", false)]
    [Arguments(134, null, null, "", "No usable version of libssl was found", null)]
    public void AnOutcomeSaysWhetherTheCommandsPathRan(int exit, string? marker, string? notReached, string stdout, string stderr, bool? reached)
    {
        var command = new CheckCommand("probe", ["x"], ["x"]) { Marker = marker, NotReachedMarker = notReached, Expect = CheckExpectation.Report };
        NativeCheck.Outcome("runner", command, exit, timedOut: false, stdout, stderr).Reached.Should().Be(reached);
    }

    [Test]
    public void TheTableNamesTheSignalAndKeepsOneLineOfStandardError()
    {
        var command = new CheckCommand("ion-tree", ["SHA-256 (Ion tree cut stem)"], ["generate"]) { Expect = CheckExpectation.Report };
        var outcomes = new[]
        {
            NativeCheck.Outcome("runner", command, 0, false, "{}", ""),
            NativeCheck.Outcome("debian:12-slim", command, 134, false, "", "No usable version of libssl was found\nmore | text"),
        };

        var table = NativeCheck.Table(outcomes);

        table.Should().Contain("| debian:12-slim | ion-tree | SHA-256 (Ion tree cut stem) | 134 (SIGABRT) | unknown | No usable version of libssl was found |");
        table.Should().Contain("| runner | ion-tree | SHA-256 (Ion tree cut stem) | 0 | yes |");
        table.Should().NotContain("more | text", "only the first line of standard error is kept, so a pipe cannot break the table");
        NativeCheck.ExitText(139).Should().Be("139 (SIGSEGV)");
        NativeCheck.ExitText(133).Should().Be("133 (SIGTRAP)");
        NativeCheck.ExitText(2).Should().Be("2");
    }

    [Test]
    public void ASucceedingCommandThatFailsIsAProblemAndAReportCommandNeverIs()
    {
        var checkedCommand = new CheckCommand("version", ["startup"], ["--version"]);
        var reported = checkedCommand with { Expect = CheckExpectation.Report };

        NativeCheck.Outcome("debian:12-slim", checkedCommand, 1, false, "", "boom").Problem
            .Should().Be("version in debian:12-slim: exit code 1\nboom");
        NativeCheck.Outcome("debian:12-slim", checkedCommand, 0, false, "1.2.3", "").Problem.Should().BeNull();
        NativeCheck.Outcome("debian:12-slim", reported, 139, false, "", "crash").Problem.Should().BeNull();
        NativeCheck.Outcome("runner", checkedCommand, -1, true, "", "").Problem.Should().Be("version in runner: timed out");
    }

    [Test]
    public void EveryCallSiteThatReachesOpenSslIsListed()
    {
        var sources = new Dictionary<string, string>
        {
            ["src/Morphir.Ion/Ir/TreeStems.fs"] = "let digest text =\n    Convert.ToHexString(SHA256.HashData(bytes))\n",
            ["src/Morphir.Tooling/Features/Management/DistHandlers.cs"] = "// HttpClient in a comment is not a call\n    HttpClient httpClient,\n",
            ["src/Morphir.Mep/Nonce.fs"] = "let nonce = RandomNumberGenerator.GetBytes 16\n",
            ["src/Morphir.Core/Plain.cs"] = "var x = 1;\n",
        };

        NativeCheck.OpenSslCallSites(sources).Select(site => $"{site.Path}:{site.Line} {site.Api}").Should().Equal(
            "src/Morphir.Ion/Ir/TreeStems.fs:2 SHA256",
            "src/Morphir.Mep/Nonce.fs:1 RandomNumberGenerator",
            "src/Morphir.Tooling/Features/Management/DistHandlers.cs:2 HttpClient");
    }
}
