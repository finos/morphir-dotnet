// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using FluentAssertions;
using Morphir.TestSupport;
using TUnit.Core;

namespace Morphir.Build.Tests;

/// <summary>
/// A publish for one runtime identifier restores for that RID, and NuGet would add a RID section to every committed
/// packages.lock.json of the publish. The build sets MorphirRidSpecificPublish=true on every RID-specific publish
/// that it drives (PublishFSharpFrontendProcess among them), and src/Directory.Build.props then writes the lock files
/// under obj/publish-lock/&lt;rid&gt;/. The redirect does not depend on a publish profile.
/// </summary>
public class ExtensionProcessLockTests
{
    private const string Project = "src/Morphir.Frontend.FSharp.Extension/Morphir.Frontend.FSharp.Extension.fsproj";

    [Test]
    public async Task ARidSpecificPublishOfTheBuildWritesItsLockFileUnderObj()
    {
        var value = await LockFilePath("-p:MorphirRidSpecificPublish=true", "-p:RuntimeIdentifier=linux-x64");
        value.Replace('\\', '/').Should().EndWith("src/Morphir.Frontend.FSharp.Extension/obj/publish-lock/linux-x64/packages.lock.json");
    }

    [Test]
    [Arguments("-p:RuntimeIdentifier=linux-x64")]
    [Arguments("-p:PublishProfile=SelfContainedProcess", "-p:RuntimeIdentifier=linux-x64")]
    [Arguments("-p:MorphirRidSpecificPublish=true")]
    [Arguments("-p:MorphirRidSpecificPublish=false", "-p:RuntimeIdentifier=linux-x64")]
    public async Task OtherBuildsKeepTheCommittedLockFile(params string[] properties) =>
        (await LockFilePath(properties)).Should().BeEmpty("NuGet then uses the committed packages.lock.json beside the project");

    private static async Task<string> LockFilePath(params string[] properties)
    {
        var root = TestFixture.GetRepositoryRoot();
        var start = ChildProcess.StartInfo(
            "dotnet",
            new[] { "msbuild", Path.Combine(root, Project), "-getProperty:NuGetLockFilePath", "-nologo", "-nodeReuse:false" }
                .Concat(properties)
                .ToArray());
        start.WorkingDirectory = root;
        var result = await ChildProcess.RunAsync(start, TimeSpan.FromMinutes(2));
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        return result.StandardOutput.Trim();
    }
}
