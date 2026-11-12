// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The session driver (design DRV-1 of the change consolidate-black-box-tests, task 6.3) against a fixture process:
/// the scripted fake worker of <c>tests/Morphir.Conformance.FakeWorker</c>, which reads and writes Content-Length
/// frames. A request gets its reply while the process is alive, and an interrupt gives the exit and its time.
/// </summary>
[Category("PlatformSpecific")]
public class ProcessSessionDriverTests
{
    private static string FakeWorker => Path.Combine(AppContext.BaseDirectory, "Morphir.Conformance.FakeWorker.dll");

    private static async Task<(ScenarioWorkspace Workspace, ProcessSessionDriver Sessions, string Script)> Arrange(string script)
    {
        var workspace = ScenarioWorkspace.Create(Path.Combine(AppContext.BaseDirectory, "Fixtures"));
        var file = workspace.File("fake script.json");
        workspace.WriteText(file, script);
        await Task.CompletedTask;
        return (workspace, new ProcessSessionDriver(new TestTarget.DevelopmentBuild(), MorphirHost.Native, FakeWorker, workspace), file.Path);
    }

    [Test]
    public async Task ARequestGetsItsReplyWhileTheProcessIsAlive()
    {
        var (workspace, sessions, script) = await Arrange("""{"modules":{"*":[{"echo":1},{"hang":true}]}}""");
        await using var _ = workspace;
        await using var session = await sessions.StartAsync(["--script", script, "extension", "worker"]);

        await session.WriteAsync(ContentLengthFrames.Frame("""{"jsonrpc":"2.0","id":1}"""));
        var replied = await session.WaitUntilAsync(() => ContentLengthFrames.Read(session.StandardOutput).Frames.Count == 1, TimeSpan.FromSeconds(30));
        var alive = await session.WaitUntilAsync(() => false, TimeSpan.FromMilliseconds(200)) is false && session.ProcessId > 0;

        replied.Should().BeTrue();
        alive.Should().BeTrue();
        Encoding.UTF8.GetString(ContentLengthFrames.Read(session.StandardOutput).Frames.Single()).Should().Be("""{"jsonrpc":"2.0","id":1}""");
        var exit = await session.WaitForExitAsync(TimeSpan.FromMilliseconds(500));
        exit.ExitCode.Should().BeNull("the fake still hangs at the time limit, so the driver stops it");
    }

    [Test]
    public async Task AnInterruptGivesTheExitAndTheTimeFromTheInterruptToTheExit()
    {
        var (workspace, sessions, script) = await Arrange("""{"modules":{"*":[{"stderr":"ready"},{"hang":true}]}}""");
        await using var _ = workspace;
        await using var session = await sessions.StartAsync(["--script", script, "extension", "worker"]);
        (await session.WaitUntilAsync(() => session.StandardError.Any(line => line.Text == "ready"), TimeSpan.FromSeconds(30))).Should().BeTrue();

        var sent = await session.InterruptAsync(ConsoleControlEvent.CtrlBreak);
        var exit = await session.WaitForExitAsync(TimeSpan.FromSeconds(30));

        exit.ExitCode.Should().NotBeNull(exit.Describe());
        exit.ExitCode.Should().NotBe(0, exit.Describe());
        exit.InterruptedAt.Should().Be(sent);
        exit.FromInterruptToExit.Should().NotBeNull();
        exit.FromInterruptToExit!.Value.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero).And.BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task TheOutputIsReadAsItComesAndStandardErrorKeepsTheTimeOfEachLine()
    {
        var (workspace, sessions, script) = await Arrange("""{"modules":{"*":[{"stderr":"first"},{"frame":{"a":1}},{"read":1},{"stderr":"second"}]}}""");
        await using var _ = workspace;
        await using var session = await sessions.StartAsync(["--script", script, "extension", "worker"]);

        (await session.WaitUntilAsync(() => session.StandardOutput.Length > 0, TimeSpan.FromSeconds(30))).Should().BeTrue();
        session.StandardError.Select(line => line.Text).Should().Equal("first");
        await session.WriteAsync(ContentLengthFrames.Frame("{}"));
        var exit = await session.WaitForExitAsync(TimeSpan.FromSeconds(30));

        exit.ExitCode.Should().Be(0, exit.Describe());
        exit.StandardError.Select(line => line.Text).Should().Equal("first", "second");
        exit.StandardError[1].At.Should().BeGreaterThanOrEqualTo(exit.StandardError[0].At);
        exit.Frames.Problem.Should().BeNull();
        exit.FromInterruptToExit.Should().BeNull();
    }

    [Test]
    public async Task TheSessionIsContainedInTheWorkspace()
    {
        var (workspace, sessions, script) = await Arrange("""{"modules":{"*":[{"exit":3}]}}""");
        await using var _ = workspace;
        await using var session = await sessions.StartAsync(
            ["--script", script, "--log", "log.jsonl", "extension", "worker", "--json"],
            new ProcessSessionOptions { Environment = new Dictionary<string, string?> { ["MORPHIR_SESSION_TEST"] = "1" } });

        var exit = await session.WaitForExitAsync(TimeSpan.FromSeconds(30));

        exit.ExitCode.Should().Be(3);
        workspace.ReadText(workspace.File("log.jsonl")).Should().Contain("\"--json\"", "the process starts in the work area of the workspace");
        session.CommandLine.Should().StartWith("dotnet ").And.Contain(FakeWorker);
    }

    [Test]
    public void ContentLengthFramesAreReadStrictly()
    {
        var two = ContentLengthFrames.Frame("{}").Concat(ContentLengthFrames.Frame("[1]")).ToArray();

        ContentLengthFrames.Read(two).Frames.Select(Encoding.UTF8.GetString).Should().Equal("{}", "[1]");
        ContentLengthFrames.Read([.. two, (byte)'x']).Problem.Should().Contain("not a frame");
        ContentLengthFrames.Read(Encoding.ASCII.GetBytes("Length: 2\r\n\r\n{}")).Problem.Should().Contain("not one Content-Length line");
        ContentLengthFrames.Read(Encoding.ASCII.GetBytes("Content-Length: 9\r\n\r\n{}")).Problem.Should().Contain("ends first");
    }
}
