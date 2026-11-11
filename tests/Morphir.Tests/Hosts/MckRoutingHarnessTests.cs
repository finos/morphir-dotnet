// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport;

namespace Morphir.Tests.Hosts;

/// <summary>
/// The routing tests describe a process that did not end (state, parent, command) while they still hold a slot of
/// <see cref="ProcessGate.Shared"/> in <see cref="ChildProcess.ConverseAsync"/>. A description that starts a child
/// (macOS has no /proc, so it runs /bin/ps) must therefore not wait for that gate: with every slot held by such tests,
/// it would wait for ever. These tests hold every shared slot and need the diagnostic to finish under a short deadline
/// of their own, so a diagnostic that goes back to the shared gate fails here within that deadline instead of hanging.
/// </summary>
[NotInParallel]
public sealed class MckRoutingHarnessTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    [Test]
    public async Task ADiagnosticChildDoesNotWaitForTheSharedProcessGate()
    {
        using var held = await HoldEverySharedSlotAsync();
        using var deadline = new CancellationTokenSource(Deadline);
        var dotnet = Path.Combine(MckRouting.DotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

        var result = await MckRouting.RunDiagnosticAsync(ChildProcess.StartInfo(dotnet, "--version"), deadline.Token);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
    }

    [Test]
    public async Task ThePsDescriptionDoesNotWaitForTheSharedProcessGate()
    {
        if (!File.Exists("/bin/ps")) Skip.Test("This machine has no /bin/ps.");
        using var held = await HoldEverySharedSlotAsync();
        using var deadline = new CancellationTokenSource(Deadline);

        var (entry, parent) = await MckRouting.DescribePsAsync(Environment.ProcessId, deadline.Token);

        entry.Should().StartWith("ps [").And.Contain(Environment.ProcessId.ToString());
        parent.Should().BePositive();
    }

    [Test]
    public async Task TheCallersCancellationStopsADiagnosticThatWaits()
    {
        // A diagnostic waits only for its own serialized gate. Holding that slot shows the caller's token is honoured.
        using var held = await MckRouting.HoldDiagnosticSlotAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var dotnet = Path.Combine(MckRouting.DotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

        var act = () => MckRouting.RunDiagnosticAsync(ChildProcess.StartInfo(dotnet, "--version"), deadline.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>Enters <see cref="ProcessGate.Shared"/> until no slot is left, as the outer routing tests can.</summary>
    private static async Task<IDisposable> HoldEverySharedSlotAsync()
    {
        var slots = new List<IDisposable>();
        using var enter = new CancellationTokenSource(Deadline);
        for (var slot = 0; slot < ProcessGate.Shared.Limit; slot++) slots.Add(await ProcessGate.Shared.EnterAsync(enter.Token));
        return new Slots(slots);
    }

    private sealed class Slots(List<IDisposable> slots) : IDisposable
    {
        public void Dispose()
        {
            foreach (var slot in slots) slot.Dispose();
        }
    }
}
