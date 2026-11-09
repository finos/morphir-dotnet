// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Nodes;
using FluentAssertions;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.TaskRecords;
using Morphir.Tooling.Tests.Features.Generate;

namespace Morphir.Tooling.Tests.Features.TaskRecords;

/// <summary>
/// Spec: compile-task-records, the out root, record, tombstone and generate-input requirements. The tests
/// play the part of the compile handler: they write the IR into <c>.dest</c> themselves.
/// </summary>
public sealed class CompileTaskStoreTests : IDisposable
{
    private const string Ir = """{"formatVersion": 4, "distribution": []}""";
    private readonly RecordWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private static async Task<TaskRecord> Succeed(CompileTaskStore store, string? projectFile = null, string ir = Ir)
    {
        using var run = (await store.BeginAsync(default)).ShouldSucceed();
        await File.WriteAllTextAsync(Path.Combine(run.Dest, "morphir-ir.json"), ir);
        return run.Complete(new CompileProduct("fsharp", RecordWorkspace.SingleFileIr(), projectFile)).ShouldSucceed();
    }

    [Test]
    public async Task AWorkspaceMemberWritesItsIrAndRecordUnderItsMemberPath()
    {
        var member = MemberPath.FromDirectories(_workspace.Root, _workspace.PathOf("packages", "orders")).ShouldSucceed();
        var store = _workspace.Store(member);

        var record = await Succeed(store, _workspace.PathOf("packages", "orders", "src", "Orders.fsproj"));

        var expected = _workspace.PathOf(".morphir", "out", "packages", "orders");
        File.ReadAllText(Path.Combine(expected, "compile.dest", "morphir-ir.json")).Should().Be(Ir);
        File.Exists(Path.Combine(expected, "compile.json")).Should().BeTrue();
        record.Module.Value.Should().Be("packages/orders");
        record.Inputs.Should().Equal("packages/orders/src/Orders.fsproj");

        var written = JsonNode.Parse(File.ReadAllText(Path.Combine(expected, "compile.json")))!.AsObject();
        written["module"]!.GetValue<string>().Should().Be("packages/orders");
        written["inputs"]!.AsArray().Select(input => input!.GetValue<string>()).Should().Equal("packages/orders/src/Orders.fsproj");
    }

    [Test]
    public async Task ACompletedRecordHasEveryMemberOfTheUpstreamExampleWithTheSameSpelling()
    {
        await Succeed(_workspace.Store());

        var upstream = JsonNode.Parse(File.ReadAllText(Path.Combine(GenerateSteps.RepositoryRoot(), "tests", "Fixtures", "MorphirOut", "compile-record.json")))!.AsObject();
        var written = JsonNode.Parse(File.ReadAllText(_workspace.Out.Task(MemberPath.Root, TaskId.Compile).Record))!.AsObject();

        written.Select(member => member.Key).Should().Equal(upstream.Select(member => member.Key));
        written["ir"]!.AsObject().Select(member => member.Key).Should().Equal(upstream["ir"]!.AsObject().Select(member => member.Key));
        written["schema"]!.GetValue<int>().Should().Be(1);
        written["task"]!.GetValue<string>().Should().Be("compile");
        written["module"]!.GetValue<string>().Should().BeEmpty();
        written["language"]!.GetValue<string>().Should().Be("fsharp");
        written["inputs"]!.AsArray().Should().BeEmpty();
        written["value"]!.ToJsonString().Should().Be("""["morphir-ir.json"]""");
        written["ir"]!.ToJsonString().Should().Be("""{"path":"morphir-ir.json","layout":"single-file","format":"json","version":"v4"}""");
        written["installed"]!.ToJsonString().Should().Be("{}");
        written["completedAt"]!.GetValue<string>().Should().Be("2026-09-02T10:00:00Z");
    }

    [Test]
    public async Task StartingARunClearsTheDestDirectory()
    {
        var store = _workspace.Store();
        using (var first = (await store.BeginAsync(default)).ShouldSucceed())
            await File.WriteAllTextAsync(Path.Combine(first.Dest, "stale.txt"), "old");

        using var second = (await store.BeginAsync(default)).ShouldSucceed();
        Directory.Exists(second.Dest).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(second.Dest).Should().BeEmpty();
    }

    [Test]
    public async Task AFailureAfterSuccessLeavesATombstoneThatKeepsTheLedgerAndNoIr()
    {
        var store = _workspace.Store();
        var dist = _workspace.PathOf("dist");
        using (var run = (await store.BeginAsync(default)).ShouldSucceed())
        {
            await File.WriteAllTextAsync(Path.Combine(run.Dest, "morphir-ir.json"), Ir);
            run.Complete(new CompileProduct("fsharp", RecordWorkspace.SingleFileIr(), null)).ShouldSucceed();
            (await run.InstallAsync(dist, default)).ShouldSucceed();
        }

        var later = _workspace.Store(now: RecordWorkspace.FirstTime.AddHours(1));
        using (var failed = (await later.BeginAsync(default)).ShouldSucceed())
        {
            // The source has an error: the handler writes no IR and does not complete the run.
            failed.PreviousInstalled.Keys.Should().Equal(dist);
        }

        var location = _workspace.Out.Task(MemberPath.Root, TaskId.Compile);
        var tombstone = JsonNode.Parse(File.ReadAllText(location.Record))!.AsObject();
        tombstone["tombstone"]!.GetValue<bool>().Should().BeTrue();
        tombstone["value"]!.AsArray().Should().BeEmpty();
        tombstone["inputs"]!.AsArray().Should().BeEmpty();
        tombstone.ContainsKey("ir").Should().BeFalse();
        tombstone.ContainsKey("language").Should().BeFalse();
        tombstone["installed"]!.ToJsonString().Should().Be(new JsonObject { [dist] = new JsonArray("morphir-ir.json") }.ToJsonString());
        tombstone["completedAt"]!.GetValue<string>().Should().Be("2026-09-02T10:00:00Z");
        Directory.EnumerateFiles(location.Dest, "*", SearchOption.AllDirectories).Should().BeEmpty();
        File.ReadAllText(Path.Combine(dist, "morphir-ir.json")).Should().Be(Ir, "a failed compile does not touch the install target");
    }

    [Test]
    public async Task ASuccessAfterATombstoneCarriesTheLedgerForward()
    {
        var store = _workspace.Store();
        var dist = _workspace.PathOf("dist");
        using (var run = (await store.BeginAsync(default)).ShouldSucceed())
        {
            await File.WriteAllTextAsync(Path.Combine(run.Dest, "morphir-ir.json"), Ir);
            run.Complete(new CompileProduct("fsharp", RecordWorkspace.SingleFileIr(), null)).ShouldSucceed();
            (await run.InstallAsync(dist, default)).ShouldSucceed();
        }
        (await store.BeginAsync(default)).ShouldSucceed().Dispose();

        var record = await Succeed(store, ir: """{"formatVersion": 4, "distribution": ["again"]}""");

        record.Tombstone.Should().BeFalse();
        record.Installed.Keys.Should().Equal(dist);
    }

    [Test]
    public async Task AnUnreadableRecordIsRemovedWithOneWarningLine()
    {
        var location = _workspace.Out.Task(MemberPath.Root, TaskId.Compile);
        Directory.CreateDirectory(Path.GetDirectoryName(location.Record)!);
        await File.WriteAllTextAsync(location.Record, "not valid json");

        using var run = (await _workspace.Store().BeginAsync(default)).ShouldSucceed();

        File.Exists(location.Record).Should().BeFalse();
        run.PreviousInstalled.Should().BeEmpty();
        _workspace.Diagnostics.Lines.Should().ContainSingle()
            .Which.Should().StartWith("warning: could not read previous task record at " + location.Record);
    }

    [Test]
    public async Task AReaderGetsTheIrOfTheCompletedCompile()
    {
        await Succeed(_workspace.Store());

        using var output = (await _workspace.Store().OpenCompletedAsync(default)).ShouldSucceed();

        output.IrPath.Should().Be(Path.Combine(_workspace.Out.Task(MemberPath.Root, TaskId.Compile).Dest, "morphir-ir.json"));
        File.ReadAllText(output.IrPath).Should().Be(Ir);
        output.Ir.Spelling.Should().Be("v4");
        output.Ir.Version.Text.Should().Be("4");
    }

    [Test]
    public async Task AReaderWithNoPriorCompileFailsAndCreatesNothing()
    {
        var failure = (await _workspace.Store().OpenCompletedAsync(default)).ShouldFail("compile.record_missing");

        failure.Kind.Should().Be(GenerationFailureKind.Invocation);
        failure.Diagnostics.Single().Message.Should().Contain("morphir compile");
        Directory.Exists(_workspace.PathOf(".morphir")).Should().BeFalse();
    }

    [Test]
    public async Task AReaderTreatsATombstoneAsAMissingRecord()
    {
        await Succeed(_workspace.Store());
        (await _workspace.Store().BeginAsync(default)).ShouldSucceed().Dispose();

        var failure = (await _workspace.Store().OpenCompletedAsync(default)).ShouldFail("compile.record_missing");
        failure.Kind.Should().Be(GenerationFailureKind.Invocation);
        failure.Diagnostics.Single().Message.Should().Contain("morphir compile");
    }

    [Test]
    public async Task AReaderRefusesARecordWithoutAnIrDescriptor()
    {
        var location = _workspace.Out.Task(MemberPath.Root, TaskId.Compile);
        Directory.CreateDirectory(location.Dest);
        await File.WriteAllTextAsync(location.Record, """{"schema": 1, "task": "compile", "module": "", "completedAt": "2026-09-02T10:00:00Z"}""");

        (await _workspace.Store().OpenCompletedAsync(default)).ShouldFail("compile.record_without_ir")
            .Kind.Should().Be(GenerationFailureKind.Invocation);
    }

    [Test]
    public async Task AReaderRefusesAnUnreadableRecord()
    {
        var location = _workspace.Out.Task(MemberPath.Root, TaskId.Compile);
        Directory.CreateDirectory(location.Dest);
        await File.WriteAllTextAsync(location.Record, "{");

        (await _workspace.Store().OpenCompletedAsync(default)).ShouldFail("compile.record_unreadable")
            .Kind.Should().Be(GenerationFailureKind.Operational);
    }

    [Test]
    public async Task ACancelledStartTouchesNothing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        (await _workspace.Store().BeginAsync(cancelled.Token)).ShouldFail("cancelled").Kind.Should().Be(GenerationFailureKind.Cancelled);
        Directory.Exists(_workspace.PathOf(".morphir")).Should().BeFalse();
    }
}
