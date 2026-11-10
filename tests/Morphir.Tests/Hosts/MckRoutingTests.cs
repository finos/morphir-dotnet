// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;
using FluentAssertions;
using Morphir.TestSupport;

namespace Morphir.Tests.Hosts;

/// <summary>
/// <c>morphir mck &lt;args&gt;</c> runs the separately installed <c>morphir-mck &lt;args&gt;</c> (cli-tool-dispatch). These
/// tests start the produced executables of the Native AOT-compatible <c>morphir</c> and of <c>dotnet-morphir</c>, with a
/// stub morphir-mck, and compare what the stub received and wrote with a standalone run of the stub.
/// </summary>
public sealed class MckRoutingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [Test]
    [Arguments("morphir")]
    [Arguments("dotnet-morphir")]
    public async Task ArgumentsPassThroughUnchanged(string entryPoint)
    {
        await using var routing = MckRouting.Create();
        string[] arguments = ["run", "--suite", "frontend cases", "--report", "ünïcode.json", "--help", "", "a\"b c", "@response.rsp", "--version"];

        var result = await ChildProcess.RunAsync(routing.EntryPoint(entryPoint, routing.Tools, ["mck", .. arguments]), Timeout);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        MckStubReport.Parse(result.StandardOutput).Arguments.Should().Equal(arguments);
    }

    [Test]
    [Arguments("morphir")]
    [Arguments("dotnet-morphir")]
    public async Task RoutedAndStandaloneRunsWriteTheSameAndExitTheSame(string entryPoint)
    {
        await using var routing = MckRouting.Create();
        var routed = routing.EntryPoint(entryPoint, routing.Tools, "mck", "verify", "--gate", "frontend");
        var standalone = routing.Standalone(routing.Tools, "verify", "--gate", "frontend");
        routed.Environment["MCK_STUB_EXIT"] = standalone.Environment["MCK_STUB_EXIT"] = "3";

        var viaCli = await ChildProcess.RunAsync(routed, Timeout);
        var direct = await ChildProcess.RunAsync(standalone, Timeout);

        viaCli.ExitCode.Should().Be(3, viaCli.CombinedOutput);
        direct.ExitCode.Should().Be(3);
        viaCli.StandardOutput.Should().Be(direct.StandardOutput, "the CLI writes nothing of its own to standard output");
        viaCli.StandardError.Should().Be(direct.StandardError);
        viaCli.StandardError.Should().Contain("morphir-mck stub: standard error");
    }

    [Test]
    [Arguments("morphir")]
    [Arguments("dotnet-morphir")]
    public async Task TheToolReadsTheCallersStandardInputInTheCallersFolder(string entryPoint)
    {
        await using var routing = MckRouting.Create();
        var routed = routing.EntryPoint(entryPoint, routing.Tools, "mck", "read");
        var standalone = routing.Standalone(routing.Tools, "read");
        routed.Environment["MCK_STUB_STDIN"] = standalone.Environment["MCK_STUB_STDIN"] = "1";

        var viaCli = MckStubReport.Parse((await ChildProcess.RunAsync(routed, Timeout, standardInput: "piped text\nsecond line\n")).StandardOutput);
        var direct = MckStubReport.Parse((await ChildProcess.RunAsync(standalone, Timeout, standardInput: "piped text\nsecond line\n")).StandardOutput);

        viaCli.StandardInput.Should().Be("piped text\nsecond line\n");
        viaCli.WorkingDirectory.Should().Be(direct.WorkingDirectory);
        Path.GetFileName(viaCli.WorkingDirectory).Should().Be("work");
    }

    [Test]
    [Arguments("morphir")]
    [Arguments("dotnet-morphir")]
    public async Task WithNoToolTheCliSaysSoOnStandardErrorAndExits127(string entryPoint)
    {
        // The test output folder, which is also the entry point's folder, holds a stub; it must not be used.
        await using var routing = MckRouting.Create();

        var result = await ChildProcess.RunAsync(routing.EntryPoint(entryPoint, routing.Empty, "mck", "run"), Timeout);

        result.ExitCode.Should().Be(127, result.CombinedOutput);
        result.StandardOutput.Should().BeEmpty();
        result.StandardError.Should().Contain("morphir-mck").And.Contain("MORPHIR_MCK").And.Contain("finos/morphir");
    }

    [Test]
    [Arguments("morphir")]
    [Arguments("dotnet-morphir")]
    public async Task AToolInTheCurrentDirectoryIsNotRun(string entryPoint)
    {
        await using var routing = MckRouting.Create();
        MckRouting.CopyStub(routing.Sandbox.Work);
        var start = routing.EntryPoint(entryPoint, string.Join(Path.PathSeparator, routing.Empty, "."), "mck", "run");

        var result = await ChildProcess.RunAsync(start, Timeout);

        result.ExitCode.Should().Be(127, result.CombinedOutput);
        result.StandardOutput.Should().BeEmpty("the planted morphir-mck must not run");
    }

    [Test]
    [Arguments("morphir")]
    [Arguments("dotnet-morphir")]
    public async Task ARelativeMorphirMckIsRefused(string entryPoint)
    {
        await using var routing = MckRouting.Create();
        MckRouting.CopyStub(routing.Sandbox.Work);
        var start = routing.EntryPoint(entryPoint, routing.Empty, "mck", "run");
        start.Environment["MORPHIR_MCK"] = "morphir-mck" + MckRouting.Suffix;

        var result = await ChildProcess.RunAsync(start, Timeout);

        result.ExitCode.Should().Be(127, result.CombinedOutput);
        result.StandardOutput.Should().BeEmpty();
        result.StandardError.Should().Contain("MORPHIR_MCK").And.Contain("absolute");
    }

    [Test]
    [Arguments("morphir")]
    [Arguments("dotnet-morphir")]
    public async Task AnAbsoluteMorphirMckIsRunWithoutPath(string entryPoint)
    {
        await using var routing = MckRouting.Create();
        var start = routing.EntryPoint(entryPoint, routing.Empty, "mck", "run");
        start.Environment["MORPHIR_MCK"] = routing.Stub;

        var result = await ChildProcess.RunAsync(start, Timeout);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        MckStubReport.Parse(result.StandardOutput).Arguments.Should().Equal("run");
    }

    [Test]
    [Arguments("morphir")]
    [Arguments("dotnet-morphir")]
    public async Task AnotherUnknownCommandStartsNothing(string entryPoint)
    {
        await using var routing = MckRouting.Create();
        MckRouting.CopyStub(routing.Tools, "morphir-lint");
        var start = routing.EntryPoint(entryPoint, routing.Tools, "lint", "src");
        start.Environment["MCK_STUB_EXIT"] = "42";

        var result = await ChildProcess.RunAsync(start, Timeout);

        result.ExitCode.Should().NotBe(0).And.NotBe(42, "morphir-lint must not run");
        result.StandardOutput.Should().NotContain("\"args\"");
        result.CombinedOutput.Should().Contain("lint");
    }

    [Test]
    [Arguments("morphir")]
    [Arguments("dotnet-morphir")]
    public async Task HelpListsMck(string entryPoint)
    {
        await using var routing = MckRouting.Create();

        var result = await ChildProcess.RunAsync(routing.EntryPoint(entryPoint, routing.Empty, "--help"), Timeout);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.CombinedOutput.Should().MatchRegex(@"(?m)^\s+mck\b", "the help lists the routed command");
    }

    [Test]
    [Arguments("morphir")]
    [Arguments("dotnet-morphir")]
    public async Task KillingTheCliOnWindowsStopsTheToolAndItsChild(string entryPoint)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("The job object is the Windows mechanism; Unix is covered by SIGTERM.");
        await using var routing = MckRouting.Create();
        var pids = routing.Sandbox.PathOf("pids.txt");
        var start = routing.EntryPoint(entryPoint, routing.Tools, "mck", "wait");
        start.Environment["MCK_STUB_PIDS"] = pids;
        var cli = 0;
        var ended = (Tool: false, Grandchild: false);

        await ChildProcess.ConverseAsync(start, Timeout, async (_, _, token) =>
        {
            var (tool, grandchild) = await MckRouting.WaitForPidsAsync(pids, token);
            // Only the CLI is killed, as Task Manager or a CI runner would do: not its tree.
            using (var process = System.Diagnostics.Process.GetProcessById(cli)) process.Kill(entireProcessTree: false);
            ended = (await MckRouting.EndsWithinAsync(tool, TimeSpan.FromSeconds(20), token), await MckRouting.EndsWithinAsync(grandchild, TimeSpan.FromSeconds(20), token));
        }, started: pid => cli = pid);

        ended.Tool.Should().BeTrue("the tool is in the CLI's kill-on-close job");
        ended.Grandchild.Should().BeTrue("the tool's child inherits the job");
    }

    [Test]
    [Arguments("morphir")]
    [Arguments("dotnet-morphir")]
    public async Task SigtermToTheCliOnUnixStopsTheToolsTree(string entryPoint)
    {
        if (OperatingSystem.IsWindows()) Skip.Test("SIGTERM exists only on Unix.");
        await using var routing = MckRouting.Create();
        var pids = routing.Sandbox.PathOf("pids.txt");
        var start = routing.EntryPoint(entryPoint, routing.Tools, "mck", "wait");
        start.Environment["MCK_STUB_PIDS"] = pids;
        var cli = 0;
        var before = "";
        (bool Ended, string Evidence) tool = default, grandchild = default;

        var result = await ChildProcess.ConverseAsync(start, Timeout, async (_, _, token) =>
        {
            var (toolPid, grandchildPid) = await MckRouting.WaitForPidsAsync(pids, token);
            before = await MckRouting.DescribeAsync(grandchildPid, token);
            SendSignal(cli, SigTerm).Should().Be(0);
            tool = await MckRouting.ObserveEndAsync(toolPid, TimeSpan.FromSeconds(20), token);
            grandchild = await MckRouting.ObserveEndAsync(grandchildPid, TimeSpan.FromSeconds(20), token);
        }, started: pid => cli = pid);

        // The evidence is the process state and ancestry (Linux /proc, macOS ps) of a process that did not end, taken
        // before its cleanup kill, with the grandchild's ancestry before SIGTERM.
        var cliReport = $"CLI exit {result.ExitCode}, output: {result.CombinedOutput}";
        tool.Ended.Should().BeTrue($"the tool must stop; before SIGTERM: {before}; after: {tool.Evidence}; {cliReport}");
        grandchild.Ended.Should().BeTrue($"the tool's child must stop; before SIGTERM: {before}; after: {grandchild.Evidence}; {cliReport}");
        result.ExitCode.Should().Be(143, result.CombinedOutput);
    }

    private const int SigTerm = 15;

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int SendSignal(int pid, int signal);
}
