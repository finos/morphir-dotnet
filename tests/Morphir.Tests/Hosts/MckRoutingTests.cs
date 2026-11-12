// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;
using FluentAssertions;
using Morphir.TestSupport;

namespace Morphir.Tests.Hosts;

/// <summary>
/// <c>morphir mck &lt;args&gt;</c> runs the separately installed <c>morphir-mck &lt;args&gt;</c> (cli-tool-dispatch). These
/// tests start the produced executables of the Native AOT-compatible <c>morphir</c> and of <c>dotnet-morphir</c>, with a
/// stub morphir-mck, and stop the CLI with a signal or a kill: no driver of the testing library sends a signal yet. The
/// other routing tests are in tests/Morphir.BlackBox.Tests/Integration/ToolRouting.feature.
/// </summary>
[Category("PlatformSpecific")]
public sealed class MckRoutingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

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
