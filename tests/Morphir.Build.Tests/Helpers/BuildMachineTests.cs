// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Build.Helpers;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// The release jobs build each platform on a machine of that platform, with a .NET SDK that runs natively there. The
/// check names what differs, so a job scheduled on the wrong runner, or an x64 SDK running under emulation on an ARM64
/// machine, fails before it builds rather than producing artifacts for another platform.
/// </summary>
public class BuildMachineTests
{
    [Test]
    [Arguments("win-x64", "windows", "x64", "x64")]
    [Arguments("win-arm64", "windows", "arm64", "arm64")]
    [Arguments("osx-arm64", "macos", "arm64", "arm64")]
    [Arguments("linux-x64", "linux", "x64", "x64")]
    [Arguments("linux-arm64", "linux", "arm64", "arm64")]
    public void ANativeMachineHasNoProblems(string rid, string os, string osArchitecture, string processArchitecture) =>
        BuildMachine.Problems(Platform(rid), os, osArchitecture, processArchitecture).Should().BeEmpty();

    [Test]
    public void AnX64RunnerIsNotAWindowsArm64BuildMachine()
    {
        var problems = BuildMachine.Problems(TargetPlatform.WinArm64, "windows", "x64", "x64");

        problems.Should().ContainSingle().Which.Should().Contain("win-arm64").And.Contain("arm64").And.Contain("x64");
    }

    [Test]
    public void AnX64SdkUnderEmulationOnWindowsArm64IsRefused()
    {
        // Windows on ARM runs x64 programs under emulation; an x64 SDK there reports an ARM64 machine and an x64 process.
        var problems = BuildMachine.Problems(TargetPlatform.WinArm64, "windows", "arm64", "x64");

        problems.Should().ContainSingle().Which.Should().Contain("emulation").And.Contain("x64");
    }

    [Test]
    public void AnotherOperatingSystemIsRefused()
    {
        var problems = BuildMachine.Problems(TargetPlatform.WinArm64, "linux", "arm64", "arm64");

        problems.Should().ContainSingle().Which.Should().Contain("windows").And.Contain("linux");
    }

    [Test]
    public void EveryDifferenceIsNamed() =>
        BuildMachine.Problems(TargetPlatform.WinArm64, "macos", "x64", "arm64").Should().HaveCount(3);

    [Test]
    public void TheArchitectureNamesFollowTheRuntimeIdentifiers()
    {
        BuildMachine.ArchitectureName(System.Runtime.InteropServices.Architecture.X64).Should().Be("x64");
        BuildMachine.ArchitectureName(System.Runtime.InteropServices.Architecture.Arm64).Should().Be("arm64");
        BuildMachine.ArchitectureName(System.Runtime.InteropServices.Architecture.X86).Should().Be("x86");
    }

    private static TargetPlatform Platform(string rid)
    {
        TargetPlatform.TryParse(rid, out var platform).Should().BeTrue();
        return platform!;
    }
}
