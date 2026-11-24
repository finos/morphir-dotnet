// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.Tooling.Features.TaskRecords;
using TUnit.Core.Enums;
using Path = System.IO.Path;

namespace Morphir.Tooling.Tests.Features.TaskRecords;

/// <summary>
/// A virus scanner or a search indexer opens a file just after it is written, without delete sharing, and while it
/// holds the record Windows refuses to rename the new record over it. The write waits a bounded time, and it does
/// not replace a record that changed during the wait (compile-task-records: records are written only by the run
/// that holds the task's lock). Other systems rename over a file that another handle holds open, so these tests run
/// on Windows only. The handle is released or the record changed from the hook that runs after each wait, so no
/// timing decides the outcome.
/// </summary>
[Category("PlatformSpecific")]
public sealed class RecordFileTests
{
    // As a scanner does: it reads, and shares reading and writing but not deletion.
    private static FileStream HoldWithoutDeleteSharing(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    private static string[] OtherFiles(string path) =>
        Directory.GetFiles(Path.GetDirectoryName(path)!).Where(file => file != path).ToArray();

    /// <summary>
    /// A cleanup of the staged record whose delete fails as a scanner's handle makes it fail on Windows, the first
    /// <paramref name="failures"/> times. These tests use it on every system, so no handle and no timing decides.
    /// </summary>
    private sealed class ScannedCleanup(int failures)
    {
        public int Deletes { get; private set; }

        public PathRemoval Cleanup => new(path =>
        {
            if (++Deletes <= failures) throw new IOException("The process cannot access the file because it is being used by another process.");
            File.Delete(path);
        }, _ => true, _ => { });
    }

    [Test]
    public void ACancelledReplacementRetriesTheCleanupOfItsStagedRecordAndStaysCancelled()
    {
        using var sandbox = TestSandbox.Create("morphir-record-");
        var path = sandbox.PathOf("out", "compile.json");
        var record = TaskRecordMother.CompletedCompile();
        RecordFile.Write(path, record);
        using var cancellation = new CancellationTokenSource();
        var scanner = new ScannedCleanup(failures: 2);

        Action write = () => RecordFile.Write(path, record.ToTombstone(), afterWait: null, cancellation.Token, cancellation.Cancel, scanner.Cleanup);

        write.Should().Throw<OperationCanceledException>("the cancellation is the outcome, not the cleanup");
        scanner.Deletes.Should().Be(3, "two deletes met the handle and the third removed the staged record");
        File.ReadAllBytes(path).Should().Equal(TaskRecordCodec.Encode(record), "the record is not replaced");
        OtherFiles(path).Should().BeEmpty("the staged record is gone");
    }

    [Test]
    public void ACleanupThatKeepsFailingDoesNotHideTheCancellation()
    {
        using var sandbox = TestSandbox.Create("morphir-record-");
        var path = sandbox.PathOf("out", "compile.json");
        var record = TaskRecordMother.CompletedCompile();
        RecordFile.Write(path, record);
        using var cancellation = new CancellationTokenSource();
        var scanner = new ScannedCleanup(failures: int.MaxValue);

        Action write = () => RecordFile.Write(path, record.ToTombstone(), afterWait: null, cancellation.Token, cancellation.Cancel, scanner.Cleanup);

        write.Should().Throw<OperationCanceledException>();
        scanner.Deletes.Should().Be(Morphir.Files.FileReplacement.Attempts, "the cleanup tries a bounded number of times");
        File.ReadAllBytes(path).Should().Equal(TaskRecordCodec.Encode(record));
    }

    [Test]
    [RunOn(OS.Windows)]
    public void TheWriteWaitsForAShortLivedHandleOnTheRecord()
    {
        using var sandbox = TestSandbox.Create("morphir-record-");
        var path = sandbox.PathOf("out", "compile.json");
        var record = TaskRecordMother.CompletedCompile();
        RecordFile.Write(path, record);
        var scanner = HoldWithoutDeleteSharing(path);
        var waits = 0;
        try
        {
            RecordFile.Write(path, record.ToTombstone(), afterWait: () =>
            {
                if (waits++ == 0) scanner.Dispose();
            });
        }
        finally { scanner.Dispose(); }

        waits.Should().Be(1, "the first rename met the handle and the second did not");
        File.ReadAllBytes(path).Should().Equal(TaskRecordCodec.Encode(record.ToTombstone()));
        OtherFiles(path).Should().BeEmpty("the temporary file is gone");
    }

    [Test]
    [RunOn(OS.Windows)]
    public void TheWriteDoesNotReplaceARecordChangedDuringTheWait()
    {
        using var sandbox = TestSandbox.Create("morphir-record-");
        var path = sandbox.PathOf("out", "compile.json");
        var record = TaskRecordMother.CompletedCompile();
        RecordFile.Write(path, record);
        var other = Encoding.UTF8.GetBytes("{\"written\":\"by another tool\"}");
        var scanner = HoldWithoutDeleteSharing(path);
        Action write = () => RecordFile.Write(path, record.ToTombstone(), afterWait: () =>
        {
            if (!scanner.CanRead) return;
            // The scanner's sharing lets another writer change the record in place while the scanner holds it.
            using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            {
                writer.SetLength(0);
                writer.Write(other);
            }
            scanner.Dispose();
        });
        try
        {
            write.Should().Throw<IOException>().WithMessage("*changed while its replacement waited*");
        }
        finally { scanner.Dispose(); }

        File.ReadAllBytes(path).Should().Equal(other, "the other writer's bytes are kept");
        OtherFiles(path).Should().BeEmpty("the temporary file is gone");
    }

    [Test]
    [RunOn(OS.Windows)]
    public void TheWriteGivesUpOnAHandleThatStaysOpenAndKeepsTheOldRecord()
    {
        using var sandbox = TestSandbox.Create("morphir-record-");
        var path = sandbox.PathOf("out", "compile.json");
        var record = TaskRecordMother.CompletedCompile();
        RecordFile.Write(path, record);
        var waits = 0;
        var clock = Stopwatch.StartNew();
        Exception? failure = null;
        using (HoldWithoutDeleteSharing(path))
        {
            try { RecordFile.Write(path, record.ToTombstone(), afterWait: () => waits++); }
            catch (Exception error) { failure = error; }
        }
        clock.Stop();

        failure.Should().NotBeNull();
        TaskFailures.IsIo(failure!).Should().BeTrue("the compile reports compile.record_write_failed");
        waits.Should().Be(9, "the rename is tried 10 times");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "the waits add up to about 2.6 seconds");
        File.ReadAllBytes(path).Should().Equal(TaskRecordCodec.Encode(record));
        OtherFiles(path).Should().BeEmpty("the temporary file is gone");
    }
}
