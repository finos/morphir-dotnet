// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Nodes;
using FluentAssertions;
using Morphir.IR;
using Path = System.IO.Path;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.TaskRecords;

namespace Morphir.Tooling.Tests.Features.TaskRecords;

/// <summary>
/// Spec: compile-task-records and the pinned upstream out-directory contract, "Install". <c>-o</c> copies
/// the record's <c>value</c> into the target and keeps an <c>installed</c> ledger of the files it wrote.
/// </summary>
[Category("PlatformSpecific")]
public sealed class TaskInstallerTests : IDisposable
{
    private readonly RecordWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private string Dist => _workspace.PathOf("dist");
    private TaskLocation Location => _workspace.Out.Task(MemberPath.Root, TaskId.Compile);

    private static readonly IrDescriptor TreeIr = IrDescriptor.Create(
        RecordPath.Parse("morphir-ir"), IrLayout.DocumentTree, IrStorageFormat.Json, Versioning.parse("4").ResultValue);

    /// <summary>One compile run that writes <paramref name="files"/> into .dest and installs to <paramref name="target"/>.</summary>
    private async Task<Outcome<InstallReport>> CompileAndInstall(IrDescriptor ir, string target, params (string Path, string Text)[] files)
    {
        using var run = (await _workspace.Store().BeginAsync(default)).ShouldSucceed();
        foreach (var (path, text) in files)
        {
            var file = Path.Combine(run.Dest, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, text);
        }
        run.Complete(new CompileProduct("fsharp", ir, null)).ShouldSucceed();
        return await run.InstallAsync(target, default);
    }

    private JsonObject Record() => JsonNode.Parse(File.ReadAllText(Location.Record))!.AsObject();

    private string[] Ledger(string target) =>
        Record()["installed"]![target]!.AsArray().Select(path => path!.GetValue<string>()).ToArray();

    [Test]
    public async Task AReinstallRetiresOnlyFilesItWroteAndKeepsForeignContent()
    {
        var first = (await CompileAndInstall(RecordWorkspace.SingleFileIr(), Dist, ("morphir-ir.json", "v1"))).ShouldSucceed();
        first.Target.Should().Be(Dist);
        first.Written.Should().Equal("morphir-ir.json");
        File.ReadAllText(Path.Combine(Dist, "morphir-ir.json")).Should().Be("v1");
        Ledger(Dist).Should().Equal("morphir-ir.json");

        // The user adds files beside and inside the directory that the next run installs into.
        await File.WriteAllTextAsync(Path.Combine(Dist, "notes.txt"), "mine");
        Directory.CreateDirectory(Path.Combine(Dist, "morphir-ir"));
        await File.WriteAllTextAsync(Path.Combine(Dist, "morphir-ir", "user.txt"), "mine too");

        var second = (await CompileAndInstall(TreeIr, Dist,
            ("morphir-ir/manifest.json", "{}"), ("morphir-ir/Module.json", "{\"m\": 1}"))).ShouldSucceed();

        second.Copied.Should().Equal("morphir-ir");
        second.Removed.Should().Equal("morphir-ir.json");
        second.Written.Should().BeEquivalentTo("morphir-ir/manifest.json", "morphir-ir/Module.json");
        File.Exists(Path.Combine(Dist, "morphir-ir.json")).Should().BeFalse();
        File.ReadAllText(Path.Combine(Dist, "notes.txt")).Should().Be("mine");
        File.ReadAllText(Path.Combine(Dist, "morphir-ir", "user.txt")).Should().Be("mine too");
        File.ReadAllText(Path.Combine(Dist, "morphir-ir", "Module.json")).Should().Be("{\"m\": 1}");
        Ledger(Dist).Should().Equal("morphir-ir/Module.json", "morphir-ir/manifest.json");
    }

    [Test]
    public async Task AnIdenticalReinstallLeavesFilesUnchangedAndAShrinkingOnePrunesEmptyDirectories()
    {
        (await CompileAndInstall(TreeIr, Dist, ("morphir-ir/a/One.json", "1"), ("morphir-ir/manifest.json", "{}"))).ShouldSucceed();

        var repeat = (await CompileAndInstall(TreeIr, Dist, ("morphir-ir/a/One.json", "1"), ("morphir-ir/manifest.json", "{}"))).ShouldSucceed();
        repeat.Written.Should().BeEmpty();
        repeat.Unchanged.Should().BeEquivalentTo("morphir-ir/a/One.json", "morphir-ir/manifest.json");
        repeat.Removed.Should().BeEmpty();

        var shrink = (await CompileAndInstall(TreeIr, Dist, ("morphir-ir/manifest.json", "{}"))).ShouldSucceed();
        shrink.Removed.Should().Equal("morphir-ir/a/One.json");
        Directory.Exists(Path.Combine(Dist, "morphir-ir", "a")).Should().BeFalse("a directory left empty by a removal is removed");
        Directory.Exists(Path.Combine(Dist, "morphir-ir")).Should().BeTrue();
        Ledger(Dist).Should().Equal("morphir-ir/manifest.json");
    }

    [Test]
    public async Task AConflictWithForeignContentLeavesTheDirectoryUnchanged()
    {
        Directory.CreateDirectory(Dist);
        await File.WriteAllTextAsync(Path.Combine(Dist, "morphir-ir.json"), "the user's file");
        await File.WriteAllTextAsync(Path.Combine(Dist, "other.txt"), "other");
        var before = TreeSnapshot.Of(Dist);

        var failure = (await CompileAndInstall(RecordWorkspace.SingleFileIr(), Dist, ("morphir-ir.json", "generated")))
            .ShouldFail("install.foreign_content");

        failure.Kind.Should().Be(GenerationFailureKind.Invocation);
        failure.Diagnostics.Single().Message.Should().Contain("morphir-ir.json");
        TreeSnapshot.Of(Dist).Should().Equal(before);
        Record()["installed"]!.AsObject().Count.Should().Be(0, "a refused install records nothing");
    }

    [Test]
    public async Task AConflictAfterAnInstallRemovesNothingAndListsEveryConflict()
    {
        (await CompileAndInstall(RecordWorkspace.SingleFileIr(), Dist, ("morphir-ir.json", "v1"))).ShouldSucceed();
        Directory.CreateDirectory(Path.Combine(Dist, "morphir-ir"));
        await File.WriteAllTextAsync(Path.Combine(Dist, "morphir-ir", "manifest.json"), "foreign");
        await File.WriteAllTextAsync(Path.Combine(Dist, "morphir-ir", "Module.json"), "foreign");
        var before = TreeSnapshot.Of(Dist);

        var failure = (await CompileAndInstall(TreeIr, Dist,
            ("morphir-ir/manifest.json", "{}"), ("morphir-ir/Module.json", "{}"))).ShouldFail("install.foreign_content");

        failure.Diagnostics.Single().Message.Should().Contain("morphir-ir/manifest.json").And.Contain("morphir-ir/Module.json");
        TreeSnapshot.Of(Dist).Should().Equal(before, "the stale owned file is not removed either");
        Ledger(Dist).Should().Equal("morphir-ir.json");
    }

    [Test]
    public async Task AForeignFileInPlaceOfAnOutputDirectoryRemovesNothing()
    {
        (await CompileAndInstall(RecordWorkspace.SingleFileIr(), Dist, ("morphir-ir.json", "v1"))).ShouldSucceed();
        // The user puts a file where the next install needs the directory morphir-ir.
        await File.WriteAllTextAsync(Path.Combine(Dist, "morphir-ir"), "the user's file");
        var before = TreeSnapshot.Of(Dist);

        var failure = (await CompileAndInstall(TreeIr, Dist,
            ("morphir-ir/manifest.json", "{}"), ("morphir-ir/Module.json", "{}"))).ShouldFail("install.foreign_content");

        failure.Kind.Should().Be(GenerationFailureKind.Invocation);
        failure.Diagnostics.Single().Message.Should().Contain(": morphir-ir.");
        TreeSnapshot.Of(Dist).Should().Equal(before, "the stale owned file is not removed either");
        Ledger(Dist).Should().Equal("morphir-ir.json");
    }

    [Test]
    public async Task AnOwnedFileInPlaceOfAnOutputDirectoryIsReplaced()
    {
        // An earlier install wrote a file named morphir-ir; the next one needs a directory of that name.
        (await CompileAndInstall(RecordWorkspace.SingleFileIr() with { Path = RecordPath.Parse("morphir-ir") }, Dist,
            ("morphir-ir", "v1"))).ShouldSucceed();

        var report = (await CompileAndInstall(TreeIr, Dist, ("morphir-ir/manifest.json", "{}"))).ShouldSucceed();

        report.Removed.Should().Equal("morphir-ir");
        File.ReadAllText(Path.Combine(Dist, "morphir-ir", "manifest.json")).Should().Be("{}");
        Ledger(Dist).Should().Equal("morphir-ir/manifest.json");
    }

    [Test]
    public async Task AnInstallAfterAFailedCompileStillRecognizesItsOwnFiles()
    {
        (await CompileAndInstall(RecordWorkspace.SingleFileIr(), Dist, ("morphir-ir.json", "v1"))).ShouldSucceed();
        (await _workspace.Store().BeginAsync(default)).ShouldSucceed().Dispose();

        var report = (await CompileAndInstall(RecordWorkspace.SingleFileIr(), Dist, ("morphir-ir.json", "v2"))).ShouldSucceed();

        report.Written.Should().Equal("morphir-ir.json");
        File.ReadAllText(Path.Combine(Dist, "morphir-ir.json")).Should().Be("v2");
    }

    [Test]
    public async Task ATombstoneIsNotInstalled()
    {
        using var run = (await _workspace.Store().BeginAsync(default)).ShouldSucceed();
        Directory.CreateDirectory(Path.GetDirectoryName(Location.Record)!);
        await File.WriteAllTextAsync(Location.Record,
            """{"schema": 1, "task": "compile", "module": "", "value": [], "tombstone": true, "completedAt": "2026-09-02T10:00:00Z"}""");

        (await run.InstallAsync(Dist, default)).ShouldFail("install.tombstone").Kind.Should().Be(GenerationFailureKind.Invocation);
        Directory.Exists(Dist).Should().BeFalse();
    }

    [Test]
    public async Task ATargetThatIsAFileIsRefused()
    {
        await File.WriteAllTextAsync(Dist, "a file");

        (await CompileAndInstall(RecordWorkspace.SingleFileIr(), Dist, ("morphir-ir.json", "v1")))
            .ShouldFail("install.target_not_directory").Diagnostics.Single().Message.Should().Contain(Dist);
        File.ReadAllText(Dist).Should().Be("a file");
    }

    [Test]
    public async Task ATargetInsideTheDestDirectoryIsRefused()
    {
        (await CompileAndInstall(RecordWorkspace.SingleFileIr(), Path.Combine(Location.Dest, "nested"), ("morphir-ir.json", "v1")))
            .ShouldFail("install.target_in_dest");
    }

    [Test]
    public async Task ALinkBelowTheTargetIsRefused()
    {
        var outside = _workspace.PathOf("outside");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(Dist);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(Dist, "morphir-ir"), outside);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return; // This host cannot create links (Windows without developer mode).
        }

        (await CompileAndInstall(TreeIr, Dist, ("morphir-ir/manifest.json", "{}"))).ShouldFail("install.link_below_target");
        Directory.EnumerateFileSystemEntries(outside).Should().BeEmpty();
    }

    [Test]
    public async Task UnknownRecordMembersSurviveTheLedgerRewrite()
    {
        using var run = (await _workspace.Store().BeginAsync(default)).ShouldSucceed();
        await File.WriteAllTextAsync(Path.Combine(run.Dest, "morphir-ir.json"), "v1");
        run.Complete(new CompileProduct("fsharp", RecordWorkspace.SingleFileIr(), null)).ShouldSucceed();
        var record = Record();
        record["inputsHash"] = "sha256:abc";
        await File.WriteAllTextAsync(Location.Record, record.ToJsonString());

        (await run.InstallAsync(Dist, default)).ShouldSucceed();

        Record()["inputsHash"]!.GetValue<string>().Should().Be("sha256:abc");
        Ledger(Dist).Should().Equal("morphir-ir.json");
    }

    [Test]
    public async Task TheInstallLockLivesInTheMorphirHomeAndNotInTheTarget()
    {
        (await CompileAndInstall(RecordWorkspace.SingleFileIr(), Dist, ("morphir-ir.json", "v1"))).ShouldSucceed();

        Directory.EnumerateFiles(Path.Combine(_workspace.MorphirHome, "locks", "install"), "*.lock").Should().ContainSingle();
        TreeSnapshot.Of(Dist).Should().Equal("morphir-ir.json " + Convert.ToHexString("v1"u8.ToArray()));
    }
}
