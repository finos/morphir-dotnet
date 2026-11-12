// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.ComponentModel;
using FluentAssertions;
using Morphir.Tooling.Features.Mck;

namespace Morphir.Tooling.Tests.Features.Mck;

/// <summary>
/// MCK-4: a stop request (SIGTERM, SIGHUP or cancellation) is recorded before the tool starts, so a request that
/// arrives while the tool is being started still stops it; and a tree stop that fails for part of the tree is reported
/// on standard error instead of escaping a signal handler or a cancellation callback.
/// </summary>
public class MckTerminationTests
{
    [Test]
    public void ARequestBeforeTheToolStartsStopsItWhenItAttaches()
    {
        var termination = new MckTermination();
        var stops = 0;

        termination.Request(143);
        termination.Attach(() => stops++).Should().BeFalse("the tool was asked to stop before it started");

        stops.Should().Be(1);
        termination.ExitCode.Should().Be(143);
    }

    [Test]
    public void ARequestWhileTheToolRunsStopsItOnce()
    {
        var termination = new MckTermination();
        var stops = 0;

        termination.Attach(() => stops++).Should().BeTrue();
        termination.Request(143);
        termination.Request(130);

        stops.Should().Be(2, "each request stops the tree again, which is harmless once it has ended");
        termination.ExitCode.Should().Be(143, "the first request decides the exit code");
    }

    [Test]
    public void ARequestAfterDetachDoesNotTouchTheDisposedTool()
    {
        var termination = new MckTermination();
        var stops = 0;
        termination.Attach(() => stops++);
        termination.Detach();

        termination.Request(143);

        stops.Should().Be(0);
    }

    [Test]
    public void DetachWaitsForAStopInProgress()
    {
        var termination = new MckTermination();
        using var stopping = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        termination.Attach(() =>
        {
            stopping.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        });
        var request = Task.Run(() => termination.Request(143));
        stopping.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

        var detach = Task.Run(termination.Detach);
        detach.Wait(TimeSpan.FromMilliseconds(200)).Should().BeFalse("the tool must not be disposed while it is being stopped");
        release.Set();

        Task.WaitAll([request, detach], TimeSpan.FromSeconds(10)).Should().BeTrue();
    }

    [Test]
    public void APartialTreeStopIsReportedAndDoesNotThrow()
    {
        using var diagnostics = new StringWriter();
        var failures = Enumerable.Range(1, 5).Select(index => new Win32Exception($"process {index} could not be killed")).ToArray();

        var act = () => MckProcess.StopTree(() => throw new AggregateException(failures), diagnostics);

        act.Should().NotThrow("a signal handler or cancellation callback must not throw");
        var text = diagnostics.ToString();
        text.Should().Contain("5 processes").And.Contain("process 1 could not be killed").And.Contain("process 3 could not be killed");
        text.Should().NotContain("process 4 could not be killed", "the report names at most three failures");
    }

    [Test]
    public void AToolThatHasExitedIsNotAnError()
    {
        using var diagnostics = new StringWriter();

        MckProcess.StopTree(() => throw new InvalidOperationException("No process is associated with this object."), diagnostics);

        diagnostics.ToString().Should().BeEmpty();
    }

    [Test]
    public void ATreeThatCannotBeReadIsReported()
    {
        using var diagnostics = new StringWriter();

        MckProcess.StopTree(() => throw new Win32Exception("access denied"), diagnostics);

        diagnostics.ToString().Should().Contain("could not be stopped").And.Contain("access denied");
    }

    [Test]
    [TUnit.Core.Category("PlatformSpecific")]
    public void ACancellationBeforeStartStartsNothing()
    {
        using var diagnostics = new StringWriter();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        // A path that cannot start: starting it would give 126, not 130.
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "morphir-mck" + (OperatingSystem.IsWindows() ? ".exe" : ""));

        var exitCode = MckProcess.Run(missing, [], diagnostics, cancelled.Token);

        exitCode.Should().Be(130, diagnostics.ToString());
        diagnostics.ToString().Should().Contain("cancelled").And.NotContain("could not be started");
    }
}
