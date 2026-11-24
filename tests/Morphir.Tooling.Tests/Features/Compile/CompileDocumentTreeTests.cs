// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Compilation;
using Morphir.Files;
using Morphir.Frontend.FSharp;
using Morphir.IR.Trees;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.TaskRecords;
using Morphir.Tooling.Scenarios;
using GenerationReport = Morphir.Tooling.Features.Generate.GenerationReport;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>
/// <c>ir.layout = "document-tree"</c> and <c>--ir-layout</c>: the compile writes a JSON document tree to
/// <c>compile.dest/morphir-ir</c>, records it in <c>compile.json</c>, installs it with <c>-o</c>, generates from it with
/// <c>--target</c>, and gives bounded <c>generate</c> a record that it reads.
/// </summary>
[Category("PlatformSpecific")]
public sealed class CompileDocumentTreeTests
{
    private const string TreeDirectory = ".morphir/out/compile.dest/morphir-ir";

    private static CompileWorkspace Project()
    {
        var workspace = new CompileWorkspace();
        workspace.Write("src/Rates.fs", CompileRunnerTests.Rates);
        workspace.Write("src/Pricing.fs", CompileRunnerTests.Pricing);
        return workspace;
    }

    private static CompileOverrides Inputs(string version = "4", string layout = "document-tree", string? output = null) => new()
    {
        Language = "fsharp",
        Inputs = ["src/Rates.fs", "src/Pricing.fs"],
        PackageName = "acme/car-rental",
        IrVersion = version,
        IrLayout = layout,
        Output = output,
    };

    private static async Task<CompileResult> Run(CompileWorkspace workspace, CompileOverrides overrides) =>
        CompileHandler.Fold(await CompileRunners.Events(CompileRunners.Create([FSharpFrontend.Registration]), overrides, workspace.Context()));

    private static CompileSuccess Succeeded(CompileResult result)
    {
        result.ExitCode.Should().Be(0, string.Join("\n", result.Diagnostics.Select(d => d.Diagnostic)));
        return ((CompileConclusion.Succeeded)result.Conclusion).Value;
    }

    /// <summary>The tree at <paramref name="directory"/> as the document-tree store reads it.</summary>
    private static StoredModel ReadTree(string directory)
    {
        VFileTree.TryLoadFromDisk(directory, out var files, out _).Should().BeTrue();
        var read = DocumentTreeStore.Read(ReadOptionsModule.defaults, files!);
        StoredModel.TryOfTreeRead(read, out var model, out _, out _, out var problems)
            .Should().BeTrue(problems is null ? "" : string.Join("\n", problems));
        return model!;
    }

    /// <summary>The plain single-file JSON of a tree model joined back into one file.</summary>
    private static byte[] SingleFileBytes(StoredModel model)
    {
        model.TryConvert(ModelLayout.SingleFile, out var single, out var problems).Should().BeTrue(problems is null ? "" : string.Join("\n", problems));
        single!.TryWriteSingleFile(false, out var bytes, out problems).Should().BeTrue(problems is null ? "" : string.Join("\n", problems));
        return bytes!;
    }

    private static string[] Files(string root) =>
        [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    [Test]
    [Arguments("4", "4", "v4")]
    [Arguments("3", "3", "v3.1.0")]
    public async Task ADocumentTreeCompileWritesATreeThatJoinsToTheSingleFileModel(string version, string storedVersion, string recorded)
    {
        using var workspace = Project();
        var single = Succeeded(await Run(workspace, Inputs(version, "single-file")));
        // The oracle is the file that the single-file compile published, read as JSON and not through the tree codecs.
        var singleBytes = File.ReadAllBytes(single.Publication.IrPath);

        var success = Succeeded(await Run(workspace, Inputs(version)));

        success.Publication.IrPath.Should().Be(workspace.PathOf(TreeDirectory));
        Directory.Exists(success.Publication.IrPath).Should().BeTrue();
        File.Exists(workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json")).Should().BeFalse("the run clears .dest and writes only the tree");
        var tree = ReadTree(success.Publication.IrPath);
        tree.IrVersion.Should().Be(storedVersion);
        // A tree keeps no module order, so the join lists the modules by name. JSON object members have no order, so
        // DeepEquals compares the members of each object by name and every array in order.
        using (var joined = JsonDocument.Parse(SingleFileBytes(tree)))
        using (var expected = JsonDocument.Parse(singleBytes))
            JsonElement.DeepEquals(joined.RootElement, expected.RootElement).Should().BeTrue("the tree holds the model of the single file");

        using var record = JsonDocument.Parse(File.ReadAllText(workspace.PathOf(".morphir/out/compile.json")));
        var root = record.RootElement;
        root.GetProperty("value").EnumerateArray().Select(e => e.GetString()).Should().Equal("morphir-ir");
        var ir = root.GetProperty("ir");
        ir.GetProperty("path").GetString().Should().Be("morphir-ir");
        ir.GetProperty("layout").GetString().Should().Be("document-tree");
        ir.GetProperty("format").GetString().Should().Be("json");
        ir.GetProperty("version").GetString().Should().Be(recorded);
    }

    private static string Dest(CompileWorkspace workspace) => workspace.PathOf(".morphir/out/compile.dest");

    private static string OutRoot(CompileWorkspace workspace) => workspace.PathOf(".morphir/out");

    private static TaskStoreOptions Quiet => new() { Diagnostics = TextWriter.Null };

    private static async Task<CompileResult> RunWith(CompileWorkspace workspace, CompileOverrides overrides, CompilePublicationHooks hooks,
        TaskStoreOptions? options = null, CancellationToken ct = default)
    {
        var publisher = new TaskRecordCompilePublisher(options ?? Quiet, hooks);
        return CompileHandler.Fold(await CompileRunners.Events(CompileRunners.Create([FSharpFrontend.Registration], publisher: publisher),
            overrides, workspace.Context(), ct));
    }

    private static bool IsTombstone(CompileWorkspace workspace)
    {
        using var record = JsonDocument.Parse(File.ReadAllText(workspace.PathOf(".morphir/out/compile.json")));
        return record.RootElement.TryGetProperty("tombstone", out var tombstone) && tombstone.GetBoolean();
    }

    /// <summary>Neither the IR, a staged IR nor a staged record stays: <c>compile.dest</c> is empty, and only the record is beside it.</summary>
    private static void NothingIsLeft(CompileWorkspace workspace)
    {
        Directory.EnumerateFileSystemEntries(Dest(workspace)).Should().BeEmpty("neither the staged nor a partial IR stays");
        Directory.EnumerateFiles(OutRoot(workspace), ".compile.json.*").Should().BeEmpty("no staged record stays");
    }

    /// <summary>
    /// Writes the files of the tree that the writer gave to <paramref name="staging"/>, then cuts its manifest to half of its
    /// bytes, as a disk that fills up during the stage leaves it.
    /// </summary>
    private static void StagePartly(string staging, VFileTree files)
    {
        VFileTreeModule.MatchPublish<string?>(new PublishOptions(staging, ManifestMode.NoManifest), files, _ => null, TreeWriteErrorModule.describe)
            .Should().BeNull();
        var manifest = Path.Combine(staging, "manifest.json");
        var bytes = File.ReadAllBytes(manifest);
        File.WriteAllBytes(manifest, bytes[..(bytes.Length / 2)]);
    }

    /// <summary>
    /// Copies the tree that the last compile published to <paramref name="staging"/>, as a run that crashed after its stage
    /// leaves it: the files of a real tree, by the writer.
    /// </summary>
    private static void LeaveTheTreeAsAStaleStage(CompileWorkspace workspace, string staging)
    {
        var tree = Path.Combine(Dest(workspace), "morphir-ir");
        foreach (var file in Files(tree))
        {
            var copy = Path.Combine(staging, file);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(Path.Combine(tree, file), copy);
        }
    }

    /// <summary>A stage that writes part of the IR and then fails, as a full disk does.</summary>
    private static CompilePublicationHooks FailingStage(string layout) => layout == "document-tree"
        ? new CompilePublicationHooks
        {
            StageTree = (staging, files) =>
            {
                StagePartly(staging, files);
                throw new IOException("The disk is full.");
            },
        }
        : new CompilePublicationHooks
        {
            StageFile = async (staging, bytes, ct) =>
            {
                await File.WriteAllBytesAsync(staging, bytes[..16], ct);
                throw new IOException("The disk is full.");
            },
        };

    [Test]
    [Arguments("document-tree")]
    [Arguments("single-file")]
    public async Task AFailureDuringStagingRemovesThePartlyStagedIrAndKeepsTheTombstone(string layout)
    {
        using var workspace = Project();
        Succeeded(await Run(workspace, Inputs(layout: layout)));

        var failed = await RunWith(workspace, Inputs(layout: layout), FailingStage(layout));

        failed.ExitCode.Should().Be(2);
        CompileRunners.ErrorCodes(failed).Should().Equal("compile.publish_failed");
        NothingIsLeft(workspace);
        IsTombstone(workspace).Should().BeTrue("the record of the failed run stays a tombstone");
    }

    [Test]
    public async Task AFirstCompileThatFailsDuringStagingLeavesNoRecord()
    {
        using var workspace = Project();

        var failed = await RunWith(workspace, Inputs(), FailingStage("document-tree"));

        failed.ExitCode.Should().Be(2);
        NothingIsLeft(workspace);
        File.Exists(workspace.PathOf(".morphir/out/compile.json")).Should().BeFalse("no earlier record, so no tombstone");
    }

    [Test]
    [Arguments("document-tree")]
    [Arguments("single-file")]
    public async Task ACancellationBeforeTheMoveLeavesNoPartialIrAndKeepsTheTombstone(string layout)
    {
        using var workspace = Project();
        Succeeded(await Run(workspace, Inputs(layout: layout)));
        using var cancellation = new CancellationTokenSource();

        var cancelled = await RunWith(workspace, Inputs(layout: layout), new CompilePublicationHooks { AfterStaged = _ => cancellation.Cancel() },
            ct: cancellation.Token);

        cancelled.ExitCode.Should().Be(130);
        NothingIsLeft(workspace);
        IsTombstone(workspace).Should().BeTrue("a cancelled run completes no record");
    }

    [Test]
    [Arguments("document-tree")]
    [Arguments("single-file")]
    public async Task ACancellationAfterTheMoveRemovesTheMovedIrAndKeepsTheTombstone(string layout)
    {
        using var workspace = Project();
        Succeeded(await Run(workspace, Inputs(layout: layout)));
        using var cancellation = new CancellationTokenSource();
        string? moved = null;

        var cancelled = await RunWith(workspace, Inputs(layout: layout), new CompilePublicationHooks
        {
            AfterMoved = target =>
            {
                moved = target;
                cancellation.Cancel();
            },
        }, ct: cancellation.Token);

        cancelled.ExitCode.Should().Be(130);
        moved.Should().StartWith(Dest(workspace));
        NothingIsLeft(workspace);
        IsTombstone(workspace).Should().BeTrue("a cancelled run completes no record");
    }

    [Test]
    public async Task ACancellationJustBeforeTheRecordReplacementKeepsTheTombstone()
    {
        using var workspace = Project();
        Succeeded(await Run(workspace, Inputs()));
        using var cancellation = new CancellationTokenSource();

        var cancelled = await RunWith(workspace, Inputs(), new CompilePublicationHooks(),
            Quiet with { BeforeRecordReplace = cancellation.Cancel }, cancellation.Token);

        cancelled.ExitCode.Should().Be(130);
        NothingIsLeft(workspace);
        IsTombstone(workspace).Should().BeTrue("the record is not replaced after the cancellation");
    }

    [Test]
    [Arguments("document-tree")]
    [Arguments("single-file")]
    public async Task ARecordReplacementFailureRemovesTheMovedIrAndKeepsTheTombstone(string layout)
    {
        using var workspace = Project();
        Succeeded(await Run(workspace, Inputs(layout: layout)));

        var failed = await RunWith(workspace, Inputs(layout: layout), new CompilePublicationHooks(),
            Quiet with { BeforeRecordReplace = () => throw new IOException("The disk is full.") });

        failed.ExitCode.Should().Be(2);
        CompileRunners.ErrorCodes(failed).Should().Equal("compile.record_write_failed");
        NothingIsLeft(workspace);
        IsTombstone(workspace).Should().BeTrue("the tombstone of the run stays when its record cannot replace it");
    }

    /// <summary>
    /// The lock file that the tree publication of Morphir.Files takes for an output root: the SHA-256 of the full root
    /// (upper case on Windows) in the per-user temp directory <c>morphir-publish-locks</c>.
    /// </summary>
    private static string PublishLockOf(string root)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var identity = OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
        var name = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
        return Path.Combine(Path.GetTempPath(), "morphir-publish-locks", name + ".lock");
    }

    [Test]
    public async Task RepeatedTreeCompilesStageInOneRootAndShareOnePublishLock()
    {
        using var workspace = Project();
        var staged = new List<string>();
        var hooks = new CompilePublicationHooks { AfterStaged = staged.Add };

        for (var run = 0; run < 3; run++) Succeeded(await RunWith(workspace, Inputs(), hooks));

        staged.Should().HaveCount(3).And.OnlyContain(path => path == staged[0], "a run holds compile.lock, so one staging root serves every run");
        Path.GetDirectoryName(staged[0]).Should().Be(Dest(workspace));
        File.Exists(PublishLockOf(staged[0])).Should().BeTrue("the publication of the tree takes the lock of its one staging root");
    }

    /// <summary>
    /// A removal whose delete fails as a scanner's handle makes it fail on Windows, the first <paramref name="failures"/>
    /// times for each path. It runs on every system, so no handle and no timing decides.
    /// </summary>
    private sealed class ScannedRemoval(int failures)
    {
        private readonly Dictionary<string, int> _deletes = new(StringComparer.Ordinal);

        public int DeletesOf(string path) => _deletes.GetValueOrDefault(path);

        public PathRemoval Removal => new(path =>
        {
            _deletes[path] = DeletesOf(path) + 1;
            if (DeletesOf(path) <= failures) throw new IOException("The process cannot access the file because it is being used by another process.");
            PathRemoval.DeletePath(path);
        }, _ => true, _ => { });
    }

    /// <summary>A started publication of a tree compile and its request, with a stale staging root left in <c>compile.dest</c>.</summary>
    private static async Task<(ICompilePublication Publication, CompilePublishRequest Request, string Staging)> StalePublicationAsync(
        CompileWorkspace workspace, CompilePublicationHooks hooks)
    {
        var crashed = workspace.PathOf("crashed-run");
        Succeeded(await Run(workspace, Inputs()));
        LeaveTheTreeAsAStaleStage(workspace, crashed);
        var written = File.ReadAllBytes(Succeeded(await Run(workspace, Inputs(layout: "single-file"))).Publication.IrPath);
        var request = CompilePublishRequest.ForDocumentTree("fsharp", Morphir.IR.Versioning.parse("4").ResultValue, null, written,
            Passed(CompileDocumentTree.Write(written)));
        var settings = Passed(new ProjectCompileSettingsResolver().Resolve(Inputs(), workspace.Context()));
        var publication = Passed(await new TaskRecordCompilePublisher(Quiet, hooks).BeginAsync(settings, default));
        // The run's own begin cleared compile.dest; the stale root appears after it, as another tool would leave it.
        var staging = Path.Combine(Dest(workspace), ".morphir-ir.staging");
        Directory.Move(crashed, staging);
        return (publication, request, staging);
    }

    [Test]
    public async Task AStaleStagingRootIsClearedWithRetriesBeforeTheTreeIsStaged()
    {
        using var workspace = Project();
        var scanner = new ScannedRemoval(failures: 2);
        var (publication, request, staging) = await StalePublicationAsync(workspace, new CompilePublicationHooks { Removal = scanner.Removal });
        using (publication)
        {
            var published = Passed(await publication.PublishAsync(request, default));
            ReadTree(published.IrPath).IrVersion.Should().Be("4");
        }

        scanner.DeletesOf(staging).Should().Be(3, "two deletes met the handle and the third cleared the stale root");
        Directory.Exists(staging).Should().BeFalse();
    }

    [Test]
    public async Task ACompileClearsAStaleStageOfACrashedRunWithRetriesBeforeItStarts()
    {
        using var workspace = Project();
        Succeeded(await Run(workspace, Inputs()));
        var staging = Path.Combine(Dest(workspace), ".morphir-ir.staging");
        LeaveTheTreeAsAStaleStage(workspace, staging);
        var scanner = new ScannedRemoval(failures: 2);

        var compiled = Succeeded(await RunWith(workspace, Inputs(), new CompilePublicationHooks(), Quiet with { Removal = scanner.Removal }));

        scanner.DeletesOf(Dest(workspace)).Should().Be(3, "two deletes met the handle and the third cleared compile.dest");
        Directory.Exists(staging).Should().BeFalse();
        ReadTree(compiled.Publication.IrPath).IrVersion.Should().Be("4");
        IsTombstone(workspace).Should().BeFalse();
    }

    [Test]
    public async Task ACompileThatCannotClearTheStaleStageOfACrashedRunFailsBeforeItStarts()
    {
        using var workspace = Project();
        Succeeded(await Run(workspace, Inputs()));
        var staging = Path.Combine(Dest(workspace), ".morphir-ir.staging");
        LeaveTheTreeAsAStaleStage(workspace, staging);
        var scanner = new ScannedRemoval(failures: int.MaxValue);

        var failed = await RunWith(workspace, Inputs(), new CompilePublicationHooks(), Quiet with { Removal = scanner.Removal });

        failed.ExitCode.Should().Be(2);
        CompileRunners.ErrorCodes(failed).Should().Equal("compile.prepare_failed");
        failed.Diagnostics.Single(d => d.Diagnostic.IsError).Diagnostic.Message.Should().Contain(Dest(workspace));
        scanner.DeletesOf(Dest(workspace)).Should().Be(Morphir.Files.FileReplacement.Attempts, "the begin tries a bounded number of times");
        IsTombstone(workspace).Should().BeTrue("the begin wrote the tombstone before it cleared compile.dest");
        LockIsFree(workspace).Should().BeTrue("a begin that fails releases compile.lock");
    }

    /// <summary>
    /// Whether <c>compile.lock</c> is there and can be taken exclusively, as a run takes it: no run of this process still
    /// holds it. A lock file that is gone throws, so it is never taken for a free lock.
    /// </summary>
    private static bool LockIsFree(CompileWorkspace workspace)
    {
        try
        {
            using var taken = new FileStream(workspace.PathOf(".morphir/out/compile.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    [Test]
    public async Task ACancellationWhileTheBeginRetriesTheClearExitsWithOneThirtyAndReleasesTheLock()
    {
        using var workspace = Project();
        Succeeded(await Run(workspace, Inputs()));
        LeaveTheTreeAsAStaleStage(workspace, Path.Combine(Dest(workspace), ".morphir-ir.staging"));
        using var cancellation = new CancellationTokenSource();
        var deletes = 0;
        // Every delete meets the handle, and the first wait for it is where the user cancels.
        var removal = new PathRemoval(_ =>
        {
            deletes++;
            throw new IOException("The process cannot access the file because it is being used by another process.");
        }, _ => true, _ => cancellation.Cancel());

        var cancelled = await RunWith(workspace, Inputs(), new CompilePublicationHooks(), Quiet with { Removal = removal }, cancellation.Token);

        cancelled.ExitCode.Should().Be(130, "a cancellation before publication is exit code 130, as the compile contract says");
        deletes.Should().Be(1, "the retries stop at the cancellation");
        LockIsFree(workspace).Should().BeTrue("a cancelled begin releases compile.lock");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ACancellationDuringTheLastAttemptOfTheClearExitsWithOneThirty(bool transient)
    {
        using var workspace = Project();
        Succeeded(await Run(workspace, Inputs()));
        LeaveTheTreeAsAStaleStage(workspace, Path.Combine(Dest(workspace), ".morphir-ir.staging"));
        using var cancellation = new CancellationTokenSource();
        var last = transient ? Morphir.Files.FileReplacement.Attempts : 1;
        var deletes = 0;
        // The user cancels during the last delete that the removal makes, after which it would give up.
        var removal = new PathRemoval(_ =>
        {
            if (++deletes == last) cancellation.Cancel();
            throw new IOException("The process cannot access the file because it is being used by another process.");
        }, _ => transient, _ => { });

        var cancelled = await RunWith(workspace, Inputs(), new CompilePublicationHooks(), Quiet with { Removal = removal }, cancellation.Token);

        cancelled.ExitCode.Should().Be(130, "the cancellation decides, not the removal that gave up after it");
        deletes.Should().Be(last);
        LockIsFree(workspace).Should().BeTrue("a cancelled begin releases compile.lock");
    }

    /// <summary>A stderr that fails every write with <paramref name="failure"/>, and runs <paramref name="onWrite"/> before it.</summary>
    private sealed class BrokenWriter(Func<Exception> failure, Action onWrite) : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void Write(char value) => Fail();
        public override void Write(string? value) => Fail();
        public override void WriteLine(string? value) => Fail();

        private void Fail()
        {
            onWrite();
            throw failure();
        }
    }

    [Test]
    [Arguments("io")]
    [Arguments("invalid-operation")]
    public async Task AWarningThatCannotBeWrittenKeepsTheCancellationAndEveryRemoval(string failure)
    {
        using var workspace = Project();
        Succeeded(await Run(workspace, Inputs()));
        using var cancellation = new CancellationTokenSource();
        var scanner = new ScannedRemoval(failures: int.MaxValue);
        var target = Path.Combine(Dest(workspace), "morphir-ir");
        var staging = Path.Combine(Dest(workspace), ".morphir-ir.staging");
        var firstWrite = (Staging: -1, Target: -1);
        var stderr = new BrokenWriter(
            () => failure == "io" ? new IOException("The pipe is broken.") : new InvalidOperationException("The writer is closed."),
            () => firstWrite = firstWrite.Staging < 0 ? (scanner.DeletesOf(staging), scanner.DeletesOf(target)) : firstWrite);

        var cancelled = await RunWith(workspace, Inputs(), new CompilePublicationHooks
        {
            Removal = scanner.Removal,
            // Both paths of the rollback are there: the moved tree, and a staging root that another tool put back.
            AfterMoved = _ =>
            {
                LeaveTheTreeAsAStaleStage(workspace, staging);
                cancellation.Cancel();
            },
        }, Quiet with { Diagnostics = stderr }, cancellation.Token);

        cancelled.ExitCode.Should().Be(130, "a warning that stderr refuses never replaces the cancellation");
        firstWrite.Should().Be((Morphir.Files.FileReplacement.Attempts, Morphir.Files.FileReplacement.Attempts),
            "every removal of the rollback runs to its end before the first warning is written");
    }

    [Test]
    [Arguments("document-tree")]
    [Arguments("single-file")]
    public async Task ACancellationWhileTheStageFailsExitsWithOneThirty(string layout)
    {
        using var workspace = Project();
        Succeeded(await Run(workspace, Inputs(layout: layout)));
        using var cancellation = new CancellationTokenSource();
        // The user cancels while the disk fills up: the cancellation decides, not the failure that it interrupted.
        var hooks = layout == "document-tree"
            ? new CompilePublicationHooks
            {
                StageTree = (staging, files) =>
                {
                    StagePartly(staging, files);
                    cancellation.Cancel();
                    throw new IOException("The disk is full.");
                },
            }
            : new CompilePublicationHooks
            {
                StageFile = (staging, bytes, _) =>
                {
                    File.WriteAllBytes(staging, bytes[..16].ToArray());
                    cancellation.Cancel();
                    throw new IOException("The disk is full.");
                },
            };

        var cancelled = await RunWith(workspace, Inputs(layout: layout), hooks, ct: cancellation.Token);

        cancelled.ExitCode.Should().Be(130);
        NothingIsLeft(workspace);
        IsTombstone(workspace).Should().BeTrue();
    }

    [Test]
    public async Task ACancellationWhileTheStagingRootIsClearedEndsThePublicationAsCancelled()
    {
        using var workspace = Project();
        using var cancellation = new CancellationTokenSource();
        var removal = new PathRemoval(_ => throw new IOException("The process cannot access the file because it is being used by another process."),
            _ => true, _ => cancellation.Cancel());
        var (publication, request, staging) = await StalePublicationAsync(workspace, new CompilePublicationHooks { Removal = removal });

        using (publication)
        {
            var publish = () => publication.PublishAsync(request, cancellation.Token);
            await publish.Should().ThrowAsync<OperationCanceledException>("the runner reports a cancellation as exit code 130");
        }
        Directory.Exists(staging).Should().BeTrue("the stale root is left for the next compile");
        IsTombstone(workspace).Should().BeTrue();
    }

    [Test]
    public async Task ACancellationThatMeetsAFailedRecordReplacementKeepsTheTombstone()
    {
        using var workspace = Project();
        Succeeded(await Run(workspace, Inputs()));
        using var cancellation = new CancellationTokenSource();

        var cancelled = await RunWith(workspace, Inputs(), new CompilePublicationHooks(), Quiet with
        {
            BeforeRecordReplace = () =>
            {
                cancellation.Cancel();
                throw new IOException("The record could not be renamed.");
            },
        }, cancellation.Token);

        cancelled.ExitCode.Should().Be(130, "the cancellation decides, not the rename that failed with it");
        NothingIsLeft(workspace);
        IsTombstone(workspace).Should().BeTrue();
    }

    [Test]
    public async Task TheWarningOfARefusedRollbackNamesACompletedRecord()
    {
        using var workspace = Project();
        var written = File.ReadAllBytes(Succeeded(await Run(workspace, Inputs(layout: "single-file"))).Publication.IrPath);
        var request = CompilePublishRequest.ForDocumentTree("fsharp", Morphir.IR.Versioning.parse("4").ResultValue, null, written,
            Passed(CompileDocumentTree.Write(written)));
        var settings = Passed(new ProjectCompileSettingsResolver().Resolve(Inputs(), workspace.Context()));
        var warnings = new StringWriter();
        var hooks = new CompilePublicationHooks { Removal = new ScannedRemoval(failures: int.MaxValue).Removal };

        using (var publication = Passed(await new TaskRecordCompilePublisher(Quiet with { Diagnostics = warnings }, hooks).BeginAsync(settings, default)))
        {
            Passed(await publication.PublishAsync(request, default));
            // The second publication cannot move onto the first IR, and the file system refuses the removal of its stage.
            (await publication.PublishAsync(request, default)).IsPassed.Should().BeFalse();
        }

        IsTombstone(workspace).Should().BeFalse("the first publication completed the record");
        warnings.ToString().Should().Contain(".morphir-ir.staging").And.Contain("compile.json is the completed record")
            .And.NotContain("tombstone");
    }

    [Test]
    public async Task TheWarningOfARefusedRollbackOfAFirstCompileSaysThatThereIsNoRecord()
    {
        using var workspace = Project();
        using var cancellation = new CancellationTokenSource();
        var warnings = new StringWriter();

        var cancelled = await RunWith(workspace, Inputs(), new CompilePublicationHooks
        {
            Removal = new ScannedRemoval(failures: int.MaxValue).Removal,
            AfterMoved = _ => cancellation.Cancel(),
        }, Quiet with { Diagnostics = warnings }, cancellation.Token);

        cancelled.ExitCode.Should().Be(130);
        File.Exists(workspace.PathOf(".morphir/out/compile.json")).Should().BeFalse();
        warnings.ToString().Should().Contain("could not be removed").And.Contain("no compile.json").And.NotContain("tombstone")
            .And.NotContain("succeeded", "an earlier compile can have succeeded and left a record that could not be read");
    }

    [Test]
    public async Task AStagingRootThatCannotBeClearedRefusesThePublication()
    {
        using var workspace = Project();
        var scanner = new ScannedRemoval(failures: int.MaxValue);
        var (publication, request, staging) = await StalePublicationAsync(workspace, new CompilePublicationHooks { Removal = scanner.Removal });
        Checked<PublishedCompile> refused;
        using (publication) refused = await publication.PublishAsync(request, default);

        refused.IsPassed.Should().BeFalse("the tree is never written into a root that still holds other files");
        refused.Diagnostics.Select(d => d.CodeOrNull?.Value).Should().Equal("compile.publish_failed");
        refused.Diagnostics[0].Message.Should().Contain(staging);
        scanner.DeletesOf(staging).Should().Be(Morphir.Files.FileReplacement.Attempts);
        Directory.Exists(Path.Combine(Dest(workspace), "morphir-ir")).Should().BeFalse();
        IsTombstone(workspace).Should().BeTrue("no record is completed");
    }

    [Test]
    [Arguments(2)]
    [Arguments(int.MaxValue)]
    public async Task TheRollbackOfACancelledPublicationRetriesItsDeleteAndStaysCancelled(int failures)
    {
        using var workspace = Project();
        Succeeded(await Run(workspace, Inputs()));
        using var cancellation = new CancellationTokenSource();
        var scanner = new ScannedRemoval(failures);
        var target = Path.Combine(Dest(workspace), "morphir-ir");
        var warnings = new StringWriter();

        var cancelled = await RunWith(workspace, Inputs(), new CompilePublicationHooks
        {
            Removal = scanner.Removal,
            AfterMoved = _ => cancellation.Cancel(),
        }, Quiet with { Diagnostics = warnings }, cancellation.Token);

        cancelled.ExitCode.Should().Be(130, "a cleanup that fails never hides the cancellation");
        IsTombstone(workspace).Should().BeTrue();
        if (failures == 2)
        {
            scanner.DeletesOf(target).Should().Be(3);
            NothingIsLeft(workspace);
            warnings.ToString().Should().BeEmpty("nothing remains to report");
        }
        else
        {
            scanner.DeletesOf(target).Should().Be(Morphir.Files.FileReplacement.Attempts, "the rollback tries a bounded number of times");
            Directory.Exists(target).Should().BeTrue("the file system refused every delete");
            warnings.ToString().Should().Contain("warning:").And.Contain(target).And.Contain("could not be removed")
                .And.Contain("compile.json is the tombstone of the run");
        }
    }

    [Test]
    [Arguments("document-tree")]
    [Arguments("single-file")]
    public async Task ASecondPublicationThatCannotMoveLeavesTheFirstIrInPlace(string layout)
    {
        using var workspace = Project();
        var written = File.ReadAllBytes(Succeeded(await Run(workspace, Inputs(layout: "single-file"))).Publication.IrPath);
        var version = Morphir.IR.Versioning.parse("4").ResultValue;
        var request = layout == "document-tree"
            ? CompilePublishRequest.ForDocumentTree("fsharp", version, null, written, Passed(CompileDocumentTree.Write(written)))
            : new CompilePublishRequest("fsharp", version, null, written);
        var settings = Passed(new ProjectCompileSettingsResolver().Resolve(Inputs(layout: layout), workspace.Context()));

        using var publication = Passed(await new TaskRecordCompilePublisher(Quiet).BeginAsync(settings, default));
        var first = Passed(await publication.PublishAsync(request, default));
        var second = await publication.PublishAsync(request, default);

        second.IsPassed.Should().BeFalse("the first IR is already at the target");
        second.Diagnostics.Select(d => d.CodeOrNull?.Value).Should().Equal("compile.publish_failed");
        (File.Exists(first.IrPath) || Directory.Exists(first.IrPath)).Should().BeTrue("the second attempt did not move anything, so it removes nothing");
        if (layout == "document-tree") ReadTree(first.IrPath).IrVersion.Should().Be("4");
        else File.ReadAllBytes(first.IrPath).Should().Equal(written);
        IsTombstone(workspace).Should().BeFalse("the record of the first publication stays");
        Directory.EnumerateFileSystemEntries(Dest(workspace)).Should().ContainSingle("no staged IR stays");
    }

    private static T Passed<T>(Checked<T> result)
    {
        result.TryGetValue(out var value).Should().BeTrue(string.Join("\n", result.Diagnostics));
        return value!;
    }

    [Test]
    public async Task AClassicTreeIsRequestedAsThreeAndNotAsThreePointOne()
    {
        using var workspace = Project();
        var refused = await Run(workspace, Inputs("3.1.0"));
        refused.ExitCode.Should().Be(2);
        CompileRunners.ErrorCodes(refused).Should().Equal("compile.unsupported_ir_version");
    }

    [Test]
    public async Task EveryFileOfAClassicTreeSaysThreePointOne()
    {
        using var workspace = Project();
        var success = Succeeded(await Run(workspace, Inputs("3")));
        var files = Files(success.Publication.IrPath);
        files.Should().Contain("manifest.json");
        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(success.Publication.IrPath, file)));
            document.RootElement.GetProperty("formatVersion").GetString().Should().Be("3.1.0", file);
        }
    }

    [Test]
    public async Task OutputInstallsTheTreeAndAReinstallReplacesIt()
    {
        using var workspace = Project();
        var dist = workspace.PathOf("dist");
        var first = Succeeded(await Run(workspace, Inputs(output: "dist")));
        first.Publication.Installed.Should().Equal(Path.Combine(dist, "morphir-ir"));
        Files(Path.Combine(dist, "morphir-ir")).Should().Equal(Files(first.Publication.IrPath));
        ReadTree(Path.Combine(dist, "morphir-ir")).IrVersion.Should().Be("4");

        var again = Succeeded(await Run(workspace, Inputs(output: "dist")));
        again.Publication.Installed.Should().Equal(Path.Combine(dist, "morphir-ir"));

        // A single-file compile into the same target retires the files of the tree that the earlier install wrote.
        var single = Succeeded(await Run(workspace, Inputs(layout: "single-file", output: "dist")));
        single.Publication.Installed.Should().Equal(Path.Combine(dist, "morphir-ir.json"));
        File.Exists(Path.Combine(dist, "morphir-ir", "manifest.json")).Should().BeFalse();
        File.Exists(Path.Combine(dist, "morphir-ir.json")).Should().BeTrue();
    }

    private static async Task<(int Exit, CompileCommandTests.CapturedOutput Output)> Command(CompileWorkspace workspace, string arguments)
    {
        var output = new CompileCommandTests.CapturedOutput();
        var args = ("compile " + arguments).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        await using var app = MorphirCli.CreateBuilder(args)
            .AddScenario<GenerationScenario>()
            .AddScenario<CompilationScenario>()
            .ConfigureServices(services =>
            {
                services.AddSingleton(FSharpFrontend.Registration);
                services.AddSingleton<ICompileOutput>(output);
                services.AddSingleton(new CompileContextSource(() => workspace.Context()));
            })
            .Build();
        return (await app.RunAsync(), output);
    }

    private const string CommandInputs = "--input src/Rates.fs src/Pricing.fs --language fsharp --package-name acme/car-rental";

    [Test]
    public async Task TheIrLayoutOptionSelectsTheDocumentTree()
    {
        using var workspace = Project();
        var run = await Command(workspace, $"--json=document {CommandInputs} --ir-layout document-tree");
        run.Exit.Should().Be(0, run.Output.Text);
        using var report = JsonDocument.Parse(run.Output.Text);
        report.RootElement.GetProperty("output").GetString().Should().Be(workspace.PathOf(TreeDirectory));
        ReadTree(workspace.PathOf(TreeDirectory)).IrVersion.Should().Be("4");
    }

    [Test]
    public async Task AnInvalidIrLayoutOptionIsAnInvocationOfTheSettings()
    {
        using var workspace = Project();
        var run = await Command(workspace, $"--json=document {CommandInputs} --ir-layout folder");
        run.Exit.Should().Be(2, run.Output.Text);
        run.Output.Text.Should().Contain("compile.invalid_setting").And.Contain("--ir-layout");
    }

    [Test]
    [NotInParallel(nameof(CompileTargetTests))]
    [Arguments("4")]
    [Arguments("3")]
    public async Task CompileWithATargetGeneratesTheSameFilesFromATree(string version)
    {
        using var workspace = Project();
        var tree = await Command(workspace, $"--json=document {CommandInputs} --ir-version {version} --ir-layout document-tree --target fsharp --generate-output gen");
        tree.Exit.Should().Be(0, tree.Output.Text);
        using (var report = JsonDocument.Parse(tree.Output.Text))
            report.RootElement.GetProperty("output").GetString().Should().Be(workspace.PathOf(TreeDirectory));

        var single = await Command(workspace, $"--json=document {CommandInputs} --ir-version {version} --target fsharp --generate-output gen2");
        single.Exit.Should().Be(0, single.Output.Text);

        string Content(string root, string file) => File.ReadAllText(Path.Combine(root, file));
        var generated = Files(workspace.PathOf("gen"));
        generated.Should().NotBeEmpty().And.Equal(Files(workspace.PathOf("gen2")));
        foreach (var file in generated)
            Content(workspace.PathOf("gen"), file).Should().Be(Content(workspace.PathOf("gen2"), file), file);
    }

    [Test]
    public async Task ABuiltInBackendRefusesARecordedJsonTree()
    {
        using var workspace = Project();
        var compiled = await Command(workspace, $"--json=document {CommandInputs} --ir-layout document-tree");
        compiled.Exit.Should().Be(0, compiled.Output.Text);

        using var app = MorphirCli.CreateBuilder([]).AddScenario<GenerationScenario>().Build();
        var invocation = new GenerateOverrides(Target: "fsharp", Output: workspace.PathOf("generated"));
        var outcome = await app.Services.GetRequiredService<IMediator>().InvokeAsync<Outcome<GenerationSuccess>>(
            new GenerateRequest(invocation, new InvocationContext(workspace.Root) { Environment = workspace.Context().Environment }));
        var report = GenerationReport.From(outcome, invocation);

        report.ExitCode.Should().Be(1, report.ToJson());
        report.Diagnostics.Select(d => d.Code).Should().Equal(["unsupported-format"], report.ToJson());
        Directory.Exists(workspace.PathOf("generated")).Should().BeFalse();
    }
}
