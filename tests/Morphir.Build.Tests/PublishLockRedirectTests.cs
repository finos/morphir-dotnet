// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using FluentAssertions;
using Morphir.Build.Helpers;
using Morphir.TestSupport;
using TUnit.Core;

namespace Morphir.Build.Tests;

/// <summary>
/// A publish for one runtime identifier restores for that RID, and NuGet would add a RID section to every committed
/// packages.lock.json of the publish. The build sets <see cref="TargetSettings.RidSpecificPublishProperty"/> on such a
/// publish, and src/Directory.Build.props then writes the lock files under obj/publish-lock/&lt;rid&gt;/ instead.
/// </summary>
[Category("PlatformSpecific")]
public class PublishLockRedirectTests
{
    private const string Project = "src/Morphir.Managed/Morphir.Managed.csproj";

    [Test]
    public async Task ARidSpecificPublishWritesItsLockFileUnderObj()
    {
        var value = await LockFilePath($"-p:{TargetSettings.RidSpecificPublishProperty}=true", "-p:RuntimeIdentifier=linux-x64");
        value.Replace('\\', '/').Should().EndWith("src/Morphir.Managed/obj/publish-lock/linux-x64/packages.lock.json");
    }

    [Test]
    [Arguments("-p:RuntimeIdentifier=linux-x64")]
    [Arguments("-p:MorphirRidSpecificPublish=true")]
    [Arguments("-p:MorphirRidSpecificPublish=false", "-p:RuntimeIdentifier=linux-x64")]
    public async Task OtherBuildsKeepTheCommittedLockFile(params string[] properties) =>
        (await LockFilePath(properties)).Should().BeEmpty("NuGet then uses the committed packages.lock.json beside the project");

    private static async Task<string> LockFilePath(params string[] properties)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = TestFixture.GetRepositoryRoot() };
        foreach (var argument in new[] { "msbuild", Path.Combine(TestFixture.GetRepositoryRoot(), Project), "-getProperty:NuGetLockFilePath", "-nologo", "-nodeReuse:false" }.Concat(properties))
            start.ArgumentList.Add(argument);
        var result = await ChildProcess.RunAsync(start, TimeSpan.FromMinutes(2));
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        return result.StandardOutput.Trim();
    }
}
