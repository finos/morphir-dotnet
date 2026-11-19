// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Morphir.Build.Helpers;
using Morphir.TestSupport;
using TUnit.Core;

namespace Morphir.Build.Tests;

[Category("PlatformSpecific")]
public class ExtensionProcessProducerTests
{
    [Test]
    [Arguments("Morphir.Frontend.FSharp.Extension", "morphir-fsharp-frontend", "win-x64")]
    [Arguments("Morphir.CodeGeneration.FSharp.Extension", "morphir-fsharp-backend", "win-x64")]
    [Arguments("Morphir.Frontend.FSharp.Extension", "morphir-fsharp-frontend", "win-arm64")]
    [Arguments("Morphir.CodeGeneration.FSharp.Extension", "morphir-fsharp-backend", "win-arm64")]
    public async Task SelfContainedProcessIsACompleteUntrimmedRuntimeFolder(string project, string command, string rid)
    {
        var properties = await Properties(project, "-p:PublishProfile=SelfContainedProcess", "-p:RuntimeIdentifier=" + rid, "-p:MorphirRidSpecificPublish=true");
        properties["SelfContained"].Should().Be("true");
        foreach (var name in new[] { "PublishSingleFile", "PublishTrimmed", "PublishAot", "PublishReadyToRun" })
            properties[name].Should().Be("false", name + " must not remove runtime/compiler/generator dependencies");
        properties["AssemblyName"].Should().Be(command);
        properties["NuGetLockFilePath"].Replace('\\', '/').Should().EndWith($"{project}/obj/publish-lock/{rid}/packages.lock.json");
    }

    [Test]
    [Arguments("Morphir.Frontend.FSharp.Extension", "morphir-fsharp-frontend")]
    [Arguments("Morphir.CodeGeneration.FSharp.Extension", "morphir-fsharp-backend")]
    public async Task OrdinaryDeliveryRemainsItsOwnFrameworkDependentTool(string project, string command)
    {
        var properties = await Properties(project);
        properties["PackAsTool"].Should().Be("true");
        properties["ToolCommandName"].Should().Be(command);
        properties["PackageId"].Should().Be(project);
        properties["SelfContained"].Should().NotBe("true");
    }

    [Test]
    [Arguments("PublishFSharpFrontendProcess")]
    [Arguments("PublishFSharpBackendProcess")]
    [Arguments("PackWindowsToolchain")]
    public void BothProvidersHaveAnOfficialFocusedPublishTarget(string target)
    {
        var build = typeof(ReleaseArchive).Assembly.GetType("Build", throwOnError: true)!;
        build.GetProperty(target, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Should().NotBeNull("the Windows toolchain uses the existing producers");
    }

    private static async Task<Dictionary<string, string>> Properties(string project, params string[] options)
    {
        var root = TestFixture.GetRepositoryRoot();
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var names = "SelfContained,PublishSingleFile,PublishTrimmed,PublishAot,PublishReadyToRun,AssemblyName,NuGetLockFilePath,PackAsTool,ToolCommandName,PackageId";
        var start = ChildProcess.StartInfo(host,
            new[] { "msbuild", Path.Combine(root, "src", project, project + ".fsproj"), "-getProperty:" + names, "-nologo", "-m:1", "-p:UseSharedCompilation=false", "-nodeReuse:false" }.Concat(options).ToArray());
        start.WorkingDirectory = root;
        var result = await ChildProcess.RunAsync(start, TimeSpan.FromMinutes(2));
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        using var json = JsonDocument.Parse(result.StandardOutput);
        return json.RootElement.GetProperty("Properties").EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString()!);
    }
}
