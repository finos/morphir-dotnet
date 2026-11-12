// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>The contained folder of one test (design DRV-1 and DRV-4 of the change consolidate-black-box-tests).</summary>
[Category("PlatformSpecific")]
public class ScenarioWorkspaceTests
{
    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Workspace");

    private static ScenarioWorkspace Create() => ScenarioWorkspace.Create(Fixtures);

    private static IEnumerable<string> Paths(IScenarioWorkspace workspace) =>
        [workspace.Root, workspace.Work.Path, workspace.Home.Path, workspace.MorphirHome.Path, workspace.Temp.Path];

    private static async Task<Dictionary<string, string>> ChildEnvironment(IScenarioWorkspace workspace)
    {
        var info = OperatingSystem.IsWindows()
            ? ChildProcess.StartInfo("cmd.exe", "/c", "set")
            : ChildProcess.StartInfo("/usr/bin/env");
        info.Environment["MORPHIR_PROFILES"] = "inherited";
        workspace.Contain(info);
        var result = await ChildProcess.RunAsync(info, TimeSpan.FromSeconds(30));
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Contains('='))
            .GroupBy(line => line[..line.IndexOf('=')], comparer)
            .ToDictionary(group => group.Key, group => group.Last()[(group.Last().IndexOf('=') + 1)..], comparer);
    }

    [Test]
    public async Task TwoWorkspacesHaveNoPathInCommon()
    {
        await using var first = Create();
        await using var second = Create();

        Paths(first).Should().NotIntersectWith(Paths(second));
        foreach (var path in Paths(first))
            path.Should().NotStartWith(second.Root).And.StartWith(first.Root);
    }

    [Test]
    public async Task ThePathOfTheWorkspaceHasNoLink()
    {
        await using var workspace = Create();

        for (var directory = new DirectoryInfo(workspace.Root); directory is not null; directory = directory.Parent)
            directory.LinkTarget.Should().BeNull($"{directory.FullName} is on the path of the workspace");
    }

    [Test]
    public async Task EachLocationOfTheWorkspaceIsADirectory()
    {
        await using var workspace = Create();

        Directory.Exists(workspace.Work.Path).Should().BeTrue();
        Directory.Exists(workspace.Home.Path).Should().BeTrue();
        Directory.Exists(workspace.Temp.Path).Should().BeTrue();
        Directory.Exists(workspace.MorphirHome.Path).Should().BeFalse("each test decides if the Morphir home is there");
    }

    [Test]
    public async Task AProductProcessGetsTheHomeTheMachineLocationsAndTheTemporaryDirectoryOfTheWorkspace()
    {
        await using var workspace = Create();

        var environment = await ChildEnvironment(workspace);

        environment["HOME"].Should().Be(workspace.Home.Path);
        environment["USERPROFILE"].Should().Be(workspace.Home.Path);
        environment["MORPHIR_HOME"].Should().Be(workspace.MorphirHome.Path);
        environment["MORPHIR_DISCOVERY_BOUNDARY"].Should().Be(workspace.Root);
        environment["MORPHIR_ISOLATED"].Should().Be("1");
        foreach (var name in new[] { "APPDATA", "LOCALAPPDATA", "PROGRAMDATA", "XDG_CONFIG_HOME", "XDG_CACHE_HOME" })
            environment[name].Should().StartWith(workspace.Root, $"{name} is a machine location");
        foreach (var name in new[] { "TMPDIR", "TEMP", "TMP" })
            environment[name].TrimEnd(Path.DirectorySeparatorChar).Should().Be(workspace.Temp.Path);
        environment.Should().NotContainKey("MORPHIR_PROFILES", "the variables of the developer do not reach the product");
    }

    [Test]
    public async Task AProductProcessStartsInTheWorkArea()
    {
        await using var workspace = Create();
        var info = new ProcessStartInfo("anything");

        workspace.Contain(info);

        info.WorkingDirectory.Should().Be(workspace.Work.Path);
    }

    [Test]
    public async Task DisposeRemovesTheFolder()
    {
        var workspace = Create();
        workspace.WriteText(workspace.File("a", "b.txt"), "text");

        await workspace.DisposeAsync();

        Directory.Exists(workspace.Root).Should().BeFalse();
    }

    [Test]
    public async Task AKeptWorkspaceStaysAfterDispose()
    {
        var workspace = Create();
        try
        {
            workspace.Keep();
            await workspace.DisposeAsync();

            Directory.Exists(workspace.Root).Should().BeTrue();
        }
        finally
        {
            TolerantDirectory.TryDelete(workspace.Root);
        }
    }

    [Test]
    public async Task AFixtureIsCopiedIntoTheWorkArea()
    {
        await using var workspace = Create();

        var model = workspace.CopyFixture("model.json");
        var renamed = workspace.CopyFixture("model.json", "input/other.json");

        model.Path.Should().Be(Path.Combine(workspace.Work.Path, "model.json"));
        workspace.ReadText(model).Should().Contain("\"fixture\"");
        renamed.Path.Should().Be(Path.Combine(workspace.Work.Path, "input", "other.json"));
        workspace.Exists(renamed).Should().BeTrue();
    }

    [Test]
    public async Task AFixtureDirectoryIsCopiedWithItsFiles()
    {
        await using var workspace = Create();

        var project = workspace.CopyFixtureDirectory("project");

        workspace.Exists(project.File("src", "A.txt")).Should().BeTrue();
    }

    [Test]
    public async Task AFixtureThatIsNotThereGivesItsNameAndTheFixtureFolder()
    {
        await using var workspace = Create();

        var copy = () => workspace.CopyFixture("none.json");

        copy.Should().Throw<TestSessionException>().WithMessage("*none.json*").WithMessage($"*{Fixtures}*");
    }

    [Test]
    [Arguments("../outside.txt")]
    [Arguments("a/../../outside.txt")]
    public async Task APathOutsideTheWorkAreaIsRefused(string relative)
    {
        await using var workspace = Create();

        var file = () => workspace.File(relative);

        file.Should().Throw<ArgumentException>().WithMessage("*outside*");
    }

    [Test]
    public async Task TheFilesWrittenSinceAMarkAreTheNewAndTheChangedFiles()
    {
        await using var workspace = Create();
        var kept = workspace.File("kept.txt");
        var changed = workspace.File("changed.txt");
        workspace.WriteText(kept, "same");
        workspace.WriteText(changed, "before");
        var mark = workspace.Mark();

        workspace.WriteText(changed, "after, and longer");
        workspace.WriteText(workspace.File("out", "new.txt"), "new");

        workspace.FilesWrittenSince(mark).Should().Equal("changed.txt", "out/new.txt");
    }

    [Test]
    public async Task NoFileIsWrittenSinceAMarkWithNoWrite()
    {
        await using var workspace = Create();
        workspace.WriteText(workspace.File("kept.txt"), "same");

        var mark = workspace.Mark();

        workspace.FilesWrittenSince(mark).Should().BeEmpty();
    }

    [Test]
    public async Task AWorkspaceMakesADirectoryRemovesAFileAndReadsAFixtureWithNoCopy()
    {
        await using var workspace = Create();
        var empty = workspace.Directory("an empty", "directory");
        var file = workspace.File("a file.txt");
        var fixture = Directory.EnumerateFiles(Fixtures, "*", SearchOption.AllDirectories).First();
        var name = Path.GetRelativePath(Fixtures, fixture).Replace('\\', '/');

        workspace.CreateDirectory(empty);
        workspace.WriteText(file, "text");
        workspace.Delete(file);
        workspace.Delete(file);

        workspace.Exists(empty).Should().BeTrue();
        workspace.Exists(file).Should().BeFalse("a second delete of a file that is not there does nothing");
        workspace.ReadFixtureText(name).Should().Be(File.ReadAllText(fixture));
        workspace.FilesIn(workspace.Work).Should().BeEmpty("a fixture that the test only reads is not copied");
        ((Action)(() => workspace.ReadFixtureText("no such fixture.txt"))).Should().Throw<TestSessionException>().WithMessage("*no such fixture.txt*");
    }
}
