// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Morphir.IR.Trees;
using Morphir.Mep;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Generate;

[Category("PlatformSpecific")]
public sealed class BoundedArtifactStageTests
{
    [Test]
    public void StagesOneFileAcrossExchangesAndReturnsTheFullFileDigest()
    {
        using var sandbox = TestSandbox.Create();
        var limits = Limits();
        using var stage = Open(sandbox, limits);
        var part = new string('x', 108);
        var expected = Encoding.UTF8.GetBytes(new string('x', 540) + "odd");
        expected.Length.Should().BeGreaterThan(limits.MaxFrameBytes);

        for (var index = 0; index < 5; index++)
        {
            var step = ModelAccessStep.NewContinue("run", index + 1, [], [Chunk("sub/order.txt", index * 108L, part)], []);
            // Leave room for the JSON-RPC response envelope.
            JsonSerializer.SerializeToUtf8Bytes(step, MepJson.Default.ModelAccessStep).Length.Should().BeLessThan(limits.MaxFrameBytes - 64);
            Accept(stage.Apply(step)).Should().BeNull();
        }
        var completed = Accept(stage.Apply(Complete(Chunk("sub/order.txt", 540, "odd", true))))!;

        completed.Directory.Should().Be(stage.Directory);
        completed.Artifacts.Should().ContainSingle().Which.Should().Be(
            new StagedGenerationArtifact("sub/order.txt", expected.Length, Sha256Digest.Compute(expected)));
        File.ReadAllBytes(Path.Combine(completed.Directory, "sub", "order.txt")).Should().Equal(expected);
    }

    [Test]
    public void AcceptsAnEmptyFileAndAnEmptyClosingChunk()
    {
        using var sandbox = TestSandbox.Create();
        using var stage = Open(sandbox, Limits());
        Accept(stage.Apply(Continue(Chunk("a.txt", 0, "a")))).Should().BeNull();

        var completed = Accept(stage.Apply(Complete(Chunk("a.txt", 1, "", true), Chunk("empty.txt", 0, "", true))))!;

        File.ReadAllText(Path.Combine(completed.Directory, "a.txt")).Should().Be("a");
        new FileInfo(Path.Combine(completed.Directory, "empty.txt")).Length.Should().Be(0);
    }

    [Test]
    [Arguments(14L, false)]
    [Arguments(15L, true)]
    public void ChargesEachSmallChunkAndTheFinalFileHash(long workLimit, bool accepted)
    {
        using var sandbox = TestSandbox.Create();
        var limits = Limits();
        var disk = new GenerationSessionDiskBudget(limits.MaxSessionDiskBytes);
        using var stage = new BoundedArtifactStage(sandbox.Work, limits, disk);
        var work = new BoundedTreeWorkBudget(workLimit);

        // The first chunk charges 2 for encoded admission, 2 for the path and 3 for byte processing.
        Accept(stage.Apply(Continue(Chunk("a.txt", 0, "a")), work: work)).Should().BeNull();
        // The final chunk costs the same, plus one block to hash the whole staged file.
        var result = stage.Apply(Complete(Chunk("a.txt", 1, "b", true)), work: work);
        if (accepted)
        {
            var output = Accept(result)!;
            File.ReadAllText(Path.Combine(output.Directory, "a.txt")).Should().Be("ab");
        }
        else
        {
            Refused(result).Should().Be("extension.bounded_work_limit");
            Directory.Exists(stage.Directory).Should().BeFalse();
            disk.TryReserve(limits.MaxSessionDiskBytes).Should().BeTrue();
        }
    }

    [Test]
    [Arguments("../escape")]
    [Arguments("sub/../../escape")]
    [Arguments("/absolute")]
    [Arguments("sub\\escape")]
    [Arguments("C:escape")]
    [Arguments("sub/NUL.txt")]
    [Arguments("sub/file.")]
    [Arguments("sub/file?")]
    public void RefusesAnUnsafePathAndDiscardsEarlierOutput(string path)
    {
        using var sandbox = TestSandbox.Create();
        using var stage = Open(sandbox, Limits());
        Accept(stage.Apply(Continue(Chunk("prior", 0, "safe", true)))).Should().BeNull();

        Refused(stage.Apply(Complete(Chunk(path, 0, "bad", true)))).Should().Be("extension.unsafe_artifact");
        Directory.Exists(stage.Directory).Should().BeFalse();
        Directory.EnumerateFileSystemEntries(sandbox.Work).Should().BeEmpty();
    }

    [Test]
    [Arguments(0L)]
    [Arguments(4L)]
    public void RefusesAReplayOrGap(long offset)
    {
        using var sandbox = TestSandbox.Create();
        using var stage = Open(sandbox, Limits());
        Accept(stage.Apply(Continue(Chunk("order.txt", 0, "one")))).Should().BeNull();

        Refused(stage.Apply(Complete(Chunk("order.txt", offset, "two", true)))).Should().Be("extension.artifact_offset");
        Directory.Exists(stage.Directory).Should().BeFalse();
    }

    [Test]
    [Arguments("order.txt")]
    [Arguments("ORDER.txt")]
    [Arguments("order.txt/child")]
    public void RefusesAClosedPathOrAFileDirectoryCollision(string second)
    {
        using var sandbox = TestSandbox.Create();
        using var stage = Open(sandbox, Limits());
        Accept(stage.Apply(Continue(Chunk("order.txt", 0, "one", true)))).Should().BeNull();

        Refused(stage.Apply(Complete(Chunk(second, 0, "two", true)))).Should().Be("extension.unsafe_artifact");
        Directory.Exists(stage.Directory).Should().BeFalse();
    }

    [Test]
    public void CompletionWithAnOpenArtifactDiscardsTheStage()
    {
        using var sandbox = TestSandbox.Create();
        using var stage = Open(sandbox, Limits());
        Accept(stage.Apply(Continue(Chunk("order.txt", 0, "one")))).Should().BeNull();

        Refused(stage.Apply(Complete())).Should().Be("extension.artifact_unclosed");
        Directory.Exists(stage.Directory).Should().BeFalse();
        Refused(stage.Apply(Complete(Chunk("order.txt", 3, "", true)))).Should().Be("extension.artifact_state");
    }

    [Test]
    [Arguments("AB==", 1)]
    [Arguments("YQ==", 2)]
    [Arguments("Yg==", 1)]
    public void RefusesNoncanonicalEncodingWrongLengthOrWrongDigest(string encoded, int length)
    {
        using var sandbox = TestSandbox.Create();
        using var stage = Open(sandbox, Limits());
        var invalid = new ModelArtifactChunk("order.txt", 0, length, Sha256Digest.Compute("a"u8), encoded, true);

        Refused(stage.Apply(Complete(invalid))).Should().Be("extension.artifact_integrity");
        Directory.Exists(stage.Directory).Should().BeFalse();
    }

    [Test]
    [Arguments("maxArtifactChunkBytes")]
    [Arguments("maxEncodedBytesPerExchange")]
    [Arguments("maxDecodedBytesPerExchange")]
    [Arguments("maxArtifactBytes")]
    [Arguments("maxTotalArtifactBytes")]
    [Arguments("maxSessionDiskBytes")]
    [Arguments("maxHostCacheBytes")]
    public void ReportsTheLimitItEnforces(string limit)
    {
        using var sandbox = TestSandbox.Create();
        using var stage = Open(sandbox, Limits(limit));
        // Each chunk's canonical base64 is 8 bytes. Together they are 10 decoded and 16 encoded bytes.
        var result = stage.Apply(Complete(Chunk("one", 0, "12345"), Chunk("one", 5, "67890", true)));

        Refused(result).Should().Be("extension.artifact_limit");
        ((Outcome<StagedGenerationOutput?>.Failure)result).Error.Diagnostics[0].Message.Should().Contain(limit);
        Directory.Exists(stage.Directory).Should().BeFalse();
    }

    [Test]
    [Arguments("maxEncodedBytesPerExchange", false)]
    [Arguments("maxEncodedBytesPerExchange", true)]
    [Arguments("maxDecodedBytesPerExchange", false)]
    [Arguments("maxDecodedBytesPerExchange", true)]
    public void RefusesTheEntireOversizedExchangeBeforeDecodingAnyChunk(string limit, bool complete)
    {
        using var sandbox = TestSandbox.Create();
        using var stage = Open(sandbox, Limits(limit));
        // The first payload has a bad digest but fits every byte ceiling by itself.
        // The combined lengths must be rejected before hashing or writing it.
        var first = new ModelArtifactChunk("one", 0, 5, Sha256Digest.Compute("54321"u8), "MTIzNDU=", true);
        var second = Chunk("two", 0, "67890", true);
        var result = stage.Apply(complete ? Complete(first, second) : Continue(first, second));

        var diagnostic = result.Should().BeOfType<Outcome<StagedGenerationOutput?>.Failure>().Which.Error.Diagnostics[0];
        diagnostic.Code.Should().Be("extension.artifact_limit");
        diagnostic.Message.Should().Contain(limit);
        Directory.Exists(stage.Directory).Should().BeFalse();
    }

    [Test]
    public void RefusesAForgedDecodedLengthBeforeTouchingAnEarlierChunk()
    {
        using var sandbox = TestSandbox.Create();
        using var stage = Open(sandbox, Limits("maxDecodedBytesPerExchange"));
        // The declared lengths fit the nine-byte budget. The base64 content holds ten bytes.
        var first = new ModelArtifactChunk("one", 0, 5, Sha256Digest.Compute("54321"u8), "MTIzNDU=", true);
        var forged = new ModelArtifactChunk("two", 0, 4, Sha256Digest.Compute("67890"u8), "Njc4OTA=", true);
        var result = stage.Apply(Complete(first, forged));

        var diagnostic = result.Should().BeOfType<Outcome<StagedGenerationOutput?>.Failure>().Which.Error.Diagnostics[0];
        diagnostic.Code.Should().Be("extension.artifact_integrity");
        diagnostic.Path.Should().Be("two", "the batch preflight must detect the lie before checking the first digest");
        Directory.Exists(stage.Directory).Should().BeFalse();
    }

    [Test]
    public void AcceptsTwoArtifactsAtTheExactAggregateLimits()
    {
        using var sandbox = TestSandbox.Create();
        using var stage = Open(sandbox, Limits("exactExchange"));
        Accept(stage.Apply(Complete(Chunk("one", 0, "12345", true), Chunk("two", 0, "67890", true))))
            .Should().NotBeNull();
        File.ReadAllText(Path.Combine(stage.Directory, "one")).Should().Be("12345");
        File.ReadAllText(Path.Combine(stage.Directory, "two")).Should().Be("67890");
    }

    [Test]
    public void ChargesClosedMetadataEvenForAnEmptyOutputFile()
    {
        using var sandbox = TestSandbox.Create();
        using var stage = Open(sandbox, Limits("closedMetadata"));

        Refused(stage.Apply(Complete(Chunk("a", 0, "", true)))).Should().Be("extension.artifact_limit");
        Directory.Exists(stage.Directory).Should().BeFalse();
    }

    [Test]
    public void AFailedCleanupDoesNotHideTheCancellationDuringDisposal()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows is required to hold a file without delete sharing.");
        using var sandbox = TestSandbox.Create();
        var limits = Limits("maxSessionDiskBytes");
        var disk = new GenerationSessionDiskBudget(limits.MaxSessionDiskBytes);
        using var stage = new BoundedArtifactStage(sandbox.Work, limits, disk);
        Accept(stage.Apply(Continue(Chunk("order.txt", 0, "partial")))).Should().BeNull();
        using (File.Open(Path.Combine(stage.Directory, "order.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = stage.Apply(Complete(), new CancellationToken(canceled: true));
            Refused(result).Should().Be("cancelled");
            ((Outcome<StagedGenerationOutput?>.Failure)result).Error.Diagnostics.Select(note => note.Code)
                .Should().Contain("extension.artifact_cleanup");
            Action dispose = stage.Dispose;
            dispose.Should().NotThrow();
            disk.TryReserve(3).Should().BeFalse("the seven staged bytes are still on disk");
        }
        stage.Cleanup().Should().BeNull();
        Directory.Exists(stage.Directory).Should().BeFalse();
        disk.TryReserve(9).Should().BeTrue("successful cleanup releases the reservation");
    }

    [Test]
    public void SharesTheDiskLimitWithOtherSessionFiles()
    {
        using var sandbox = TestSandbox.Create();
        var limits = Limits("maxSessionDiskBytes");
        var disk = new GenerationSessionDiskBudget(limits.MaxSessionDiskBytes);
        disk.TryReserve(5).Should().BeTrue("a spill already owns five of the nine bytes");
        using (var stage = new BoundedArtifactStage(sandbox.Work, limits, disk))
        {
            Refused(stage.Apply(Complete(Chunk("order.txt", 0, "12345", true)))).Should().Be("extension.artifact_limit");
            Directory.Exists(stage.Directory).Should().BeFalse();
        }
        using (var stage = new BoundedArtifactStage(sandbox.Work, limits, disk))
        {
            Accept(stage.Apply(Complete(Chunk("order.txt", 0, "1234", true)))).Should().NotBeNull();
            disk.TryReserve(1).Should().BeFalse("artifacts remain reserved while the stage exists");
        }
        disk.TryReserve(4).Should().BeTrue("only the spill still holds a reservation");
    }

    [Test]
    public void LimitsDiagnosticsAndDeletesTheStageOnCancellation()
    {
        using var sandbox = TestSandbox.Create();
        using (var stage = Open(sandbox, Limits()))
        {
            var note = new Diagnostic(DiagnosticSeverity.Warning, null, "example", null, [], null);
            Refused(stage.Apply(ModelAccessStep.NewComplete("run", [], [note, note]))).Should().Be("extension.artifact_limit");
            Directory.Exists(stage.Directory).Should().BeFalse();
        }
        using (var stage = Open(sandbox, Limits()))
        {
            Accept(stage.Apply(Continue(Chunk("order.txt", 0, "partial")))).Should().BeNull();
            Refused(stage.Apply(Complete(), new CancellationToken(canceled: true))).Should().Be("cancelled");
            Directory.Exists(stage.Directory).Should().BeFalse();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void ChargesRetainedDiagnosticsAndArtifactMetadataToTheSameBudget(bool addArtifact)
    {
        using var sandbox = TestSandbox.Create();
        using var stage = Open(sandbox, Limits());
        var note = new Diagnostic(DiagnosticSeverity.Warning, null, new string('x', addArtifact ? 1750 : 1100), null, [], null);
        Accept(stage.Apply(ModelAccessStep.NewContinue("run", 1, [], [], [note]))).Should().BeNull();

        var next = addArtifact ? Complete(Chunk("order.txt", 0, "", true)) : ModelAccessStep.NewComplete("run", [], [note]);
        var failure = stage.Apply(next).Should().BeOfType<Outcome<StagedGenerationOutput?>.Failure>().Which.Error;
        failure.Diagnostics[0].Message.Should().Contain("maxHostCacheBytes");
        Directory.Exists(stage.Directory).Should().BeFalse();
    }

    [Test]
    [Arguments(0L, true)]
    [Arguments(1L, false)]
    public void SharesTheExactCacheCapWithReservedDiscoveryPaths(long excess, bool accepted)
    {
        using var sandbox = TestSandbox.Create();
        var limits = Limits();
        // A closed a.txt retains 512 + 2 * 5 bytes in the stage.
        var reservation = limits.MaxHostCacheBytes - 522L + excess;
        using var stage = new BoundedArtifactStage(sandbox.Work, limits,
            new GenerationSessionDiskBudget(limits.MaxSessionDiskBytes), reservation);
        var result = stage.Apply(Complete(Chunk("a.txt", 0, "", true)));
        if (accepted) Accept(result).Should().NotBeNull();
        else Refused(result).Should().Be("extension.artifact_limit");
    }
    private static StagedGenerationOutput? Accept(Outcome<StagedGenerationOutput?> result) =>
        result.Should().BeOfType<Outcome<StagedGenerationOutput?>.Success>().Which.Value;

    private static string Refused(Outcome<StagedGenerationOutput?> result) =>
        result.Should().BeOfType<Outcome<StagedGenerationOutput?>.Failure>().Which.Error.Diagnostics[0].Code;

    private static ModelArtifactChunk Chunk(string path, long offset, string text, bool end = false)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return new(path, offset, bytes.Length, Sha256Digest.Compute(bytes), Convert.ToBase64String(bytes), end);
    }

    private static ModelAccessStep Continue(params ModelArtifactChunk[] chunks) =>
        ModelAccessStep.NewContinue("run", 1, [], chunks, []);

    private static ModelAccessStep Complete(params ModelArtifactChunk[] chunks) =>
        ModelAccessStep.NewComplete("run", chunks, []);

    private static BoundedArtifactStage Open(TestSandbox sandbox, ModelAccessLimits limits) =>
        new(sandbox.Work, limits, new GenerationSessionDiskBudget(limits.MaxSessionDiskBytes));

    private static ModelAccessLimits Limits(string? reduced = null) => new(
        512,
        reduced switch { "exactExchange" => 16, "maxEncodedBytesPerExchange" => 12, _ => 512 },
        reduced switch { "exactExchange" => 10, "maxDecodedBytesPerExchange" => 9, _ => 192 },
        8, 32, 256, 1, 8, 4096,
        reduced switch { "maxHostCacheBytes" => 1, "closedMetadata" => 194, _ => 4096 },
        reduced == "maxArtifactChunkBytes" ? 4 : 128,
        reduced == "maxArtifactBytes" ? 9 : 1024,
        reduced == "maxTotalArtifactBytes" ? 9 : 4096,
        reduced == "maxSessionDiskBytes" ? 9 : 8192,
        4096, 16, 500, 2000);
}
