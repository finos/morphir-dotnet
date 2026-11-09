// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Tooling.Features.TaskRecords;

namespace Morphir.Tooling.Tests.Features.TaskRecords;

/// <summary>
/// Spec: compile-task-records, "Runs of one task are serialized". Each test waits on a signal, never on a
/// sleep, and every wait has a timeout, so the tests are deterministic and bounded.
/// </summary>
public sealed class CompileLockTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
    private readonly RecordWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Test]
    public async Task GenerateWaitsForARunningCompileAndThenReadsTheCompletedIr()
    {
        // The earlier compile: generate must not read this IR.
        using (var earlier = (await _workspace.Store().BeginAsync(default)).ShouldSucceed())
        {
            await File.WriteAllTextAsync(Path.Combine(earlier.Dest, "morphir-ir.json"), "earlier");
            earlier.Complete(new CompileProduct("fsharp", RecordWorkspace.SingleFileIr(), null)).ShouldSucceed();
        }

        // A compile starts and holds the exclusive lock.
        var running = (await _workspace.Store().BeginAsync(default)).ShouldSucceed();

        // Generate starts while the compile runs.
        var readerDiagnostics = new LineWriter();
        var reading = _workspace.Store(diagnostics: readerDiagnostics).OpenCompletedAsync(default).AsTask();
        await readerDiagnostics.FirstLine.WaitAsync(Bound);
        reading.IsCompleted.Should().BeFalse("the reader waits while the compile holds the lock");

        // The compile writes its IR and record, then releases the lock.
        await File.WriteAllTextAsync(Path.Combine(running.Dest, "morphir-ir.json"), "completed");
        running.Complete(new CompileProduct("fsharp", RecordWorkspace.SingleFileIr(), null)).ShouldSucceed();
        running.Dispose();

        using var output = (await reading.WaitAsync(Bound)).ShouldSucceed();
        File.ReadAllText(output.IrPath).Should().Be("completed");
        readerDiagnostics.Lines.Should().Equal(
            $"waiting for another Morphir run to finish this task ({_workspace.Out.Task(MemberPath.Root, TaskId.Compile).Lock})");
    }

    [Test]
    public async Task ACompileWaitsForAReaderToFinishReading()
    {
        await CompleteOnce();
        var output = (await _workspace.Store().OpenCompletedAsync(default)).ShouldSucceed();

        var writerDiagnostics = new LineWriter();
        var starting = _workspace.Store(diagnostics: writerDiagnostics).BeginAsync(default).AsTask();
        await writerDiagnostics.FirstLine.WaitAsync(Bound);
        starting.IsCompleted.Should().BeFalse("a compile cannot clear .dest under a reader");
        File.ReadAllText(output.IrPath).Should().Be("ir");

        output.Dispose();
        using var run = (await starting.WaitAsync(Bound)).ShouldSucceed();
        writerDiagnostics.Lines.Should().ContainSingle();
    }

    [Test]
    public async Task ASecondCompileWaitsForTheFirst()
    {
        var first = (await _workspace.Store().BeginAsync(default)).ShouldSucceed();

        var secondDiagnostics = new LineWriter();
        var second = _workspace.Store(diagnostics: secondDiagnostics).BeginAsync(default).AsTask();
        await secondDiagnostics.FirstLine.WaitAsync(Bound);
        second.IsCompleted.Should().BeFalse();

        first.Dispose();
        using var run = (await second.WaitAsync(Bound)).ShouldSucceed();
        secondDiagnostics.Lines.Should().ContainSingle();
    }

    [Test]
    public async Task ReadersShareTheLockAndDoNotWait()
    {
        await CompleteOnce();

        using var first = (await _workspace.Store().OpenCompletedAsync(default)).ShouldSucceed();
        using var second = (await _workspace.Store().OpenCompletedAsync(default).AsTask().WaitAsync(Bound)).ShouldSucceed();

        _workspace.Diagnostics.Lines.Should().BeEmpty();
        File.Exists(_workspace.Out.Task(MemberPath.Root, TaskId.Compile).Lock).Should().BeTrue("the lock file stays on disk");
    }

    [Test]
    public async Task AnUncontendedLockWritesNoWaitingLine()
    {
        using (var run = (await _workspace.Store().BeginAsync(default)).ShouldSucceed()) { }
        _workspace.Diagnostics.Lines.Should().BeEmpty();
        new FileInfo(_workspace.Out.Task(MemberPath.Root, TaskId.Compile).Lock).Length.Should().Be(0, "the lock file is empty");
    }

    [Test]
    public async Task AWaitingRunCanBeCancelled()
    {
        using var first = (await _workspace.Store().BeginAsync(default)).ShouldSucceed();
        using var cancellation = new CancellationTokenSource();
        var waiterDiagnostics = new LineWriter();
        var waiting = _workspace.Store(diagnostics: waiterDiagnostics).OpenCompletedAsync(cancellation.Token).AsTask();
        await waiterDiagnostics.FirstLine.WaitAsync(Bound);

        await cancellation.CancelAsync();

        (await waiting.WaitAsync(Bound)).ShouldFail("cancelled");
    }

    private async Task CompleteOnce()
    {
        using var run = (await _workspace.Store().BeginAsync(default)).ShouldSucceed();
        await File.WriteAllTextAsync(Path.Combine(run.Dest, "morphir-ir.json"), "ir");
        run.Complete(new CompileProduct("fsharp", RecordWorkspace.SingleFileIr(), null)).ShouldSucceed();
    }
}
