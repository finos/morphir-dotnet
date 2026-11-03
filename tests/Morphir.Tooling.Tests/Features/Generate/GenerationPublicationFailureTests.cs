using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Morphir.IR.Pipeline;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Generate;

public sealed class GenerationPublicationFailureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-publish-failure-" + Guid.NewGuid().ToString("N"));
    private string Output => Path.Combine(_root, "output");
    public GenerationPublicationFailureTests() => Directory.CreateDirectory(_root);
    private static GenerationArtifacts Tree(params VFile[] files) => new(VFileTreeModule.fromFiles(ListModule.OfSeq(files)), []);
    private async Task<Outcome<GenerationPublication>> Publish(GenerationArtifacts artifacts) =>
        await new GenerationPublisher().PublishAsync(artifacts, new(Path.Combine(_root, "source.json")), new(Output), default);
    private Dictionary<string, string> Snapshot() => Directory.GetFiles(Output).ToDictionary(path => Path.GetFileName(path), File.ReadAllText);

    [Test]
    public async Task RemovesOnlyStaleOwnedFilesAndPreservesUnownedContent()
    {
        await Publish(Tree(VFileModule.ofText("A.fs", "one"), VFileModule.ofText("B.fs", "stale")));
        File.WriteAllText(Path.Combine(Output, "user.fs"), "user content");
        var receipt = ((Outcome<GenerationPublication>.Success)await Publish(Tree(VFileModule.ofText("A.fs", "two")))).Value;
        receipt.Removed.Should().Equal("B.fs"); receipt.Written.Should().Equal("A.fs");
        File.Exists(Path.Combine(Output, "B.fs")).Should().BeFalse();
        File.ReadAllText(Path.Combine(Output, "user.fs")).Should().Be("user content");
    }

    [Test]
    public async Task StagingFailurePreservesPreviousFilesAndOwnership()
    {
        await Publish(Tree(VFileModule.ofText("A.fs", "prior")));
        var before = Snapshot();
        var broken = VFileModule.ofStream("B.fs", new StreamSource(FSharpFunc<Unit, Stream>.FromConverter(_ => throw new IOException("Cannot open content")), FSharpOption<long>.None));
        var result = (Outcome<GenerationPublication>.Failure)await Publish(Tree(VFileModule.ofText("A.fs", "next"), broken));
        result.Error.Kind.Should().Be(GenerationFailureKind.Operational);
        result.Error.Diagnostics[0].Code.Should().Be("publication-failed");
        Snapshot().Should().Equal(before);
    }

    [Test]
    public async Task CommitTimeFailureReportsThePossibilityOfMixedOutput()
    {
        await Publish(Tree(VFileModule.ofText("A.fs", "prior"), VFileModule.ofText("B.fs", "prior B")));
        Directory.CreateDirectory(Path.Combine(Output, "C.fs"));
        var result = (Outcome<GenerationPublication>.Failure)await Publish(Tree(VFileModule.ofText("A.fs", "next"), VFileModule.ofText("C.fs", "blocked")));
        result.Error.Kind.Should().Be(GenerationFailureKind.Operational);
        result.Error.Diagnostics[0].Message.Should().Contain("commit").And.Contain("mixed");
        File.ReadAllText(Path.Combine(Output, "A.fs")).Should().Be("next", "the first move completed before the blocked destination failed");
        File.ReadAllText(Path.Combine(Output, "B.fs")).Should().Be("prior B");
    }
    public void Dispose() => Directory.Delete(_root, true);
}
