// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.Tests.Hosts;

namespace Morphir.Managed.Tests;

/// <summary>
/// The managed <c>morphir</c> routes <c>mck</c> as the other entry points do (cli-tool-dispatch). The produced managed
/// apphost runs with a stub morphir-mck; Morphir.Tests covers the AOT-compatible morphir and dotnet-morphir.
/// </summary>
public sealed class ManagedMckRoutingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [Test]
    public async Task TheManagedMorphirPassesTheArgumentsAndTheExitCodeThrough()
    {
        await using var routing = MckRouting.Create();
        string[] arguments = ["run", "--suite", "frontend cases", "ünïcode.json", "--help", ""];
        var routed = routing.EntryPoint("morphir", routing.Tools, ["mck", .. arguments]);
        var standalone = routing.Standalone(routing.Tools, arguments);
        routed.Environment["MCK_STUB_EXIT"] = standalone.Environment["MCK_STUB_EXIT"] = "3";

        var viaCli = await ChildProcess.RunAsync(routed, Timeout);
        var direct = await ChildProcess.RunAsync(standalone, Timeout);

        viaCli.ExitCode.Should().Be(3, viaCli.CombinedOutput);
        MckStubReport.Parse(viaCli.StandardOutput).Arguments.Should().Equal(arguments);
        viaCli.StandardOutput.Should().Be(direct.StandardOutput);
        viaCli.StandardError.Should().Be(direct.StandardError);
    }

    [Test]
    public async Task WithNoToolTheManagedMorphirExits127()
    {
        await using var routing = MckRouting.Create();

        var result = await ChildProcess.RunAsync(routing.EntryPoint("morphir", routing.Empty, "mck", "run"), Timeout);

        result.ExitCode.Should().Be(127, result.CombinedOutput);
        result.StandardOutput.Should().BeEmpty();
        result.StandardError.Should().Contain("morphir-mck").And.Contain("MORPHIR_MCK");
    }

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
