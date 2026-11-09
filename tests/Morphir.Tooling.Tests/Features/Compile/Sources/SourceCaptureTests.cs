using System.Text;
using FluentAssertions;
using Morphir.Compilation;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Compile.Sources;

namespace Morphir.Tooling.Tests.Features.Compile.Sources;

public sealed class SourceCaptureTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("morphir-sources-").FullName;

    private string Write(string relative, byte[] content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private string Write(string relative, string content) => Write(relative, Encoding.UTF8.GetBytes(content));

    private SourceSelection Select(params string[] documents) => SourceSelection.Create(_root, documents, null);

    private static Task<Checked<CapturedSources>> Capture(SourceSelection selection, SourceReadLimits? limits = null,
        CancellationToken ct = default) =>
        new SourceCapture().CaptureAsync(selection, limits ?? SourceReadLimits.Default, ct);

    private static CapturedSources Passed(Checked<CapturedSources> result)
    {
        result.Diagnostics.Should().BeEmpty();
        result.TryGetValue(out var sources).Should().BeTrue();
        return sources!;
    }

    private static IReadOnlyList<Diagnostic> Failed(Checked<CapturedSources> result)
    {
        result.IsPassed.Should().BeFalse();
        return result.Diagnostics;
    }

    [Test]
    public async Task DocumentsAreCapturedInSelectionOrderAsOwnedText()
    {
        Write("Rates.fs", "module Rates");
        Write("Pricing.fs", [0xEF, 0xBB, 0xBF, .. "module Pricing"u8.ToArray()]);

        var sources = Passed(await Capture(Select("Rates.fs", "Pricing.fs")));
        File.WriteAllText(Path.Combine(_root, "Rates.fs"), "changed");

        sources.Selection.Root.Should().Be(_root);
        sources.Documents.Select(document => document.Path).Should().Equal("Rates.fs", "Pricing.fs");
        sources.Documents.Select(document => document.Text).Should().Equal("module Rates", "module Pricing");
        sources.Documents[1].Entry.Length.Should().Be(17, "the owned bytes keep the byte order mark");
    }

    [Test]
    public async Task LinkedSourceIsRefusedAndNamed()
    {
        var real = Write("real/Rates.fs", "module Rates");
        Write("Pricing.fs", "module Pricing");
        File.CreateSymbolicLink(Path.Combine(_root, "Rates.fs"), real);

        var diagnostics = Failed(await Capture(Select("Rates.fs", "Pricing.fs")));

        diagnostics.Should().ContainSingle();
        diagnostics[0].CodeOrNull!.Value.Should().Be("sources.unsupported_entry");
        diagnostics[0].IsError.Should().BeTrue();
        diagnostics[0].LocationOrNull!.Uri.Should().Be("Rates.fs");
        diagnostics[0].Message.Should().Contain("Rates.fs");
    }

    [Test]
    public async Task DocumentInALinkedDirectoryIsRefused()
    {
        Write("real/Rates.fs", "module Rates");
        Directory.CreateSymbolicLink(Path.Combine(_root, "linked"), Path.Combine(_root, "real"));

        var diagnostics = Failed(await Capture(Select("linked/Rates.fs")));

        diagnostics.Select(d => d.CodeOrNull!.Value).Should().Equal("sources.unsupported_entry");
        diagnostics[0].LocationOrNull!.Uri.Should().Be("linked/Rates.fs");
    }

    [Test]
    public async Task InvalidUtf8IsAnErrorAtTheFirstInvalidByte()
    {
        Write("Rates.fs", "module Rates");
        Write("Pricing.fs", [.. "module Pricing\n  // é "u8.ToArray(), 0xC3, 0x28, .. "\n"u8.ToArray()]);

        var diagnostics = Failed(await Capture(Select("Rates.fs", "Pricing.fs")));

        diagnostics.Should().ContainSingle();
        diagnostics[0].CodeOrNull!.Value.Should().Be("sources.invalid_utf8");
        var location = diagnostics[0].LocationOrNull!;
        location.Uri.Should().Be("Pricing.fs");
        location.Range.Start.Should().Be(Position.Create(1, 7), "the position counts UTF-16 code units");
        location.Range.End.Should().Be(Position.Create(1, 8));
    }

    [Test]
    public async Task IndependentDocumentProblemsAreAllReported()
    {
        var real = Write("real/Rates.fs", "module Rates");
        File.CreateSymbolicLink(Path.Combine(_root, "Rates.fs"), real);
        Write("Pricing.fs", [0xFF]);

        var diagnostics = Failed(await Capture(Select("Rates.fs", "Missing.fs", "Pricing.fs")));

        diagnostics.Select(d => (d.CodeOrNull!.Value, d.LocationOrNull!.Uri)).Should().Equal(
            ("sources.unsupported_entry", "Rates.fs"),
            ("sources.unreadable", "Missing.fs"),
            ("sources.invalid_utf8", "Pricing.fs"));
    }

    [Test]
    public async Task SpecialFilesAreRefused()
    {
        Write("Rates.fs", "module Rates");
        var capture = new SourceCapture(_ => false);

        var result = await capture.CaptureAsync(Select("Rates.fs"), SourceReadLimits.Default, default);

        Failed(result).Select(d => d.CodeOrNull!.Value).Should().Equal("sources.unsupported_entry");
    }

    [Test]
    [Arguments("bytes")]
    [Arguments("documents")]
    [Arguments("path")]
    public async Task BudgetsAreSharedAndStopCapture(string dimension)
    {
        Write("A.fs", "1234");
        Write("B.fs", "5678");
        var limits = dimension switch
        {
            "bytes" => new SourceReadLimits(MaxBytes: 7),
            "documents" => new SourceReadLimits(MaxDocuments: 1),
            _ => new SourceReadLimits(MaxPathLength: _root.Length + 2),
        };

        var diagnostics = Failed(await Capture(Select("A.fs", "B.fs"), limits));

        diagnostics.Select(d => d.CodeOrNull!.Value).Should().Equal("sources.limit_exceeded");
    }

    [Test]
    public async Task InvalidBudgetsAreRefusedBeforeReading()
    {
        Write("A.fs", "1");

        var diagnostics = Failed(await Capture(Select("A.fs"), new SourceReadLimits(MaxBytes: 0)));

        diagnostics.Select(d => d.CodeOrNull!.Value).Should().Equal("sources.invalid_limits");
        diagnostics[0].LocationOrNull.Should().BeNull();
    }

    [Test]
    public async Task CancellationThrows()
    {
        Write("A.fs", "1");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var capture = () => Capture(Select("A.fs"), ct: cancellation.Token);

        await capture.Should().ThrowAsync<OperationCanceledException>();
    }

    public void Dispose() => TolerantDirectory.TryDelete(_root);
}
