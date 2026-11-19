// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;

namespace Morphir.Build.Helpers;

/// <summary>
/// Whether a machine builds a platform natively: the operating system and architecture of the RID, with a .NET SDK
/// whose process runs as that architecture. The release jobs check this before they build, so a job scheduled on the
/// wrong runner, or an x64 SDK running under emulation on Windows ARM64, fails rather than building for another platform.
/// The CheckBuildMachine target in Build.ReleaseProfiles.cs reads this machine; this part is pure.
/// </summary>
public static class BuildMachine
{
    /// <summary>
    /// What differs between this machine and a native build machine for <paramref name="target"/>; empty when it is one.
    /// <paramref name="os"/> is <c>windows</c>, <c>macos</c> or <c>linux</c>; the architectures are RID names such as
    /// <c>x64</c> and <c>arm64</c> (see <see cref="ArchitectureName"/>).
    /// </summary>
    public static IReadOnlyList<string> Problems(TargetPlatform target, string os, string osArchitecture, string processArchitecture)
    {
        var problems = new List<string>();
        if (os != target.OperatingSystem)
            problems.Add($"{target.Rid} is built on a {target.OperatingSystem} machine; this machine runs {os}.");
        if (osArchitecture != target.Architecture)
        {
            problems.Add(
                $"{target.Rid} is built on an {target.Architecture} machine; this machine is {osArchitecture}. The release jobs do not cross-compile between architectures.");
        }

        if (processArchitecture != osArchitecture)
        {
            problems.Add(
                $"The .NET SDK runs as an {processArchitecture} process under emulation on this {osArchitecture} machine; install the native {osArchitecture} SDK.");
        }

        return problems;
    }

    /// <summary>The RID name of an architecture: <c>x64</c>, <c>arm64</c>, <c>x86</c> and so on.</summary>
    public static string ArchitectureName(Architecture architecture) => architecture.ToString().ToLowerInvariant();
}
