// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using FluentAssertions;

namespace Morphir.TestSupport.Tests;

public class ChildProcessTests
{
    // A shell that starts a grandchild, so killing only the direct child would leave work running.
    private static ProcessStartInfo Sleep(int seconds, string? workingDirectory = null)
    {
        var info = OperatingSystem.IsWindows()
            ? ChildProcess.StartInfo("cmd.exe", "/c", $"ping -n {seconds + 1} 127.0.0.1 >nul")
            : ChildProcess.StartInfo("/bin/sh", "-c", $"sleep {seconds}; echo done");
        if (workingDirectory is not null) info.WorkingDirectory = workingDirectory;
        return info;
    }

    private static ProcessStartInfo Echo(string text) =>
        OperatingSystem.IsWindows()
            ? ChildProcess.StartInfo("cmd.exe", "/c", $"echo {text}")
            : ChildProcess.StartInfo("/bin/sh", "-c", $"echo {text}");

    [Test]
    public async Task Timeout_KillsTreeAndWaits_SoWorkingDirectoryCanBeDeleted()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("morphir-child-").FullName;

        var run = () => ChildProcess.RunAsync(Sleep(30, workingDirectory), TimeSpan.FromSeconds(1), gate: new ProcessGate(4));

        var failure = await run.Should().ThrowAsync<ChildProcessTimeoutException>();
        failure.Which.Timeout.Should().Be(TimeSpan.FromSeconds(1));
        failure.Which.ProcessExited.Should().BeTrue("the process tree is killed and awaited before the error surfaces");

        // A running process holds its working directory open on Windows. A strict delete proves it is gone.
        var strictDelete = () => Directory.Delete(workingDirectory, recursive: true);
        strictDelete.Should().NotThrow();
    }

    // The direct child exits at once; a background grandchild keeps its pipes and would write a marker later.
    // Killing the exited parent's tree cannot reach it, so the runner must own a group/job that outlives the parent.
    [Test]
    public async Task Timeout_KillsAGrandchildThatOutlivedItsParent()
    {
        if (OperatingSystem.IsMacOS()) return; // No owned process group on macOS: see ChildProcess.

        var directory = Directory.CreateTempSubdirectory("morphir-orphan-").FullName;
        var marker = Path.Combine(directory, "grandchild-finished");
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { Arguments = $"/c start \"\" /b cmd /c \"ping -n 4 127.0.0.1 >nul & type nul > {marker}\"" }
            : ChildProcess.StartInfo("/bin/sh", "-c", $"(sleep 3; touch '{marker}') & exit 0");

        var run = () => ChildProcess.RunAsync(info, TimeSpan.FromSeconds(1), gate: new ProcessGate(4));

        var failure = await run.Should().ThrowAsync<ChildProcessTimeoutException>();
        failure.Which.ProcessExited.Should().BeTrue("the whole tree, grandchild included, is gone");
        await Task.Delay(TimeSpan.FromSeconds(5));
        File.Exists(marker).Should().BeFalse("the orphaned grandchild was killed with its group");
        TolerantDirectory.TryDelete(directory);
    }

    [Test]
    public async Task LargeOutputOnBothStreams_DoesNotDeadlock()
    {
        const int size = 2_000_000;
        var info = OperatingSystem.IsWindows()
            ? ChildProcess.StartInfo("powershell.exe", "-NoProfile", "-Command",
                $"[Console]::Out.Write('a' * {size}); [Console]::Error.Write('b' * {size})")
            : ChildProcess.StartInfo("/bin/sh", "-c",
                $"head -c {size} /dev/zero | tr '\\0' a; head -c {size} /dev/zero | tr '\\0' b >&2");

        var result = await ChildProcess.RunAsync(info, TimeSpan.FromSeconds(60), gate: new ProcessGate(4));

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Length.Should().Be(size);
        result.StandardError.Length.Should().Be(size);
    }

    [Test]
    public async Task GateOfOne_RunsChildrenOneAfterAnother()
    {
        var gate = new ProcessGate(1);

        var results = await Task.WhenAll(
            ChildProcess.RunAsync(Sleep(1), TimeSpan.FromSeconds(30), gate: gate),
            ChildProcess.RunAsync(Sleep(1), TimeSpan.FromSeconds(30), gate: gate));

        var ordered = results.OrderBy(r => r.StartedAt).ToArray();
        ordered[1].StartedAt.Should().BeOnOrAfter(ordered[0].ExitedAt, "a gate of one never runs two children at once");
    }

    [Test]
    public async Task TimeoutStartsAfterTheGateIsHeld()
    {
        var gate = new ProcessGate(1);

        var slow = ChildProcess.RunAsync(Sleep(2), TimeSpan.FromSeconds(30), gate: gate);
        await Task.Delay(200); // let the slow child take the gate first
        var queued = ChildProcess.RunAsync(Echo("hello"), TimeSpan.FromSeconds(1), gate: gate);

        // The quick child waits about 2 seconds for the gate, longer than its 1 second budget.
        var result = await queued;
        await slow;

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("hello");
    }

    [Test]
    public async Task Cancellation_KillsTreeAndThrows()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("morphir-child-").FullName;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var run = () => ChildProcess.RunAsync(Sleep(30, workingDirectory), TimeSpan.FromSeconds(60), cancel.Token, new ProcessGate(4));

        await run.Should().ThrowAsync<OperationCanceledException>();
        var strictDelete = () => Directory.Delete(workingDirectory, recursive: true);
        strictDelete.Should().NotThrow();
    }

    // A child that answers one line of stdin, so the caller must write, then read, then write again.
    private static ProcessStartInfo Answer() =>
        OperatingSystem.IsWindows()
            ? ChildProcess.StartInfo("powershell.exe", "-NoProfile", "-Command",
                "$line = [Console]::In.ReadLine(); [Console]::Out.WriteLine('got ' + $line); [Console]::Out.Flush(); $rest = [Console]::In.ReadToEnd(); [Console]::Out.WriteLine('rest ' + $rest.Trim())")
            : ChildProcess.StartInfo("/bin/sh", "-c", "read line; echo \"got $line\"; rest=$(cat); echo \"rest $rest\"");

    [Test]
    public async Task Converse_WritesAfterReadingAReply_ThenReturnsTheRestOfStdout()
    {
        var result = await ChildProcess.ConverseAsync(Answer(), TimeSpan.FromMinutes(1), async (input, output, ct) =>
        {
            await input.WriteAsync("hello\n"u8.ToArray(), ct);
            await input.FlushAsync(ct);
            using var reader = new StreamReader(output, leaveOpen: true);
            (await reader.ReadLineAsync(ct)).Should().Be("got hello", "the child answered before the second write");
            await input.WriteAsync("world\n"u8.ToArray(), ct);
            await input.FlushAsync(ct);
        }, gate: new ProcessGate(4));

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.StandardOutput.Trim().Should().Be("rest world", "stdin closes when the conversation ends");
    }

    [Test]
    public async Task Converse_Timeout_KillsTheTreeWhileTheConversationWaits()
    {
        var run = () => ChildProcess.ConverseAsync(Sleep(30), TimeSpan.FromSeconds(1), async (_, output, ct) =>
        {
            // The child writes nothing for 30 seconds: this read waits until the deadline.
            await output.ReadExactlyAsync(new byte[1], ct);
        }, gate: new ProcessGate(4));

        var failure = await run.Should().ThrowAsync<ChildProcessTimeoutException>();
        failure.Which.ProcessExited.Should().BeTrue("the process tree is killed and awaited before the error surfaces");
    }

    [Test]
    [Arguments("1", 12, 1)]
    [Arguments("3", 12, 3)]
    [Arguments(null, 12, 6)]
    [Arguments("", 2, 2)]
    [Arguments("0", 12, 6)]
    [Arguments("lots", 12, 6)]
    public async Task GateLimit_FromEnvironmentValue(string? value, int processorCount, int expected)
    {
        ProcessGate.LimitFrom(value, processorCount).Should().Be(expected);
        await Task.CompletedTask;
    }
}
