// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport;
using TUnit.Core;

namespace Morphir.Build.Tests;

/// <summary>
/// The projects of this repository use the System.Text.Json plugin through
/// src/Morphir.Myriad.SystemTextJson/repository/Morphir.Myriad.SystemTextJson.Repository.targets: the plugin, the task
/// and the Myriad host come from build outputs. An IDE loads such a project before the first build, when the task
/// assembly is not built. It runs CoreCompile without the compiler, then reads the packages to prune. A failure there
/// makes the IDE restore without pruning, and the lock file then refuses the restore (NU1102).
/// </summary>
[Category("PlatformSpecific")]
public class MyriadSystemTextJsonRepositoryTargetsTests
{
    private static readonly string[] SourceProjects =
    [
        "src/Morphir.Myriad.SystemTextJson",
        "src/Morphir.Myriad.SystemTextJson.Build",
        "src/Morphir.Myriad.SystemTextJson.Annotations",
        "src/Morphir.Myriad.Hosting",
    ];

    /// <summary>The length and the time of each restore file (obj, not below a configuration) of the source projects.</summary>
    private static Dictionary<string, (long, DateTime)> RestoreFiles(string root) =>
        SourceProjects
            .Select(project => Path.Combine(root, project, "obj"))
            .Where(Directory.Exists)
            .SelectMany(obj => Directory.EnumerateFiles(obj))
            .ToDictionary(file => file, file => (new FileInfo(file).Length, File.GetLastWriteTimeUtc(file)));

    [Test]
    public async Task AnIdeProjectLoadPassesWhenTheTaskAssemblyIsNotBuilt()
    {
        var root = TestFixture.GetRepositoryRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Release";
        var before = RestoreFiles(root);
        using var sandbox = TestSandbox.Create("stj-design-time-");
        string Source(string relative) => Path.Combine(root, relative);
        File.WriteAllText(Path.Combine(sandbox.Work, "Types.fs"), "namespace Fixture\n\nmodule Marker =\n    let value = 0\n");
        File.WriteAllText(Path.Combine(sandbox.Work, "Fixture.fsproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
                <Nullable>enable</Nullable>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Types.fs" />
                <FSharpJsonContext Include="Types.fs" Name="FixtureJson" Namespace="Fixture.Json" />
              </ItemGroup>
              <ItemGroup>
                <ProjectReference Include="{Source("src/Morphir.Myriad.SystemTextJson/Morphir.Myriad.SystemTextJson.fsproj")}" ReferenceOutputAssembly="false" PrivateAssets="all" />
                <ProjectReference Include="{Source("src/Morphir.Myriad.SystemTextJson.Build/Morphir.Myriad.SystemTextJson.Build.csproj")}" ReferenceOutputAssembly="false" PrivateAssets="all" />
                <ProjectReference Include="{Source("src/Morphir.Myriad.SystemTextJson.Annotations/Morphir.Myriad.SystemTextJson.Annotations.csproj")}" />
              </ItemGroup>
              <Import Project="{Source("src/Morphir.Myriad.SystemTextJson/repository/Morphir.Myriad.SystemTextJson.Repository.targets")}" />
            </Project>
            """);
        var missing = Path.Combine(sandbox.Work, "not-built", "Morphir.Myriad.SystemTextJson.Build.dll");

        // RestoreRecursive=false restores the fixture only. A restore of the source projects from the sandbox (its
        // NuGet configuration and its NUGET_PACKAGES) writes their restore files again, and the next solution build
        // then compiles them and each project that uses them.
        var start = ChildProcess.StartInfo(
            "dotnet",
            "msbuild",
            "Fixture.fsproj",
            "-restore",
            "-p:RestoreRecursive=false",
            "-t:Compile;CoreCompile;CollectPrunePackageReferences",
            $"-p:Configuration={configuration}",
            "-p:SkipCompilerExecution=true",
            "-p:BuildProjectReferences=false",
            $"-p:MorphirSystemTextJsonTaskAssembly={missing}",
            "-nologo");
        start.WorkingDirectory = sandbox.Work;
        sandbox.Apply(start);
        var result = await ChildProcess.RunAsync(start, TimeSpan.FromMinutes(2));

        result.ExitCode.Should().Be(0, $"the project load targets pass: {result.CombinedOutput}");
        RestoreFiles(root).Should().Equal(before, "the load of the fixture does not restore the source projects again");
    }
}
