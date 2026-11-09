// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Concurrent;
using System.Text;
using FluentAssertions;
using Morphir.IR;
using Path = System.IO.Path;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.TaskRecords;

namespace Morphir.Tooling.Tests.Features.TaskRecords;

/// <summary>A temporary workspace with its own out root and Morphir home.</summary>
internal sealed class RecordWorkspace : IDisposable
{
    public RecordWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "morphir-records-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        MorphirHome = Path.Combine(Root, "home", ".morphir");
        Out = ((Outcome<OutRoot>.Success)OutRoot.Resolve(new OutRootRequest(Root, Root))).Value;
    }

    public string Root { get; }
    public string MorphirHome { get; }
    public OutRoot Out { get; }
    public LineWriter Diagnostics { get; } = new();

    public static readonly DateTimeOffset FirstTime = new(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);

    public CompileTaskStore Store(MemberPath? member = null, LineWriter? diagnostics = null, DateTimeOffset? now = null) =>
        new(Out, member ?? MemberPath.Root, new TaskStoreOptions
        {
            Diagnostics = diagnostics ?? Diagnostics,
            Time = new FixedTime(now ?? FirstTime),
            MorphirHome = MorphirHome,
            PollInterval = TimeSpan.FromMilliseconds(10),
        });

    public string PathOf(params string[] segments) => Path.Combine([Root, .. segments]);

    public static IrDescriptor SingleFileIr(string version = "4") =>
        IrDescriptor.Create(RecordPath.Parse("morphir-ir.json"), IrLayout.SingleFile, IrStorageFormat.Json, Versioning.parse(version).ResultValue);

    public void Dispose() => TolerantDirectory.TryDelete(Root);
}

/// <summary>A clock that always answers one time.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Collects written lines, and signals when the first one arrives.</summary>
internal sealed class LineWriter : TextWriter
{
    private readonly StringBuilder _pending = new();
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly TaskCompletionSource _first = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override Encoding Encoding => Encoding.UTF8;
    public IReadOnlyList<string> Lines => _lines.ToArray();
    public Task FirstLine => _first.Task;

    public override void Write(char value)
    {
        lock (_pending)
        {
            if (value == '\n')
            {
                _lines.Enqueue(_pending.ToString().TrimEnd('\r'));
                _pending.Clear();
                _first.TrySetResult();
            }
            else
            {
                _pending.Append(value);
            }
        }
    }
}

internal static class OutcomeAssertions
{
    public static T ShouldSucceed<T>(this Outcome<T> outcome)
    {
        if (outcome is Outcome<T>.Failure failure)
            throw new InvalidOperationException("Expected success: " + string.Join("; ", failure.Error.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return ((Outcome<T>.Success)outcome).Value;
    }

    public static GenerationFailure ShouldFail<T>(this Outcome<T> outcome, string code)
    {
        var failure = outcome.Should().BeOfType<Outcome<T>.Failure>().Subject.Error;
        failure.Diagnostics.Select(d => d.Code).Should().Contain(code);
        return failure;
    }
}

/// <summary>A snapshot of a directory tree: every path, and the bytes of every file.</summary>
internal static class TreeSnapshot
{
    public static string[] Of(string root) =>
        !Directory.Exists(root)
            ? []
            : Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')
                    + (File.Exists(path) ? " " + Convert.ToHexString(File.ReadAllBytes(path)) : "/"))
                .ToArray();
}
