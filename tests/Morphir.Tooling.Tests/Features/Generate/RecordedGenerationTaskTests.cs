// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Text;
using FluentAssertions;
using Morphir.Mep;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.TaskRecords;

namespace Morphir.Tooling.Tests.Features.Generate;

[Category("PlatformSpecific")]
public sealed class RecordedGenerationTaskTests
{
    // A named publication mother: real stage chunks and schema-1 records, with no model construction.
    private sealed class PublicationCase : IDisposable
    {
        private readonly TestSandbox _sandbox = TestSandbox.Create("recorded-generation-publication-");
        public TaskLocation Location { get; }
        public string Output => _sandbox.PathOf("generated");
        public string Unrelated => _sandbox.PathOf("unrelated");
        public TaskStoreOptions Options => new() { MorphirHome = _sandbox.MorphirHome, Diagnostics = TextWriter.Null };
        public PublicationCase()
        {
            var root = ((Outcome<OutRoot>.Success)OutRoot.Resolve(new OutRootRequest(_sandbox.Work, _sandbox.Work)
            { OutDirFlag = "relocated" })).Value;
            TaskId.TryParse("generate/text", out var task, out _).Should().BeTrue();
            Location = root.Task(MemberPath.Root, task!);
        }
        public TaskRecord PreviousOwnedOutput()
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "order.txt"), "73\n");
            Directory.CreateDirectory(Location.Dest);
            File.WriteAllText(Path.Combine(Location.Dest, "order.txt"), "73\n");
            var previous = new TaskRecord
            {
                Task = Location.Task,
                Module = Location.Member,
                Inputs = ["compile"],
                Value = [RecordPath.Parse("order.txt")],
                CompletedAt = RecordTimestamp.At(DateTimeOffset.UnixEpoch),
                Installed = ImmutableSortedDictionary.Create<string, ImmutableArray<RecordPath>>(StringComparer.Ordinal)
                    .Add(Output, [RecordPath.Parse("order.txt")])
            };
            RecordFile.Write(Location.Record, previous);
            return previous;
        }
        public void WithForeignOutput()
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "order.txt"), "foreign\n");
        }
        public BoundedArtifactStage Stage() => new(Location.Dest, Limits(), new GenerationSessionDiskBudget(8192));
        public void WithLinkedTaskPath(string selected)
        {
            PreviousOwnedOutput();
            Directory.CreateDirectory(Unrelated);
            var sentinel = Path.Combine(Unrelated, "keep.txt");
            File.WriteAllText(sentinel, "unrelated\n");
            var path = selected switch
            {
                "record" => Location.Record,
                "lock" => Location.Lock,
                "dest" => Location.Dest,
                "output" => Output,
                _ => throw new ArgumentException("Unknown task path.", nameof(selected))
            };
            if (selected is "dest" or "output")
            {
                Directory.Move(path, path + ".previous");
                Directory.CreateSymbolicLink(path, Unrelated);
            }
            else
            {
                if (File.Exists(path)) File.Move(path, path + ".previous");
                File.CreateSymbolicLink(path, sentinel);
            }
        }
        public static StagedGenerationOutput Complete(BoundedArtifactStage stage, string name, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var chunk = new ModelArtifactChunk(name, 0, bytes.Length, Sha256Digest.Compute(bytes), Convert.ToBase64String(bytes), true);
            return ((Outcome<StagedGenerationOutput?>.Success)stage.Apply(ModelAccessStep.NewComplete("run", [chunk], []))).Value!;
        }
        private static ModelAccessLimits Limits() => new(512, 512, 192, 8, 32, 256, 1, 8, 4096, 4096,
            128, 1024, 4096, 8192, 4096, 16, 500, 2000);
        public void Dispose() => _sandbox.Dispose();
    }

    [Test]
    [Arguments("record")]
    [Arguments("lock")]
    [Arguments("dest")]
    [Arguments("output")]
    public async Task LinkedTaskPathsAreRefusedBeforePreparation(string selected)
    {
        if (OperatingSystem.IsWindows()) Skip.Test("This link control runs without Windows developer-mode privileges.");
        using var data = new PublicationCase();
        data.WithLinkedTaskPath(selected);
        Func<Task> open = async () =>
        {
            using var run = await RecordedGenerationTask.OpenAsync(data.Location, data.Output, data.Options, default);
        };
        await open.Should().ThrowAsync<IOException>();
        File.ReadAllText(Path.Combine(data.Unrelated, "keep.txt")).Should().Be("unrelated\n");
    }

    [Test]
    public async Task CancellationBeforeCommitLeavesThePriorLedgerAndNoInstalledPrelude()
    {
        using var data = new PublicationCase();
        var previous = data.PreviousOwnedOutput();
        using var run = await RecordedGenerationTask.OpenAsync(data.Location, data.Output, data.Options, default);
        run.Prepare();
        using var stage = data.Stage();
        var staged = PublicationCase.Complete(stage, "prelude.txt", "private\n");
        Action commit = () => run.Commit(stage, staged, new CancellationToken(canceled: true));
        commit.Should().Throw<OperationCanceledException>();
        var record = ((RecordReading.Found)RecordFile.Read(data.Location.Record)).Record;
        record.Tombstone.Should().BeTrue();
        record.Value.Should().BeEmpty();
        record.CompletedAt.Should().Be(previous.CompletedAt);
        record.Installed.Should().BeEquivalentTo(previous.Installed);
        Directory.Exists(stage.Directory).Should().BeFalse();
        File.Exists(Path.Combine(data.Location.Dest, "prelude.txt")).Should().BeFalse();
        File.Exists(Path.Combine(data.Output, "prelude.txt")).Should().BeFalse();
        File.ReadAllText(Path.Combine(data.Output, "order.txt")).Should().Be("73\n");
    }

    [Test]
    public async Task InstallRefusalKeepsAValidWorkingResultAndDoesNotClaimForeignContent()
    {
        using var data = new PublicationCase();
        data.WithForeignOutput();
        using var run = await RecordedGenerationTask.OpenAsync(data.Location, data.Output, data.Options, default);
        run.Prepare();
        using var stage = data.Stage();
        var staged = PublicationCase.Complete(stage, "order.txt", "74\n");
        run.Commit(stage, staged, default);
        var installed = await run.InstallAsync(default);
        installed.Should().BeOfType<Outcome<InstallReport>.Failure>().Which.Error.Diagnostics.Select(d => d.Code)
            .Should().Contain("install.foreign_content");
        var record = ((RecordReading.Found)RecordFile.Read(data.Location.Record)).Record;
        record.Tombstone.Should().BeFalse();
        record.Inputs.Should().Equal("compile");
        record.Value.Select(path => path.Value).Should().Equal("order.txt");
        record.Installed.Should().BeEmpty();
        Directory.Exists(stage.Directory).Should().BeFalse();
        File.ReadAllText(Path.Combine(data.Location.Dest, "order.txt")).Should().Be("74\n");
        File.ReadAllText(Path.Combine(data.Output, "order.txt")).Should().Be("foreign\n");
    }

    [Test]
    public async Task CancellationOfInstallKeepsTheCompletedWorkingProductAndPriorOwnership()
    {
        using var data = new PublicationCase();
        var previous = data.PreviousOwnedOutput();
        using var run = await RecordedGenerationTask.OpenAsync(data.Location, data.Output, data.Options, default);
        run.Prepare();
        using var stage = data.Stage();
        run.Commit(stage, PublicationCase.Complete(stage, "order.txt", "74\n"), default);
        var result = await run.InstallAsync(new CancellationToken(canceled: true));
        result.Should().BeOfType<Outcome<InstallReport>.Failure>().Which.Error.Kind.Should().Be(GenerationFailureKind.Cancelled);
        var record = ((RecordReading.Found)RecordFile.Read(data.Location.Record)).Record;
        record.Tombstone.Should().BeFalse();
        record.Installed.Should().BeEquivalentTo(previous.Installed);
        File.ReadAllText(Path.Combine(data.Location.Dest, "order.txt")).Should().Be("74\n");
        File.ReadAllText(Path.Combine(data.Output, "order.txt")).Should().Be("73\n");
    }

    [Test]
    public async Task ACompletedRunCanReplaceItsOwnEarlierInstalledOutput()
    {
        using var data = new PublicationCase();
        data.PreviousOwnedOutput();
        using var run = await RecordedGenerationTask.OpenAsync(data.Location, data.Output, data.Options, default);
        run.Prepare();
        using var stage = data.Stage();
        run.Commit(stage, PublicationCase.Complete(stage, "order.txt", "74\n"), default);
        (await run.InstallAsync(default)).Should().BeOfType<Outcome<InstallReport>.Success>();
        File.ReadAllText(Path.Combine(data.Output, "order.txt")).Should().Be("74\n");
        ((RecordReading.Found)RecordFile.Read(data.Location.Record)).Record.Installed[data.Output]
            .Select(path => path.Value).Should().Equal("order.txt");
    }
}
