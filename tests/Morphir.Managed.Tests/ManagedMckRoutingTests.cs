// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.Tests.Hosts;

namespace Morphir.Managed.Tests;

/// <summary>
/// The managed <c>morphir</c> routes <c>mck</c> as the other entry points do (cli-tool-dispatch). The produced managed
/// apphost runs with a stub morphir-mck and is killed: no driver of the testing library kills the CLI yet. The other
/// routing tests of the managed host are in tests/Morphir.BlackBox.Tests/Integration/ToolRouting.feature.
/// </summary>
[Category("PlatformSpecific")]
public sealed class ManagedMckRoutingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [Test]
    public async Task KillingTheManagedMorphirOnWindowsStopsTheToolAndItsChild()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("The job object is the Windows mechanism.");
        await using var routing = MckRouting.Create();
        var pids = routing.Sandbox.PathOf("pids.txt");
        var start = routing.EntryPoint("morphir", routing.Tools, "mck", "wait");
        start.Environment["MCK_STUB_PIDS"] = pids;
        var cli = 0;
        var ended = (Tool: false, Grandchild: false);

        await ChildProcess.ConverseAsync(start, Timeout, async (_, _, token) =>
        {
            var (tool, grandchild) = await MckRouting.WaitForPidsAsync(pids, token);
            using (var process = System.Diagnostics.Process.GetProcessById(cli)) process.Kill(entireProcessTree: false);
            ended = (await MckRouting.EndsWithinAsync(tool, TimeSpan.FromSeconds(20), token), await MckRouting.EndsWithinAsync(grandchild, TimeSpan.FromSeconds(20), token));
        }, started: pid => cli = pid);

        ended.Should().Be((true, true));
    }
}
