using FluentAssertions;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Generate;

public sealed class BoundedModelCaptureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-capture-" + Guid.NewGuid().ToString("N"));
    public BoundedModelCaptureTests() => Directory.CreateDirectory(_root);
    private string Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
    private static async Task<Outcome<RetainedInput>> Capture(string path, ModelReadLimits? limits = null, CancellationToken ct = default) =>
        await new BoundedModelCapture().CaptureAsync(new(path), limits ?? ModelReadLimits.Default, ct);
    private static GenerationFailure Error(Outcome<RetainedInput> result, string code)
    {
        var error = ((Outcome<RetainedInput>.Failure)result).Error;
        error.Diagnostics[0].Code.Should().Be(code);
        return error;
    }

    [Test]
    public async Task FileSnapshotOwnsExactBytesAtTheBoundary()
    {
        var path = Write("model.json", "1234");
        var captured = (RetainedInput.File)((Outcome<RetainedInput>.Success)await Capture(path, new(MaxBytes: 4))).Value;
        File.WriteAllText(path, "changed");
        captured.Entry.CopyBytes().Should().Equal("1234"u8.ToArray());
        captured.SourcePath.Should().Be(Path.GetFullPath(path));
        Error(await Capture(path, new(MaxBytes: 4)), "input-limit");
    }

    [Test]
    public async Task TreeBudgetIsAggregateAndCaptureIsPrivatelyOwned()
    {
        Write("a.ion", "123"); Write("sub/b.ion", "456");
        var tree = (RetainedInput.Tree)((Outcome<RetainedInput>.Success)await Capture(_root, new(MaxBytes: 6, MaxEntries: 3, MaxDepth: 2))).Value;
        tree.Entries.Select(entry => entry.RelativePath).Should().Equal("a.ion", "sub/b.ion");
        File.WriteAllText(Path.Combine(_root, "a.ion"), "changed");
        tree.Entries[0].CopyBytes().Should().Equal("123"u8.ToArray());
        Error(await Capture(_root, new(MaxBytes: 5)), "input-limit");
    }

    [Test]
    [Arguments("entries")]
    [Arguments("depth")]
    [Arguments("path")]
    public async Task BoundsApplyDuringEnumeration(string dimension)
    {
        Write("sub/model.ion", "1");
        var limits = dimension switch
        {
            "entries" => new ModelReadLimits(MaxEntries: 1),
            "depth" => new ModelReadLimits(MaxDepth: 1),
            _ => new ModelReadLimits(MaxPathLength: 4)
        };
        Error(await Capture(_root, limits), "input-limit");
    }

    [Test]
    public async Task UnreadableEntryReportsItsPathWithoutAPartialTree()
    {
        var path = Write("locked.ion", "1");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var error = Error(await Capture(_root), "input-read");
        error.Kind.Should().Be(GenerationFailureKind.Operational);
        error.Diagnostics[0].Path.Should().Be(path);
    }

    [Test]
    public async Task CancelledCaptureHasNoSuccessValue()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Error(await Capture(_root, ct: cancellation.Token), "cancelled").Kind.Should().Be(GenerationFailureKind.Cancelled);
    }

    [Test]
    public async Task InvalidBudgetsAndMissingInputsAreStructuredFailures()
    {
        Error(await Capture(_root, new(MaxBytes: 0)), "invalid-input-limits").Kind.Should().Be(GenerationFailureKind.Invocation);
        Error(await Capture(Path.Combine(_root, "missing")), "input-read");
    }

    [Test]
    public async Task LinksAreRefusedAtTheRootInsideTreesAndInAncestors()
    {
        var real = Write("real/model.ion", "1");
        var link = Path.Combine(_root, "linked.ion");
        File.CreateSymbolicLink(link, real);
        Error(await Capture(link), "unsupported-input-entry");
        Error(await Capture(_root), "unsupported-input-entry");
        File.Delete(link);
        var directoryLink = Path.Combine(_root, "linked-directory");
        Directory.CreateSymbolicLink(directoryLink, Path.GetDirectoryName(real)!);
        Error(await Capture(Path.Combine(directoryLink, "model.ion")), "unsupported-input-entry");
        Directory.Delete(directoryLink);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
